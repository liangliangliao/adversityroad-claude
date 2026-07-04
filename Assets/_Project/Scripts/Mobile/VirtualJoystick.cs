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
        [Tooltip("死区：仅滤掉贴近中心的静止噪声，保持全向 360° 全量程手感。" +
                 "超过死区后重新映射到 0–1，避免边缘跳变；设 0 即完全无死区")]
        [Range(0f, 0.4f)] public float deadZone = 0.06f;

        RectTransform _rt;

        void Awake() => _rt = GetComponent<RectTransform>();

        public void OnPointerDown(PointerEventData e) => OnDrag(e);

        public void OnDrag(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rt, e.position, e.pressEventCamera, out Vector2 local);
            Vector2 clamped = Vector2.ClampMagnitude(local, radius);
            if (handle != null) handle.anchoredPosition = clamped;
            MobileInput.Move = ApplyDeadZone(clamped / radius);
        }

        /// <summary>径向死区 + 重映射：滤掉手指微抖动，死区外平滑过渡不跳变。</summary>
        Vector2 ApplyDeadZone(Vector2 raw)
        {
            float mag = raw.magnitude;
            if (mag <= deadZone) return Vector2.zero;
            float scaled = (mag - deadZone) / (1f - deadZone);   // 死区外重映射到 0–1
            return raw / mag * Mathf.Min(scaled, 1f);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (handle != null) handle.anchoredPosition = Vector2.zero;
            MobileInput.Move = Vector2.zero;
        }
    }
}
