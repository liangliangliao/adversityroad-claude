using UnityEngine;
using UnityEngine.EventSystems;

namespace AdversityRoad.Mobile
{
    /// <summary>
    /// 浮动虚拟摇杆：在左侧大范围触控区内，手指按下处即为摇杆原点，
    /// 相对该原点计算方向；超出半径钳制，抬手立即归零复位。
    /// 用屏幕坐标记录按下原点，彻底解决"摇杆超出范围/触控漂移/松手仍移动"。
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform ring;    // 可视外环（浮动到触点）
        public RectTransform handle;  // 可视摇杆头
        public float radius = 130f;

        Vector2 _origin;              // 按下时屏幕坐标
        int _activePointer = -1;

        public void OnPointerDown(PointerEventData e)
        {
            if (_activePointer != -1) return;      // 只认第一根手指
            _activePointer = e.pointerId;
            _origin = e.position;
            if (ring != null)
            {
                ring.gameObject.SetActive(true);
                ring.position = e.position;
            }
            if (handle != null) handle.position = e.position;
            MobileInput.Move = Vector2.zero;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _activePointer) return;
            Vector2 delta = e.position - _origin;
            Vector2 clamped = Vector2.ClampMagnitude(delta, radius);
            if (handle != null) handle.position = _origin + clamped;
            MobileInput.Move = clamped / radius;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _activePointer) return;
            Reset();
        }

        void OnDisable() => Reset();

        void Reset()
        {
            _activePointer = -1;
            MobileInput.Move = Vector2.zero;
            if (ring != null) ring.gameObject.SetActive(false);
        }
    }
}
