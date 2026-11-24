using System;
using SimpleBox.Controls;
using UnityEngine;

namespace SimpleBox.Core
{
    /// <summary>
    /// User settings stored in PlayerPrefs and applied to the game:
    ///   quality tier (read by QualityManager at startup: applies after a restart),
    ///   frame rate cap, look sensitivity, invert Y, master volume, on-screen control size, FPS counter.
    /// Every setting is an index into a fixed list of choices, so the UI can step through values and show
    /// precomputed labels (no string building at runtime).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-900)]
    public sealed class GameSettings : MonoBehaviour
    {
        public enum Setting { Quality = 0, FrameRate, Sensitivity, InvertY, Volume, ControlSize, FpsCounter, Language }
        public const int SettingCount = 8;

        // ---- Choices (value tables + labels)
        private static readonly int[] FrameRates = { 0, 30, 60 };
        private static readonly string[] FrameRateLabels = { "По профилю", "30", "60" };
        private static readonly string[] QualityLabels = { "Авто", "Минимум", "Рекомендуемое" };
        private static readonly float[] Sensitivities = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f };
        private static readonly string[] SensitivityLabels = { "0.25", "0.5", "0.75", "1.0", "1.25", "1.5", "1.75", "2.0", "2.5", "3.0" };
        private static readonly string[] OffOn = { "Выкл", "Вкл" };
        private static readonly string[] VolumeLabels = { "0%", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%" };
        private static readonly float[] ControlSizes = { 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.3f };
        private static readonly string[] ControlSizeLabels = { "80%", "90%", "100%", "110%", "120%", "130%" };
        private static readonly string[] LanguageLabels = { "Авто", "Русский", "English" };

        private static readonly string[] Names =
        {
            "Качество графики", "Лимит FPS", "Чувствительность", "Инверсия Y", "Громкость", "Размер кнопок", "Счётчик FPS", "Язык"
        };
        private static readonly string[][] Labels =
        {
            QualityLabels, FrameRateLabels, SensitivityLabels, OffOn, VolumeLabels, ControlSizeLabels, OffOn, LanguageLabels
        };
        private static readonly int[] Defaults = { 0, 0, 3, 0, 10, 2, 0, 0 };
        private static readonly string[] Keys =
        {
            "sb.quality", "sb.fps", "sb.sens", "sb.invert", "sb.volume", "sb.controls", "sb.fpscounter", Loc.PrefsKey
        };

        [Header("References (required)")]
        [SerializeField] private QualityManager quality;
        [SerializeField] private MobileInputController input;

        [Header("References (optional)")]
        [Tooltip("On-screen controls scaled by the Control Size setting (around their own pivots).")]
        [SerializeField] private RectTransform[] scaledControls = new RectTransform[0];
        [Tooltip("FPS counter object shown by the FPS Counter setting.")]
        [SerializeField] private GameObject fpsCounter;

        private readonly int[] _values = new int[SettingCount];
        private Vector3[] _baseScales;
        private bool _valid;

        /// <summary>Arg: the setting that changed.</summary>
        public event Action<Setting> Changed;

        public bool IsValid => _valid;

        /// <summary>Tier saved by the user (QualityManager reads it once at startup).</summary>
        public static QualityManager.Tier SavedQualityTier
        {
            get
            {
                int v = PlayerPrefs.GetInt(Keys[(int)Setting.Quality], Defaults[(int)Setting.Quality]);
                return v == 1 ? QualityManager.Tier.Minimum : v == 2 ? QualityManager.Tier.Recommended : QualityManager.Tier.Auto;
            }
        }

        private void Awake()
        {
            if (quality == null || input == null)
            {
                Debug.LogError($"[{nameof(GameSettings)}] '{name}': Quality / Input not assigned. Component disabled.", this);
                enabled = false;
                return;
            }

            _baseScales = new Vector3[scaledControls.Length];
            for (int i = 0; i < scaledControls.Length; i++)
                _baseScales[i] = scaledControls[i] != null ? scaledControls[i].localScale : Vector3.one;

            for (int i = 0; i < SettingCount; i++)
                _values[i] = Mathf.Clamp(PlayerPrefs.GetInt(Keys[i], Defaults[i]), 0, Labels[i].Length - 1);
            _valid = true;
        }

        private void Start()
        {
            if (!_valid) return;
            for (int i = 0; i < SettingCount; i++) Apply((Setting)i);
        }

        // ------------------------------------------------------------------ Access

        public static string NameOf(Setting s) => Loc.T(Names[(int)s]);
        public int ChoiceCount(Setting s) => Labels[(int)s].Length;
        public int Get(Setting s) => _values[(int)s];
        public string LabelOf(Setting s) => Loc.T(Labels[(int)s][_values[(int)s]]);

        /// <summary>True if the setting takes effect only after a restart.</summary>
        public static bool NeedsRestart(Setting s) => s == Setting.Quality;

        /// <summary>True if the saved quality choice differs from the tier running now.</summary>
        public bool QualityRestartPending
        {
            get
            {
                QualityManager.Tier saved = SavedQualityTier;
                return saved != QualityManager.Tier.Auto && saved != quality.ActiveTier;
            }
        }

        /// <summary>Steps the setting by +1 / -1 (wraps around), saves and applies it.</summary>
        public void Step(Setting s, int direction)
        {
            if (!_valid || direction == 0) return;
            int count = Labels[(int)s].Length;
            Set(s, ((_values[(int)s] + (direction > 0 ? 1 : -1)) % count + count) % count);
        }

        public void Set(Setting s, int index)
        {
            if (!_valid) return;
            int i = (int)s;
            index = Mathf.Clamp(index, 0, Labels[i].Length - 1);
            if (_values[i] == index) return;
            _values[i] = index;
            PlayerPrefs.SetInt(Keys[i], index);
            PlayerPrefs.Save();
            Apply(s);
            Changed?.Invoke(s);
        }

        /// <summary>Restores every default.</summary>
        public void ResetAll()
        {
            for (int i = 0; i < SettingCount; i++) Set((Setting)i, Defaults[i]);
        }

        // ------------------------------------------------------------------ Apply

        private void Apply(Setting s)
        {
            int v = _values[(int)s];
            switch (s)
            {
                case Setting.Quality:
                    break; // read by QualityManager at startup
                case Setting.FrameRate:
                    QualityProfile profile = quality.Profile;
                    int profileRate = profile != null ? profile.targetFrameRate : 60;
                    Application.targetFrameRate = FrameRates[v] > 0 ? FrameRates[v] : profileRate;
                    break;
                case Setting.Sensitivity:
                    input.LookSensitivityScale = Sensitivities[v];
                    break;
                case Setting.InvertY:
                    input.InvertY = v == 1;
                    break;
                case Setting.Volume:
                    AudioListener.volume = v / 10f;
                    break;
                case Setting.ControlSize:
                    float k = ControlSizes[v];
                    for (int i = 0; i < scaledControls.Length; i++)
                    {
                        if (scaledControls[i] != null) scaledControls[i].localScale = _baseScales[i] * k;
                    }
                    break;
                case Setting.FpsCounter:
                    if (fpsCounter != null) fpsCounter.SetActive(v == 1);
                    break;
                case Setting.Language:
                    Loc.SetChoice(v); // 0 auto, 1 Russian, 2 English; static labels re-apply on Loc.Changed
                    break;
            }
        }
    }
}
