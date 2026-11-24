using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay
{
    /// <summary>
    /// Physgun: raycast from screen center, grab a Rigidbody, drive it toward the hold point by setting
    /// linear/angular velocity in FixedUpdate (the solver still resolves collisions, so props do not tunnel
    /// through walls). Freeze = isKinematic true, Unfreeze = isKinematic false.
    /// Reads no input: driven only by PhysicsGunTool (USE = ToggleGrab, ALT = ToggleFreeze, unequip = Drop).
    /// Freeze while holding freezes the prop in place; Freeze while aiming at a frozen prop unfreezes it.
    /// Grabbing a frozen prop also unfreezes it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhysicsGun : MonoBehaviour
    {
        [Header("References (required)")]
        [Tooltip("Drops the held prop when it is removed anywhere (Remover, Undo, limit recycling, physics guard).")]
        [SerializeField] private EntityRegistry registry;
        [Tooltip("Local owner id (held-by marking) and ownership checks.")]
        [SerializeField] private SandboxActions actions;
        [Tooltip("Camera transform. Ray goes from its position along its forward (= screen center).")]
        [SerializeField] private Transform aimOrigin;

        [Header("Raycast")]
        [SerializeField, Min(0.1f)] private float maxGrabDistance = 12f;
        [Tooltip("Everything the ray can hit (walls block grabbing). Exclude the Player layer.")]
        [SerializeField] private LayerMask raycastMask = ~0;
        [Tooltip("Layers whose Rigidbodies can be grabbed/frozen.")]
        [SerializeField] private LayerMask grabbableMask;

        [Header("Hold")]
        [SerializeField, Min(0.1f)] private float minHoldDistance = 1.5f;
        [SerializeField, Min(0.1f)] private float maxHoldDistance = 10f;
        [Tooltip("Linear P-gain (1/s). velocity = error * gain. Keep gain * fixedDeltaTime < 1.")]
        [SerializeField, Min(0.1f)] private float positionGain = 15f;
        [SerializeField, Min(0.1f)] private float maxLinearSpeed = 20f;
        [Tooltip("Angular P-gain (1/s).")]
        [SerializeField, Min(0.1f)] private float rotationGain = 10f;
        [SerializeField, Min(0.1f)] private float maxAngularSpeed = 20f;

        [Header("Break / Release")]
        [Tooltip("Auto-release if the grab point stays farther than this from the hold point.")]
        [SerializeField, Min(0.1f)] private float breakDistance = 4f;
        [SerializeField, Min(0f)] private float breakDelay = 0.3f;
        [SerializeField] private bool keepMomentumOnRelease = true;
        [SerializeField, Min(0f)] private float maxReleaseSpeed = 8f;

        private Rigidbody _held;
        private Vector3 _localAnchor;       // grab point in body space (relative to rb.position/rotation)
        private Quaternion _rotationOffset; // body rotation relative to player yaw at grab time
        private float _holdDistance;
        private float _breakTimer;

        private bool _savedUseGravity;
        private float _savedMaxAngularVelocity;
        private RigidbodyInterpolation _savedInterpolation;
        private CollisionDetectionMode _savedCollisionMode;

        [Header("Runtime configuration")]
        [Tooltip("Proxy physgun of a remote player on the host (network avatar): references are given by ConfigureRuntime.")]
        [SerializeField] private bool configuredAtRuntime;

        private SandboxEntity _heldEntity;
        private float _rotationSnap;
        private Vector2 _snapAccumulator;
        private bool _hasOwnerOverride;
        private int _ownerOverride;
        private bool _valid;
        private bool _subscribed;

        /// <summary>
        /// Multiplayer client: commands are forwarded to the host (network avatar) instead of simulating locally.
        /// Null offline / on the host.
        /// </summary>
        public IPhysgunRemote Remote { get; set; }

        public bool IsHolding => Remote != null ? Remote.IsHolding : _held != null;
        public Rigidbody HeldBody => _held;
        public LayerMask RaycastMask => raycastMask;
        public LayerMask GrabbableMask => grabbableMask;

        public bool IsHoldingBody(Rigidbody body) => body != null && _held == body;

        /// <summary>Owner id used for held-by marking and ownership checks.</summary>
        public int OwnerId => _hasOwnerOverride ? _ownerOverride : actions.LocalOwnerId;

        /// <summary>Current hold distance in meters (meaningful while holding).</summary>
        public float HoldDistance => _holdDistance;

        /// <summary>Rotation snap step in degrees; 0 = free rotation.</summary>
        public float RotationSnap
        {
            get => _rotationSnap;
            set
            {
                _rotationSnap = Mathf.Max(0f, value);
                _snapAccumulator = Vector2.zero;
                if (Remote != null) Remote.SetRotationSnap(_rotationSnap);
            }
        }

        /// <summary>
        /// Host side: turns this component into the physgun of a remote player (aim comes from a proxy transform
        /// updated by the network avatar, owner = that player's id).
        /// </summary>
        public void ConfigureRuntime(EntityRegistry entityRegistry, SandboxActions sandboxActions, Transform aim,
            int ownerId, LayerMask raycast, LayerMask grabbable)
        {
            if (_subscribed && registry != null) registry.EntityRemoving -= OnEntityRemoving;
            _subscribed = false;

            registry = entityRegistry;
            actions = sandboxActions;
            aimOrigin = aim;
            raycastMask = raycast;
            grabbableMask = grabbable;
            _hasOwnerOverride = true;
            _ownerOverride = ownerId;
            _valid = registry != null && actions != null && aimOrigin != null;
            enabled = _valid;
            if (_valid && isActiveAndEnabled && !_subscribed)
            {
                registry.EntityRemoving += OnEntityRemoving;
                _subscribed = true;
            }
        }

        /// <summary>
        /// Turns the held prop: x = degrees around world up, y = degrees around the camera's horizontal right axis.
        /// With a snap step, input accumulates and the prop turns in whole steps.
        /// </summary>
        public void RotateHeld(Vector2 degrees)
        {
            if (Remote != null)
            {
                if (degrees.x != 0f || degrees.y != 0f) Remote.RotateHeld(degrees);
                return;
            }
            if (_held == null || (degrees.x == 0f && degrees.y == 0f)) return;

            if (_rotationSnap > 0f)
            {
                _snapAccumulator += degrees;
                degrees.x = TakeSnapSteps(ref _snapAccumulator.x, _rotationSnap);
                degrees.y = TakeSnapSteps(ref _snapAccumulator.y, _rotationSnap);
                if (degrees.x == 0f && degrees.y == 0f) return;
            }

            // _rotationOffset is expressed in the player's yaw frame: world up = Vector3.up, camera right = Vector3.right.
            _rotationOffset = Quaternion.AngleAxis(degrees.x, Vector3.up) *
                              Quaternion.AngleAxis(degrees.y, Vector3.right) *
                              _rotationOffset;
            if (_rotationSnap > 0f) _rotationOffset = SnapRotation(_rotationOffset, _rotationSnap);
        }

        private static float TakeSnapSteps(ref float accumulator, float step)
        {
            int steps = (int)(accumulator / step);
            if (steps == 0) return 0f;
            accumulator -= steps * step;
            return steps * step;
        }

        /// <summary>Rounds Euler angles to the step so repeated snapping never drifts.</summary>
        private static Quaternion SnapRotation(Quaternion q, float step)
        {
            Vector3 e = q.eulerAngles;
            e.x = Mathf.Round(e.x / step) * step;
            e.y = Mathf.Round(e.y / step) * step;
            e.z = Mathf.Round(e.z / step) * step;
            return Quaternion.Euler(e);
        }

        /// <summary>Releases the held prop without momentum (used when the prop is despawned/recycled).</summary>
        public void Drop()
        {
            if (Remote != null)
            {
                Remote.Drop();
                return;
            }
            if (_held != null) Release(false);
        }

        private void Awake()
        {
            if (configuredAtRuntime)
            {
                enabled = false; // waits for ConfigureRuntime
                return;
            }

            _valid = aimOrigin != null && registry != null && actions != null;
            if (!_valid)
            {
                Debug.LogError($"[{nameof(PhysicsGun)}] AimOrigin / Registry / Actions not assigned on '{name}'. Component disabled.", this);
                enabled = false;
                return;
            }
            if (grabbableMask.value == 0)
            {
                Debug.LogWarning($"[{nameof(PhysicsGun)}] Grabbable Mask is empty on '{name}': nothing can be grabbed.", this);
            }
            if (maxHoldDistance < minHoldDistance) maxHoldDistance = minHoldDistance;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            registry.EntityRemoving += OnEntityRemoving;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (_held != null) Release(false);
            if (!_subscribed) return;
            if (registry != null) registry.EntityRemoving -= OnEntityRemoving;
            _subscribed = false;
        }

        private void OnEntityRemoving(SandboxEntity entity)
        {
            if (_held != null && (entity == _heldEntity || entity.Body == _held)) Release(false);
        }

        /// <summary>Grab the prop under the crosshair, or release the held one (keeps momentum if enabled).</summary>
        public void ToggleGrab()
        {
            if (Remote != null)
            {
                Remote.ToggleGrab();
                return;
            }
            if (!_valid) return;
            if (_held != null) Release(keepMomentumOnRelease);
            else TryGrab();
        }

        /// <summary>Freeze the held prop in place, or unfreeze the frozen prop under the crosshair.</summary>
        public void ToggleFreeze()
        {
            if (Remote != null)
            {
                Remote.ToggleFreeze();
                return;
            }
            if (!_valid) return;
            if (_held != null) FreezeHeld();
            else TryUnfreezeTarget();
        }

        private void FixedUpdate()
        {
            if (_held == null) return; // also true if the prop was destroyed

            if (_held.isKinematic) // frozen/kinematic-ized externally
            {
                Release(false);
                return;
            }

            float dt = Time.fixedDeltaTime;

            Quaternion yaw = Quaternion.Euler(0f, aimOrigin.eulerAngles.y, 0f);
            Quaternion targetRotation = yaw * _rotationOffset;
            Vector3 targetAnchor = aimOrigin.position + aimOrigin.forward * _holdDistance;
            Vector3 targetPosition = targetAnchor - targetRotation * _localAnchor;

            Vector3 bodyPos = _held.position;
            Quaternion bodyRot = _held.rotation;

            // Break check: grab point stuck behind geometry.
            Vector3 currentAnchor = bodyPos + bodyRot * _localAnchor;
            if ((targetAnchor - currentAnchor).sqrMagnitude > breakDistance * breakDistance)
            {
                _breakTimer += dt;
                if (_breakTimer >= breakDelay)
                {
                    Release(false);
                    return;
                }
            }
            else
            {
                _breakTimer = 0f;
            }

            // Linear: P-controller on velocity.
            Vector3 linear = (targetPosition - bodyPos) * positionGain;
            _held.linearVelocity = Vector3.ClampMagnitude(linear, maxLinearSpeed);

            // Angular: shortest-arc rotation error -> angular velocity.
            Quaternion delta = targetRotation * Quaternion.Inverse(bodyRot);
            if (delta.w < 0f)
            {
                delta.x = -delta.x;
                delta.y = -delta.y;
                delta.z = -delta.z;
                delta.w = -delta.w;
            }

            delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (angleDeg > 0.01f && IsFinite(axis))
            {
                Vector3 angular = axis * (angleDeg * Mathf.Deg2Rad * rotationGain);
                _held.angularVelocity = Vector3.ClampMagnitude(angular, maxAngularSpeed);
            }
            else
            {
                _held.angularVelocity = Vector3.zero;
            }
        }

        private bool Raycast(out RaycastHit hit)
        {
            return Physics.Raycast(aimOrigin.position, aimOrigin.forward, out hit,
                maxGrabDistance, raycastMask, QueryTriggerInteraction.Ignore);
        }

        private bool IsGrabbable(Rigidbody rb)
        {
            return rb != null && (grabbableMask.value & (1 << rb.gameObject.layer)) != 0;
        }

        private void TryGrab()
        {
            if (!Raycast(out RaycastHit hit)) return;

            Rigidbody rb = hit.rigidbody;
            if (!IsGrabbable(rb)) return;

            int owner = OwnerId;
            SandboxEntity entity = SandboxEntity.FromBody(rb); // once per grab
            if (entity != null)
            {
                if (entity.IsHeld && entity.HeldBy != owner) return;   // another player's physgun has it
                if (!actions.CanModify(owner, entity)) return;         // ownership protection
                entity.HeldBy = owner;
            }
            _heldEntity = entity;

            if (rb.isKinematic) rb.isKinematic = false; // grabbing a frozen prop unfreezes it

            _savedUseGravity = rb.useGravity;
            _savedMaxAngularVelocity = rb.maxAngularVelocity;
            _savedInterpolation = rb.interpolation;
            _savedCollisionMode = rb.collisionDetectionMode;

            rb.useGravity = false;
            rb.maxAngularVelocity = maxAngularSpeed;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // Speculative CCD: valid for both dynamic and kinematic, so freezing later produces no warnings.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.WakeUp();

            Quaternion rbRot = rb.rotation;
            _localAnchor = Quaternion.Inverse(rbRot) * (hit.point - rb.position);
            _holdDistance = Mathf.Clamp(hit.distance, minHoldDistance, maxHoldDistance);

            Quaternion yaw = Quaternion.Euler(0f, aimOrigin.eulerAngles.y, 0f);
            _rotationOffset = Quaternion.Inverse(yaw) * rbRot;
            _snapAccumulator = Vector2.zero;

            _breakTimer = 0f;
            _held = rb;
        }

        private void Release(bool keepMomentum)
        {
            Rigidbody rb = _held;
            _held = null;
            _breakTimer = 0f;

            SandboxEntity entity = _heldEntity;
            _heldEntity = null;
            if (entity != null && entity.HeldBy == OwnerId) entity.HeldBy = SandboxEntity.NoHolder;

            if (rb == null) return;

            rb.useGravity = _savedUseGravity;
            rb.maxAngularVelocity = _savedMaxAngularVelocity;
            rb.interpolation = _savedInterpolation;
            rb.collisionDetectionMode = _savedCollisionMode;

            if (rb.isKinematic) return;

            if (keepMomentum)
            {
                rb.linearVelocity = Vector3.ClampMagnitude(rb.linearVelocity, maxReleaseSpeed);
                rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, maxAngularSpeed * 0.5f);
            }
            else
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        private void FreezeHeld()
        {
            Rigidbody rb = _held;
            Release(false);            // zero velocities while still dynamic
            if (rb != null) rb.isKinematic = true;
        }

        private void TryUnfreezeTarget()
        {
            if (!Raycast(out RaycastHit hit)) return;

            Rigidbody rb = hit.rigidbody;
            if (!IsGrabbable(rb) || !rb.isKinematic) return;

            rb.isKinematic = false;
            rb.WakeUp();
        }

        /// <summary>Change hold distance (e.g. from pinch/slider UI). Positive = push away.</summary>
        public void AdjustHoldDistance(float delta)
        {
            if (Remote != null)
            {
                Remote.AdjustHoldDistance(delta);
                return;
            }
            _holdDistance = Mathf.Clamp(_holdDistance + delta, minHoldDistance, maxHoldDistance);
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
                   !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
                   !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }
    }
}
