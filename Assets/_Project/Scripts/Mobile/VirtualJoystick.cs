using UnityEngine;
using UnityEngine.EventSystems;

namespace AdversityRoad.Mobile
{
    /// <summary>
    /// 左下角虚拟摇杆：拖动写入 MobileInput.Move。
    /// 为「平稳 + 精准」做了四件事：
    ///   ① 径向死区（滤中心静止噪声，保持全向 360°）；
    ///   ② 响应曲线（小幅推动更细腻，慢走/微调更精准）；
    ///   ③ **极坐标平滑**——力度与方向【分开】平滑。二维向量做 SmoothDamp 会把两者
    ///      绑在同一个时间常数上，而手感要求恰恰相反：力度糊了读作"起步/收步发肉"，
    ///      方向抖了读作"角色/镜头细碎乱动"。故力度保持跟手、方向另给更长的时间常数；
    ///   ④ **轻推时方向平滑加重 + 角速率限幅**：同样幅度的手指横向抖动，在轻推
    ///      （半径小）时对应的角度误差最大——那正是"稍微一动方向就变"的输入端来源。
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform handle;
        public float radius = 100f;
        [Tooltip("死区：仅滤掉贴近中心的静止噪声，保持全向 360° 全量程手感；设 0 即无死区")]
        [Range(0f, 0.4f)] public float deadZone = 0.06f;
        [Tooltip("力度平滑时间（秒）：越大越稳，越小越跟手")]
        public float smoothTime = 0.06f;
        [Tooltip("方向平滑时间（秒·满推时）：比力度更稳一点，滤掉手指的横向微抖")]
        public float dirSmoothTime = 0.075f;
        [Tooltip("轻推时的方向平滑倍率：轻推时同样的手指抖动对应更大的角度误差，故加重")]
        public float lightPushDirMult = 2.6f;
        [Tooltip("方向变化速率上限（度/秒）：封顶噪声引起的瞬时换向；正常搓杆远达不到")]
        public float maxDirRate = 900f;
        [Tooltip("响应曲线指数（>1 让小幅推动更精细，利于慢走与微调对准）")]
        public float responseExp = 1.3f;

        RectTransform _rt;
        Vector2 _target;   // 本帧目标（已去死区+曲线）
        Vector2 _value;    // 平滑后的实际输出
        float _mag, _magVel;      // 力度（极坐标）
        float _angle, _angleVel;  // 方向角（极坐标，度）
        bool _angleInit;
        bool _held;

        void Awake() => _rt = GetComponent<RectTransform>();

        public void OnPointerDown(PointerEventData e)
        {
            _held = true;
            MobileInput.MoveHeld = true;   // 零延迟：镜头据此判断"还想不想往某处去"
            OnDrag(e);
        }

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
            MobileInput.MoveHeld = false;
            _target = Vector2.zero;
            if (handle != null) handle.anchoredPosition = Vector2.zero;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            // ---- 力度：保持跟手（松手时更短，避免"松手还在走"）----
            float targetMag = _target.magnitude;
            _mag = Mathf.SmoothDamp(_mag, targetMag, ref _magVel,
                _held ? smoothTime : smoothTime * 0.5f, Mathf.Infinity, dt);

            // ---- 方向：更稳，且推得越轻越稳 ----
            if (targetMag > 1e-4f)
            {
                float targetAngle = Mathf.Atan2(_target.x, _target.y) * Mathf.Rad2Deg;
                if (!_angleInit) { _angleInit = true; _angle = targetAngle; _angleVel = 0f; }
                _angle = Mathf.SmoothDampAngle(_angle, targetAngle, ref _angleVel,
                    dirSmoothTime * Mathf.Lerp(lightPushDirMult, 1f, Mathf.Clamp01(targetMag)),
                    maxDirRate, dt);
            }
            else _angleInit = false;   // 归零/松手后重推：立刻按新方向起手，不带旧角度

            if (_mag < 1e-4f) { _value = Vector2.zero; _magVel = 0f; }
            else
            {
                float rad = _angle * Mathf.Deg2Rad;
                _value = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * Mathf.Min(_mag, 1f);
            }
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
