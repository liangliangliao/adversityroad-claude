using UnityEngine;
using UnityEngine.EventSystems;

namespace AdversityRoad.Mobile
{
    /// <summary>
    /// 左下角虚拟摇杆：拖动写入 MobileInput.Move。
    /// 为「平稳 + 精准」做了三件事：
    ///   ① 径向死区（滤中心静止噪声，保持全向 360°）；
    ///   ② 响应曲线（小幅推动更细腻，慢走/微调更精准）；
    ///   ③ 逐帧临界阻尼平滑（滤掉手指微抖，输出平稳不跳变）。
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform handle;
        public float radius = 100f;
        [Tooltip("死区：仅滤掉贴近中心的静止噪声，保持全向 360° 全量程手感；设 0 即无死区")]
        [Range(0f, 0.4f)] public float deadZone = 0.06f;
        [Tooltip("响应平滑时间（秒）：越大越稳（滤手指抖动），越小越跟手")]
        public float smoothTime = 0.06f;
        [Tooltip("响应曲线指数（>1 让小幅推动更精细，利于慢走与微调对准）")]
        public float responseExp = 1.3f;

        RectTransform _rt;
        Vector2 _target;   // 本帧目标（已去死区+曲线）
        Vector2 _value;    // 平滑后的实际输出
        Vector2 _vel;
        bool _held;

        void Awake() => _rt = GetComponent<RectTransform>();

        public void OnPointerDown(PointerEventData e) { _held = true; OnDrag(e); }

        public void OnDrag(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rt, e.position, e.pressEventCamera, out Vector2 local);
            Vector2 clamped = Vector2.ClampMagnitude(local, radius);
            if (handle != null) handle.anchoredPosition = clamped;
            _target = ApplyCurve(ApplyDeadZone(clamped / radius));
        }

        public void OnPointerUp(PointerEventData e)
        {
            _held = false;
            _target = Vector2.zero;
            if (handle != null) handle.anchoredPosition = Vector2.zero;
        }

        void Update()
        {
            // 逐帧临界阻尼平滑：手指的高频微抖被滤掉，方向/力度输出平稳；
            // 松手时用更短的时间快速归零，避免"松手还在走"。
            _value = Vector2.SmoothDamp(_value, _target, ref _vel,
                _held ? smoothTime : smoothTime * 0.5f);
            if (!_held && _value.sqrMagnitude < 1e-5f) _value = Vector2.zero;
            MobileInput.Move = _value;
        }

        /// <summary>径向死区 + 重映射：滤掉手指微抖动，死区外平滑过渡不跳变。</summary>
        Vector2 ApplyDeadZone(Vector2 raw)
        {
            float mag = raw.magnitude;
            if (mag <= deadZone) return Vector2.zero;
            float scaled = (mag - deadZone) / (1f - deadZone);   // 死区外重映射到 0–1
            return raw / mag * Mathf.Min(scaled, 1f);
        }

        /// <summary>响应曲线：保持方向，仅对力度做指数塑形，小幅推动更精细。</summary>
        Vector2 ApplyCurve(Vector2 v)
        {
            float m = v.magnitude;
            if (m < 1e-4f) return Vector2.zero;
            return v / m * Mathf.Pow(Mathf.Clamp01(m), responseExp);
        }
    }
}
