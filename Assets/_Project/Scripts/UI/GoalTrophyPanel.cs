using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Goals;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 目标达成庆祝页（方案 26.2 Hall of Goals 的"当场那一下"）。
    ///
    /// 走完一整条旅程——五个以上专属关卡、若干次失败与重来——原来只换来一条
    /// 两秒就滚走的字幕。那是把最该被看见的时刻处理成了一次普通提示。
    /// 玩家写下目标、被挡了一路、终于把最后一个挡路的打掉，这一刻需要一个**停下来的画面**：
    /// 奖杯、目标原文、这一路的数字，以及一句"它已经立在你的 Hall of Goals 里"。
    ///
    /// 数字全部来自旅程本身（里程碑、关卡、失败次数、天数），不编造成绩——
    /// 庆祝的是他真的做完的事，不是一个装饰性的弹窗。
    /// </summary>
    public class GoalTrophyPanel : MonoBehaviour
    {
        GameObject _panel;
        Text _titleText, _bodyText;
        RectTransform _cup;
        float _shownAt = -99f;

        public static GoalTrophyPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<GoalTrophyPanel>();
            comp.Build(canvas);
            return comp;
        }

        void OnEnable() => GoalOS.GoalCompleted += Show;
        void OnDisable() => GoalOS.GoalCompleted -= Show;

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "GoalTrophyPanel", new Vector2(1100, 900),
                new Color(0.06f, 0.06f, 0.09f, 0.985f));

            var head = UiUtil.MakeText(_panel.transform, "Head", "目 标 达 成", 46,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f));
            UiUtil.SetRect(head, new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(800, 60));

            // 奖杯用文字符号：不引入任何美术资源，运行时构建（与全项目一致）
            var cup = UiUtil.MakeText(_panel.transform, "Cup", "🏆", 120,
                TextAnchor.MiddleCenter, new Color(1f, 0.82f, 0.3f));
            _cup = UiUtil.SetRect(cup, new Vector2(0.5f, 1f), new Vector2(0, -180), new Vector2(300, 160));

            _titleText = UiUtil.MakeText(_panel.transform, "Title", "", 34,
                TextAnchor.MiddleCenter, Color.white);
            UiUtil.SetRect(_titleText, new Vector2(0.5f, 1f), new Vector2(0, -300), new Vector2(980, 60));

            _bodyText = UiUtil.MakeText(_panel.transform, "Body", "", 24,
                TextAnchor.UpperLeft, new Color(0.9f, 0.92f, 0.96f));
            UiUtil.SetRect(_bodyText, new Vector2(0.5f, 1f), new Vector2(0, -520), new Vector2(940, 360));

            UiUtil.MakeButton(_panel.transform, "收下这一座", new Vector2(0.5f, 0f),
                new Vector2(0, 70), new Vector2(360, 84),
                new Color(0.55f, 0.42f, 0.15f, 0.96f), Hide, 30);

            _panel.SetActive(false);
        }

        void Show(GoalData g)
        {
            if (g == null || _panel == null) return;

            _titleText.text = "「" + g.title + "」";

            var p = GoalJourneyGraph.Progress(g);
            int aiCleared = 0, aiTotal = 0, legacyCleared = 0;
            foreach (var c in g.chapters)
            {
                if (c.source == ChapterSource.Legacy) { if (c.cleared) legacyCleared++; continue; }
                aiTotal++;
                if (c.cleared) aiCleared++;
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(g.why))
                sb.Append("当初写下的理由：").Append(g.why).Append("\n\n");
            sb.Append("走完的路：\n");
            sb.Append("  · 里程碑        ").Append(p.milestonesDone).Append(" / ").Append(p.milestonesTotal).Append('\n');
            sb.Append("  · 消除的障碍    ").Append(p.obstaclesRemoved).Append(" / ").Append(p.obstaclesTotal).Append('\n');
            sb.Append("  · 专属关卡      ").Append(aiCleared).Append(" / ").Append(aiTotal).Append('\n');
            if (legacyCleared > 0)
                sb.Append("  · 另外打通的经典关卡 ").Append(legacyCleared).Append('\n');
            sb.Append("  · 现实行动打卡  ").Append(p.actionsDone).Append(" / ").Append(p.actionsTotal).Append('\n');
            sb.Append("  · 经过的天数    ").Append(OpenWorld.WorldState.Day).Append(" 天\n");
            if (g.evolution.Count > 0)
                sb.Append("  · 中途重铸目标  ").Append(g.evolution.Count).Append(" 次（改方向不是放弃）\n");
            sb.Append("\n它已经立在你的 Hall of Goals 里——「逆境史」面板随时能回来看。");
            _bodyText.text = sb.ToString();

            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            _shownAt = Time.unscaledTime;
            Time.timeScale = 0f;
            GameAudio.Play(GameAudio.Sfx.Cast, 0.9f);
        }

        void Update()
        {
            // 奖杯轻微起伏：静态弹窗看着像报错，动一下才像"庆祝"
            if (_panel == null || !_panel.activeSelf || _cup == null) return;
            float t = Time.unscaledTime - _shownAt;
            _cup.localScale = Vector3.one * (1f + Mathf.Sin(t * 3.2f) * 0.06f);
            _cup.anchoredPosition = new Vector2(0, -180f + Mathf.Sin(t * 2.1f) * 6f);
        }

        public void Toggle()
        {
            if (_panel == null) return;
            if (_panel.activeSelf) { Hide(); return; }
            var g = GoalOS.Active;
            if (g != null && g.completed) Show(g);
        }

        void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
