using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Combat;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 招式测试面板：逐个直接触发每个招式验证实际效果，
    /// 并提供意势填满/状态回满等调试开关——方便测试与调参。
    /// </summary>
    public class DebugMovesPanel : MonoBehaviour
    {
        GameObject _panel;

        static readonly (string label, Action<PlayerCombatController> act)[] Moves =
        {
            ("疾影突刺(前+重)", c => c.Debug_DashStrike()),
            ("旋身空翻踢(后+重)", c => c.Debug_Blowback()),
            ("左旋风斩(左+重)", c => c.Debug_LeftSpin()),
            ("右旋风斩(右+重)", c => c.Debug_RightSpin()),
            ("能量斩(需2势远程)", c => c.Debug_EnergyBlade()),
            ("切手撩斩(连段中轻点重)", c => c.Debug_QiShou()),
            ("蓄力·巨剑跳劈", c => c.Debug_HeavyCharged()),
            ("旋风终结(2势)", c => c.Debug_Finisher()),
            ("觉醒乱舞(3势)", c => c.Debug_RanWu()),
            ("空袭跳劈(跳+剑)", c => c.Debug_JumpAttack()),
            ("飞踢(跳+拳)", c => c.Debug_JumpKick()),
            ("空袭裂地跳劈(跳+重)", c => c.Debug_AirLeap()),
            ("扫堂腿(蹲+拳)", c => c.Debug_Sweep()),
            ("低位突刺(蹲+剑)", c => c.Debug_CrouchThrust()),
            ("意势填满", c => c.Debug_FillMomentum()),
            ("状态回满", c => c.Debug_RestoreAll()),
        };

        // 动作库全量清单：资源目录 Anims/ 的 19 个原始动作，逐个命名、点击试播预览
        static readonly (string label, string clip)[] Clips =
        {
            ("待机", "Idle"),
            ("行走", "Walking"),
            ("奔跑", "Running"),
            ("格斗架势", "Fighting Idle"),
            ("前手刺拳", "Lead Jab"),
            ("贯手重拳", "Cross Punch"),
            ("正踢", "Kicking"),
            ("侧踹", "Side Kick"),
            ("旋身空翻踢", "Spin Flip Kick"),
            ("飞踢", "Flying Kick"),
            ("巨剑横斩", "Great Sword Slash"),
            ("巨剑撩斩", "Great Sword Slash (1)"),
            ("巨剑旋风斩", "Great Sword High Spin Attack"),
            ("巨剑跳劈", "Great Sword Jump Attack"),
            ("突刺", "Stabbing"),
            ("施法聚气", "Spell Casting"),
            ("受击反应", "Hit Reaction"),
            ("击倒躺地", "Knocked Down"),
            ("倒地死亡", "Dying"),
            // —— 补充动作（下载放入 Anims/ 后生效；缺失会提示"动作缺失"）——
            // 实战启用时机：格挡=按住挡键；翻滚=闪键；硬直=破绽/被言语反制；
            // 蓄力=按住重键；扫堂=蹲+腿
            ("格挡(挡键)", "Great Sword Blocking"),
            ("翻滚(闪键)", "Stand To Roll"),
            ("硬直(破绽)", "Stunned"),
            ("蓄力(按住重)", "Great Sword Casting"),
            ("扫堂(蹲+拳)", "Leg Sweep"),
        };

        public static DebugMovesPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<DebugMovesPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "DebugMovesPanel", new Vector2(1800, 1000),
                new Color(0.07f, 0.08f, 0.11f, 0.96f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "招式测试台 · 动作库预览", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(900, 52));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "左：组合招式即点即放（先用「敌人+」放个靶子）。右：19 个原始动作逐个试播预览。",
                22, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.55f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -84), new Vector2(1400, 34));

            var secL = UiUtil.MakeText(_panel.transform, "SecL", "—— 组合招式 ——", 26,
                TextAnchor.MiddleCenter, new Color(0.7f, 0.85f, 1f));
            UiUtil.SetRect(secL, new Vector2(0.5f, 1f), new Vector2(-470, -128), new Vector2(500, 36));
            var secR = UiUtil.MakeText(_panel.transform, "SecR", "—— 动作库（原始动作试播）——", 26,
                TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.6f));
            UiUtil.SetRect(secR, new Vector2(0.5f, 1f), new Vector2(440, -128), new Vector2(600, 36));

            for (int i = 0; i < Moves.Length; i++)
            {
                var m = Moves[i];
                int col = i % 2, row = i / 2;
                UiUtil.MakeButton(_panel.transform, m.label, new Vector2(0.5f, 1f),
                    new Vector2(-650 + col * 370, -200 - row * 100),
                    new Vector2(350, 84), new Color(0.28f, 0.32f, 0.42f, 0.95f), () =>
                    {
                        var combat = FindObjectOfType<PlayerCombatController>();
                        if (combat == null) return;
                        Hide();                       // 关面板恢复时间流再出招
                        m.act(combat);
                        GameEvents.RaiseSubtitle("测试：" + m.label);
                    }, 23);
            }

            for (int i = 0; i < Clips.Length; i++)
            {
                var cdef = Clips[i];
                int col = i % 3, row = i / 3;
                UiUtil.MakeButton(_panel.transform, cdef.label, new Vector2(0.5f, 1f),
                    new Vector2(140 + col * 300, -200 - row * 92),
                    new Vector2(280, 78), new Color(0.4f, 0.32f, 0.26f, 0.95f), () =>
                    {
                        var combat = FindObjectOfType<PlayerCombatController>();
                        if (combat == null) return;
                        var anim = combat.GetComponent<HumanoidAnimator>();
                        if (anim == null) return;
                        Hide();                       // 关面板恢复时间流再播
                        bool ok = anim.PlayClipPreview(cdef.clip);
                        GameEvents.RaiseSubtitle(ok
                            ? "动作预览：" + cdef.label + "（" + cdef.clip + "）"
                            : "动作缺失：" + cdef.clip);
                    }, 23);
            }

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(0, 50),
                new Vector2(260, 70), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 28);

            _panel.SetActive(false);
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        void Hide()
        {
            _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
