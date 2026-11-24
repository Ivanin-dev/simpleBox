using SimpleBox.Controls;
using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay
{
    /// <summary>
    /// Sitting in contraptions. Aim at a Seat prop within range -> SIT button (E) appears; press to sit, press again
    /// (EXIT) to stand up next to the seat in a free spot. While seated the joystick drives the contraption:
    /// forward = binding 1, back = 2, left = 3, right = 4 (merged with the binding buttons by PlayerCommands),
    /// and the camera switches to third person (PlayerController).
    /// The seat lookup (GetComponent) runs only when the aimed body changes. If the seat is removed / recycled,
    /// the player is unseated before the prop is deactivated.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeatController : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private MobileInputController input;
        [SerializeField] private PlayerController player;
        [Tooltip("Camera transform: ray origin and direction.")]
        [SerializeField] private Transform aimOrigin;
        [SerializeField] private EntityRegistry registry;

        [Header("Targeting")]
        [SerializeField, Min(0.5f)] private float range = 3f;
        [Tooltip("Environment + props (walls block the ray). Exclude the Player layer.")]
        [SerializeField] private LayerMask raycastMask = ~0;

        [Header("Driving")]
        [Tooltip("Joystick deflection that counts as pressing a direction.")]
        [SerializeField, Range(0.1f, 0.9f)] private float driveThreshold = 0.4f;

        private static readonly Vector3[] ExitOffsets =
        {
            new Vector3(1.2f, 0f, 0f), new Vector3(-1.2f, 0f, 0f), new Vector3(0f, 0f, -1.2f),
            new Vector3(0f, 0f, 1.2f), new Vector3(1.6f, 0f, -1.6f), new Vector3(-1.6f, 0f, -1.6f)
        };

        private Rigidbody _aimedBody;
        private Seat _aimedSeat;
        private Seat _seat;
        private SandboxEntity _seatEntity;
        private bool _valid;
        private bool _subscribed;

        public bool IsSeated => _seat != null;
        public bool CanSit => _seat == null && _aimedSeat != null && !_aimedSeat.Occupied && !player.IsFlying;

        /// <summary>Joystick as bindings while seated (bit 0 = 1 forward, 1 = 2 back, 2 = 3 left, 3 = 4 right).</summary>
        public int DriveMask
        {
            get
            {
                if (_seat == null) return 0;
                Vector2 m = input.Move;
                int mask = 0;
                if (m.y > driveThreshold) mask |= 1;
                if (m.y < -driveThreshold) mask |= 2;
                if (m.x < -driveThreshold) mask |= 4;
                if (m.x > driveThreshold) mask |= 8;
                return mask;
            }
        }

        private void Awake()
        {
            string missing = string.Empty;
            if (input == null) missing += " Input";
            if (player == null) missing += " Player";
            if (aimOrigin == null) missing += " AimOrigin";
            if (registry == null) missing += " Registry";
            if (missing.Length > 0 || !input.HasSeatWidgets)
            {
                Debug.LogError($"[{nameof(SeatController)}] '{name}': required references not assigned:{missing}" +
                               (input != null && !input.HasSeatWidgets ? " (MobileInputController has no Sit Button)" : "") +
                               ". Component disabled.", this);
                enabled = false;
                return;
            }
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            registry.EntityRemoving += OnEntityRemoving;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (_seat != null) Exit();
            if (!_subscribed) return;
            if (registry != null) registry.EntityRemoving -= OnEntityRemoving;
            _subscribed = false;
        }

        private void Update()
        {
            bool sitPressed = input.ConsumeSit();

            if (_seat != null)
            {
                if (sitPressed || !_seat.isActiveAndEnabled) Exit();
                return;
            }

            RefreshTarget();
            if (sitPressed && CanSit) Enter(_aimedSeat);
        }

        private void RefreshTarget()
        {
            Rigidbody body = null;
            if (Physics.Raycast(aimOrigin.position, aimOrigin.forward, out RaycastHit hit, range, raycastMask, QueryTriggerInteraction.Ignore))
                body = hit.rigidbody;

            if (body == _aimedBody) return;
            _aimedBody = body;
            _aimedSeat = body != null ? body.GetComponent<Seat>() : null; // only when the aimed body changes
        }

        private void Enter(Seat seat)
        {
            if (!player.EnterSeat(seat.SitPoint)) return;
            _seat = seat;
            _seatEntity = seat.GetComponent<SandboxEntity>();
            seat.Occupied = true;
        }

        public void Exit()
        {
            Seat seat = _seat;
            _seat = null;
            _seatEntity = null;
            _aimedBody = null;
            _aimedSeat = null;
            if (seat != null) seat.Occupied = false;
            player.ExitSeat(FindExitPosition(seat));
        }

        private Vector3 FindExitPosition(Seat seat)
        {
            if (seat == null) return player.transform.position + Vector3.up;

            Transform t = seat.transform;
            Vector3 basePos = t.position;
            for (int i = 0; i < ExitOffsets.Length; i++)
            {
                Vector3 offset = t.TransformDirection(ExitOffsets[i]);
                offset.y = 0f;
                Vector3 feet = basePos + offset;
                if (player.CanStandAt(feet)) return feet;
            }
            return basePos + Vector3.up * 2f; // nothing free around: drop from above the seat
        }

        private void OnEntityRemoving(SandboxEntity entity)
        {
            if (_seat != null && entity == _seatEntity) Exit(); // before the prop is pooled / destroyed
        }
    }
}
