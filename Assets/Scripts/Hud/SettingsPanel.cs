using System;
using SimpleBox.Controls;
using SimpleBox.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Settings menu: the gear button or Esc opens it; one row per GameSettings.Setting with &lt; / &gt; steppers and the
    /// current value. Labels come from GameSettings (precomputed); refreshed on open and on change only.
    /// While open, keyboard shortcuts of the game are blocked (Esc still closes the menu).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPanel : MonoBehaviour
    {
        [Serializable]
        public sealed class Row
        {
            public GameSettings.Setting setting;
            public Text valueLabel;
            public TouchButton prevButton;
            public TouchButton nextButton;
        }

        private const string RestartNote = "Качество применится после перезапуска игры";

        [Header("References (required)")]
        [SerializeField] private GameSettings settings;
        [SerializeField] private MobileInputController input;
        [SerializeField] private TouchButton toggleButton;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Row[] rows = new Row[0];

        [Header("References (optional)")]
        [SerializeField] private TouchButton closeButton;
        [SerializeField] private TouchButton resetButton;
        [Tooltip("Shows 'applies after restart' when the quality choice differs from the running tier.")]
        [SerializeField] private Text noteLabel;
        [Tooltip("Restarts the first-launch tutorial and closes the menu.")]
        [SerializeField] private TouchButton tutorialButton;
        [SerializeField] private OnboardingGuide onboarding;
        [Tooltip("\"В МЕНЮ\": raises ExitToMenuRequested (MainMenu). Hidden while the main menu itself is shown.")]
        [SerializeField] private TouchButton exitToMenuButton;

        private bool _valid;
        private bool _subscribed;

        public bool IsOpen => _valid && panelRoot.activeSelf;

        /// <summary>The player pressed \"В МЕНЮ\" (the panel is already closed).</summary>
        public event Action ExitToMenuRequested;

        public void Open() { if (_valid) SetOpen(true); }
        public void Close() { if (_valid && panelRoot.activeSelf) SetOpen(false); }

        /// <summary>Shows / hides the \"В МЕНЮ\" button (hidden while the main menu is shown).</summary>
        public void SetExitAvailable(bool available)
        {
            if (exitToMenuButton != null && exitToMenuButton.gameObject.activeSelf != available)
                exitToMenuButton.gameObject.SetActive(available);
        }

        private void Awake()
        {
            _valid = settings != null && input != null && toggleButton != null && panelRoot != null && rows != null && rows.Length > 0;
            for (int i = 0; _valid && i < rows.Length; i++)
            {
                Row r = rows[i];
                if (r == null || r.valueLabel == null || r.prevButton == null || r.nextButton == null) _valid = false;
            }
            if (!_valid)
            {
                Debug.LogError($"[{nameof(SettingsPanel)}] '{name}': required references not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            panelRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            settings.Changed += OnChanged;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (_subscribed && settings != null) settings.Changed -= OnChanged;
            _subscribed = false;
            if (input != null && panelRoot != null && panelRoot.activeSelf) input.MenuOpen = false;
        }

        private void Update()
        {
            bool escape = input.ConsumeSettingsMenu();
            if (toggleButton.ConsumePress() | escape) SetOpen(!panelRoot.activeSelf);
            if (!panelRoot.activeSelf) return;

            if (closeButton != null && closeButton.ConsumePress())
            {
                SetOpen(false);
                return;
            }
            if (resetButton != null && resetButton.ConsumePress()) settings.ResetAll();
            if (tutorialButton != null && onboarding != null && tutorialButton.ConsumePress())
            {
                SetOpen(false);
                onboarding.Restart();
                return;
            }
            if (exitToMenuButton != null && exitToMenuButton.isActiveAndEnabled && exitToMenuButton.ConsumePress())
            {
                SetOpen(false);
                ExitToMenuRequested?.Invoke();
                return;
            }

            for (int i = 0; i < rows.Length; i++)
            {
                Row r = rows[i];
                if (r.prevButton.ConsumePress()) settings.Step(r.setting, -1);
                if (r.nextButton.ConsumePress()) settings.Step(r.setting, 1);
            }
        }

        private void SetOpen(bool open)
        {
            panelRoot.SetActive(open);
            if (open)
            {
                RefreshAll();
            }
            input.MenuOpen = open;
        }

        private void OnChanged(GameSettings.Setting s)
        {
            if (!panelRoot.activeSelf) return;
            if (s == GameSettings.Setting.Language)
            {
                RefreshAll(); // every value label switches language
                return;
            }
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].setting == s) rows[i].valueLabel.text = settings.LabelOf(s);
            }
            RefreshNote();
        }

        private void RefreshAll()
        {
            for (int i = 0; i < rows.Length; i++) rows[i].valueLabel.text = settings.LabelOf(rows[i].setting);
            RefreshNote();
        }

        private void RefreshNote()
        {
            if (noteLabel == null) return;
            bool pending = settings.QualityRestartPending;
            if (noteLabel.gameObject.activeSelf != pending) noteLabel.gameObject.SetActive(pending);
            if (pending) noteLabel.text = Loc.T(RestartNote);
        }
    }
}
