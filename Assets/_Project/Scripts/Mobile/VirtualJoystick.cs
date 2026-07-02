using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AdversityRoad.Mobile
{
    /// <summary>左下角虚拟摇杆：拖动写入 MobileInput.Move。</summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform handle;
        public float radius = 100f;

        RectTransform _rt;

        void Awake() => _rt = GetComponent<RectTransform>();

        public void OnPointerDown(PointerEventData e) => OnDrag(e);

        public void OnDrag(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rt, e.position, e.pressEventCamera, out Vector2 local);
            Vector2 clamped = Vector2.ClampMagnitude(local, radius);
            if (handle != null) handle.anchoredPosition = clamped;
            MobileInput.Move = clamped / radius;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (handle != null) handle.anchoredPosition = Vector2.zero;
            MobileInput.Move = Vector2.zero;
        }
    }
}
