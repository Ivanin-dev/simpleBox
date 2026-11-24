using UnityEngine;

namespace SimpleBox.Core
{
    /// <summary>
    /// Device-tier budget: frame rate, physics settings and gameplay limits.
    /// Two assets are expected: Minimum (4–5 year old mid-range) and Recommended (last 2–3 years).
    /// Create: Assets → Create → SimpleBox → Quality Profile.
    /// </summary>
    [CreateAssetMenu(fileName = "QualityProfile", menuName = "SimpleBox/Quality Profile")]
    public sealed class QualityProfile : ScriptableObject
    {
        [Header("Frame")]
        [Min(15)] public int targetFrameRate = 60;
        [Tooltip("Index in Project Settings → Quality. -1 = do not change.")]
        public int qualityLevel = -1;

        [Header("Physics")]
        [Tooltip("Time.fixedDeltaTime. 0.02 = 50 Hz, 0.0333 = 30 Hz.")]
        [Range(0.01f, 0.05f)] public float fixedTimestep = 0.02f;
        [Tooltip("Time.maximumDeltaTime: caps physics catch-up steps after a hitch (prevents the spiral of death).")]
        [Range(0.05f, 0.33f)] public float maximumDeltaTime = 0.1f;
        [Range(1, 20)] public int solverIterations = 6;
        [Range(1, 10)] public int solverVelocityIterations = 1;
        [Tooltip("Energy below which a body goes to sleep. Higher = sleeps sooner = cheaper.")]
        [Min(0f)] public float sleepThreshold = 0.005f;
        [Tooltip("Max speed at which overlapping bodies are pushed apart (m/s). Low values stop 'explosions' of jammed props.")]
        [Min(0.1f)] public float maxDepenetrationVelocity = 3f;

        [Header("Limits")]
        [Min(1)] public int maxEntitiesTotal = 150;
        [Min(1)] public int maxEntitiesPerOwner = 40;
        [Min(1)] public int maxThrusters = 32;
        [Min(1)] public int maxConstraintsPerEntity = 8;
        [Min(1)] public int maxConstraintsTotal = 150;

        [Header("Physics guard")]
        [Min(1f)] public float maxLinearSpeed = 60f;
        [Min(1f)] public float maxAngularSpeed = 25f;
    }
}
