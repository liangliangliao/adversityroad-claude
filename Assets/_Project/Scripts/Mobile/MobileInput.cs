using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Mobile
{
    /// <summary>
    /// 虚拟输入中枢：触屏摇杆/按钮把输入写到这里，
    /// 玩家脚本同时读键盘鼠标与本类，两端输入自动合并。
    /// GetDown 为消费式读取（读到即清除，2 帧有效期），
    /// 彻底规避脚本执行顺序不同导致的按键丢帧——每个按钮只应有一个消费者。
    /// 按钮名约定：Jump / Dodge / Light / Heavy / Guard / Lock / Inner / Skill1 / Skill2 / Interact
    /// </summary>
    public static class MobileInput
    {
        public static Vector2 Move;          // 虚拟摇杆
        public static Vector2 LookDelta;     // 触屏转镜头增量（由镜头消费式读取）

        static readonly Dictionary<string, int> _pressFrame = new Dictionary<string, int>();
        static readonly HashSet<string> _held = new HashSet<string>();

        public static void Press(string btn) { _pressFrame[btn] = Time.frameCount; _held.Add(btn); }
        public static void Release(string btn) => _held.Remove(btn);

        public static bool GetDown(string btn)
        {
            if (_pressFrame.TryGetValue(btn, out int f) && Time.frameCount - f <= 2)
            {
                _pressFrame.Remove(btn);
                return true;
            }
            return false;
        }

        public static bool GetHeld(string btn) => _held.Contains(btn);

        /// <summary>镜头消费转镜头增量：读取后清零，避免与清帧顺序赛跑。</summary>
        public static Vector2 ConsumeLook()
        {
            Vector2 v = LookDelta;
            LookDelta = Vector2.zero;
            return v;
        }

        /// <summary>兼容旧接口：按键改为消费式后无需帧末清理。</summary>
        public static void EndFrame() { }
    }

    /// <summary>兼容保留：按键改为消费式读取后本组件不再需要做任何事。</summary>
    public class MobileInputPump : MonoBehaviour
    {
    }
}
