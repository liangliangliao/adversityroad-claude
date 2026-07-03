using UnityEngine;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 第三人称跟随镜头（防眩晕版）：
    /// - 真机只读触屏转镜头区（旧输入系统会把手指映射成"鼠标"，必须屏蔽）；
    /// - 触屏灵敏度按屏幕高度归一化，不同分辨率手感一致，且限幅防止猛甩；
    /// - 自动跟随：移动且一段时间未手动转镜头时，镜头缓缓转到玩家背后，
    ///   玩家改变方向后镜头自动跟上，无需一直手动掰镜头；
    /// - 锁定运镜：锁定敌人时镜头自动朝向敌人；
    /// - 角度/位置双重平滑，专注低时的摇晃改为连续正弦。
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0, 2.2f, -4.5f);
        public float mouseSensitivity = 3f;
        [Tooltip("触屏灵敏度：整屏高度拖动对应的旋转角度")]
        public float touchSensitivity = 200f;
        public float minPitch = -20f, maxPitch = 60f;
        public float rotateSmooth = 12f;
        public float followSmooth = 16f;

        [Header("自动跟随")]
        public bool autoFollow = true;
        public float autoFollowDelay = 1.1f;   // 手动转镜头后暂停自动跟随的秒数
        public float autoFollowSpeed = 55f;    // 度/秒

        public PlayerController player;
        public LockOnSystem lockOn;

        float _yaw, _pitch = 14f;
        float _curYaw, _curPitch = 14f;
        float _kick;
        float _lastManualLook;
        Vector3 _lastTargetPos;

        public void Kick(float strength) => _kick = Mathf.Max(_kick, strength);

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.unscaledDeltaTime;

            // ---- 输入 ----
            Vector2 touch = MobileInput.ConsumeLook();
            float norm = touchSensitivity / Mathf.Max(1, Screen.height);
            float lookX = touch.x * norm;
            float lookY = touch.y * norm;
            if (!Application.isMobilePlatform)
            {
                lookX += Input.GetAxis("Mouse X") * mouseSensitivity;
                lookY += Input.GetAxis("Mouse Y") * mouseSensitivity;
            }
            // 限幅：单帧最大转角，防止猛甩造成眩晕
            lookX = Mathf.Clamp(lookX, -10f, 10f);
            lookY = Mathf.Clamp(lookY, -8f, 8f);

            if (Mathf.Abs(lookX) > 0.02f || Mathf.Abs(lookY) > 0.02f)
                _lastManualLook = Time.unscaledTime;

            _yaw += lookX;
            _pitch = Mathf.Clamp(_pitch - lookY, minPitch, maxPitch);

            // ---- 锁定运镜：镜头自动朝向锁定的敌人 ----
            Transform lockTarget = lockOn != null ? lockOn.CurrentTarget : null;
            if (lockTarget != null)
            {
                Vector3 toEnemy = lockTarget.position - target.position;
                toEnemy.y = 0;
                if (toEnemy.sqrMagnitude > 0.1f)
                {
                    float wantYaw = Quaternion.LookRotation(toEnemy).eulerAngles.y;
                    _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, autoFollowSpeed * 1.6f * dt);
                }
            }
            // ---- 自动跟随：移动中且近期未手动转镜头 → 转到玩家背后 ----
            else if (autoFollow && Time.unscaledTime - _lastManualLook > autoFollowDelay)
            {
                float moveSpeed = (target.position - _lastTargetPos).magnitude / Mathf.Max(dt, 0.0001f);
                if (moveSpeed > 0.8f)
                {
                    float wantYaw = target.eulerAngles.y;
                    float speedK = Mathf.Clamp01(moveSpeed / 5f);
                    _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, autoFollowSpeed * speedK * dt);
                }
            }
            _lastTargetPos = target.position;

            // ---- 平滑与摆位 ----
            _curYaw = Mathf.LerpAngle(_curYaw, _yaw, rotateSmooth * dt);
            _curPitch = Mathf.Lerp(_curPitch, _pitch, rotateSmooth * dt);

            Quaternion rot = Quaternion.Euler(_curPitch, _curYaw, 0);
            Vector3 desired = target.position + rot * offset;

            if (player != null && player.Stats.focus < player.Stats.maxFocus * 0.3f)
            {
                float s = (1f - player.Stats.focus / (player.Stats.maxFocus * 0.3f)) * 0.07f;
                desired += new Vector3(
                    Mathf.Sin(Time.time * 9f), Mathf.Sin(Time.time * 11.3f), 0) * s;
            }

            if (_kick > 0.001f)
            {
                desired += Random.insideUnitSphere * _kick * 0.12f;
                _kick = Mathf.Lerp(_kick, 0, 8f * dt);
            }

            Vector3 pivot = target.position + Vector3.up * 1.5f;
            if (Physics.Linecast(pivot, desired, out RaycastHit hit))
                desired = hit.point + hit.normal * 0.3f;

            transform.position = Vector3.Lerp(transform.position, desired, followSmooth * dt);
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * 0.3f - transform.position);
        }
    }
}
