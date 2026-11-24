using System;
using SimpleBox.Controls;
using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay
{
    /// <summary>
    /// First-person movement via CharacterController. Yaw rotates the body, pitch rotates CameraPivot.
    /// Runs in Update (CharacterController is not a physics body); props use Rigidbody interpolation.
    /// Noclip (FLY / V): CharacterController disabled (no collisions, no gravity), movement along the view direction,
    /// JUMP/Space = up, DOWN/LeftCtrl = down. Allowed by SandboxActions.CanUseNoclip (server rule).
    /// Leaving flight inside geometry is refused (capsule overlap check), so the player never gets stuck.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private MobileInputController input;
        [Tooltip("Child transform holding the camera. Receives pitch only.")]
        [SerializeField] private Transform cameraPivot;
        [Tooltip("Noclip permission (server rule) and local owner id.")]
        [SerializeField] private SandboxActions actions;

        [Header("References (optional)")]
        [Tooltip("Camera (child of CameraPivot). Moved back for the third-person view while seated.")]
        [SerializeField] private Transform cameraTransform;

        [Header("Seat")]
        [Tooltip("Camera local position relative to CameraPivot while seated (third person).")]
        [SerializeField] private Vector3 seatedCameraOffset = new Vector3(0f, 0.6f, -5f);

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [SerializeField, Min(0f)] private float groundAcceleration = 40f;
        [SerializeField, Min(0f)] private float airAcceleration = 10f;
        [SerializeField, Min(0f)] private float jumpHeight = 1.2f;
        [SerializeField, Min(0f)] private float gravity = 20f;
        [Tooltip("Downward velocity applied while grounded to keep isGrounded stable on slopes/steps.")]
        [SerializeField] private float groundedStickVelocity = -2f;

        [Header("Noclip")]
        [SerializeField, Min(0.1f)] private float flySpeed = 8f;
        [SerializeField, Min(0.1f)] private float flyVerticalSpeed = 6f;
        [Tooltip("Layers that block leaving flight (environment + props). Exclude the Player layer.")]
        [SerializeField] private LayerMask flightExitBlockMask = ~0;

        [Header("Look")]
        [SerializeField, Range(-89f, 0f)] private float minPitch = -85f;
        [SerializeField, Range(0f, 89f)] private float maxPitch = 85f;

        [Header("Pushing props")]
        [SerializeField, Min(0f)] private float pushSpeed = 2f;
        [SerializeField, Min(0f)] private float maxPushMass = 50f;

        private const string MsgFlyOn = "Полёт: ВКЛ";
        private const string MsgFlyOff = "Полёт: ВЫКЛ";
        private const string MsgFlyForbidden = "Полёт запрещён на сервере";
        private const string MsgFlyUnavailable = "Полёт недоступен (нет кнопок FLY / DOWN)";
        private const string MsgFlyBlocked = "Нельзя выйти из полёта внутри объекта";

        private CharacterController _controller;
        private Transform _transform;
        private float _yaw;
        private float _pitch;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private bool _flying;
        private bool _flightAvailable;
        private bool _seated;
        private Vector3 _cameraDefaultLocalPos;

        /// <summary>Arg: flying now.</summary>
        public event Action<bool> FlyStateChanged;
        /// <summary>Short status message for the toast HUD (constant strings).</summary>
        public event Action<string> Notice;

        public bool IsFlying => _flying;
        public bool IsSeated => _seated;

        public Vector3 Velocity
        {
            get
            {
                Vector3 v = _horizontalVelocity;
                v.y = _verticalVelocity;
                return v;
            }
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _transform = transform;

            if (input == null || cameraPivot == null || actions == null)
            {
                Debug.LogError($"[{nameof(PlayerController)}] Input / CameraPivot / Actions not assigned on '{name}'. Component disabled.", this);
                enabled = false;
                return;
            }

            _flightAvailable = input.HasFlyWidgets;
            if ((flightExitBlockMask.value & (1 << gameObject.layer)) != 0)
            {
                Debug.LogWarning($"[{nameof(PlayerController)}] Flight Exit Block Mask contains the player's own layer: leaving flight will always be refused.", this);
            }

            if (cameraTransform != null) _cameraDefaultLocalPos = cameraTransform.localPosition;

            _yaw = _transform.eulerAngles.y;
            _pitch = NormalizeAngle(cameraPivot.localEulerAngles.x);
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        private void OnDisable()
        {
            if (_flying && _controller != null) _controller.enabled = true;
            _flying = false;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (input.ConsumeFlyToggle() && !_seated) ToggleFlight();

            HandleLook();
            if (_seated)
            {
                input.ConsumeJump(); // the joystick drives the contraption (SeatController), no walking / jumping
                return;
            }
            if (_flying) HandleFlight(dt);
            else HandleMovement(dt);
        }

        // ------------------------------------------------------------------ Seat

        /// <summary>Parents the rig to the seat, disables the CharacterController, switches to third person.</summary>
        public bool EnterSeat(Transform sitPoint)
        {
            if (_seated || _flying || sitPoint == null) return false;

            _seated = true;
            _controller.enabled = false;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;

            _transform.SetParent(sitPoint, false);
            _transform.localPosition = Vector3.zero;
            _transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (cameraTransform != null) cameraTransform.localPosition = seatedCameraOffset;
            return true;
        }

        /// <summary>Unparents the rig at the given feet position and restores walking and the first-person camera.</summary>
        public void ExitSeat(Vector3 feetPosition)
        {
            if (!_seated) return;
            _seated = false;

            _transform.SetParent(null, true);
            _transform.position = feetPosition;
            _transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (cameraTransform != null) cameraTransform.localPosition = _cameraDefaultLocalPos;
            _controller.enabled = true;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }

        /// <summary>Instant velocity change (explosion knockback). Ignored while seated or flying.</summary>
        public void AddImpulse(Vector3 velocityChange)
        {
            if (_seated || _flying || !enabled) return;
            _horizontalVelocity.x += velocityChange.x;
            _horizontalVelocity.z += velocityChange.z;
            _verticalVelocity = Mathf.Max(_verticalVelocity, 0f) + velocityChange.y;
        }

        /// <summary>True if the player capsule fits with its feet at this point (no environment / props overlap).</summary>
        public bool CanStandAt(Vector3 feetPosition)
        {
            float radius = _controller.radius * 0.95f;
            float half = Mathf.Max(0f, _controller.height * 0.5f - _controller.radius);
            Vector3 center = feetPosition + _controller.center + Vector3.up * 0.05f;
            return !Physics.CheckCapsule(center + Vector3.up * half, center - Vector3.up * half, radius,
                flightExitBlockMask, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------ Flight

        public void ToggleFlight()
        {
            if (_flying) TryExitFlight();
            else TryEnterFlight();
        }

        private void TryEnterFlight()
        {
            if (!_flightAvailable)
            {
                Notice?.Invoke(MsgFlyUnavailable);
                return;
            }
            if (!actions.CanUseNoclip(actions.LocalOwnerId))
            {
                Notice?.Invoke(MsgFlyForbidden);
                return;
            }

            _flying = true;
            _controller.enabled = false; // no collisions while flying
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            FlyStateChanged?.Invoke(true);
            Notice?.Invoke(MsgFlyOn);
        }

        private void TryExitFlight()
        {
            if (CapsuleOverlapsWorld())
            {
                Notice?.Invoke(MsgFlyBlocked);
                return;
            }

            _flying = false;
            _controller.enabled = true;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            FlyStateChanged?.Invoke(false);
            Notice?.Invoke(MsgFlyOff);
        }

        private bool CapsuleOverlapsWorld()
        {
            Vector3 center = _transform.TransformPoint(_controller.center);
            float radius = _controller.radius * 0.95f;
            float half = Mathf.Max(0f, _controller.height * 0.5f - _controller.radius);
            Vector3 up = _transform.up * half;
            return Physics.CheckCapsule(center + up, center - up, radius, flightExitBlockMask, QueryTriggerInteraction.Ignore);
        }

        private void HandleFlight(float dt)
        {
            input.ConsumeJump(); // discard: JUMP acts as "up" while flying, no jump on landing

            Vector2 move = input.Move;
            Vector3 velocity = (cameraPivot.forward * move.y + cameraPivot.right * move.x) * flySpeed;

            float vertical = (input.FlyUpHeld ? 1f : 0f) - (input.FlyDownHeld ? 1f : 0f);
            velocity.y += vertical * flyVerticalSpeed;

            _transform.position += velocity * dt;
        }

        // ------------------------------------------------------------------ Walking

        private void HandleLook()
        {
            Vector2 look = input.ConsumeLookDegrees();
            if (look.x == 0f && look.y == 0f) return;

            _yaw = Mathf.Repeat(_yaw + look.x, 360f);
            _pitch = Mathf.Clamp(_pitch + look.y, minPitch, maxPitch);

            _transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void HandleMovement(float dt)
        {
            bool grounded = _controller.isGrounded;
            Vector2 move = input.Move;
            bool jumpPressed = input.ConsumeJump(); // consume every frame so presses do not buffer

            Vector3 wish = _transform.right * move.x + _transform.forward * move.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            wish *= moveSpeed;

            float accel = grounded ? groundAcceleration : airAcceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, wish, accel * dt);

            if (grounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = groundedStickVelocity;
            }

            if (jumpPressed && grounded)
            {
                _verticalVelocity = Mathf.Sqrt(2f * jumpHeight * gravity);
            }

            _verticalVelocity -= gravity * dt;

            Vector3 motion = _horizontalVelocity;
            motion.y = _verticalVelocity;
            CollisionFlags flags = _controller.Move(motion * dt);

            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f)
            {
                _verticalVelocity = 0f;
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody body = hit.rigidbody;
            if (body == null || body.isKinematic || body.mass > maxPushMass) return;
            if (hit.moveDirection.y < -0.3f) return; // standing on it, do not push down

            Vector3 dir = hit.moveDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            Vector3 v = body.linearVelocity;
            if (Vector3.Dot(v, dir) >= pushSpeed) return;

            v.x = dir.x * pushSpeed;
            v.z = dir.z * pushSpeed;
            body.linearVelocity = v;
        }

        private static float NormalizeAngle(float a)
        {
            a = Mathf.Repeat(a + 180f, 360f) - 180f;
            return a;
        }
    }
}
