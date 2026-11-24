using System;
using SimpleBox.World;
using Unity.Netcode;
using UnityEngine;

namespace SimpleBox.Net
{
    /// <summary>Replicated description of one rope / elastic (clients draw it; joints exist only on the host).</summary>
    public struct CableState : INetworkSerializable, IEquatable<CableState>
    {
        public int Id;
        public int AId;
        public int BId;
        public Vector3 AnchorA;
        public Vector3 AnchorB;
        public byte Kind;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref AId);
            serializer.SerializeValue(ref BId);
            serializer.SerializeValue(ref AnchorA);
            serializer.SerializeValue(ref AnchorB);
            serializer.SerializeValue(ref Kind);
        }

        public bool Equals(CableState other) => Id == other.Id && AId == other.AId && BId == other.BId &&
                                                 AnchorA == other.AnchorA && AnchorB == other.AnchorB && Kind == other.Kind;

        public override bool Equals(object obj) => obj is CableState other && Equals(other);
        public override int GetHashCode() => Id;
    }

    /// <summary>
    /// Scene-wide replicated world state (in-scene NetworkObject): currently the list of cables.
    /// Host: mirrors EntityRegistry cable constraints into a NetworkList. Clients: rebuild CableVisuals on change.
    /// Late joiners get the full list automatically.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkWorldState : NetworkBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private EntityRegistry registry;
        [SerializeField] private CableVisuals cableVisuals;

        [Header("References (optional)")]
        [Tooltip("Host: explosions to forward to clients.")]
        [SerializeField] private ExplosionSystem explosions;
        [Tooltip("Clients: plays forwarded explosions.")]
        [SerializeField] private SimpleBox.Gameplay.ExplosionFx explosionFx;

        private NetworkList<CableState> _cables;
        private bool _registrySubscribed;
        private bool _listSubscribed;

        private void Awake()
        {
            _cables = new NetworkList<CableState>();
            NetworkObject no = GetComponent<NetworkObject>();
            if (no != null) no.AutoObjectParentSync = false;
            if (registry == null || cableVisuals == null)
                Debug.LogError($"[{nameof(NetworkWorldState)}] '{name}': Registry / Cable Visuals not assigned.", this);
        }

        public override void OnNetworkSpawn()
        {
            if (registry == null || cableVisuals == null) return;

            if (IsServer)
            {
                _cables.Clear();
                for (int i = 0; i < registry.ConstraintCount; i++) Publish(registry.GetConstraintAt(i));
                registry.ConstraintAdded += Publish;
                registry.ConstraintRemoved += Unpublish;
                if (explosions != null) explosions.Exploded += ForwardExplosion;
                _registrySubscribed = true;
                return;
            }

            _cables.OnListChanged += OnCablesChanged;
            _listSubscribed = true;
            RebuildClientVisuals();
        }

        public override void OnNetworkDespawn()
        {
            if (_registrySubscribed && registry != null)
            {
                registry.ConstraintAdded -= Publish;
                registry.ConstraintRemoved -= Unpublish;
                if (explosions != null) explosions.Exploded -= ForwardExplosion;
            }
            _registrySubscribed = false;

            if (_listSubscribed)
            {
                _cables.OnListChanged -= OnCablesChanged;
                _listSubscribed = false;
                if (cableVisuals != null) cableVisuals.ClearAll();
            }
        }

        private void Publish(SandboxConstraint c)
        {
            if (!c.IsCable || c.A == null) return;
            _cables.Add(new CableState
            {
                Id = c.Id,
                AId = c.A.Id,
                BId = c.B != null ? c.B.Id : 0,
                AnchorA = c.AnchorA,
                AnchorB = c.AnchorB,
                Kind = (byte)c.Kind
            });
        }

        private void Unpublish(SandboxConstraint c)
        {
            if (!c.IsCable) return;
            for (int i = 0; i < _cables.Count; i++)
            {
                if (_cables[i].Id != c.Id) continue;
                _cables.RemoveAt(i);
                return;
            }
        }

        private void ForwardExplosion(Vector3 center, float radius) => ExplosionRpc(center, radius);

        /// <summary>Clients: effect, sound and local knockback (the host already played them).</summary>
        [Rpc(SendTo.NotServer)]
        private void ExplosionRpc(Vector3 center, float radius)
        {
            if (explosionFx != null) explosionFx.Play(center, radius);
        }

        private void OnCablesChanged(NetworkListEvent<CableState> change) => RebuildClientVisuals();

        private void RebuildClientVisuals()
        {
            cableVisuals.ClearAll();
            for (int i = 0; i < _cables.Count; i++)
            {
                CableState s = _cables[i];
                cableVisuals.Add(s.Id, s.AId, s.BId, s.AnchorA, s.AnchorB, (ConstraintKind)s.Kind);
            }
        }
    }
}
