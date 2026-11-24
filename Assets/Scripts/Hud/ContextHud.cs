using SimpleBox.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Shows only the controls that make sense right now:
    /// - holdControls (ROTATE, distance strip) while the physgun holds a prop;
    /// - flightControls (DOWN) while flying; the JUMP label reads "UP" and the FLY indicator is tinted.
    /// Holding is polled (one bool per frame); flight comes from PlayerController.FlyStateChanged.
    /// Active state changes only on transitions. Hidden TouchButtons release their pointer in OnDisable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ContextHud : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private PhysicsGun physicsGun;
        [SerializeField] private PlayerController player;
        [SerializeField] private GameObject[] holdControls = new GameObject[0];
        [SerializeField] private GameObject[] flightControls = new GameObject[0];

        [Header("References (optional)")]
        [Tooltip("JUMP button label: switches to the 'up' text while flying.")]
        [SerializeField] private Text jumpLabel;
        [Tooltip("Seat state (SIT / EXIT button).")]
        [SerializeField] private SeatController seat;
        [SerializeField] private GameObject sitButton;
        [SerializeField] private Text sitLabel;
        [Tooltip("Tinted while flying (e.g. the FLY button image).")]
        [SerializeField] private Graphic flyIndicator;

        [Header("Look")]
        [SerializeField] private string jumpText = "JUMP";
        [SerializeField] private string upText = "UP";
        [SerializeField] private string sitText = "SIT";
        [SerializeField] private string exitText = "EXIT";
        [SerializeField] private Color flyOnColor = new Color(0.25f, 0.75f, 1f, 0.65f);

        private Color _flyOffColor;
        private bool _holdShown;
        private bool _sitShown;
        private bool _seatedShown;
        private bool _valid;
        private bool _subscribed;

        private void Awake()
        {
            if (physicsGun == null || player == null || holdControls == null || flightControls == null)
            {
                Debug.LogError($"[{nameof(ContextHud)}] '{name}': Physics Gun / Player / control arrays not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            if (flyIndicator != null) _flyOffColor = flyIndicator.color;
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            player.FlyStateChanged += ApplyFlight;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (player != null) player.FlyStateChanged -= ApplyFlight;
            _subscribed = false;
        }

        private void Start()
        {
            if (!_valid) return;
            _holdShown = physicsGun.IsHolding;
            SetActive(holdControls, _holdShown);
            ApplyFlight(player.IsFlying);
            if (sitButton != null) sitButton.SetActive(false);
        }

        private void Update()
        {
            UpdateSeat();
            UpdateHold();
        }

        private void UpdateSeat()
        {
            if (seat == null || sitButton == null) return;
            bool seated = seat.IsSeated;
            bool show = seated || seat.CanSit;
            if (show != _sitShown)
            {
                _sitShown = show;
                sitButton.SetActive(show);
            }
            if (seated != _seatedShown)
            {
                _seatedShown = seated;
                if (sitLabel != null) sitLabel.text = seated ? exitText : sitText;
            }
        }

        private void UpdateHold()
        {
            bool holding = physicsGun.IsHolding;
            if (holding == _holdShown) return;
            _holdShown = holding;
            SetActive(holdControls, holding);
        }

        private void ApplyFlight(bool flying)
        {
            SetActive(flightControls, flying);
            if (jumpLabel != null) jumpLabel.text = flying ? upText : jumpText;
            if (flyIndicator != null) flyIndicator.color = flying ? flyOnColor : _flyOffColor;
        }

        private static void SetActive(GameObject[] objects, bool active)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject go = objects[i];
                if (go != null && go.activeSelf != active) go.SetActive(active);
            }
        }
    }
}
