using SimpleBox.Core;
using System;
using SimpleBox.Controls;
using SimpleBox.Gameplay.Tools;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Tool options panel. The active tool describes its settings (BaseTool.SettingCount / GetSettingInfo);
    /// the panel fills a fixed pool of rows: label, value, − and + buttons (steppers step, toggles flip, choices cycle).
    /// The open button (SET) is hidden for tools without settings. The panel closes on tool change.
    /// Value labels are built once when the panel opens (user action); pressing −/+ only swaps cached strings.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolSettingsPanel : MonoBehaviour
    {
        [Serializable]
        public sealed class SettingRow
        {
            public GameObject root;
            public Text label;
            public Text value;
            public TouchButton minus;
            public TouchButton plus;

            public bool IsComplete => root != null && label != null && value != null && minus != null && plus != null;
        }

        [Header("References (required)")]
        [SerializeField] private ToolManager toolManager;
        [Tooltip("SET button that opens / closes the panel.")]
        [SerializeField] private TouchButton openButton;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text title;
        [SerializeField] private SettingRow[] rows = new SettingRow[0];

        [Header("References (optional)")]
        [SerializeField] private TouchButton closeButton;

        [Header("Text")]
        [SerializeField] private string onText = "ВКЛ";
        [SerializeField] private string offText = "ВЫКЛ";
        [Tooltip("Steppers with more values than this are not shown (protects the label cache).")]
        [SerializeField, Min(2)] private int maxStepperValues = 201;

        private BaseTool _tool;
        private string[][] _labels;
        private int[] _indices;
        private ToolSettingInfo[] _infos;
        private int _visibleRows;
        private bool _open;
        private bool _valid;
        private bool _subscribed;

        public bool IsOpen => _open;

        private void Awake()
        {
            string missing = string.Empty;
            if (toolManager == null) missing += " ToolManager";
            if (openButton == null) missing += " OpenButton";
            if (panelRoot == null) missing += " PanelRoot";
            if (title == null) missing += " Title";
            if (rows == null || rows.Length == 0) missing += " Rows";
            else
            {
                for (int i = 0; i < rows.Length; i++)
                {
                    if (rows[i] == null || !rows[i].IsComplete) missing += $" Rows[{i}]";
                }
            }
            if (missing.Length > 0)
            {
                Debug.LogError($"[{nameof(ToolSettingsPanel)}] '{name}': required references not assigned:{missing}. Component disabled.", this);
                enabled = false;
                return;
            }

            _labels = new string[rows.Length][];
            _indices = new int[rows.Length];
            _infos = new ToolSettingInfo[rows.Length];
            panelRoot.SetActive(false);
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            toolManager.ActiveToolChanged += OnActiveToolChanged;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (toolManager != null) toolManager.ActiveToolChanged -= OnActiveToolChanged;
            _subscribed = false;
        }

        private void Start()
        {
            if (_valid) Bind(toolManager.ActiveTool);
        }

        private void Update()
        {
            if (openButton.isActiveAndEnabled && openButton.ConsumePress())
            {
                if (_open) Close();
                else Open();
            }
            if (closeButton != null && closeButton.isActiveAndEnabled && closeButton.ConsumePress()) Close();
            if (!_open) return;

            for (int i = 0; i < _visibleRows; i++)
            {
                SettingRow row = rows[i];
                int delta = (row.plus.ConsumePress() ? 1 : 0) - (row.minus.ConsumePress() ? 1 : 0);
                if (delta != 0) Change(i, delta);
            }
        }

        private void OnActiveToolChanged(int index) => Bind(toolManager.ActiveTool);

        private void Bind(BaseTool tool)
        {
            Close();
            _tool = tool;
            bool hasSettings = tool != null && tool.SettingCount > 0;
            if (openButton.gameObject.activeSelf != hasSettings) openButton.gameObject.SetActive(hasSettings);
            if (hasSettings && tool.SettingCount > rows.Length)
            {
                Debug.LogWarning($"[{nameof(ToolSettingsPanel)}] Tool '{tool.DisplayName}' has {tool.SettingCount} settings, " +
                                 $"only {rows.Length} rows exist. Extra settings are hidden.", this);
            }
        }

        public void Open()
        {
            if (_tool == null || _tool.SettingCount == 0) return;

            _visibleRows = 0;
            int count = Mathf.Min(_tool.SettingCount, rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                bool show = i < count && BuildRow(i);
                rows[i].root.SetActive(show);
                if (show) _visibleRows = i + 1;
            }

            title.text = Loc.T(_tool.DisplayName);
            panelRoot.SetActive(true);
            _open = true;
        }

        public void Close()
        {
            if (!_valid) return;
            _open = false;
            if (panelRoot.activeSelf) panelRoot.SetActive(false);
        }

        /// <summary>Builds labels for one row (allocates; called on Open only).</summary>
        private bool BuildRow(int i)
        {
            ToolSettingInfo info = _tool.GetSettingInfo(i);
            int n = info.ValueCount;
            if (n <= 0 || (info.Kind == ToolSettingKind.Stepper && n > maxStepperValues)) return false;

            string[] labels;
            switch (info.Kind)
            {
                case ToolSettingKind.Toggle:
                    labels = new[] { Loc.T(offText), Loc.T(onText) };
                    break;
                case ToolSettingKind.Choice:
                    labels = new string[info.Choices.Length]; // translated copy (Open only)
                    for (int k = 0; k < labels.Length; k++) labels[k] = Loc.T(info.Choices[k]);
                    break;
                default:
                    labels = new string[n];
                    for (int k = 0; k < n; k++)
                        labels[k] = info.FromIndex(k).ToString(string.IsNullOrEmpty(info.Format) ? "0.##" : info.Format) + Loc.T(info.Unit);
                    break;
            }

            _infos[i] = info;
            _labels[i] = labels;
            _indices[i] = info.ToIndex(_tool.GetSettingValue(i));
            rows[i].label.text = Loc.T(info.Label);
            rows[i].value.text = labels[_indices[i]];
            return true;
        }

        private void Change(int i, int delta)
        {
            ToolSettingInfo info = _infos[i];
            int n = _labels[i].Length;
            int index = _indices[i];

            switch (info.Kind)
            {
                case ToolSettingKind.Toggle:
                    index = index == 0 ? 1 : 0;
                    break;
                case ToolSettingKind.Choice:
                    index = ((index + delta) % n + n) % n;
                    break;
                default:
                    index = Mathf.Clamp(index + delta, 0, n - 1);
                    break;
            }

            _tool.SetSettingValue(i, info.FromIndex(index));
            _indices[i] = info.ToIndex(_tool.GetSettingValue(i)); // the tool may refuse a value
            rows[i].value.text = _labels[i][_indices[i]];
        }
    }
}
