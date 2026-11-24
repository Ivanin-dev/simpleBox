using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Average FPS over a short window, shown with precomputed labels (no allocations).
    /// Shown / hidden by GameSettings (FPS Counter setting).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FpsCounter : MonoBehaviour
    {
        private const int MaxShown = 240;

        [SerializeField] private Text label;
        [SerializeField, Min(0.1f)] private float window = 0.5f;
        [Tooltip("Below this the counter turns red.")]
        [SerializeField, Min(1)] private int warnBelow = 28;

        private static string[] _labels;
        private float _elapsed;
        private int _frames;

        private void Awake()
        {
            if (label == null)
            {
                Debug.LogError($"[{nameof(FpsCounter)}] '{name}': Label not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            if (_labels == null)
            {
                _labels = new string[MaxShown + 1];
                for (int i = 0; i <= MaxShown; i++) _labels[i] = i + " FPS";
            }
        }

        private void OnEnable()
        {
            _elapsed = 0f;
            _frames = 0;
        }

        private void Update()
        {
            _elapsed += Time.unscaledDeltaTime;
            _frames++;
            if (_elapsed < window) return;

            int fps = Mathf.Clamp(Mathf.RoundToInt(_frames / _elapsed), 0, MaxShown);
            label.text = _labels[fps];
            label.color = fps < warnBelow ? new Color(1f, 0.35f, 0.3f) : Color.white;
            _elapsed = 0f;
            _frames = 0;
        }
    }
}
