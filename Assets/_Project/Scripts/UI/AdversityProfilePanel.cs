using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Adversity;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 弱点/优势图谱面板（方案 8.5 / 23.3 第 26 条）：
    /// **个性化推断必须可删除、可关闭，并显示置信度逻辑。**
    ///
    /// 这里显示的每一条都是"可观察条件 → 可观察行为"的统计，
    /// 不是关于玩家是谁的结论；样本不足的只标成"假设"，
    /// 只有 ≥10 次且跨场景稳定出现的才会被敌人用于针对性战术。
    /// </summary>
    public class AdversityProfilePanel : MonoBehaviour
    {
        GameObject _panel;
        Text _bodyText, _toggleLabel;

        public static AdversityProfilePanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<AdversityProfilePanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "AdversityProfilePanel", new Vector2(1320, 1000),
                new Color(0.07f, 0.07f, 0.1f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "逆 境 画 像", 40,
                TextAnchor.MiddleCenter, new Color(0.75f, 0.7f, 0.95f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(700, 56));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "这里只有可观察的游戏行为统计——不是对你这个人的判断，也不做任何现实层面的推断。\n" +
                "样本 <3 次只算「假设」，3-9 次是「弱证据」，≥10 次且跨多个场景才算「稳定模式」。\n" +
                "只有稳定模式才允许被敌人用于针对性战术；任何一条你都可以直接删掉。", 21,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.85f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -116), new Vector2(1220, 90));

            _bodyText = UiUtil.MakeText(_panel.transform, "Body", "", 20,
                TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.95f));
            UiUtil.SetRect(_bodyText, new Vector2(0.5f, 1f), new Vector2(0, -560), new Vector2(1220, 780));
            _bodyText.lineSpacing = 1.1f;

            var toggleBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 0f),
                new Vector2(-420, 116), new Vector2(400, 70),
                new Color(0.32f, 0.36f, 0.45f, 0.95f), OnToggle, 22);
            _toggleLabel = toggleBtn.GetComponentInChildren<Text>();

            UiUtil.MakeButton(_panel.transform, "删除全部个性化推断", new Vector2(0.5f, 0f),
                new Vector2(20, 116), new Vector2(400, 70),
                new Color(0.5f, 0.25f, 0.25f, 0.95f), OnDeleteAll, 22);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(400, 116), new Vector2(260, 70),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 24);

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

        void OnToggle()
        {
            AdversityProfile.Enabled = !AdversityProfile.Enabled;
            GameEvents.RaiseSubtitle(AdversityProfile.Enabled
                ? "个性化推断已开启——敌人会根据你的可观察打法调整战术。"
                : "个性化推断已关闭——不再记录新的行为统计，敌人只用固定战术。");
            Refresh();
        }

        void OnDeleteAll()
        {
            AdversityProfile.DeleteAll();
            GameEvents.RaiseSubtitle("全部个性化推断已删除——从这里重新开始观察。");
            Refresh();
        }

        void Refresh()
        {
            if (_toggleLabel != null)
                _toggleLabel.text = AdversityProfile.Enabled ? "个性化推断：开启（点此关闭）" : "个性化推断：已关闭（点此开启）";
            if (_bodyText == null) return;

            var sb = new StringBuilder();

            sb.Append("── 弱点图谱（可观察条件 → 可观察行为）──\n");
            if (AdversityProfile.Vulnerabilities.Count == 0)
                sb.Append("  （还没有观察到任何模式）\n");
            foreach (var e in AdversityProfile.Vulnerabilities)
            {
                sb.Append("  ").Append(e.triggerTag).Append(" → ").Append(e.behaviorTag)
                  .Append("    出现率 ").Append(Mathf.RoundToInt(e.Rate * 100f)).Append('%')
                  .Append("  样本 ").Append(e.sampleCount)
                  .Append("  场景 ").Append(e.sceneDiversity.Count)
                  .Append("  置信度 ").Append(e.confidence.ToString("F2"))
                  .Append("  【").Append(e.EvidenceLabel()).Append('】');
                if (e.UsableForTargeting) sb.Append(" ← 已达到可被针对的门槛");
                sb.Append('\n');
                if (e.counterStrategies.Count > 0)
                {
                    sb.Append("      可用反制：");
                    foreach (var c in e.counterStrategies) sb.Append(c).Append("；");
                    sb.Append('\n');
                }
            }

            sb.Append("\n── 优势图谱（V2.0 明确禁止只建立「缺陷画像」）──\n");
            if (AdversityProfile.Strengths.Count == 0)
                sb.Append("  （还没有观察到稳定优势）\n");
            foreach (var s in AdversityProfile.Strengths)
            {
                sb.Append("  ").Append(s.strengthTag)
                  .Append("    样本 ").Append(s.sampleCount)
                  .Append("  场景 ").Append(s.sceneDiversity.Count)
                  .Append("  置信度 ").Append(s.confidence.ToString("F2"))
                  .Append("  【").Append(s.EvidenceLabel()).Append('】');
                if (s.DeservesShowcase) sb.Append(" ← 导演会为它安排发挥窗口");
                sb.Append('\n');
            }

            sb.Append("\n── 经历图谱（只保存角色类型与事件结构）──\n");
            if (AdversityProfile.Experiences.Count == 0)
                sb.Append("  （还没有记录）\n");
            int shown = 0;
            foreach (var x in AdversityProfile.Experiences)
            {
                if (shown++ >= 4) break;
                sb.Append("  · 环境：").Append(x.environment)
                  .Append("  角色类型：").Append(x.roleType).Append('\n');
                sb.Append("    发生了什么：").Append(x.what).Append('\n');
                if (!string.IsNullOrEmpty(x.altStrategy))
                    sb.Append("    下次想换的做法：").Append(x.altStrategy).Append('\n');
            }

            _bodyText.text = sb.ToString();
        }
    }
}
