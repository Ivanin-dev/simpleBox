using System;
using System.Collections.Generic;
using SimpleBox.Core;
using SimpleBox.Gameplay;
using SimpleBox.World;
using Unity.Netcode;
using UnityEngine;

namespace SimpleBox.Net
{
    /// <summary>
    /// Glue between NetworkManager and the sandbox systems (lives on the NetworkManager object).
    /// Host: owner ids = NGO client ids (host = 0, same as single player); registered entities are network-spawned,
    /// removed entities despawned (pooled props: Despawn(false), so the pool keeps the instance).
    /// Client: local offline world is cleared, SandboxActions / PhysicsGun forward to the local avatar,
    /// the physics guard is off (no physics authority). Leaving returns everything to single-player mode.
    /// Server rule: a disconnected player's entities are removed (optional).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    public sealed class NetworkGameplay : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private SandboxActions actions;
        [SerializeField] private EntityRegistry registry;
        [SerializeField] private PhysicsGuard physicsGuard;
        [SerializeField] private PhysicsGun localPhysgun;
        [Tooltip("Local player root (CharacterController). The owner's avatar follows it.")]
        [SerializeField] private Transform localPlayer;
        [Tooltip("Local camera transform (aim for the remote physgun).")]
        [SerializeField] private Transform localAim;

        [Header("Server rules")]
        [SerializeField] private bool removeEntitiesOnDisconnect = true;

        private NetworkManager _network;
        private NetworkSessionService _session;
        private NetworkPlayerAvatar _localAvatar;
        private readonly List<NetworkPlayerAvatar> _players = new List<NetworkPlayerAvatar>(8);

        /// <summary>A player joined / left, or a nickname / auth id arrived.</summary>
        public event Action PlayersChanged;

        public int PlayerCount => _players.Count;
        public NetworkPlayerAvatar GetPlayer(int index) => _players[index];
        public bool IsHost => _network != null && _network.IsServer;
        public string SessionCode => _session != null ? _session.Code : string.Empty;
        private bool _valid;
        private bool _subscribed;
        private bool _sceneClosing;

        public SandboxActions Actions => actions;
        public EntityRegistry Registry => registry;
        public PhysicsGun LocalPhysgun => localPhysgun;
        public Transform LocalPlayer => localPlayer;
        public Transform LocalAim => localAim;
        public PropThruster ThrusterPrefab => actions.ThrusterPrefab;
        public bool IsShuttingDownScene => _sceneClosing;

        private void Awake()
        {
            string missing = string.Empty;
            if (actions == null) missing += " Actions";
            if (registry == null) missing += " Registry";
            if (physicsGuard == null) missing += " PhysicsGuard";
            if (localPhysgun == null) missing += " LocalPhysgun";
            if (localPlayer == null) missing += " LocalPlayer";
            if (localAim == null) missing += " LocalAim";
            if (missing.Length > 0)
            {
                Debug.LogError($"[{nameof(NetworkGameplay)}] '{name}': required references not assigned:{missing}. Component disabled.", this);
                enabled = false;
                return;
            }
            _network = GetComponent<NetworkManager>();
            _session = GetComponent<NetworkSessionService>(); // optional: kick also removes the player from the session
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            _network.OnServerStarted += OnServerStarted;
            _network.OnClientStarted += OnClientStarted;
            _network.OnClientStopped += OnClientStopped;
            _network.OnClientDisconnectCallback += OnClientDisconnected;
            registry.EntityAdded += OnEntityAdded;
            registry.EntityRemoving += OnEntityRemoving;
            BlockList.Changed += ApplyBlocks;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (_network != null)
            {
                _network.OnServerStarted -= OnServerStarted;
                _network.OnClientStarted -= OnClientStarted;
                _network.OnClientStopped -= OnClientStopped;
                _network.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            if (registry != null)
            {
                registry.EntityAdded -= OnEntityAdded;
                registry.EntityRemoving -= OnEntityRemoving;
            }
            BlockList.Changed -= ApplyBlocks;
            _subscribed = false;
        }

        private void OnApplicationQuit() => _sceneClosing = true;
        private void OnDestroy() => _sceneClosing = true;

        // ------------------------------------------------------------------ Session lifecycle

        private void OnServerStarted()
        {
            actions.SetLocalOwnerId((int)NetworkManager.ServerClientId);
            actions.SetTransport(null);
            localPhysgun.Remote = null;
            physicsGuard.enabled = true;

            // Props spawned while offline become network objects too.
            for (int i = 0; i < registry.EntityCount; i++)
            {
                SandboxEntity e = registry.GetEntityAt(i);
                if (e != null && e.Pool != null && e.Replication is NetworkEntity ne && !ne.IsSpawned)
                    ne.NetworkObject.Spawn(true);
            }
        }

        private void OnClientStarted()
        {
            if (_network.IsServer) return; // host is handled by OnServerStarted

            localPhysgun.Drop();
            registry.ClearLocalWorldForClient(); // host world arrives as mirrors
            physicsGuard.enabled = false;        // no physics authority on a client
        }

        private void OnClientStopped(bool wasHost)
        {
            _localAvatar = null;
            _players.Clear();
            ApplyBlocks(); // unhide everything
            PlayersChanged?.Invoke();
            actions.SetTransport(null);
            actions.SetLocalOwnerId((int)NetworkManager.ServerClientId);
            localPhysgun.Remote = null;
            physicsGuard.enabled = true;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!_network.IsServer || clientId == NetworkManager.ServerClientId) return;
            actions.Bindings.ClearOwner((int)clientId); // no stuck throttle / motor after a disconnect
            if (removeEntitiesOnDisconnect) actions.ExecuteRemoveAllOwned((int)clientId, out _);
        }

        /// <summary>Called by the local player's avatar when it spawns.</summary>
        public void BindLocalAvatar(NetworkPlayerAvatar avatar)
        {
            _localAvatar = avatar;
            actions.SetLocalOwnerId((int)avatar.OwnerClientId);
            if (_network.IsServer) return; // host executes locally

            actions.SetTransport(avatar);
            localPhysgun.Remote = avatar;
        }

        public void UnbindLocalAvatar(NetworkPlayerAvatar avatar)
        {
            if (_localAvatar != avatar) return;
            _localAvatar = null;
            actions.SetTransport(null);
            localPhysgun.Remote = null;
        }

        // ------------------------------------------------------------------ Players (all peers)

        public void RegisterPlayer(NetworkPlayerAvatar avatar)
        {
            if (avatar == null || _players.Contains(avatar)) return;
            _players.Add(avatar);
            NotifyPlayersChanged();
        }

        public void UnregisterPlayer(NetworkPlayerAvatar avatar)
        {
            if (_players.Remove(avatar)) NotifyPlayersChanged();
        }

        public void NotifyPlayersChanged()
        {
            ApplyBlocks();
            PlayersChanged?.Invoke();
        }

        /// <summary>Player with this sandbox owner id (= client id), or null.</summary>
        public NetworkPlayerAvatar FindPlayer(int ownerId)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkPlayerAvatar p = _players[i];
                if (p != null && p.PlayerOwnerId == ownerId) return p;
            }
            return null;
        }

        /// <summary>
        /// Host only: removes the player from the session (relay) and disconnects them. Their entities are removed by
        /// the disconnect rule.
        /// </summary>
        public void Kick(NetworkPlayerAvatar avatar)
        {
            if (avatar == null || !_network.IsServer || avatar.OwnerClientId == NetworkManager.ServerClientId) return;
            if (_session != null) _session.RemovePlayer(avatar.AuthId);
            _network.DisconnectClient(avatar.OwnerClientId, "Хост исключил вас из игры");
        }

        /// <summary>
        /// Local view of blocks: a blocked player's body / name and the entities they own are hidden for us.
        /// Runs on block changes and player list changes (not per frame).
        /// </summary>
        private void ApplyBlocks()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkPlayerAvatar p = _players[i];
                if (p != null && !p.IsLocalPlayer) p.SetHiddenByBlock(BlockList.IsBlocked(p.AuthId));
            }
            for (int i = 0; i < registry.EntityCount; i++)
            {
                SandboxEntity e = registry.GetEntityAt(i);
                if (e != null) e.SetHidden(IsOwnerBlocked(e.OwnerId));
            }
        }

        private bool IsOwnerBlocked(int ownerId)
        {
            if (ownerId < 0 || _players.Count == 0) return false;
            NetworkPlayerAvatar p = FindPlayer(ownerId);
            return p != null && !p.IsLocalPlayer && BlockList.IsBlocked(p.AuthId);
        }

        // ------------------------------------------------------------------ Entity replication (host)

        private void OnEntityAdded(SandboxEntity entity)
        {
            if (IsOwnerBlocked(entity.OwnerId)) entity.SetHidden(true);
            else if (entity.IsHidden) entity.SetHidden(false); // pooled instance hidden in a previous life

            if (!_network.IsServer || !_network.IsListening) return;
            if (!(entity.Replication is NetworkEntity ne)) return;

            if (ne.IsSpawned) ne.PublishIdentity();                 // e.g. hand-placed prop registered after spawn
            else if (entity.Pool != null) ne.NetworkObject.Spawn(true); // pooled prop: OnNetworkSpawn publishes identity
        }

        private void OnEntityRemoving(SandboxEntity entity)
        {
            if (!_network.IsServer || !_network.IsListening) return;
            if (!(entity.Replication is NetworkEntity ne) || !ne.IsSpawned) return;

            // External Destroy / SetActive(false): the object is already going away and NGO cleans it up itself.
            if (!entity.gameObject.activeInHierarchy) return;

            ne.NetworkObject.Despawn(entity.Pool == null); // pooled instances stay alive for reuse
        }
    }
}
