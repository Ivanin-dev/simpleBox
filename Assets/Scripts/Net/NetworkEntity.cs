using System.Collections.Generic;
using SimpleBox.Gameplay;
using SimpleBox.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SimpleBox.Net
{
    /// <summary>
    /// Network state of a prop (on prop prefabs and hand-placed props, next to NetworkObject / NetworkTransform /
    /// NetworkRigidbody). Host: publishes entity id, owner, type id and the thruster list.
    /// Clients: register a mirror entity in EntityRegistry (so tools can target it by the host's id) and show
    /// visual-only thrusters. Physics, joints and forces exist only on the host; NetworkRigidbody keeps client
    /// bodies kinematic and NetworkTransform moves them.
    /// Offline the component is inert (not spawned).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SandboxEntity))]
    public sealed class NetworkEntity : NetworkBehaviour, IEntityReplication
    {
        private readonly NetworkVariable<int> _entityId = new NetworkVariable<int>();
        private readonly NetworkVariable<int> _ownerId = new NetworkVariable<int>(SandboxEntity.WorldOwner);
        private readonly NetworkVariable<FixedString32Bytes> _typeId = new NetworkVariable<FixedString32Bytes>();
        private readonly NetworkVariable<int> _color = new NetworkVariable<int>(-1);
        private NetworkList<ThrusterVisualState> _thrusters;

        private SandboxEntity _entity;
        private NetworkGameplay _gameplay;
        private readonly List<PropThruster> _visuals = new List<PropThruster>(2);
        private bool _listSubscribed;

        public bool IsNetworkSpawned => IsSpawned;
        public SandboxEntity Entity => _entity;

        private void Awake()
        {
            _entity = GetComponent<SandboxEntity>();
            _entity.Replication = this;
            _entity.CacheRenderers();
            _thrusters = new NetworkList<ThrusterVisualState>();

            // Props are never parented under other NetworkObjects, but the pool moves them between the pool root and
            // the scene root. With parent sync on, NGO reverts every reparent while offline / not spawned, so recycled
            // props stayed visible after Undo / Clear / Remover. Parent sync is not needed for props.
            NetworkObject networkObject = GetComponent<NetworkObject>();
            if (networkObject != null) networkObject.AutoObjectParentSync = false;
        }

        public override void OnNetworkSpawn()
        {
            _gameplay = NetworkManager != null ? NetworkManager.GetComponent<NetworkGameplay>() : null;
            if (_gameplay == null)
            {
                Debug.LogError($"[{nameof(NetworkEntity)}] NetworkGameplay not found on the NetworkManager object.", this);
                return;
            }

            if (IsServer)
            {
                PublishIdentity();
                OnAttachmentsChanged();
                return;
            }

            _gameplay.Registry.RegisterMirror(_entity, _entityId.Value, _ownerId.Value, _typeId.Value.ToString());
            _thrusters.OnListChanged += OnThrustersChanged;
            _color.OnValueChanged += OnColorValueChanged;
            ApplyReplicatedColor(_color.Value);
            _listSubscribed = true;
            RebuildVisuals();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer || _gameplay == null) return;

            if (_listSubscribed)
            {
                _thrusters.OnListChanged -= OnThrustersChanged;
                _color.OnValueChanged -= OnColorValueChanged;
                _listSubscribed = false;
            }
            ClearVisuals();

            string typeId = _entity.TypeId;
            _gameplay.Registry.UnregisterMirror(_entity);

            // A hand-placed prop stays in the scene after leaving the session: it becomes a local world prop again.
            if (NetworkObject.InScenePlaced && gameObject.scene.isLoaded && !_gameplay.IsShuttingDownScene)
                _gameplay.Registry.RegisterEntity(_entity, SandboxEntity.WorldOwner, typeId);
        }

        /// <summary>Host: writes id / owner / type (also after the entity is re-registered with a new id).</summary>
        public void PublishIdentity()
        {
            if (!IsServer || !IsSpawned) return;
            _entityId.Value = _entity.Id;
            _ownerId.Value = _entity.OwnerId;
            _typeId.Value = new FixedString32Bytes(_entity.TypeId ?? string.Empty);
            _color.Value = _entity.ColorIndex;
        }

        /// <summary>Host: rebuilds the replicated thruster list from the entity's attachments.</summary>
        public void OnAttachmentsChanged()
        {
            if (!IsSpawned || !IsServer) return;

            _thrusters.Clear();
            for (int i = 0; i < _entity.AttachmentCount; i++)
            {
                if (!(_entity.GetAttachment(i) is PropThruster thruster) || thruster == null || !thruster.gameObject.activeSelf)
                    continue;

                Transform t = thruster.transform;
                _thrusters.Add(new ThrusterVisualState
                {
                    LocalPosition = t.localPosition,
                    LocalRotation = t.localRotation,
                    On = thruster.IsFiring
                });
            }
        }

        /// <summary>Host: publishes the paint index.</summary>
        public void OnColorChanged()
        {
            if (!IsSpawned || !IsServer) return;
            _color.Value = _entity.ColorIndex;
        }

        private void OnColorValueChanged(int previous, int current) => ApplyReplicatedColor(current);

        private void ApplyReplicatedColor(int index)
        {
            if (_gameplay == null || _entity.ColorIndex == index) return;
            _entity.ApplyColor(index, _gameplay.Actions.ColorPalette);
        }

        // ------------------------------------------------------------------ Client visuals

        private void OnThrustersChanged(NetworkListEvent<ThrusterVisualState> change) => RebuildVisuals();

        private void RebuildVisuals()
        {
            PropThruster prefab = _gameplay.ThrusterPrefab;
            if (prefab == null) return;

            int count = _thrusters.Count;
            while (_visuals.Count > count)
            {
                int last = _visuals.Count - 1;
                if (_visuals[last] != null) Destroy(_visuals[last].gameObject);
                _visuals.RemoveAt(last);
            }
            while (_visuals.Count < count)
            {
                PropThruster visual = Instantiate(prefab, transform);
                CompensateParentScale(visual.transform, transform);
                _visuals.Add(visual);
            }

            for (int i = 0; i < count; i++)
            {
                ThrusterVisualState s = _thrusters[i];
                PropThruster visual = _visuals[i];
                if (visual == null) continue;
                visual.transform.SetLocalPositionAndRotation(s.LocalPosition, s.LocalRotation);
                visual.SetVisualOnly(s.On);
            }
        }

        private void ClearVisuals()
        {
            for (int i = 0; i < _visuals.Count; i++)
            {
                if (_visuals[i] != null) Destroy(_visuals[i].gameObject);
            }
            _visuals.Clear();
        }

        private static void CompensateParentScale(Transform child, Transform parent)
        {
            Vector3 s = parent.lossyScale;
            child.localScale = new Vector3(
                Mathf.Abs(s.x) > 1e-4f ? 1f / s.x : 1f,
                Mathf.Abs(s.y) > 1e-4f ? 1f / s.y : 1f,
                Mathf.Abs(s.z) > 1e-4f ? 1f / s.z : 1f);
        }

        public override void OnDestroy()
        {
            ClearVisuals();
            base.OnDestroy(); // NetworkBehaviour disposes its NetworkVariables / NetworkList
        }
    }
}
