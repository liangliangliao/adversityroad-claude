using UnityEngine;
using UnityEngine.EventSystems;

namespace AdversityRoad.Mobile
{
    /// <summary>
    /// 屏幕右侧透明区域：拖动转镜头，写入 MobileInput.LookDelta。
    /// 双击＝镜头回正（把镜头一次性拉到角色行进方向背后）。
    ///
    /// 为什么需要"回正"这个动作：摇杆是镜头相对的 ⇒ 角色行进方向 H = 镜头偏航 C + 摇杆角 θ，
    /// 而"镜头对着角色正前方"要求 C = H ⇒ **θ = 0**。也就是说只要摇杆推在非正前方向，
    /// 镜头在几何上就不可能对着角色正前方，这与镜头转多快、聚焦在哪里都无关。
    /// 大型动作游戏对此的通行解法正是给玩家一个显式的回正动作（魂系 R3 等），
    /// 而不是让镜头自己去追一个无解的目标。
    /// </summary>
    public class TouchLookArea : MonoBehaviour, IDragHandler, IPointerDownHandler
    {
        public float sensitivity = 0.15f;

        const float DoubleTapWindow = 0.32f;
        float _lastTap = -99f;
        bool _dragged;

        public void OnDrag(PointerEventData e)
        {
            _dragged = true;
            MobileInput.LookDelta += e.delta * sensitivity;
        }

        public void OnPointerDown(PointerEventData e)
        {
            // 拖动过的那一次不算点击，避免"转完镜头顺手又触发回正"
            if (!_dragged && Time.unscaledTime - _lastTap < DoubleTapWindow)
            {
                MobileInput.RequestRecenter();
                _lastTap = -99f;
            }
            else _lastTap = Time.unscaledTime;
            _dragged = false;
        }
    }
}
