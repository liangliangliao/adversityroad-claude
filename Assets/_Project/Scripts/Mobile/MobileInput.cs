using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Mobile
{
    /// <summary>
    /// 虚拟输入中枢：触屏摇杆/按钮把输入写到这里，
    /// 玩家脚本同时读键盘鼠标与本类，两端输入自动合并。
    /// 按钮名约定：Jump / Dodge / Light / Heavy / Guard / Lock / Inner
    /// </summary>
    public static class MobileInput
    {
        public static Vector2 Move;          // 虚拟摇杆
        public static Vector2 LookDelta;     // 触屏转镜头增量

        static readonly HashSet<string> _down = new HashSet<string>();
        static readonly HashSet<string> _held = new HashSet<string>();

        public static void Press(string btn) { _down.Add(btn); _held.Add(btn); }
        public static void Release(string btn) { _held.Remove(btn); }
        public static bool GetDown(string btn) => _down.Contains(btn);
        public static bool GetHeld(string btn) => _held.Contains(btn);

        /// <summary>每帧末尾由 MobileInputPump 调用，清理单帧状态。</summary>
        public static void EndFrame()
        {
            _down.Clear();
            LookDelta = Vector2.zero;
        }
    }

    /// <summary>挂在场景任意常驻物体上，负责每帧清理单帧输入。</summary>
    public class MobileInputPump : MonoBehaviour
    {
        void LateUpdate() => MobileInput.EndFrame();
    }
}
