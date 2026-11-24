using SimpleBox.Controls;
using SimpleBox.Net;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Multiplayer panel: NET opens it; HOST creates a session (shows the code), JOIN connects with the typed code,
    /// LEAVE exits. Status / code come from NetworkSessionService events.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionPanel : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private NetworkSessionService session;
        [SerializeField] private TouchButton toggleButton;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private InputField codeInput;
        [SerializeField] private TouchButton hostButton;
        [SerializeField] private TouchButton joinButton;
        [SerializeField] private TouchButton leaveButton;
        [SerializeField] private Text statusText;

        [Header("References (optional)")]
        [Tooltip("Large code label shown to the host (share it with friends).")]
        [SerializeField] private Text codeText;
        [Tooltip("Keyboard shortcuts are blocked while the code field is focused (desktop).")]
        [SerializeField] private MobileInputController input;

        private bool _valid;
        private bool _subscribed;

        private void Awake()
        {
            _valid = session != null && toggleButton != null && panelRoot != null && codeInput != null &&
                     hostButton != null && joinButton != null && leaveButton != null && statusText != null;
            if (!_valid)
            {
                Debug.LogError($"[{nameof(SessionPanel)}] '{name}': required references not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            panelRoot.SetActive(false);
            statusText.text = "Не в сети";
            if (codeText != null) codeText.text = string.Empty;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            session.StatusChanged += OnStatus;
            session.CodeChanged += OnCode;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (session != null)
            {
                session.StatusChanged -= OnStatus;
                session.CodeChanged -= OnCode;
            }
            _subscribed = false;
        }

        private void Update()
        {
            if (toggleButton.ConsumePress()) panelRoot.SetActive(!panelRoot.activeSelf);
            bool open = panelRoot.activeSelf;
            if (input != null) input.KeyboardBlocked = open && codeInput.isFocused;
            if (!open) return;

            if (hostButton.ConsumePress()) session.Host();
            if (joinButton.ConsumePress()) session.Join(codeInput.text);
            if (leaveButton.ConsumePress()) session.Leave();
        }

        private void OnStatus(string message) => statusText.text = message;

        private void OnCode(string code)
        {
            if (codeText != null) codeText.text = string.IsNullOrEmpty(code) ? string.Empty : code;
        }
    }
}
