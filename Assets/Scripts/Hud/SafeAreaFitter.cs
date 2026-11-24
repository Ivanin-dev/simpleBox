using UnityEngine;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Fits this RectTransform (full-screen child of the Canvas that holds all HUD elements) to Screen.safeArea,
    /// so buttons never sit under notches, camera cut-outs, rounded corners or system gesture bars.
    /// Re-applies only when the safe area, resolution or orientation changes (struct comparison, no allocation).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _lastSafeArea;
        private int _lastWidth;
        private int _lastHeight;
        private ScreenOrientation _lastOrientation;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            Apply();
        }

        private void Update()
        {
            if (Screen.safeArea != _lastSafeArea || Screen.width != _lastWidth ||
                Screen.height != _lastHeight || Screen.orientation != _lastOrientation)
            {
                Apply();
            }
        }

        private void Apply()
        {
            Rect safe = Screen.safeArea;
            int w = Screen.width;
            int h = Screen.height;

            _lastSafeArea = safe;
            _lastWidth = w;
            _lastHeight = h;
            _lastOrientation = Screen.orientation;

            if (w <= 0 || h <= 0 || safe.width <= 0f || safe.height <= 0f) return;

            Vector2 min = safe.position;
            Vector2 max = safe.position + safe.size;
            min.x /= w;
            min.y /= h;
            max.x /= w;
            max.y /= h;

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
