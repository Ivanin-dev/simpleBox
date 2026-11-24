using System;
using System.Collections.Generic;
using SimpleBox.Controls;
using SimpleBox.Core;
using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay
{
    /// <summary>
    /// Prop selection, spawn placement and the per-type instance pool.
    /// Request side (local player): Update reads input, computes a free pose in front of the camera and calls
    /// SandboxActions.SpawnProp. Authority side: SpawnAt re-checks the pose, enforces limits (per owner: recycle the
    /// owner's oldest unheld entity; session: refuse), takes an instance from the pool and registers it in EntityRegistry.
    /// Removal is always done by EntityRegistry, which hands the instance back through IEntityPool.Recycle.
    /// Placement: BoxCast + Raycast along the aim (then flat forward), optional lift, final CheckBox against
    /// Environment + Props + Player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PropSpawner : MonoBehaviour, IEntityPool
    {
        [Header("References (required)")]
        [SerializeField] private MobileInputController input;
        [Tooltip("Camera transform. Spawn direction = its forward.")]
        [SerializeField] private Transform aimOrigin;
        [SerializeField] private PropCatalog catalog;
        [SerializeField] private SandboxActions actions;
        [SerializeField] private EntityRegistry registry;
        [SerializeField] private QualityManager quality;

        [Header("Request")]
        [SerializeField, Min(0f)] private float spawnCooldown = 0.2f;

        [Header("Placement")]
        [Tooltip("Distance from the camera to the prop's bounds center when nothing is in the way.")]
        [SerializeField, Min(0.5f)] private float spawnDistance = 3f;
        [Tooltip("Placements closer than this to the camera are rejected (prop would be in the player's face).")]
        [SerializeField, Min(0f)] private float minSpawnDistance = 1.2f;
        [Tooltip("Gap left between the prop and the surface it was swept against.")]
        [SerializeField, Min(0f)] private float surfaceSkin = 0.05f;
        [Tooltip("Extra margin added to the overlap test box on the request side.")]
        [SerializeField, Min(0f)] private float overlapPadding = 0.02f;
        [Tooltip("Layers the placement sweep stops at: Default/Environment + Props. Must NOT contain Player.")]
        [SerializeField] private LayerMask placementMask;
        [Tooltip("Layers that make a spot occupied: Default/Environment + Props + Player.")]
        [SerializeField] private LayerMask overlapMask;
        [Tooltip("Layer(s) prop prefabs must be on to be grabbable (validation warning only).")]
        [SerializeField] private LayerMask propLayers;

        private static readonly float[] LiftSteps = { 0f, 0.3f, 0.6f, 1.0f };

        private sealed class RuntimeInfo
        {
            public bool Valid;
            public Vector3 Center;          // bounds center relative to prefab pivot (prefab-local axes, root scale applied)
            public Vector3 HalfExtents;
            public Quaternion BaseRotation; // prefab root rotation
            public bool UseGravity;
            public CollisionDetectionMode CollisionMode;
            public Stack<SandboxEntity> Pool;
        }

        private RuntimeInfo[] _info;
        private Dictionary<string, int> _typeIndexById;
        private Transform _poolRoot;
        private int _selected = -1;
        private float _nextRequestTime;
        private bool _valid;
        private bool _subscribed;

        // Gizmo debug of the last placement attempt.
        private Vector3 _lastCheckCenter;
        private Quaternion _lastCheckRotation = Quaternion.identity;
        private Vector3 _lastCheckHalfExtents;
        private bool _lastCheckOk;
        private bool _hasLastCheck;

        /// <summary>Arg: new selected index.</summary>
        public event Action<int> SelectionChanged;
        /// <summary>Args: entities owned by the local player, per-owner max.</summary>
        public event Action<int, int> ActiveCountChanged;
        /// <summary>Local spawn request failed.</summary>
        public event Action<ActionResult> SpawnFailed;

        public bool IsValid => _valid;
        public int MaxActiveProps => _valid ? quality.Profile.maxEntitiesPerOwner : 0;
        public int ActiveCount => _valid ? registry.CountOwnedBy(actions.LocalOwnerId) : 0;
        public int SelectedIndex => _selected;
        public PropCatalog Catalog => catalog;
        public PropDefinition SelectedDefinition => _valid && _selected >= 0 ? catalog.Get(_selected) : null;

        // ------------------------------------------------------------------ Lifecycle

        private void Awake()
        {
            string missing = string.Empty;
            if (input == null) missing += " Input";
            if (aimOrigin == null) missing += " AimOrigin";
            if (catalog == null) missing += " Catalog";
            if (actions == null) missing += " Actions";
            if (registry == null) missing += " Registry";
            if (quality == null) missing += " Quality";
            if (missing.Length > 0)
            {
                Fail($"required references not assigned:{missing}.");
                return;
            }
            if (!input.HasPropWidgets)
            {
                Fail("MobileInputController has no Spawn/NextProp buttons assigned.");
                return;
            }
            if (quality.Profile == null)
            {
                Fail("Quality Manager has no active profile.");
                return;
            }
            if (placementMask.value == 0 || overlapMask.value == 0)
            {
                Fail("Placement Mask / Overlap Mask is empty.");
                return;
            }
            if ((placementMask.value & (1 << aimOrigin.root.gameObject.layer)) != 0)
            {
                Debug.LogWarning($"[{nameof(PropSpawner)}] Placement Mask contains the player's layer: the sweep will hit the player capsule.", this);
            }
            if ((overlapMask.value & (1 << aimOrigin.root.gameObject.layer)) == 0)
            {
                Debug.LogWarning($"[{nameof(PropSpawner)}] Overlap Mask does not contain the player's layer: props may spawn inside the player.", this);
            }
            if (catalog.Count == 0)
            {
                Fail("Catalog is empty.");
                return;
            }

            int perTypePool = quality.Profile.maxEntitiesPerOwner;
            _info = new RuntimeInfo[catalog.Count];
            _typeIndexById = new Dictionary<string, int>(catalog.Count);
            int validCount = 0;
            for (int i = 0; i < _info.Length; i++)
            {
                _info[i] = BuildInfo(i, perTypePool);
                if (_info[i].Valid) validCount++;
            }
            if (validCount == 0)
            {
                Fail("Catalog has no valid entries (see errors above).");
                return;
            }

            // Active props live at the scene root: a NetworkObject may not be spawned under a plain parent.
            var poolGo = new GameObject("[PropPool]");
            poolGo.SetActive(false); // children are inactive while pooled
            _poolRoot = poolGo.transform;

            _selected = FindNextValid(-1, 1);
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            registry.OwnerCountChanged += OnOwnerCountChanged;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (registry != null) registry.OwnerCountChanged -= OnOwnerCountChanged;
            _subscribed = false;
        }

        private void Start()
        {
            if (!_valid) return;

            for (int i = 0; i < _info.Length; i++)
            {
                if (!_info[i].Valid) continue;
                int count = catalog.Get(i).prewarmCount;
                for (int k = 0; k < count; k++) _info[i].Pool.Push(CreateInstance(i));
            }

            SelectionChanged?.Invoke(_selected);
            ActiveCountChanged?.Invoke(ActiveCount, MaxActiveProps);
        }

        private void OnDestroy()
        {

            if (_poolRoot != null) Destroy(_poolRoot.gameObject);
        }

        private void Update()
        {
            int cycle = input.ConsumePropCycle();
            if (cycle != 0) CycleSelection(cycle);

            int direct = input.ConsumePropSelect();
            if (direct >= 0) SelectProp(direct);

            if (input.ConsumeSpawn()) RequestSpawn();
        }

        private void OnOwnerCountChanged(int ownerId, int count)
        {
            if (ownerId == actions.LocalOwnerId) ActiveCountChanged?.Invoke(count, MaxActiveProps);
        }

        private void Fail(string reason)
        {
            Debug.LogError($"[{nameof(PropSpawner)}] '{name}': {reason} Component disabled.", this);
            _valid = false;
            enabled = false;
        }

        // ------------------------------------------------------------------ Type ids

        public bool TryGetTypeIndex(string typeId, out int index)
        {
            index = -1;
            return _valid && typeId != null && _typeIndexById.TryGetValue(typeId, out index);
        }

        public string GetTypeId(int index) => catalog.Get(index).id;

        /// <summary>Spawn box of a type (prefab-local center and half extents, root scale applied).</summary>
        public bool TryGetBounds(int typeIndex, out Vector3 center, out Vector3 halfExtents)
        {
            center = default;
            halfExtents = default;
            if (!_valid || typeIndex < 0 || typeIndex >= _info.Length || !_info[typeIndex].Valid) return false;
            center = _info[typeIndex].Center;
            halfExtents = _info[typeIndex].HalfExtents;
            return true;
        }

        /// <summary>True if the type fits at this pose (nothing on the overlap layers inside its spawn box).</summary>
        public bool IsSpaceFree(int typeIndex, Vector3 position, Quaternion rotation)
        {
            if (!TryGetBounds(typeIndex, out Vector3 center, out Vector3 he)) return false;
            return !Physics.CheckBox(position + rotation * center, he, rotation, overlapMask, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------ Selection

        public bool SelectProp(int index)
        {
            if (!_valid || index < 0 || index >= _info.Length || !_info[index].Valid) return false;
            if (index == _selected) return true;
            _selected = index;
            SelectionChanged?.Invoke(_selected);
            return true;
        }

        public void NextProp() => CycleSelection(1);
        public void PreviousProp() => CycleSelection(-1);

        private void CycleSelection(int direction)
        {
            if (!_valid) return;
            int next = FindNextValid(_selected, direction > 0 ? 1 : -1);
            if (next >= 0) SelectProp(next);
        }

        private int FindNextValid(int from, int direction)
        {
            int count = _info.Length;
            for (int step = 1; step <= count; step++)
            {
                int i = ((from + direction * step) % count + count) % count;
                if (_info[i].Valid) return i;
            }
            return -1;
        }

        // ------------------------------------------------------------------ Request (local player)

        /// <summary>Computes a free pose for the selected prop and sends the spawn request.</summary>
        public ActionResult RequestSpawn()
        {
            if (!_valid || _selected < 0) return ActionResult.Unavailable;

            float now = Time.time;
            if (now < _nextRequestTime) return ActionResult.Invalid;
            _nextRequestTime = now + spawnCooldown;

            ActionResult result = TryFindSpawnPose(_info[_selected], out Vector3 position, out Quaternion rotation)
                ? actions.SpawnProp(_selected, position, rotation)
                : ActionResult.NoSpace;

            if (result != ActionResult.Ok && result != ActionResult.Pending) SpawnFailed?.Invoke(result);
            return result;
        }

        // ------------------------------------------------------------------ Authority

        /// <summary>Called by SandboxActions on the authority. Validates the pose and limits, spawns and registers.</summary>
        public ActionResult SpawnAt(int typeIndex, Vector3 position, Quaternion rotation, int ownerId, out SandboxEntity entity,
            bool skipOverlapCheck = false)
        {
            entity = null;
            if (!_valid) return ActionResult.Unavailable;
            if (typeIndex < 0 || typeIndex >= _info.Length || !_info[typeIndex].Valid) return ActionResult.Invalid;

            RuntimeInfo info = _info[typeIndex];

            // Re-check the requested pose (the requester may be a remote client).
            Vector3 boundsCenter = position + rotation * info.Center;
            if (!skipOverlapCheck && Physics.CheckBox(boundsCenter, info.HalfExtents, rotation, overlapMask, QueryTriggerInteraction.Ignore))
                return ActionResult.NoSpace;

            QualityProfile p = quality.Profile;

            PropDefinition def = catalog.Get(typeIndex);
            if (def.maxPerOwner > 0 && registry.CountOwnedBy(ownerId, def.id) >= def.maxPerOwner)
            {
                SandboxEntity oldestOfType = registry.FindOldestRemovable(ownerId, def.id);
                if (oldestOfType == null || !registry.RemoveEntity(oldestOfType)) return ActionResult.OwnerLimit;
            }

            if (registry.CountOwnedBy(ownerId) >= p.maxEntitiesPerOwner)
            {
                SandboxEntity oldest = registry.FindOldestRemovable(ownerId);
                if (oldest == null || !registry.RemoveEntity(oldest)) return ActionResult.OwnerLimit;
            }

            int playerEntities = registry.EntityCount - registry.CountOwnedBy(SandboxEntity.WorldOwner);
            if (playerEntities >= p.maxEntitiesTotal) return ActionResult.ServerLimit;

            entity = Acquire(typeIndex);
            Activate(entity, info, position, rotation);
            registry.RegisterEntity(entity, ownerId, def.id);
            return ActionResult.Ok;
        }

        // ------------------------------------------------------------------ Pool (IEntityPool)

        public void Recycle(SandboxEntity entity)
        {
            if (entity.TypeIndex < 0 || _info == null) return;
            entity.Pooled = true;
            entity.transform.SetParent(_poolRoot, false); // inactive parent deactivates the instance
            _info[entity.TypeIndex].Pool.Push(entity);
        }

        public void HandleExternalLoss(SandboxEntity entity, bool destroyed)
        {
            if (destroyed || entity.TypeIndex < 0 || _info == null) return;
            // Disabled by someone else: keep it; Activate() reparents and re-enables it.
            entity.Pooled = true;
            _info[entity.TypeIndex].Pool.Push(entity);
        }

        private SandboxEntity Acquire(int index)
        {
            Stack<SandboxEntity> pool = _info[index].Pool;
            while (pool.Count > 0)
            {
                SandboxEntity e = pool.Pop();
                if (e != null) return e; // skip instances destroyed while pooled
            }
            return CreateInstance(index);
        }

        private SandboxEntity CreateInstance(int index)
        {
            PropDefinition def = catalog.Get(index);
            GameObject go = Instantiate(def.prefab, _poolRoot); // inactive parent: Awake/OnEnable deferred
            go.name = def.prefab.name;

            SandboxEntity entity = go.GetComponent<SandboxEntity>();
            if (entity == null) entity = go.AddComponent<SandboxEntity>();
            entity.Pool = this;
            entity.CacheRenderers(); // before anything (thrusters) is attached
            entity.CacheParts();     // ragdoll bones: prefab local poses for the reset on every spawn
            entity.TypeIndex = index;
            entity.Pooled = true;
            return entity;
        }

        private void Activate(SandboxEntity entity, RuntimeInfo info, Vector3 position, Quaternion rotation)
        {
            Transform t = entity.transform;
            t.SetPositionAndRotation(position, rotation); // still under the inactive pool root
            entity.Pooled = false;
            t.SetParent(null, true);                      // becomes active here (scene root), already at the target pose

            GameObject go = entity.gameObject;
            if (!go.activeSelf) go.SetActive(true);      // was disabled externally before being pooled

            Rigidbody rb = entity.Body;
            rb.isKinematic = false;                       // clear freeze from a previous life
            if (entity.ColorIndex != -1) entity.ApplyColor(-1, null); // clear paint from a previous life
            rb.useGravity = info.UseGravity;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = info.CollisionMode;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = position;
            rb.rotation = rotation;
            rb.WakeUp();
            entity.ResetParts(false);                     // ragdoll bones back to the prefab pose, unfrozen, at rest
        }

        // ------------------------------------------------------------------ Placement

        private bool TryFindSpawnPose(RuntimeInfo info, out Vector3 position, out Quaternion rotation)
        {
            Vector3 origin = aimOrigin.position;
            Quaternion yaw = Quaternion.Euler(0f, aimOrigin.eulerAngles.y, 0f);
            rotation = yaw * info.BaseRotation;

            Vector3 he = info.HalfExtents;
            Vector3 checkHe = he;
            checkHe.x += overlapPadding;
            checkHe.y += overlapPadding;
            checkHe.z += overlapPadding;
            Vector3 centerOffset = rotation * info.Center;

            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 dir = pass == 0 ? aimOrigin.forward : yaw * Vector3.forward;
                float dist = spawnDistance;

                if (Physics.BoxCast(origin, he, dir, out RaycastHit boxHit, rotation, spawnDistance,
                        placementMask, QueryTriggerInteraction.Ignore))
                {
                    dist = boxHit.distance - surfaceSkin;
                }

                // BoxCast misses colliders the box already overlaps at the start (camera near a wall):
                // a center ray keeps the prop on the camera side of that wall.
                if (Physics.Raycast(origin, dir, out RaycastHit rayHit, spawnDistance,
                        placementMask, QueryTriggerInteraction.Ignore))
                {
                    float rayLimit = rayHit.distance - ProjectedExtent(dir, rotation, he) - surfaceSkin;
                    if (rayLimit < dist) dist = rayLimit;
                }

                if (dist < minSpawnDistance) continue;

                Vector3 boundsCenter = origin + dir * dist;

                for (int s = 0; s < LiftSteps.Length; s++)
                {
                    float lift = LiftSteps[s];
                    Vector3 c = boundsCenter;
                    if (lift > 0f)
                    {
                        if (Physics.BoxCast(boundsCenter, he, Vector3.up, rotation, lift + surfaceSkin,
                                placementMask, QueryTriggerInteraction.Ignore))
                        {
                            break; // do not lift through a ceiling
                        }
                        c.y += lift;
                    }

                    bool occupied = Physics.CheckBox(c, checkHe, rotation, overlapMask, QueryTriggerInteraction.Ignore);
                    RecordCheck(c, rotation, checkHe, !occupied);
                    if (!occupied)
                    {
                        position = c - centerOffset;
                        return true;
                    }
                }
            }

            position = default;
            return false;
        }

        private static float ProjectedExtent(Vector3 dir, Quaternion rotation, Vector3 he)
        {
            return Mathf.Abs(Vector3.Dot(dir, rotation * Vector3.right)) * he.x +
                   Mathf.Abs(Vector3.Dot(dir, rotation * Vector3.up)) * he.y +
                   Mathf.Abs(Vector3.Dot(dir, rotation * Vector3.forward)) * he.z;
        }

        private void RecordCheck(Vector3 center, Quaternion rotation, Vector3 halfExtents, bool ok)
        {
            _lastCheckCenter = center;
            _lastCheckRotation = rotation;
            _lastCheckHalfExtents = halfExtents;
            _lastCheckOk = ok;
            _hasLastCheck = true;
        }

        // ------------------------------------------------------------------ Validation / bounds (Awake only)

        private RuntimeInfo BuildInfo(int index, int poolCapacity)
        {
            var info = new RuntimeInfo();
            PropDefinition def = catalog.Get(index);
            string label = def != null && !string.IsNullOrEmpty(def.displayName) ? def.displayName : $"#{index}";

            if (def == null || def.prefab == null)
            {
                Debug.LogError($"[{nameof(PropSpawner)}] Catalog entry {label}: prefab not assigned. Entry skipped.", catalog);
                return info;
            }
            if (string.IsNullOrWhiteSpace(def.id))
            {
                Debug.LogError($"[{nameof(PropSpawner)}] '{label}': type id is empty. Entry skipped.", catalog);
                return info;
            }
            if (_typeIndexById.ContainsKey(def.id))
            {
                Debug.LogError($"[{nameof(PropSpawner)}] '{label}': duplicate type id '{def.id}'. Entry skipped.", catalog);
                return info;
            }

            GameObject prefab = def.prefab;
            Rigidbody rb = prefab.GetComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.LogError($"[{nameof(PropSpawner)}] '{label}': prefab root has no Rigidbody. Entry skipped.", prefab);
                return info;
            }
            if (prefab.GetComponentInChildren<MeshCollider>(true) != null)
            {
                Debug.LogError($"[{nameof(PropSpawner)}] '{label}': MeshCollider found. Use Box/Sphere/Capsule only. Entry skipped.", prefab);
                return info;
            }
            if (rb.isKinematic)
            {
                Debug.LogWarning($"[{nameof(PropSpawner)}] '{label}': prefab Rigidbody is kinematic; it will be spawned dynamic.", prefab);
            }
            if (rb.collisionDetectionMode == CollisionDetectionMode.Continuous ||
                rb.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic)
            {
                Debug.LogWarning($"[{nameof(PropSpawner)}] '{label}': use Discrete or ContinuousSpeculative " +
                                 "(Continuous/ContinuousDynamic warn when the prop is frozen and cost more on mobile).", prefab);
            }
            if ((propLayers.value & (1 << prefab.layer)) == 0)
            {
                Debug.LogWarning($"[{nameof(PropSpawner)}] '{label}': prefab layer '{LayerMask.LayerToName(prefab.layer)}' " +
                                 "is not in Prop Layers; tools will not target it.", prefab);
            }

            Vector3 center;
            Vector3 halfExtents;
            if (def.boundsMode == PropBoundsMode.Manual)
            {
                center = def.manualCenter;
                halfExtents = new Vector3(Mathf.Abs(def.manualHalfExtents.x), Mathf.Abs(def.manualHalfExtents.y), Mathf.Abs(def.manualHalfExtents.z));
            }
            else if (!TryComputeColliderBounds(prefab, out center, out halfExtents))
            {
                Debug.LogError($"[{nameof(PropSpawner)}] '{label}': no non-trigger Box/Sphere/Capsule colliders. Entry skipped.", prefab);
                return info;
            }

            if (halfExtents.x <= 0f || halfExtents.y <= 0f || halfExtents.z <= 0f)
            {
                Debug.LogError($"[{nameof(PropSpawner)}] '{label}': spawn bounds have zero size. Entry skipped.", prefab);
                return info;
            }

            _typeIndexById.Add(def.id, index);
            info.Center = center;
            info.HalfExtents = halfExtents;
            info.BaseRotation = prefab.transform.rotation;
            info.UseGravity = rb.useGravity;
            info.CollisionMode = rb.collisionDetectionMode;
            info.Pool = new Stack<SandboxEntity>(poolCapacity + def.prewarmCount);
            info.Valid = true;
            return info;
        }

        /// <summary>AABB of all non-trigger primitive colliders in prefab-root space (root rotation removed, root scale kept).</summary>
        private static bool TryComputeColliderBounds(GameObject prefab, out Vector3 center, out Vector3 halfExtents)
        {
            Transform root = prefab.transform;
            Matrix4x4 rootInv = Matrix4x4.TRS(root.position, root.rotation, Vector3.one).inverse;

            Collider[] colliders = prefab.GetComponentsInChildren<Collider>(true);
            bool any = false;
            var bounds = new Bounds();

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider col = colliders[i];
                if (col.isTrigger) continue;

                Vector3 localCenter;
                Vector3 localExtents;
                switch (col)
                {
                    case BoxCollider box:
                        localCenter = box.center;
                        localExtents = box.size * 0.5f;
                        break;
                    case SphereCollider sphere:
                        localCenter = sphere.center;
                        localExtents = Vector3.one * sphere.radius;
                        break;
                    case CapsuleCollider capsule:
                        localCenter = capsule.center;
                        localExtents = Vector3.one * capsule.radius;
                        localExtents[capsule.direction] = Mathf.Max(capsule.height * 0.5f, capsule.radius);
                        break;
                    default:
                        continue;
                }

                Matrix4x4 m = rootInv * col.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = localCenter + new Vector3(
                        (c & 1) == 0 ? -localExtents.x : localExtents.x,
                        (c & 2) == 0 ? -localExtents.y : localExtents.y,
                        (c & 4) == 0 ? -localExtents.z : localExtents.z);
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }

            center = bounds.center;
            halfExtents = bounds.extents;
            return any;
        }

        private void OnDrawGizmosSelected()
        {
            if (!_hasLastCheck) return;
            Gizmos.color = _lastCheckOk ? Color.green : Color.red;
            Gizmos.matrix = Matrix4x4.TRS(_lastCheckCenter, _lastCheckRotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, _lastCheckHalfExtents * 2f);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
