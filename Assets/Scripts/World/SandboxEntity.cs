using System.Collections.Generic;
using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Identity of a world object (prop) on its Rigidbody root: stable Id, owner, type id, held state,
    /// plus everything attached to it (attachments such as thrusters, constraints such as welds).
    /// Ids come from EntityRegistry and are never reused; a pooled prop gets a new Id on every spawn,
    /// so Undo / network / save references to an old life simply stop resolving.
    /// Spawned props: PropSpawner adds this component and registers the entity.
    /// Hand-placed props: add this component, set Scene Registry + Scene Type Id; it registers itself in Start (owner = World).
    /// Removal must go through EntityRegistry.RemoveEntity; external Destroy / SetActive(false) is detected and cleaned up.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SandboxEntity : MonoBehaviour
    {
        public const int WorldOwner = -1;
        public const int NoHolder = int.MinValue;

        [Header("Hand-placed entities only (leave empty on prop prefabs)")]
        [SerializeField] private EntityRegistry sceneRegistry;
        [Tooltip("Catalog type id of this prop (e.g. 'crate'). Used by save/duplicator.")]
        [SerializeField] private string sceneTypeId = string.Empty;

        private Rigidbody _body;

        /// <summary>0 = not registered.</summary>
        public int Id { get; internal set; }
        public int OwnerId { get; internal set; } = WorldOwner;
        public string TypeId { get; internal set; } = string.Empty;
        public bool IsRegistered => Id != 0;

        /// <summary>Owner id of the player whose physgun holds this entity, or NoHolder.</summary>
        public int HeldBy { get; set; } = NoHolder;
        public bool IsHeld => HeldBy != NoHolder;

        /// <summary>Cached Rigidbody (GetComponent once; works while the object is still inactive in the pool).</summary>
        public Rigidbody Body
        {
            get
            {
                if (_body == null) _body = GetComponent<Rigidbody>();
                return _body;
            }
        }

        /// <summary>
        /// Entity owning a Rigidbody: the entity on the body itself or, for ragdoll bones, the entity referenced by
        /// its EntityPart. No allocations; meant for user actions (one call per tool use / grab).
        /// </summary>
        public static SandboxEntity FromBody(Rigidbody body)
        {
            if (body == null) return null;
            if (body.TryGetComponent(out SandboxEntity entity)) return entity;
            return body.TryGetComponent(out EntityPart part) ? part.Entity : null;
        }

        // ---- Extra bodies (ragdoll bones): cached once, reset by the spawner on every spawn
        private Rigidbody[] _partBodies;
        private Vector3[] _partLocalPositions;
        private Quaternion[] _partLocalRotations;

        /// <summary>Child Rigidbodies other than the root (ragdoll bones). Empty for ordinary props.</summary>
        public Rigidbody[] PartBodies
        {
            get
            {
                CacheParts();
                return _partBodies;
            }
        }

        /// <summary>Remembers child bodies and their prefab local poses (called while the instance is still pristine).</summary>
        public void CacheParts()
        {
            if (_partBodies != null) return;
            Rigidbody[] all = GetComponentsInChildren<Rigidbody>(true);
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].gameObject != gameObject) count++;
            }
            _partBodies = new Rigidbody[count];
            _partLocalPositions = new Vector3[count];
            _partLocalRotations = new Quaternion[count];
            int k = 0;
            for (int i = 0; i < all.Length; i++)
            {
                Rigidbody rb = all[i];
                if (rb.gameObject == gameObject) continue;
                _partBodies[k] = rb;
                _partLocalPositions[k] = rb.transform.localPosition;
                _partLocalRotations[k] = rb.transform.localRotation;
                k++;
            }
        }

        /// <summary>
        /// Puts every child body back into its prefab pose relative to the root, unfrozen and at rest
        /// (after the root pose was set). Clients (mirror) keep the bodies kinematic.
        /// </summary>
        public void ResetParts(bool kinematic)
        {
            CacheParts();
            for (int i = 0; i < _partBodies.Length; i++)
            {
                Rigidbody rb = _partBodies[i];
                if (rb == null) continue;
                Transform t = rb.transform;
                t.SetLocalPositionAndRotation(_partLocalPositions[i], _partLocalRotations[i]);
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = kinematic;
                rb.position = t.position;
                rb.rotation = t.rotation;
                if (!kinematic) rb.WakeUp();
            }
        }

        /// <summary>
        /// Makes every child body kinematic (client copies: moved through their transforms by NetworkTransform, so
        /// Rigidbody interpolation is turned off) or dynamic and interpolated, without touching poses.
        /// </summary>
        public void SetPartsKinematic(bool kinematic)
        {
            CacheParts();
            for (int i = 0; i < _partBodies.Length; i++)
            {
                Rigidbody rb = _partBodies[i];
                if (rb == null) continue;
                rb.isKinematic = kinematic;
                rb.interpolation = kinematic ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            }
        }

        /// <summary>Network state component (NetworkEntity) if the prop is networked; null offline-only props.</summary>
        public IEntityReplication Replication { get; set; }

        /// <summary>True on clients: a read-only copy of the host's entity (no physics authority, no pool).</summary>
        public bool IsMirror { get; internal set; }

        // ---- Spawner / pool bookkeeping
        internal IEntityPool Pool;        // the spawner pool that owns this instance (null for hand-placed)
        internal int TypeIndex = -1;
        internal bool Pooled;

        // ---- Registry bookkeeping
        internal EntityRegistry Registry;
        internal bool RemovalInProgress;
        internal readonly List<IEntityAttachment> Attachments = new List<IEntityAttachment>(2);
        internal readonly List<SandboxConstraint> Constraints = new List<SandboxConstraint>(4);

        public int AttachmentCount => Attachments.Count;
        public int ConstraintCount => Constraints.Count;
        public IEntityAttachment GetAttachment(int index) => Attachments[index];
        public SandboxConstraint GetConstraint(int index) => Constraints[index];

        // ---- Paint (palette index, -1 = original prefab materials)
        private Renderer[] _renderers;
        private Material[] _originalMaterials;

        /// <summary>Palette index of the current paint, -1 = original materials.</summary>
        public int ColorIndex { get; private set; } = -1;

        /// <summary>
        /// Remembers the prop's own renderers and materials. Called before anything is attached (pool creation,
        /// Start of hand-placed props, network spawn), so thruster renderers are never included.
        /// </summary>
        public void CacheRenderers()
        {
            if (_renderers != null) return;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _originalMaterials = new Material[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++) _originalMaterials[i] = _renderers[i].sharedMaterial;
        }

        /// <summary>
        /// Applies a palette material (shared material: keeps SRP batching) or restores the originals (index -1 / null palette).
        /// </summary>
        public void ApplyColor(int index, Material[] palette)
        {
            CacheRenderers();
            bool restore = index < 0 || palette == null || index >= palette.Length || palette[index] == null;
            ColorIndex = restore ? -1 : index;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                r.sharedMaterial = restore ? _originalMaterials[i] : palette[index];
            }
            if (Replication != null) Replication.OnColorChanged();
        }

        private void Start()
        {
            CacheRenderers();
            if (!IsRegistered && Pool == null && sceneRegistry != null)
                sceneRegistry.RegisterEntity(this, WorldOwner, sceneTypeId);
        }

        private void OnDisable()
        {
            if (IsRegistered && !RemovalInProgress && Registry != null) Registry.HandleExternalRemoval(this, false);
        }

        private void OnDestroy()
        {
            if (IsRegistered && !RemovalInProgress && Registry != null) Registry.HandleExternalRemoval(this, true);
        }
    }
}
