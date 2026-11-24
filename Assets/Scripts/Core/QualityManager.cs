using UnityEngine;

namespace SimpleBox.Core
{
    /// <summary>
    /// Picks the Minimum or Recommended profile for the current device and applies frame rate,
    /// Quality level and global physics settings. Other systems read limits through Profile.
    /// Profile initializes lazily on first access, so readers do not depend on Awake order.
    /// Detection:
    ///   iOS     — recommended if deviceModel "iPhoneNN,x" has NN >= recommendedIosModelMajor (14 = iPhone 13).
    ///   Android — recommended if RAM >= recommendedAndroidMemoryMb and CPU cores >= recommendedAndroidCores.
    ///   Editor / desktop — editorTier.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class QualityManager : MonoBehaviour
    {
        public enum Tier { Auto = 0, Minimum = 1, Recommended = 2 }

        [Header("Profiles (required)")]
        [SerializeField] private QualityProfile minimum;
        [SerializeField] private QualityProfile recommended;

        [Header("Selection")]
        [Tooltip("Auto = detect by hardware. Force a tier to test limits on any device.")]
        [SerializeField] private Tier forceTier = Tier.Auto;
        [Tooltip("Tier used in the Editor / on desktop when Force Tier = Auto.")]
        [SerializeField] private Tier editorTier = Tier.Recommended;

        [Header("Detection thresholds")]
        [SerializeField, Min(1)] private int recommendedAndroidMemoryMb = 5500;
        [SerializeField, Min(1)] private int recommendedAndroidCores = 8;
        [SerializeField, Min(1)] private int recommendedIosModelMajor = 14;

        private QualityProfile _active;
        private Tier _activeTier;
        private bool _initialized;

        public bool IsValid => minimum != null && recommended != null;
        public Tier ActiveTier
        {
            get
            {
                EnsureInitialized();
                return _activeTier;
            }
        }

        /// <summary>Active profile. Null only if the manager is misconfigured.</summary>
        public QualityProfile Profile
        {
            get
            {
                EnsureInitialized();
                return _active;
            }
        }

        private void Awake()
        {
            if (!IsValid)
            {
                Debug.LogError($"[{nameof(QualityManager)}] '{name}': Minimum / Recommended profile not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_initialized || !IsValid) return;
            _initialized = true;

            _activeTier = forceTier != Tier.Auto ? forceTier : Detect();
            _active = _activeTier == Tier.Recommended ? recommended : minimum;
            Apply(_active);

            Debug.Log($"[{nameof(QualityManager)}] Tier: {_activeTier} ('{_active.name}'), " +
                      $"RAM {SystemInfo.systemMemorySize} MB, cores {SystemInfo.processorCount}, model '{SystemInfo.deviceModel}'.");
        }

        private Tier Detect()
        {
#if UNITY_EDITOR
            return editorTier == Tier.Auto ? Tier.Recommended : editorTier;
#elif UNITY_IOS
            return ParseIosModelMajor(SystemInfo.deviceModel) >= recommendedIosModelMajor ? Tier.Recommended : Tier.Minimum;
#elif UNITY_ANDROID
            return SystemInfo.systemMemorySize >= recommendedAndroidMemoryMb &&
                   SystemInfo.processorCount >= recommendedAndroidCores
                ? Tier.Recommended
                : Tier.Minimum;
#else
            return editorTier == Tier.Auto ? Tier.Recommended : editorTier;
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (recommendedAndroidMemoryMb < 1) recommendedAndroidMemoryMb = 1;
            if (recommendedAndroidCores < 1) recommendedAndroidCores = 1;
            if (recommendedIosModelMajor < 1) recommendedIosModelMajor = 1;
        }
#endif

        /// <summary>"iPhone14,5" -> 14. Non-iPhone or unparsable -> 0.</summary>
        private static int ParseIosModelMajor(string model)
        {
            const string prefix = "iPhone";
            if (string.IsNullOrEmpty(model) || !model.StartsWith(prefix)) return 0;
            int value = 0;
            for (int i = prefix.Length; i < model.Length; i++)
            {
                char c = model[i];
                if (c < '0' || c > '9') break;
                value = value * 10 + (c - '0');
            }
            return value;
        }

        private static void Apply(QualityProfile p)
        {
            if (p.qualityLevel >= 0 && p.qualityLevel < QualitySettings.names.Length)
                QualitySettings.SetQualityLevel(p.qualityLevel, true);

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = p.targetFrameRate;

            Time.fixedDeltaTime = p.fixedTimestep;
            Time.maximumDeltaTime = p.maximumDeltaTime;

            Physics.defaultSolverIterations = p.solverIterations;
            Physics.defaultSolverVelocityIterations = p.solverVelocityIterations;
            Physics.sleepThreshold = p.sleepThreshold;
            Physics.defaultMaxDepenetrationVelocity = p.maxDepenetrationVelocity;
            Physics.defaultMaxAngularSpeed = p.maxAngularSpeed;
        }
    }
}
