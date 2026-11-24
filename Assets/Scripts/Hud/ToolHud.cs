using SimpleBox.Core;
using SimpleBox.Gameplay;
using SimpleBox.Gameplay.Tools;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Shows the active tool name, its hint line (optional) and the global thrust state (optional).
    /// Event-driven: labels change only on tool / hint / thrust-state events, using constant strings (no allocation).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolHud : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private ToolManager toolManager;
        [SerializeField] private Text toolLabel;

        [Header("References (optional)")]
        [Tooltip("Hidden when the active tool has no icon.")]
        [SerializeField] private Image toolIcon;
        [Tooltip("Tool hint / status line.")]
        [SerializeField] private Text hintLabel;
        [Tooltip("Thrust state source (global THRUST toggle).")]
        [SerializeField] private ThrusterRegistry thrusterRegistry;
        [Tooltip("Graphic tinted while any thruster is on (e.g. the THRUST button label).")]
        [SerializeField] private Graphic thrustIndicator;

        [Header("Colors")]
        [SerializeField] private Color thrustOnColor = new Color(0.4f, 1f, 0.45f, 1f);
        [SerializeField] private Color thrustOffColor = new Color(1f, 1f, 1f, 0.9f);

        private const string NoTool = "—";

        private BaseTool _hintSource;
        private bool _valid;
        private bool _subscribed;

        private void Awake()
        {
            string missing = string.Empty;
            if (toolManager == null) missing += " ToolManager";
            if (toolLabel == null) missing += " ToolLabel";
            if (missing.Length > 0)
            {
                Debug.LogError($"[{nameof(ToolHud)}] '{name}': required references not assigned:{missing}. Component disabled.", this);
                enabled = false;
                return;
            }
            if (thrustIndicator != null && thrusterRegistry == null)
            {
                Debug.LogWarning($"[{nameof(ToolHud)}] '{name}': Thrust Indicator set without Thruster Registry; indicator ignored.", this);
            }
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            toolManager.ActiveToolChanged += OnActiveToolChanged;
            if (thrusterRegistry != null) thrusterRegistry.StateChanged += OnThrustStateChanged;
            _subscribed = true;
        }

        private void OnDisable()
        {
            BindHintSource(null);
            if (!_subscribed) return;
            if (toolManager != null) toolManager.ActiveToolChanged -= OnActiveToolChanged;
            if (thrusterRegistry != null) thrusterRegistry.StateChanged -= OnThrustStateChanged;
            _subscribed = false;
        }

        private void Start()
        {
            if (!_valid) return;
            Show(toolManager.ActiveTool);
            OnThrustStateChanged(thrusterRegistry != null && thrusterRegistry.AnyOn);
        }

        private void OnActiveToolChanged(int index) => Show(toolManager.ActiveTool);

        private void Show(BaseTool tool)
        {
            toolLabel.text = tool != null && !string.IsNullOrEmpty(tool.DisplayName) ? Loc.T(tool.DisplayName) : NoTool;

            if (toolIcon != null)
            {
                Sprite sprite = tool != null ? tool.Icon : null;
                toolIcon.sprite = sprite;
                toolIcon.enabled = sprite != null;
            }

            BindHintSource(tool);
            OnHintChanged();
        }

        private void BindHintSource(BaseTool tool)
        {
            if (_hintSource == tool) return;
            if (_hintSource != null) _hintSource.HintChanged -= OnHintChanged;
            _hintSource = tool;
            if (_hintSource != null) _hintSource.HintChanged += OnHintChanged;
        }

        private void OnHintChanged()
        {
            if (hintLabel == null) return;
            hintLabel.text = _hintSource != null ? _hintSource.Hint : string.Empty;
        }

        private void OnThrustStateChanged(bool anyOn)
        {
            if (thrustIndicator == null || thrusterRegistry == null) return;
            thrustIndicator.color = anyOn ? thrustOnColor : thrustOffColor;
        }
    }
}
