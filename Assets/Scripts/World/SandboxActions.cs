using System.Collections.Generic;
using SimpleBox.Core;
using SimpleBox.Gameplay;
using UnityEngine;

namespace SimpleBox.World
{
    public enum ActionResult
    {
        Ok = 0,
        Unavailable,     // system misconfigured / disabled
        Invalid,         // bad arguments (e.g. weld to itself)
        NotFound,        // entity id does not exist (removed, pooled, never existed)
        NotAllowed,      // ownership protection
        NoSpace,         // spawn pose overlaps something
        OwnerLimit,      // per-player limit and nothing could be recycled
        EntityLimit,     // per-entity limit (constraints on one prop)
        ServerLimit,     // session-wide limit
        AlreadyExists,   // e.g. the pair is already welded
        NothingToUndo,
        Pending          // multiplayer client: request sent to the host; failures arrive via RemoteResult
    }

    /// <summary>
    /// The only entry point that changes the world. Tools and UI never spawn / weld / remove directly:
    /// they call a request method (local player), which executes on the authority.
    /// Single player: requests execute immediately with LocalOwnerId.
    /// Multiplayer: on a client, request methods forward to IActionTransport (return Pending); the host runs the same
    /// Execute* methods with the sender's owner id. All arguments are plain data (ids, poses, settings) for that reason.
    /// Every successful "create" is pushed to the owner's UndoHistory.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-400)]
    public sealed partial class SandboxActions : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private EntityRegistry registry;
        [SerializeField] private PropSpawner spawner;
        [SerializeField] private ThrusterRegistry thrusters;
        [SerializeField] private QualityManager quality;
        [Tooltip("Prefab with PropThruster on the root and no colliders.")]
        [SerializeField] private PropThruster thrusterPrefab;
        [Tooltip("Per-owner held contraption bindings 1-4.")]
        [SerializeField] private InputBindings bindings;

        [Header("References (optional)")]
        [Tooltip("Needed for the detonator.")]
        [SerializeField] private ExplosionSystem explosions;

        [Header("Paint")]
        [Tooltip("Shared paint materials (index = color id in saves / network). Shared materials keep SRP batching.")]
        [SerializeField] private Material[] colorPalette = new Material[0];

        [Header("Elastic (per kg of the lighter body)")]
        [SerializeField, Min(0.1f)] private float elasticStiffness = 40f;
        [SerializeField, Min(0f)] private float elasticDamping = 2f;

        [Header("Hydraulic (per kg of the lighter body)")]
        [SerializeField, Min(1f)] private float hydraulicStiffness = 600f;
        [SerializeField, Min(0f)] private float hydraulicDamping = 40f;

        [Header("Permissions")]
        [Tooltip("Players may modify only their own entities and world props. No effect in single player (one owner).")]
        [SerializeField] private bool ownershipProtection = true;
        [Tooltip("Server rule: players may use noclip flight.")]
        [SerializeField] private bool allowNoclip = true;

        [Header("Undo")]
        [SerializeField, Min(1)] private int undoDepth = 64;

        private const int SinglePlayerOwner = 0;

        private int _localOwner = SinglePlayerOwner;
        private Dictionary<int, UndoHistory> _undo;
        private IActionTransport _transport;
        private bool _valid;

        /// <summary>Host rejected a request of the local client (multiplayer only).</summary>
        public event System.Action<ActionResult> RemoteResult;

        public bool IsValid => _valid;
        public int LocalOwnerId => _localOwner;
        public EntityRegistry Registry => registry;
        public PropThruster ThrusterPrefab => thrusterPrefab;
        public InputBindings Bindings => bindings;
        public Material[] ColorPalette => colorPalette;

        /// <summary>Detonates an explosive prop.</summary>
        public ActionResult Detonate(int entityId)
        {
            if (_transport == null) return ExecuteDetonate(_localOwner, entityId);
            _transport.SendDetonate(entityId);
            return ActionResult.Pending;
        }

        public ActionResult ExecuteDetonate(int owner, int entityId)
        {
            if (!_valid || explosions == null) return ActionResult.Unavailable;
            if (!registry.TryGetEntity(entityId, out SandboxEntity entity)) return ActionResult.NotFound;
            if (!CanModify(owner, entity)) return ActionResult.NotAllowed;
            ExplosiveProp explosive = entity.GetComponent<ExplosiveProp>(); // user action only
            if (explosive == null) return ActionResult.Invalid;
            explosions.Request(explosive, 0f);
            return ActionResult.Ok;
        }

        /// <summary>Paints the entity with palette color index (-1 = original materials).</summary>
        public ActionResult SetColor(int entityId, int colorIndex)
        {
            if (_transport == null) return ExecuteSetColor(_localOwner, entityId, colorIndex);
            _transport.SendSetColor(entityId, colorIndex);
            return ActionResult.Pending;
        }

        public ActionResult ExecuteSetColor(int owner, int entityId, int colorIndex)
        {
            if (!_valid) return ActionResult.Unavailable;
            if (!registry.TryGetEntity(entityId, out SandboxEntity entity)) return ActionResult.NotFound;
            if (!CanModify(owner, entity)) return ActionResult.NotAllowed;
            if (colorIndex >= colorPalette.Length) return ActionResult.Invalid;
            entity.ApplyColor(colorIndex < 0 ? -1 : colorIndex, colorPalette);
            return ActionResult.Ok;
        }

        /// <summary>True on a connected non-host client: requests go to the host.</summary>
        public bool IsRemoteClient => _transport != null;

        /// <summary>Network layer: set on a client after connecting, null when offline / host.</summary>
        public void SetTransport(IActionTransport transport) => _transport = transport;

        /// <summary>Network layer: the host reported the result of one of our requests.</summary>
        public void ReportRemoteResult(ActionResult result)
        {
            if (result != ActionResult.Ok) RemoteResult?.Invoke(result);
        }

        private void Awake()
        {
            string missing = string.Empty;
            if (registry == null) missing += " Registry";
            if (spawner == null) missing += " Spawner";
            if (thrusters == null) missing += " Thrusters";
            if (quality == null) missing += " Quality";
            if (thrusterPrefab == null) missing += " ThrusterPrefab";
            if (bindings == null) missing += " Bindings";
            if (missing.Length > 0)
            {
                Fail($"required references not assigned:{missing}.");
                return;
            }
            if (thrusterPrefab.GetComponentInChildren<Collider>(true) != null)
            {
                Fail("Thruster Prefab contains a Collider (it would merge into the prop's compound collider).");
                return;
            }
            _undo = new Dictionary<int, UndoHistory>(8);
            _valid = true;
        }

        /// <summary>Network layer sets the local player's owner id after connecting.</summary>
        public void SetLocalOwnerId(int ownerId) => _localOwner = ownerId;

        public bool TryGetEntity(int id, out SandboxEntity entity)
        {
            entity = null;
            return _valid && registry.TryGetEntity(id, out entity);
        }

        public bool CanModify(int ownerId, SandboxEntity entity)
        {
            return !ownershipProtection || entity.OwnerId == ownerId || entity.OwnerId == SandboxEntity.WorldOwner;
        }

        /// <summary>Server rule check for noclip (per owner in multiplayer: admins etc. can be added here).</summary>
        public bool CanUseNoclip(int ownerId) => _valid && allowNoclip;

        // ================================================================== Requests (local player)

        // On a multiplayer client every request is forwarded (Pending); otherwise it executes here.

        public ActionResult SpawnProp(int typeIndex, Vector3 position, Quaternion rotation)
        {
            if (_transport == null) return ExecuteSpawnProp(_localOwner, typeIndex, position, rotation);
            _transport.SendSpawnProp(typeIndex, position, rotation);
            return ActionResult.Pending;
        }

        public ActionResult RemoveEntity(int entityId)
        {
            if (_transport == null) return ExecuteRemoveEntity(_localOwner, entityId);
            _transport.SendRemoveEntity(entityId);
            return ActionResult.Pending;
        }

        /// <summary>bId = 0 welds A to the world.</summary>
        public ActionResult Weld(int aId, int bId, WeldSettings settings)
        {
            if (_transport == null) return ExecuteWeld(_localOwner, aId, bId, settings);
            if (aId == bId) return ActionResult.Invalid;
            _transport.SendWeld(aId, bId, settings);
            return ActionResult.Pending;
        }

        public ActionResult RemoveConstraintsOf(int entityId)
        {
            if (_transport == null) return ExecuteRemoveConstraintsOf(_localOwner, entityId);
            _transport.SendRemoveConstraintsOf(entityId);
            return ActionResult.Pending;
        }

        /// <summary>Pose is in the entity's local space (nozzle = local forward).</summary>
        public ActionResult AttachThruster(int entityId, Vector3 localPosition, Quaternion localRotation, float thrustForce, int binding, bool startOn)
        {
            if (_transport == null) return ExecuteAttachThruster(_localOwner, entityId, localPosition, localRotation, thrustForce, binding, startOn);
            _transport.SendAttachThruster(entityId, localPosition, localRotation, thrustForce, binding, startOn);
            return ActionResult.Pending;
        }

        public ActionResult SetEntityThrusters(int entityId, bool on)
        {
            if (_transport == null) return ExecuteSetEntityThrusters(_localOwner, entityId, on);
            _transport.SendSetEntityThrusters(entityId, on);
            return ActionResult.Pending;
        }

        /// <summary>nowOn is only meaningful when the result is Ok (not Pending).</summary>
        public ActionResult ToggleAllThrusters(out bool nowOn)
        {
            if (_transport == null) return ExecuteToggleAllThrusters(_localOwner, out nowOn);
            nowOn = false;
            _transport.SendToggleAllThrusters();
            return ActionResult.Pending;
        }

        public ActionResult Undo(out EntityRegistry.RemovedKind kind)
        {
            if (_transport == null) return ExecuteUndo(_localOwner, out kind);
            kind = EntityRegistry.RemovedKind.None;
            _transport.SendUndo();
            return ActionResult.Pending;
        }

        public ActionResult RemoveAllOwned(out int removed)
        {
            if (_transport == null) return ExecuteRemoveAllOwned(_localOwner, out removed);
            removed = 0;
            _transport.SendRemoveAllOwned();
            return ActionResult.Pending;
        }

        /// <summary>
        /// Rope or Elastic between A (anchor local to A) and B (anchor local to B), or the world (bId = 0, anchorB = world point).
        /// length = max length (rope) or rest length (elastic).
        /// </summary>
        public ActionResult CreateCable(int aId, Vector3 anchorA, int bId, Vector3 anchorB, ConstraintKind kind, float length)
        {
            if (_transport == null) return ExecuteCreateCable(_localOwner, aId, anchorA, bId, anchorB, kind, length);
            if (aId == bId) return ActionResult.Invalid;
            _transport.SendCreateCable(aId, anchorA, bId, anchorB, kind, length);
            return ActionResult.Pending;
        }

        /// <summary>Hinge on A (anchor / axis local to A) connected to B, or the world (bId = 0).</summary>
        public ActionResult CreateAxis(int aId, Vector3 anchorA, Vector3 axisA, int bId, AxisMotorSettings motor)
        {
            if (_transport == null) return ExecuteCreateAxis(_localOwner, aId, anchorA, axisA, bId, motor);
            if (aId == bId) return ActionResult.Invalid;
            _transport.SendCreateAxis(aId, anchorA, axisA, bId, motor);
            return ActionResult.Pending;
        }

        /// <summary>Piston between A and B (or the world, bId = 0, anchorB = world point); starts at the current distance.</summary>
        public ActionResult CreateHydraulic(int aId, Vector3 anchorA, int bId, Vector3 anchorB, HydraulicSettings settings)
        {
            if (_transport == null) return ExecuteCreateHydraulic(_localOwner, aId, anchorA, bId, anchorB, settings);
            if (aId == bId) return ActionResult.Invalid;
            _transport.SendCreateHydraulic(aId, anchorA, bId, anchorB, settings);
            return ActionResult.Pending;
        }

        /// <summary>Held contraption bindings (bit mask). Call only when the mask changes.</summary>
        public void SetBindings(int mask)
        {
            if (_transport == null) ExecuteSetBindings(_localOwner, mask);
            else _transport.SendBindings(mask);
        }

        // ================================================================== Execution (authority only)

        public void ExecuteSetBindings(int owner, int mask)
        {
            if (_valid) bindings.SetMask(owner, mask);
        }

        public ActionResult ExecuteCreateCable(int owner, int aId, Vector3 anchorA, int bId, Vector3 anchorB, ConstraintKind kind, float length)
        {
            if (kind != ConstraintKind.Rope && kind != ConstraintKind.Elastic) return ActionResult.Invalid;
            ActionResult check = ValidatePair(owner, aId, bId, out SandboxEntity a, out SandboxEntity b);
            if (check != ActionResult.Ok) return check;
            if (!IsFinite(anchorA) || !IsFinite(anchorB) || float.IsNaN(length) || length < 0f || length > 100f) return ActionResult.Invalid;

            Rigidbody bodyB = b != null ? b.Body : null;
            Joint joint = kind == ConstraintKind.Rope
                ? (Joint)ConstraintFactory.CreateRope(a.Body, anchorA, bodyB, anchorB, length)
                : ConstraintFactory.CreateElastic(a.Body, anchorA, bodyB, anchorB, length, elasticStiffness, elasticDamping);

            var constraint = new SandboxConstraint(kind, owner, a, b, joint, anchorA, anchorB, length, default);
            registry.AddConstraint(constraint);
            History(owner).Push(constraint.Id);

            a.Body.WakeUp();
            if (b != null) b.Body.WakeUp();
            return ActionResult.Ok;
        }

        public ActionResult ExecuteCreateAxis(int owner, int aId, Vector3 anchorA, Vector3 axisA, int bId, AxisMotorSettings motor)
        {
            ActionResult check = ValidatePair(owner, aId, bId, out SandboxEntity a, out SandboxEntity b);
            if (check != ActionResult.Ok) return check;
            if (!IsFinite(anchorA) || !IsFinite(axisA) || axisA.sqrMagnitude < 1e-6f) return ActionResult.Invalid;

            motor.ForwardBinding = Mathf.Clamp(motor.ForwardBinding, 0, InputBindings.Count);
            motor.BackwardBinding = Mathf.Clamp(motor.BackwardBinding, 0, InputBindings.Count);
            motor.Speed = Mathf.Clamp(motor.Speed, 0f, 5000f);
            motor.Force = Mathf.Clamp(motor.Force, 0f, 20000f);

            HingeJoint joint = ConstraintFactory.CreateAxis(a.Body, anchorA, axisA, b != null ? b.Body : null, motor);
            var constraint = new SandboxConstraint(ConstraintKind.Axis, owner, a, b, joint, anchorA, axisA, 0f, motor);
            registry.AddConstraint(constraint);
            History(owner).Push(constraint.Id);

            a.Body.WakeUp();
            if (b != null) b.Body.WakeUp();
            return ActionResult.Ok;
        }

        public ActionResult ExecuteCreateHydraulic(int owner, int aId, Vector3 anchorA, int bId, Vector3 anchorB, HydraulicSettings h)
        {
            ActionResult check = ValidatePair(owner, aId, bId, out SandboxEntity a, out SandboxEntity b);
            if (check != ActionResult.Ok) return check;
            if (!IsFinite(anchorA) || !IsFinite(anchorB)) return ActionResult.Invalid;

            Vector3 worldA = a.transform.TransformPoint(anchorA);
            Vector3 worldB = b != null ? b.transform.TransformPoint(anchorB) : anchorB;
            float length = Vector3.Distance(worldA, worldB);
            if (length < 0.05f || length > 50f) return ActionResult.Invalid;

            h.ExtendBinding = Mathf.Clamp(h.ExtendBinding, 0, InputBindings.Count);
            h.RetractBinding = Mathf.Clamp(h.RetractBinding, 0, InputBindings.Count);
            h.MinLength = Mathf.Clamp(h.MinLength, 0.05f, length);
            h.MaxLength = Mathf.Clamp(h.MaxLength, length, length + 10f);
            h.Speed = Mathf.Clamp(h.Speed, 0.05f, 10f);

            SpringJoint joint = ConstraintFactory.CreateHydraulic(a.Body, anchorA, b != null ? b.Body : null, anchorB, length,
                hydraulicStiffness, hydraulicDamping);
            var constraint = new SandboxConstraint(owner, a, b, joint, anchorA, anchorB, length, h);
            registry.AddConstraint(constraint);
            History(owner).Push(constraint.Id);

            a.Body.WakeUp();
            if (b != null) b.Body.WakeUp();
            return ActionResult.Ok;
        }

        /// <summary>Shared checks for two-entity constraints (bId = 0 means the world).</summary>
        private ActionResult ValidatePair(int owner, int aId, int bId, out SandboxEntity a, out SandboxEntity b)
        {
            a = null;
            b = null;
            if (!_valid) return ActionResult.Unavailable;
            if (!registry.TryGetEntity(aId, out a)) return ActionResult.NotFound;
            if (bId != 0 && !registry.TryGetEntity(bId, out b)) return ActionResult.NotFound;
            if (a == b) return ActionResult.Invalid;
            if (!CanModify(owner, a) || (b != null && !CanModify(owner, b))) return ActionResult.NotAllowed;
            if (registry.HasConstraintBetween(a, b)) return ActionResult.AlreadyExists;

            QualityProfile p = quality.Profile;
            if (a.ConstraintCount >= p.maxConstraintsPerEntity ||
                (b != null && b.ConstraintCount >= p.maxConstraintsPerEntity))
                return ActionResult.EntityLimit;
            if (registry.ConstraintCount >= p.maxConstraintsTotal) return ActionResult.ServerLimit;
            return ActionResult.Ok;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
                   !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
                   !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }

        public ActionResult ExecuteSpawnProp(int owner, int typeIndex, Vector3 position, Quaternion rotation)
        {
            if (!_valid) return ActionResult.Unavailable;
            ActionResult result = spawner.SpawnAt(typeIndex, position, rotation, owner, out SandboxEntity entity);
            if (result == ActionResult.Ok) History(owner).Push(entity.Id);
            return result;
        }

        public ActionResult ExecuteRemoveEntity(int owner, int entityId)
        {
            if (!_valid) return ActionResult.Unavailable;
            if (!registry.TryGetEntity(entityId, out SandboxEntity entity)) return ActionResult.NotFound;
            if (!CanModify(owner, entity)) return ActionResult.NotAllowed;
            return registry.RemoveEntity(entity) ? ActionResult.Ok : ActionResult.Invalid;
        }

        public ActionResult ExecuteWeld(int owner, int aId, int bId, WeldSettings settings)
        {
            if (!_valid) return ActionResult.Unavailable;
            if (!registry.TryGetEntity(aId, out SandboxEntity a)) return ActionResult.NotFound;

            SandboxEntity b = null;
            if (bId != 0 && !registry.TryGetEntity(bId, out b)) return ActionResult.NotFound;

            if (a == b) return ActionResult.Invalid;
            if (!CanModify(owner, a) || (b != null && !CanModify(owner, b))) return ActionResult.NotAllowed;
            if (registry.HasConstraintBetween(a, b)) return ActionResult.AlreadyExists;

            QualityProfile p = quality.Profile;
            if (a.ConstraintCount >= p.maxConstraintsPerEntity ||
                (b != null && b.ConstraintCount >= p.maxConstraintsPerEntity))
                return ActionResult.EntityLimit;
            if (registry.ConstraintCount >= p.maxConstraintsTotal) return ActionResult.ServerLimit;

            FixedJoint joint = ConstraintFactory.CreateWeld(a.Body, b != null ? b.Body : null, settings);
            var constraint = new SandboxConstraint(ConstraintKind.Weld, owner, a, b, joint);
            registry.AddConstraint(constraint);
            History(owner).Push(constraint.Id);

            a.Body.WakeUp();
            if (b != null) b.Body.WakeUp();
            return ActionResult.Ok;
        }

        public ActionResult ExecuteRemoveConstraintsOf(int owner, int entityId)
        {
            if (!_valid) return ActionResult.Unavailable;
            if (!registry.TryGetEntity(entityId, out SandboxEntity entity)) return ActionResult.NotFound;
            if (!CanModify(owner, entity)) return ActionResult.NotAllowed;
            return registry.RemoveConstraintsOf(entity) > 0 ? ActionResult.Ok : ActionResult.NotFound;
        }

        public ActionResult ExecuteAttachThruster(int owner, int entityId, Vector3 localPosition, Quaternion localRotation, float thrustForce, int binding, bool startOn)
        {
            if (!_valid) return ActionResult.Unavailable;
            if (!registry.TryGetEntity(entityId, out SandboxEntity entity)) return ActionResult.NotFound;
            if (!CanModify(owner, entity)) return ActionResult.NotAllowed;
            if (thrusters.IsFull) return ActionResult.ServerLimit;

            Transform parent = entity.transform;
            PropThruster thruster = Instantiate(thrusterPrefab, parent);
            Transform t = thruster.transform;
            t.SetLocalPositionAndRotation(localPosition, localRotation);
            CompensateParentScale(t, parent);

            if (!thruster.Attach(entity, owner, thrusters, registry, thrustForce, binding, startOn))
            {
                Destroy(thruster.gameObject);
                return ActionResult.ServerLimit;
            }

            History(owner).Push(thruster.AttachmentId);
            entity.Body.WakeUp();
            return ActionResult.Ok;
        }

        public ActionResult ExecuteSetEntityThrusters(int owner, int entityId, bool on)
        {
            if (!_valid) return ActionResult.Unavailable;
            if (!registry.TryGetEntity(entityId, out SandboxEntity entity)) return ActionResult.NotFound;
            if (!CanModify(owner, entity)) return ActionResult.NotAllowed;

            int found = 0;
            for (int i = 0; i < entity.AttachmentCount; i++)
            {
                if (entity.GetAttachment(i) is PropThruster thruster)
                {
                    thruster.SetOn(on);
                    found++;
                }
            }
            if (found == 0) return ActionResult.NotFound;
            if (on) entity.Body.WakeUp();
            return ActionResult.Ok;
        }

        public ActionResult ExecuteToggleAllThrusters(int owner, out bool nowOn)
        {
            nowOn = false;
            if (!_valid) return ActionResult.Unavailable;
            nowOn = !thrusters.AnyOnOwnedBy(owner);
            thrusters.SetAllOwnedBy(owner, nowOn);
            return ActionResult.Ok;
        }

        public ActionResult ExecuteUndo(int owner, out EntityRegistry.RemovedKind kind)
        {
            kind = EntityRegistry.RemovedKind.None;
            if (!_valid) return ActionResult.Unavailable;

            UndoHistory history = History(owner);
            while (history.TryPop(out int id))
            {
                kind = registry.RemoveById(id); // ids of things already removed resolve to None and are skipped
                if (kind != EntityRegistry.RemovedKind.None) return ActionResult.Ok;
            }
            return ActionResult.NothingToUndo;
        }

        public ActionResult ExecuteRemoveAllOwned(int owner, out int removed)
        {
            removed = 0;
            if (!_valid) return ActionResult.Unavailable;
            removed = registry.RemoveAllOwnedBy(owner);
            History(owner).Clear();
            return ActionResult.Ok;
        }

        // ================================================================== Helpers

        private UndoHistory History(int owner)
        {
            if (!_undo.TryGetValue(owner, out UndoHistory history))
            {
                history = new UndoHistory(undoDepth); // once per owner
                _undo.Add(owner, history);
            }
            return history;
        }

        /// <summary>Keeps the child's world size when the parent is scaled (exact for uniform scale).</summary>
        private static void CompensateParentScale(Transform child, Transform parent)
        {
            Vector3 s = parent.lossyScale;
            child.localScale = new Vector3(
                Mathf.Abs(s.x) > 1e-4f ? 1f / s.x : 1f,
                Mathf.Abs(s.y) > 1e-4f ? 1f / s.y : 1f,
                Mathf.Abs(s.z) > 1e-4f ? 1f / s.z : 1f);
        }

        private void Fail(string reason)
        {
            Debug.LogError($"[{nameof(SandboxActions)}] '{name}': {reason} Component disabled.", this);
            _valid = false;
            enabled = false;
        }
    }
}
