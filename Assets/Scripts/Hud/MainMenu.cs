using SimpleBox.Controls;
using SimpleBox.Core;
using SimpleBox.Gameplay;
using SimpleBox.Net;
using SimpleBox.World;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Main menu shown over the game scene (no scene reload, so the NetworkManager and the world stay alive).
    /// While shown: the HUD canvas group is hidden and non-interactive, game keys are blocked, the player camera is
    /// replaced by a slowly orbiting menu camera.
    /// Buttons: ИГРАТЬ / ПРОДОЛЖИТЬ (hide menu), МУЛЬТИПЛЕЕР (session panel; the menu hides once a session starts),
    /// ЗАГРУЗИТЬ (save panel; hides after a load), НАСТРОЙКИ (settings panel), О ИГРЕ (version + privacy policy).
    /// Those panels have their own canvases (drawn above the menu, ignoring the hidden HUD group).
    /// "В МЕНЮ" in the settings panel returns here: autosave offline / on the host, or leave the session.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenu : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private GameObject menuRoot;
        [SerializeField] private CanvasGroup hudGroup;
        [SerializeField] private MobileInputController input;
        [SerializeField] private TouchButton playButton;
        [SerializeField] private Text playLabel;
        [SerializeField] private TouchButton multiplayerButton;
        [SerializeField] private TouchButton loadButton;
        [SerializeField] private TouchButton settingsButton;
        [SerializeField] private SettingsPanel settingsPanel;
        [SerializeField] private SaveMenuPanel saveMenu;
        [SerializeField] private SessionPanel sessionPanel;
        [SerializeField] private NetworkSessionService session;
        [SerializeField] private SaveService saves;
        [SerializeField] private EntityRegistry registry;
        [SerializeField] private SandboxActions actions;

        [Header("References (optional)")]
        [SerializeField] private TouchButton aboutButton;
        [SerializeField] private GameObject aboutRoot;
        [SerializeField] private TouchButton aboutCloseButton;
        [SerializeField] private TouchButton privacyButton;
        [SerializeField] private Text versionLabel;
        [SerializeField] private InputField nicknameField;
        [SerializeField] private OnboardingGuide onboarding;
        [SerializeField] private PhysicsGun physicsGun;

        [Header("Cameras (optional: without them the player view stays as is)")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Camera menuCamera;
        [SerializeField] private Vector3 orbitCenter = new Vector3(0f, 1f, 0f);
        [SerializeField, Min(1f)] private float orbitRadius = 18f;
        [SerializeField] private float orbitHeight = 7f;
        [SerializeField] private float orbitSpeed = 6f;

        [Header("Links")]
        [Tooltip("Privacy policy URL (required by Google Play / App Store). Empty = the button is hidden.")]
        [SerializeField] private string privacyPolicyUrl = string.Empty;

        private const string PlayText = "ИГРАТЬ";
        private const string ContinueText = "ПРОДОЛЖИТЬ";

        private bool _valid;
        private bool _shown;
        private bool _wasInSession;
        private float _orbitAngle;
        private bool _subscribed;

        public bool IsShown => _shown;

        private void Awake()
        {
            _valid = menuRoot != null && hudGroup != null && input != null && playButton != null && playLabel != null &&
                     multiplayerButton != null && loadButton != null && settingsButton != null && settingsPanel != null &&
                     saveMenu != null && sessionPanel != null && session != null && saves != null && registry != null && actions != null;
            if (!_valid)
            {
                Debug.LogError($"[{nameof(MainMenu)}] '{name}': required references not assigned. Component disabled.", this);
                enabled = false;
                return;
            }

            if (versionLabel != null) versionLabel.text = "v" + Application.version;
            if (aboutRoot != null) aboutRoot.SetActive(false);
            if (privacyButton != null) privacyButton.gameObject.SetActive(!string.IsNullOrWhiteSpace(privacyPolicyUrl));
            if (nicknameField != null)
            {
                nicknameField.characterLimit = PlayerProfile.MaxLength;
                nicknameField.text = PlayerProfile.Nickname;
            }
            if (menuCamera != null) menuCamera.enabled = false;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            settingsPanel.ExitToMenuRequested += Show;
            Loc.Changed += RefreshPlayLabel;
            saveMenu.Loaded += OnSlotLoaded;
            if (nicknameField != null) nicknameField.onEndEdit.AddListener(OnNicknameEdited);
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (settingsPanel != null) settingsPanel.ExitToMenuRequested -= Show;
            Loc.Changed -= RefreshPlayLabel;
            if (saveMenu != null) saveMenu.Loaded -= OnSlotLoaded;
            if (nicknameField != null) nicknameField.onEndEdit.RemoveListener(OnNicknameEdited);
            _subscribed = false;
        }

        private void Start()
        {
            if (_valid) Open(false);
        }

        private void Update()
        {
            if (!_shown) return;

            OrbitCamera();

            // A session started from the menu (host or join): go straight into the game.
            bool inSession = session.IsInSession;
            if (inSession && !_wasInSession)
            {
                _wasInSession = true;
                Hide();
                return;
            }
            _wasInSession = inSession;

            if (aboutRoot != null && aboutRoot.activeSelf)
            {
                if (aboutCloseButton != null && aboutCloseButton.ConsumePress()) aboutRoot.SetActive(false);
                if (privacyButton != null && privacyButton.ConsumePress()) Application.OpenURL(privacyPolicyUrl);
                return;
            }

            // A panel opened from the menu is modal: drop presses on the menu buttons instead of acting on them later.
            if (settingsPanel.IsOpen || saveMenu.IsOpen || sessionPanel.IsOpen)
            {
                DiscardPresses();
                return;
            }

            if (playButton.ConsumePress()) Hide();
            else if (multiplayerButton.ConsumePress()) sessionPanel.Open();
            else if (loadButton.ConsumePress()) saveMenu.Open();
            else if (settingsButton.ConsumePress()) settingsPanel.Open();
            else if (aboutButton != null && aboutRoot != null && aboutButton.ConsumePress()) aboutRoot.SetActive(true);
        }

        // ------------------------------------------------------------------ Show / hide

        /// <summary>Returns to the menu: leaves a multiplayer session (or autosaves offline / as host) and covers the game.</summary>
        public void Show() => Open(true);

        private void Open(bool leaveGame)
        {
            if (!_valid || _shown) return;

            if (physicsGun != null) physicsGun.Drop();
            if (leaveGame)
            {
                if (session.IsInSession) session.Leave();
                else saves.Autosave();
            }

            _shown = true;
            _wasInSession = session.IsInSession;
            menuRoot.SetActive(true);
            hudGroup.alpha = 0f;
            hudGroup.interactable = false;
            hudGroup.blocksRaycasts = false;
            input.MainMenuOpen = true;
            settingsPanel.SetExitAvailable(false);
            RefreshPlayLabel();

            if (menuCamera != null)
            {
                menuCamera.enabled = true;
                OrbitCamera();
                if (playerCamera != null) playerCamera.enabled = false;
            }
        }

        public void Hide()
        {
            if (!_valid || !_shown) return;
            _shown = false;
            menuRoot.SetActive(false);
            if (aboutRoot != null) aboutRoot.SetActive(false);
            hudGroup.alpha = 1f;
            hudGroup.interactable = true;
            hudGroup.blocksRaycasts = true;
            input.MainMenuOpen = false;
            settingsPanel.Close();
            settingsPanel.SetExitAvailable(true);

            if (menuCamera != null)
            {
                if (playerCamera != null) playerCamera.enabled = true;
                menuCamera.enabled = false;
            }
            if (onboarding != null) onboarding.StartIfFirstRun();
        }

        private void DiscardPresses()
        {
            playButton.ConsumePress();
            multiplayerButton.ConsumePress();
            loadButton.ConsumePress();
            settingsButton.ConsumePress();
            if (aboutButton != null) aboutButton.ConsumePress();
        }

        private void RefreshPlayLabel()
        {
            playLabel.text = Loc.T(registry.CountOwnedBy(actions.LocalOwnerId) > 0 ? ContinueText : PlayText);
        }

        private void OnSlotLoaded(int slot)
        {
            if (_shown) Hide();
        }

        private void OnNicknameEdited(string value)
        {
            PlayerProfile.Nickname = value;
            nicknameField.text = PlayerProfile.Nickname; // shows the sanitized / unchanged name
        }

        private void OrbitCamera()
        {
            if (menuCamera == null) return;
            _orbitAngle = Mathf.Repeat(_orbitAngle + orbitSpeed * Time.unscaledDeltaTime, 360f);
            float rad = _orbitAngle * Mathf.Deg2Rad;
            Vector3 pos = orbitCenter + new Vector3(Mathf.Sin(rad) * orbitRadius, orbitHeight, Mathf.Cos(rad) * orbitRadius);
            Transform t = menuCamera.transform;
            t.SetPositionAndRotation(pos, Quaternion.LookRotation(orbitCenter - pos, Vector3.up));
        }
    }
}
