using UnityEngine;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 简易第三人称跟随镜头。
    /// 真机上只读触屏转镜头区（旧输入系统会把第一根手指映射成"鼠标"，
    /// 若不屏蔽，拖摇杆/点按钮都会带动镜头产生眩晕感）。
    /// 加入角度平滑与位置平滑；专注值低时轻微摇晃（心理攻击画面反馈）。
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0, 2.2f, -4.5f);
        public float mouseSensitivity = 3f;
        public float minPitch = -20f, maxPitch = 60f;
        public float rotateSmooth = 14f;   // 角度平滑速度
        public float followSmooth = 18f;   // 位置平滑速度
        public PlayerController player;

        float _yaw, _pitch = 15f;
        float _curYaw, _curPitch = 15f;
        float _kick;                        // 受击震屏冲量

        /// <summary>受击/重击震屏：冲量随时间衰减。</summary>
        public void Kick(float strength) => _kick = Mathf.Max(_kick, strength);

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.unscaledDeltaTime;

            float lookX, lookY;
            Vector2 touch = MobileInput.ConsumeLook();
            if (Application.isMobilePlatform)
            {
                lookX = touch.x;
                lookY = touch.y;
            }
            else
            {
                lookX = Input.GetAxis("Mouse X") * mouseSensitivity + touch.x;
                lookY = Input.GetAxis("Mouse Y") * mouseSensitivity + touch.y;
            }

            _yaw += lookX;
            _pitch = Mathf.Clamp(_pitch - lookY, minPitch, maxPitch);

            _curYaw = Mathf.LerpAngle(_curYaw, _yaw, rotateSmooth * dt);
            _curPitch = Mathf.Lerp(_curPitch, _pitch, rotateSmooth * dt);

            Quaternion rot = Quaternion.Euler(_curPitch, _curYaw, 0);
            Vector3 desired = target.position + rot * offset;

            // 专注值低于 30% 时镜头轻微摇晃（连续正弦，避免随机抖动引发眩晕）
            if (player != null && player.Stats.focus < player.Stats.maxFocus * 0.3f)
            {
                float s = (1f - player.Stats.focus / (player.Stats.maxFocus * 0.3f)) * 0.08f;
                desired += new Vector3(
                    Mathf.Sin(Time.time * 9f), Mathf.Sin(Time.time * 11.3f), 0) * s;
            }

            // 受击震屏
            if (_kick > 0.001f)
            {
                desired += Random.insideUnitSphere * _kick * 0.12f;
                _kick = Mathf.Lerp(_kick, 0, 8f * dt);
            }

            // 镜头碰撞修正
            Vector3 pivot = target.position + Vector3.up * 1.5f;
            if (Physics.Linecast(pivot, desired, out RaycastHit hit))
                desired = hit.point + hit.normal * 0.3f;

            transform.position = Vector3.Lerp(transform.position, desired, followSmooth * dt);
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * 0.3f - transform.position);
        }
    }
}
