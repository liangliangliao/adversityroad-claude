using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Mobile
{
    /// <summary>
    /// 虚拟输入中枢：触屏摇杆/按钮把输入写到这里，
    /// 玩家脚本同时读键盘鼠标与本类，两端输入自动合并。
    /// GetDown 为消费式读取（读到即清除，2 帧有效期），
    /// 彻底规避脚本执行顺序不同导致的按键丢帧——每个按钮只应有一个消费者。
    /// 按钮名约定：Jump / Dodge / Light / Heavy / Guard / Art（技能修饰键）/ Lock / Inner /
    /// Skill1..Skill6（无独立按钮，由「术」+核心键路由产生）/ Crouch / Sheathe / Interact
    /// </summary>
    public static class MobileInput
    {
        public static Vector2 Move;          // 虚拟摇杆
        public static Vector2 LookDelta;     // 触屏转镜头增量（由镜头消费式读取）

        static readonly Dictionary<string, int> _pressFrame = new Dictionary<string, int>();
        static readonly HashSet<string> _held = new HashSet<string>();

        // ===== 修饰键「术」：把 6 个核心键临时变成 6 个技能键 =====
        //
        // 触屏最不该做的事，就是把手柄"L1+面键"的组合摊平成一屏平面按钮——
        // 手柄只有 4 个面键却能出 16 个招，靠的正是修饰键；摊平后按钮数爆炸，
        // 一半落在拇指够不到的地方（实测原布局 14 键中 4 键超出可达区、2 键视觉重叠）。
        //
        // 这里在**输入中枢**做重路由：按住「术」时按下「拳」，写进去的是 Skill1 而不是 Light。
        // 于是消费端（PlayerCombatController / SkillExecutor）一行都不用改，
        // 也天然不会出现"一次点击既出拳又放技能"——因为 Light 压根没被写入过。
        public const string Modifier = "Art";

        static readonly Dictionary<string, string> SkillMap = new Dictionary<string, string>
        {
            { "Light", "Skill1" },   // 术+拳
            { "Kick",  "Skill2" },   // 术+剑
            { "Heavy", "Skill3" },   // 术+重
            { "Jump",  "Skill4" },   // 术+跳
            { "Dodge", "Skill5" },   // 术+闪
            { "Guard", "Skill6" },   // 术+挡
        };

        /// <summary>按下时的实际去向（物理键 → 逻辑键）：抬起时按同一去向解除，
        /// 否则"按住术+挡"再松开修饰键，Skill6 会永远留在按住集合里。</summary>
        static readonly Dictionary<string, string> _routed = new Dictionary<string, string>();

        // ---- 点选式生效（latch）：触屏修饰键与手柄的关键差别 ----
        // 手柄能 L1+□ 是因为左右手分工；**一根拇指按不住「术」再去点「拳」**。
        // 所以「术」做成点一下亮起：下一次点核心键即出技能，出完自动落回普通模式。
        // 同时保留真按住（平板双手/外接手柄）：按住期间可连放多个技能。
        const float LatchWindow = 3f;   // 亮起后无操作自动熄灭（不让玩家莫名其妙放出技能）
        static bool _latched;
        static float _latchAt = -99f;

        static bool LatchActive => _latched && Time.unscaledTime - _latchAt <= LatchWindow;

        /// <summary>「术」当前是否生效（按住中 或 点选亮起中）——界面据此切换六个核心键的显示。</summary>
        public static bool ModifierHeld => _held.Contains(Modifier) || LatchActive;

        /// <summary>点选剩余时间比例（1→0），供界面画倒计时环。</summary>
        public static float ModifierLatch01 => _held.Contains(Modifier) ? 1f
            : (LatchActive ? 1f - (Time.unscaledTime - _latchAt) / LatchWindow : 0f);

        /// <summary>某物理键在「术」生效时会变成哪个技能键（界面提示用；无映射返回 null）。</summary>
        public static string MappedSkill(string btn) =>
            SkillMap.TryGetValue(btn, out string s) ? s : null;

        public static void Press(string btn)
        {
            if (btn == Modifier)
            {
                // 再点一次取消（点错了不必等它自己熄灭）
                _latched = !LatchActive;
                _latchAt = Time.unscaledTime;
            }

            string logical = btn;
            if (btn != Modifier && ModifierHeld && SkillMap.TryGetValue(btn, out string mapped))
            {
                logical = mapped;
                _latched = false;   // 用掉即落回普通模式；仍按住「术」则由 _held 继续生效
            }

            _routed[btn] = logical;
            _pressFrame[logical] = Time.frameCount;
            _held.Add(logical);
        }

        public static void Release(string btn)
        {
            // 松开时按"当初按下去的那个去向"解除：修饰键中途松开也不会残留按住状态
            if (_routed.TryGetValue(btn, out string logical))
            {
                _held.Remove(logical);
                _routed.Remove(btn);
                if (btn == Modifier) ReleaseStaleMapped();
            }
            else _held.Remove(btn);
        }

        /// <summary>修饰键松开时，清掉那些已经没有物理键按住的技能键
        /// （手指离开屏幕时 OnPointerUp 偶有丢失，避免技能键卡在按住态）。</summary>
        static void ReleaseStaleMapped()
        {
            foreach (var kv in SkillMap)
                if (!_routed.ContainsKey(kv.Key)) _held.Remove(kv.Value);
        }

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
