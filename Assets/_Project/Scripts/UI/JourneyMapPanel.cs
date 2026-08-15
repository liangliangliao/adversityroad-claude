using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Goals;
using AdversityRoad.OpenWorld;

namespace AdversityRoad.UI
{
    /// <summary>
    /// Journey Map / Obstacle Map / Chapter Sources（方案 17.2 三合一）。
    ///
    /// 旅程图是有向图不是一条线：直接挑战、先训练、找资源、绕开低价值敌人、
    /// 失败后恢复、改变阶段方案——同一个目标允许多条道路。
    /// 障碍图区分「你已经知道的 / AI 预测的 / 你自己预设的」；
    /// 章节来源区分「经典 / 玩家预设 / AI 必选」，AI 必选缺失时明确报警。
    /// </summary>
    public class JourneyMapPanel : MonoBehaviour
    {
        enum Tab { Journey, Obstacles, Chapters }

        GameObject _panel;
        Text _bodyText, _tabText;
        InputField _presetInput;
        Tab _tab = Tab.Journey;

        public static JourneyMapPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<JourneyMapPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "JourneyMapPanel", new Vector2(1360, 1010),
                new Color(0.06f, 0.08f, 0.1f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "旅 程 图", 40,
                TextAnchor.MiddleCenter, new Color(0.7f, 0.9f, 0.95f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(700, 56));

            _tabText = UiUtil.MakeText(_panel.transform, "TabHint", "", 22,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.85f));
            UiUtil.SetRect(_tabText, new Vector2(0.5f, 1f), new Vector2(0, -92), new Vector2(1180, 34));

            UiUtil.MakeButton(_panel.transform, "旅程", new Vector2(0.5f, 1f),
                new Vector2(-420, -140), new Vector2(240, 58),
                new Color(0.2f, 0.32f, 0.4f, 0.95f), () => SetTab(Tab.Journey), 24);
            UiUtil.MakeButton(_panel.transform, "障碍图", new Vector2(0.5f, 1f),
                new Vector2(-140, -140), new Vector2(240, 58),
                new Color(0.32f, 0.26f, 0.36f, 0.95f), () => SetTab(Tab.Obstacles), 24);
            UiUtil.MakeButton(_panel.transform, "章节来源", new Vector2(0.5f, 1f),
                new Vector2(140, -140), new Vector2(240, 58),
                new Color(0.34f, 0.3f, 0.22f, 0.95f), () => SetTab(Tab.Chapters), 24);

            _bodyText = UiUtil.MakeText(_panel.transform, "Body", "", 20,
                TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.95f));
            UiUtil.SetRect(_bodyText, new Vector2(0.5f, 1f), new Vector2(0, -540), new Vector2(1240, 700));
            _bodyText.lineSpacing = 1.1f;

            _presetInput = UiUtil.MakeInput(_panel.transform,
                "写下你自己预设的障碍/关卡名（例如：周一早上的会议）",
                new Vector2(0.5f, 0f), new Vector2(-90, 128), new Vector2(880, 66), false);
            _presetInput.textComponent.fontSize = 22;

            UiUtil.MakeButton(_panel.transform, "加入我的预设章节", new Vector2(0.5f, 0f),
                new Vector2(500, 128), new Vector2(300, 66),
                new Color(0.3f, 0.45f, 0.35f, 0.95f), OnAddPreset, 22);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 48), new Vector2(260, 66),
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

        void SetTab(Tab t) { _tab = t; Refresh(); }

        void OnAddPreset()
        {
            var g = GoalOS.Active;
            if (g == null) { GameEvents.RaiseSubtitle("先在「目标」面板开始一条旅程。"); return; }
            string name = _presetInput != null ? _presetInput.text.Trim() : "";
            if (name.Length == 0)
            {
                GameEvents.RaiseSubtitle("写下你预感会挡路的那个东西——它会变成旅程里的一关。");
                return;
            }
            var bp = GoalChapterGenerator.AddUserPresetChapter(g, name, name, "residential");
            if (bp != null && _presetInput != null) _presetInput.text = "";
            Refresh();
        }

        void Refresh()
        {
            if (_bodyText == null) return;
            var g = GoalOS.Active;
            if (g == null)
            {
                _tabText.text = "";
                _bodyText.text = "还没有进行中的旅程——先在「目标」面板写下你要去哪里。";
                return;
            }

            switch (_tab)
            {
                case Tab.Obstacles: BuildObstacles(g); break;
                case Tab.Chapters: BuildChapters(g); break;
                default: BuildJourney(g); break;
            }
        }

        // ================= 旅程 =================

        void BuildJourney(GoalData g)
        {
            _tabText.text = "自由不是没有方向，而是同一个目标允许多条道路——" +
                "直接打、先训练、找资源、绕开、失败后恢复，都是路。";

            var p = GoalJourneyGraph.Progress(g);
            var sb = new StringBuilder();
            sb.Append("四维进度（拒绝「完成了 63%」的百分比幻觉）\n");
            sb.Append("  里程碑    ").Append(Bar(p.MilestoneRatio)).Append(' ')
              .Append(p.milestonesDone).Append('/').Append(p.milestonesTotal).Append('\n');
            sb.Append("  关键行动  ").Append(Bar(p.ActionRatio)).Append(' ')
              .Append(p.actionsDone).Append('/').Append(p.actionsTotal).Append('\n');
            sb.Append("  已消除障碍").Append(Bar(p.ObstacleRatio)).Append(' ')
              .Append(p.obstaclesRemoved).Append('/').Append(p.obstaclesTotal).Append('\n');
            sb.Append("  能力准备  ").Append(Bar(p.capabilityReadiness)).Append(' ')
              .Append(Mathf.RoundToInt(p.capabilityReadiness * 100f)).Append("%\n\n");

            var target = GoalJourneyGraph.CurrentTarget(g);
            if (target != null)
                sb.Append("灯塔指向：【").Append(target.TypeLabel()).Append("】")
                  .Append(target.title).Append('\n');
            var beacon = GoalBeaconController.Instance;
            if (beacon != null)
            {
                string dir = beacon.DirectionLine();
                if (!string.IsNullOrEmpty(dir)) sb.Append(dir).Append('\n');
            }
            sb.Append('\n');

            int col = -1;
            foreach (var n in g.nodes)
            {
                if (n.column != col)
                {
                    col = n.column;
                    sb.Append("── 阶段 ").Append(col).Append(" ──\n");
                }
                sb.Append("  ").Append(StateMark(n.state)).Append(' ')
                  .Append(n.mainLine ? "[主干] " : "[支线] ")
                  .Append('【').Append(n.TypeLabel()).Append("】")
                  .Append(n.title).Append('\n');
            }

            sb.Append("\n能力：");
            foreach (var c in g.capabilities)
                sb.Append(c.label).Append(' ').Append(Mathf.RoundToInt(c.readiness * 100f)).Append("%  ");
            _bodyText.text = sb.ToString();
        }

        static string StateMark(GoalNodeState s)
        {
            switch (s)
            {
                case GoalNodeState.Done: return "✓";
                case GoalNodeState.Active: return "◉";
                case GoalNodeState.Available: return "○";
                case GoalNodeState.Skipped: return "–";
                default: return "·";
            }
        }

        static string Bar(float r)
        {
            int filled = Mathf.RoundToInt(Mathf.Clamp01(r) * 10f);
            var sb = new StringBuilder("[");
            for (int i = 0; i < 10; i++) sb.Append(i < filled ? '■' : '□');
            sb.Append(']');
            return sb.ToString();
        }

        // ================= 障碍图 =================

        void BuildObstacles(GoalData g)
        {
            _tabText.text = "障碍分三类来源：你已经知道的 / AI 预测的 / 你自己预设的。" +
                "消除一个，世界上就少一层压着你的东西。";

            var sb = new StringBuilder();
            AppendObstacles(sb, g, ObstacleSource.PlayerKnown, "你已经知道的障碍");
            AppendObstacles(sb, g, ObstacleSource.AIPredicted, "AI 预测的障碍");
            AppendObstacles(sb, g, ObstacleSource.UserPreset, "你自己预设的障碍");

            sb.Append("\n风险因素：");
            foreach (var r in g.riskFactors) sb.Append(r).Append("  ");
            _bodyText.text = sb.ToString();
        }

        void AppendObstacles(StringBuilder sb, GoalData g, ObstacleSource src, string title)
        {
            sb.Append("── ").Append(title).Append(" ──\n");
            bool any = false;
            foreach (var o in g.obstacles)
            {
                if (o.source != src) continue;
                any = true;
                var ms = g.FindMilestone(o.linkedMilestoneId);
                sb.Append("  ").Append(o.removed ? "✓ 已消除  " : "✕ 仍在挡路  ")
                  .Append(o.label);
                if (ms != null) sb.Append("   → 挡住【").Append(ms.title).Append('】');
                string chapter = ChapterNameFor(g, o.obstacleId);
                if (!string.IsNullOrEmpty(chapter)) sb.Append("   关卡：").Append(chapter);
                sb.Append('\n');
            }
            if (!any) sb.Append("  （暂无）\n");
            sb.Append('\n');
        }

        static string ChapterNameFor(GoalData g, string obstacleId)
        {
            foreach (var c in g.chapters)
                if (c.primaryObstacle == obstacleId) return c.chapterName;
            return "";
        }

        // ================= 章节来源 =================

        void BuildChapters(GoalData g)
        {
            _tabText.text = "每条旅程必须至少有一个 AI 专属章节——" +
                "否则系统只是在做心理标签推荐，而不是目标个性化。";

            var sb = new StringBuilder();
            sb.Append(g.journeyReady
                ? "旅程状态：正式生成完成 ✓\n\n"
                : "旅程状态：⚠ 缺少通过校验的 AI 专属章节，不能进入「正式生成完成」\n\n");

            AppendChapters(sb, g, ChapterSource.Legacy, "经典章节（V1 七大线 · 按相关性插入）");
            AppendChapters(sb, g, ChapterSource.UserPreset, "玩家预设章节");
            AppendChapters(sb, g, ChapterSource.AIRequired, "AI 专属章节（必选）");
            _bodyText.text = sb.ToString();
        }

        void AppendChapters(StringBuilder sb, GoalData g, ChapterSource src, string title)
        {
            sb.Append("── ").Append(title).Append(" ──\n");
            bool any = false;
            foreach (var c in g.chapters)
            {
                if (c.source != src) continue;
                any = true;
                sb.Append("  ").Append(c.cleared ? "✓ " : "○ ").Append(c.chapterName);
                sb.Append("   落地：").Append(ChapterModuleLibrary.District(c.worldDistrictId).name);
                if (src == ChapterSource.AIRequired)
                    sb.Append("   CQS ").Append(c.cqs.ToString("F2"))
                      .Append(c.validated ? " ✓" : " ✕ " + c.rejectReason);
                sb.Append('\n');
                sb.Append("      通关：").Append(c.successCondition).Append('\n');
                sb.Append("      失败：").Append(c.failureConsequence).Append('\n');
                if (c.physicalMechanics.Count > 0)
                {
                    sb.Append("      机制：");
                    foreach (var m in c.physicalMechanics)
                    {
                        var info = ChapterModuleLibrary.Mechanic(m);
                        sb.Append(info != null ? info.name : m).Append(' ');
                    }
                    foreach (var m in c.mentalMechanics)
                    {
                        var info = ChapterModuleLibrary.Mechanic(m);
                        sb.Append('/').Append(info != null ? info.name : m).Append(' ');
                    }
                    sb.Append('\n');
                }
                sb.Append("      种子：").Append(c.assemblySeed).Append("（相同种子必定生成同一处场景）\n");
                if (src != ChapterSource.Legacy && c.site != null && !c.site.IsEmpty)
                {
                    var st = c.site;
                    sb.Append("      场景：").Append(st.siteName)
                      .Append("（").Append(SiteKitCatalog.Kind(st.siteKind).name)
                      .Append(" · ").Append(SiteKitCatalog.LayoutName(st.layout)).Append("）");
                    sb.Append("  房间 ").Append(st.rooms.Count)
                      .Append(" · NPC ").Append(st.npcs.Count)
                      .Append(" · 台词 ").Append(st.externalLines.Count + st.internalLines.Count)
                      .Append('\n');
                    for (int r = 0; r < st.rooms.Count && r < 4; r++)
                        sb.Append("        · ").Append(st.rooms[r].name)
                          .Append("：").Append(string.Join("/", st.rooms[r].props.ToArray()))
                          .Append('\n');
                    if (st.rules.Count > 0)
                        sb.Append("        规则：").Append(st.rules[0]).Append('\n');
                }
            }
            if (!any) sb.Append("  （暂无）\n");
            sb.Append('\n');
        }
    }
}
