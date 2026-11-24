using UnityEngine;
using UnityEngine.EventSystems;

namespace SimpleBox.Controls
{
    /// <summary>
    /// Camera swipe area. Captures the first pointer that presses it and accumulates only that pointer's delta (pixels).
    /// Also detects a tap (short press with almost no movement) on the same pointer; a swipe is never a tap.
    /// Consumer must call ConsumeDelta() / ConsumeTap() once per frame.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class TouchLookZone : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const int NoPointer = int.MinValue;

        [Header("Tap detection")]
        [SerializeField, Min(0.01f)] private float tapMaxDuration = 0.25f;
        [Tooltip("Max total pointer travel in pixels for a press to count as a tap.")]
        [SerializeField, Min(0f)] private float tapMaxTravel = 12f;

        private int _pointerId = NoPointer;
        private Vector2 _accumulatedDelta;
        private float _pressTime;
        private float _pressTravel;
        private bool _tapLatched;
        private bool _pressIsPrimaryButton;

        public bool IsActive => _pointerId != NoPointer;

        /// <summary>Returns accumulated screen-pixel delta since last call and clears it.</summary>
        public Vector2 ConsumeDelta()
        {
            Vector2 d = _accumulatedDelta;
            _accumulatedDelta = Vector2.zero;
            return d;
        }

        /// <summary>True once after a tap ended on this zone.</summary>
        public bool ConsumeTap()
        {
            bool tap = _tapLatched;
            _tapLatched = false;
            return tap;
        }

        private void OnDisable() => Release();
        private void OnApplicationPause(bool paused) { if (paused) Release(); }
        private void OnApplicationFocus(bool focused) { if (!focused) Release(); }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_pointerId != NoPointer) return;
            _pointerId = eventData.pointerId;
            _accumulatedDelta = Vector2.zero;
            _pressTime = Time.unscaledTime;
            _pressTravel = 0f;
            // Touches report Left. A right-click (desktop rotate modifier) must never count as a tap.
            _pressIsPrimaryButton = eventData.button == PointerEventData.InputButton.Left;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public void OnBeginDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            Vector2 delta = eventData.delta;
            _accumulatedDelta += delta;
            _pressTravel += delta.magnitude;
        }

        // Input modules may send EndDrag before or after PointerUp: whichever arrives first ends the press.
        public void OnEndDrag(PointerEventData eventData) => EndPress(eventData.pointerId);
        public void OnPointerUp(PointerEventData eventData) => EndPress(eventData.pointerId);

        private void EndPress(int pointerId)
        {
            if (pointerId != _pointerId) return;
            if (_pressIsPrimaryButton && Time.unscaledTime - _pressTime <= tapMaxDuration && _pressTravel <= tapMaxTravel)
                _tapLatched = true;
            Release();
        }

        private void Release()
        {
            _pointerId = NoPointer;
        }
    }
}
