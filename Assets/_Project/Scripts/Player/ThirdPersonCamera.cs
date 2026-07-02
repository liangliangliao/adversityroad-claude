using UnityEngine;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 简易第三人称跟随镜头（MVP 用，正式版换 Cinemachine ThirdPersonFollow）。
    /// 专注值低时镜头摇晃：心理攻击的画面反馈。
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0, 2.2f, -4.5f);
        public float mouseSensitivity = 3f;
        public float minPitch = -20f, maxPitch = 60f;
        public PlayerController player;

        float _yaw, _pitch = 15f;

        void LateUpdate()
        {
            if (target == null) return;
            float lookX = Input.GetAxis("Mouse X") * mouseSensitivity + MobileInput.LookDelta.x;
            float lookY = Input.GetAxis("Mouse Y") * mouseSensitivity + MobileInput.LookDelta.y;
            _yaw += lookX;
            _pitch = Mathf.Clamp(_pitch - lookY, minPitch, maxPitch);

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0);
            Vector3 desired = target.position + rot * offset;

            // 专注值低于 30% 时镜头轻微摇晃
            if (player != null && player.Stats.focus < player.Stats.maxFocus * 0.3f)
            {
                float shake = (1f - player.Stats.focus / (player.Stats.maxFocus * 0.3f)) * 0.15f;
                desired += Random.insideUnitSphere * shake;
            }

            // 镜头碰撞修正
            Vector3 pivot = target.position + Vector3.up * 1.5f;
            if (Physics.Linecast(pivot, desired, out RaycastHit hit))
                desired = hit.point + hit.normal * 0.3f;

            transform.position = desired;
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * 0.3f - transform.position);
        }
    }
}
