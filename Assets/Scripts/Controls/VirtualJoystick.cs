using UnityEngine;
using UnityEngine.EventSystems;

namespace SimpleBox.Controls
{
    /// <summary>
    /// Floating/fixed virtual joystick. Tracks exactly one pointer (fingerId); other touches on the zone are ignored.
    /// Hierarchy: JoystickZone (this, Image raycastTarget=true) -> Background (pivot 0.5) -> Handle (anchors center).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class VirtualJoystick : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const int NoPointer = int.MinValue;

        [SerializeField] private RectTransform background;
        [SerializeField] private RectTransform handle;
        [Tooltip("Background jumps to the touch point on press.")]
        [SerializeField] private bool floating = true;
        [SerializeField, Range(0f, 0.5f)] private float deadZone = 0.1f;
        [Tooltip("Handle travel as a fraction of background radius.")]
        [SerializeField, Range(0.1f, 1f)] private float handleRange = 1f;

        private RectTransform _zone;
        private Vector2 _defaultBackgroundAnchoredPos;
        private Vector2 _value;
        private int _pointerId = NoPointer;

        /// <summary>Normalized stick value, magnitude 0..1, dead zone applied.</summary>
        public Vector2 Value => _value;
        public bool IsActive => _pointerId != NoPointer;

        private void Awake()
        {
            _zone = (RectTransform)transform;
            if (background == null || handle == null)
            {
                Debug.LogError($"[{nameof(VirtualJoystick)}] Background/Handle not assigned on '{name}'.", this);
                enabled = false;
                return;
            }
            _defaultBackgroundAnchoredPos = background.anchoredPosition;
        }

        private void OnDisable() => ResetStick();
        private void OnApplicationPause(bool paused) { if (paused) ResetStick(); }
        private void OnApplicationFocus(bool focused) { if (!focused) ResetStick(); }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!enabled || _pointerId != NoPointer) return;
            _pointerId = eventData.pointerId;

            if (floating &&
                RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    _zone, eventData.position, eventData.pressEventCamera, out Vector3 worldPoint))
            {
                background.position = worldPoint;
            }

            UpdateStick(eventData.position, eventData.pressEventCamera);
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            // Remove EventSystem drag threshold so the stick responds from the first pixel.
            eventData.useDragThreshold = false;
        }

        public void OnBeginDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            UpdateStick(eventData.position, eventData.pressEventCamera);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointerId) ResetStick();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointerId) ResetStick();
        }

        private void UpdateStick(Vector2 screenPos, Camera eventCamera)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    background, screenPos, eventCamera, out Vector2 local))
            {
                return;
            }

            float radius = background.rect.width * 0.5f;
            if (radius <= 0f) return;

            Vector2 offset = local / radius;
            float mag = offset.magnitude;
            if (mag > 1f)
            {
                offset /= mag;
                mag = 1f;
            }

            handle.anchoredPosition = offset * (radius * handleRange);

            if (mag < deadZone)
            {
                _value = Vector2.zero;
            }
            else
            {
                float remapped = (mag - deadZone) / (1f - deadZone);
                _value = offset * (remapped / mag);
            }
        }

        private void ResetStick()
        {
            _pointerId = NoPointer;
            _value = Vector2.zero;
            if (handle != null) handle.anchoredPosition = Vector2.zero;
            if (floating && background != null) background.anchoredPosition = _defaultBackgroundAnchoredPos;
        }
    }
}
