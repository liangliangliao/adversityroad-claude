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
            ("疾影突(前+重)", c => c.Debug_DashStrike()),
            ("吹飞踢(后+重)", c => c.Debug_Blowback()),
            ("切手技(连段中轻点重)", c => c.Debug_QiShou()),
            ("满蓄力重劈", c => c.Debug_HeavyCharged()),
            ("旋风终结(2势)", c => c.Debug_Finisher()),
            ("觉醒乱舞(3势)", c => c.Debug_RanWu()),
            ("下劈坠击(跳+拳)", c => c.Debug_JumpAttack()),
            ("飞踢(跳+腿)", c => c.Debug_JumpKick()),
            ("扫堂腿(蹲+攻)", c => c.Debug_Sweep()),
            ("意势填满", c => c.Debug_FillMomentum()),
            ("状态回满", c => c.Debug_RestoreAll()),
        };

        public static DebugMovesPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<DebugMovesPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "DebugMovesPanel", new Vector2(1280, 820),
                new Color(0.07f, 0.08f, 0.11f, 0.96f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "招式测试台", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -42), new Vector2(700, 52));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "点击即执行对应招式（自动瞄准最近敌人）。先用「敌人+」放一个见习靶子。",
                22, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.55f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -88), new Vector2(1100, 34));

            for (int i = 0; i < Moves.Length; i++)
            {
                var m = Moves[i];
                int col = i % 3, row = i / 3;
                UiUtil.MakeButton(_panel.transform, m.label, new Vector2(0.5f, 1f),
                    new Vector2(-400 + col * 400, -170 - row * 110),
                    new Vector2(370, 90), new Color(0.28f, 0.32f, 0.42f, 0.95f), () =>
                    {
                        var combat = FindObjectOfType<PlayerCombatController>();
                        if (combat == null) return;
                        Hide();                       // 关面板恢复时间流再出招
                        m.act(combat);
                        GameEvents.RaiseSubtitle("测试：" + m.label);
                    }, 24);
            }

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(0, 56),
                new Vector2(260, 74), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 28);

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
