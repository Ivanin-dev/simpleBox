using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SimpleBox.Controls
{
    /// <summary>
    /// Press-on-down UI button (fires on touch begin, not on release). Tracks a single pointer.
    /// Press is latched until ConsumePress() is called.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private const int NoPointer = int.MinValue;

        [SerializeField] private Graphic targetGraphic;
        [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] private Color pressedColor = new Color(1f, 1f, 1f, 0.75f);

        private int _pointerId = NoPointer;
        private bool _pressLatched;

        public bool IsHeld => _pointerId != NoPointer;

        public bool ConsumePress()
        {
            bool pressed = _pressLatched;
            _pressLatched = false;
            return pressed;
        }

        private void Awake()
        {
            if (targetGraphic == null) targetGraphic = GetComponent<Graphic>();
            ApplyColor(false);
        }

        private void OnDisable() => Release();
        private void OnApplicationPause(bool paused) { if (paused) Release(); }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_pointerId != NoPointer) return;
            _pointerId = eventData.pointerId;
            _pressLatched = true;
            ApplyColor(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointerId) Release();
        }

        private void Release()
        {
            _pointerId = NoPointer;
            ApplyColor(false);
        }

        private void ApplyColor(bool pressed)
        {
            if (targetGraphic != null) targetGraphic.color = pressed ? pressedColor : normalColor;
        }
    }
}
