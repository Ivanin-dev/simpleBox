using SimpleBox.Gameplay;
using SimpleBox.World;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Short status message (Undo / Clear / Thrust) that disappears after a delay. Event-driven, no allocation:
    /// the label shows the constant string it receives and is cleared by a timer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToastHud : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private PlayerCommands commands;
        [SerializeField] private Text label;

        [Header("References (optional)")]
        [Tooltip("Flight messages (noclip on/off/forbidden).")]
        [SerializeField] private PlayerController player;
        [Tooltip("Save / load messages.")]
        [SerializeField] private SaveService saves;

        [Header("Timing")]
        [SerializeField, Min(0.1f)] private float duration = 1.6f;

        private float _hideTimer;
        private bool _valid;
        private bool _subscribed;

        private void Awake()
        {
            if (commands == null || label == null)
            {
                Debug.LogError($"[{nameof(ToastHud)}] '{name}': Commands / Label not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            label.text = string.Empty;
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            commands.Message += Show;
            if (player != null) player.Notice += Show;
            if (saves != null) saves.Message += Show;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (commands != null) commands.Message -= Show;
            if (player != null) player.Notice -= Show;
            if (saves != null) saves.Message -= Show;
            _subscribed = false;
        }

        private void Update()
        {
            if (_hideTimer <= 0f) return;
            _hideTimer -= Time.unscaledDeltaTime;
            if (_hideTimer <= 0f) label.text = string.Empty;
        }

        private void Show(string message)
        {
            label.text = message;
            _hideTimer = duration;
        }
    }
}
