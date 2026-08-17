using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Adversity;
using AdversityRoad.Core;
using AdversityRoad.Goals;

namespace AdversityRoad.UI
{
    /// <summary>
    /// Adversity History / Hall of Goals（方案 9.5 / 17.2 / 26.2）。
    ///
    /// 成长不该是一个抽象等级。这里把它变成可以回看的历史：
    /// 你第一次被谁压住、输了几次、输在哪里、它学到了你的什么、
    /// 你后来换了什么打法、哪一次才算真正的逆袭。
    /// 完成的目标会在 Hall of Goals 里立成纪念碑。
    /// </summary>
    public class AdversityHistoryPanel : MonoBehaviour
    {
        enum Tab { Nemesis, Hall }

        GameObject _panel;
        Text _bodyText, _tabText;
        Tab _tab = Tab.Nemesis;

        public static AdversityHistoryPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<AdversityHistoryPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "AdversityHistoryPanel", new Vector2(1320, 1000),
                new Color(0.08f, 0.06f, 0.08f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "逆 境 史", 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.6f, 0.55f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(700, 56));

            _tabText = UiUtil.MakeText(_panel.transform, "TabHint", "", 22,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.85f));
            UiUtil.SetRect(_tabText, new Vector2(0.5f, 1f), new Vector2(0, -94), new Vector2(1180, 56));

            UiUtil.MakeButton(_panel.transform, "宿敌与逆袭", new Vector2(0.5f, 1f),
                new Vector2(-200, -156), new Vector2(300, 58),
                new Color(0.42f, 0.24f, 0.26f, 0.95f), () => SetTab(Tab.Nemesis), 24);
            UiUtil.MakeButton(_panel.transform, "Hall of Goals", new Vector2(0.5f, 1f),
                new Vector2(140, -156), new Vector2(300, 58),
                new Color(0.36f, 0.32f, 0.2f, 0.95f), () => SetTab(Tab.Hall), 24);

            _bodyText = UiUtil.MakeText(_panel.transform, "Body", "", 21,
                TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.95f));
            UiUtil.SetRect(_bodyText, new Vector2(0.5f, 1f), new Vector2(0, -570), new Vector2(1220, 720));
            _bodyText.lineSpacing = 1.12f;

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 54), new Vector2(260, 70),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        public void Toggle()
        {
            if (_panel == null) return;
            bool show = !_panel.activeSelf;
            _panel.SetActive(show);
            if (show) Refresh();
        }

        void Hide() => _panel.SetActive(false);

        void SetTab(Tab t) { _tab = t; Refresh(); }

        void Refresh()
        {
            if (_bodyText == null) return;
            if (_tab == Tab.Hall) BuildHall();
            else BuildNemesis();
        }

        void BuildNemesis()
        {
            _tabText.text = "「逆袭」不是 Boss 首杀，而是系统能证明你相对于过去发生了变化：\n" +
                "失败模式被改变 · 同一压力下生存显著提升 · 曾必须绕开的现在能稳定应对 · 用新策略而非数值碾压取胜。";

            var sb = new StringBuilder();
            sb.Append(CourageSystem.Summary())
              .Append("    世界第 ").Append(OpenWorld.WorldState.Day).Append(" 天")
              .Append("    累计失败 ").Append(OpenWorld.WorldState.TotalDefeats).Append(" 次\n");

            var stress = StressStateMachine.Instance;
            if (stress != null)
                sb.Append("当前压力：").Append(StressStateMachine.StageLabel(stress.Stage))
                  .Append("（").Append(Mathf.RoundToInt(stress.Pressure * 100f)).Append("%）\n");
            sb.Append('\n');

            if (NemesisSystem.Count == 0)
            {
                sb.Append("还没有宿敌。\n\n" +
                    "宿敌不是被安排的：某个敌人真的击败过你之后，它才会记住这一场，\n" +
                    "带着学到的东西回到世界里等你——那时这里才会有内容。");
                _bodyText.text = sb.ToString();
                return;
            }

            foreach (var n in NemesisSystem.All)
            {
                sb.Append(AdversityHistoryHook.ReplayText(n)).Append("\n\n");
            }
            _bodyText.text = sb.ToString();
        }

        void BuildHall()
        {
            _tabText.text = "长期留存不来自刷同一个心理题库，而来自不同目标、不同道路、\n" +
                "不同宿敌与不同世界状态——每条走完的旅程都在这里留下一座纪念碑。";

            var sb = new StringBuilder();
            var hall = GoalOS.HallOfGoals;
            if (hall.Count == 0)
            {
                sb.Append("Hall of Goals 还是空的。\n\n" +
                    "完成一条旅程之后，这里会立起一座纪念碑：\n" +
                    "起点、里程碑、失败次数、宿敌、最大逆袭、关键策略变化、最终胜利与目标演化。");
                _bodyText.text = sb.ToString();
                return;
            }

            foreach (var m in hall)
            {
                sb.Append("◆ ").Append(m.title).Append('\n');
                if (!string.IsNullOrEmpty(m.why)) sb.Append("   为什么：").Append(m.why).Append('\n');
                sb.Append("   里程碑 ").Append(m.milestoneCount)
                  .Append(" · 失败 ").Append(m.failureCount)
                  .Append(" 次 · 宿敌 ").Append(m.nemesisCount)
                  .Append(" · 重铸 ").Append(m.evolutionCount).Append(" 次\n");
                if (!string.IsNullOrEmpty(m.biggestComeback))
                    sb.Append("   最大逆袭：").Append(m.biggestComeback).Append('\n');
                if (!string.IsNullOrEmpty(m.keyStrategyShift))
                    sb.Append("   关键策略变化：").Append(m.keyStrategyShift).Append('\n');
                if (!string.IsNullOrEmpty(m.finalVictoryNote))
                    sb.Append("   最终胜利：").Append(m.finalVictoryNote).Append('\n');
                sb.Append('\n');
            }
            _bodyText.text = sb.ToString();
        }
    }
}
