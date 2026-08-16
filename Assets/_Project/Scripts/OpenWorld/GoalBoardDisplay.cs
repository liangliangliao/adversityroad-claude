using System.Text;
using UnityEngine;
using AdversityRoad.Goals;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 客厅那面墙上的大型目标看板——**把信息直接写在墙上**，不用先打开面板。
    ///
    /// 玩家的要求是"目标看板放大并清楚显示至少包括目标、计划、关卡路线等等信息"。
    /// 原来那块板只是一个可交互的方块：走近才提示一句今日目标，其余全在面板里。
    /// 现在做成一整面 12×4 的落地屏，分三栏常驻显示：
    ///   左：最终目标（标题 / 动机 / 完成条件）
    ///   中：当前计划（里程碑清单 + 下一步该做的事）
    ///   右：关卡路线（这条旅程要打的每一关，带状态）
    /// 走近仍然可以按 E 打开完整面板——墙上这份是"抬头就能看见"的那一份。
    ///
    /// 文字用 TextMesh（和全项目的路牌同一套），不引入任何 UI 资源；
    /// 每 2 秒刷新一次，进度一变墙上就跟着变。
    /// </summary>
    public class GoalBoardDisplay : MonoBehaviour
    {
        TextMesh _head, _left, _mid, _right;
        float _next;

        bool _faceNorth;

        /// <param name="faceNorth">true = 板面朝 +Z（挂在南墙上，站在北边看）。</param>
        public static GoalBoardDisplay Attach(GameObject board, Vector3 center, float width,
            float height, bool faceNorth = false)
        {
            var d = board.AddComponent<GoalBoardDisplay>();
            d._faceNorth = faceNorth;
            // 文字贴在板面前方一点点；朝北的板子，"前方"是 +Z
            float front = faceNorth ? 0.22f : -0.22f;
            // 朝北时左右也要跟着镜像，否则三栏顺序在玩家眼里是反的
            float side = faceNorth ? -1f : 1f;
            d._head = d.Make("Head", center + new Vector3(0, height * 0.40f, front), 0.115f, 82,
                new Color(1f, 0.86f, 0.42f), TextAnchor.MiddleCenter);
            d._left = d.Make("Left", center + new Vector3(side * -width * 0.34f, height * 0.10f, front), 0.062f, 46,
                new Color(0.95f, 0.96f, 0.99f), TextAnchor.UpperLeft);
            d._mid = d.Make("Mid", center + new Vector3(side * -width * 0.02f, height * 0.10f, front), 0.062f, 46,
                new Color(0.80f, 0.94f, 0.86f), TextAnchor.UpperLeft);
            d._right = d.Make("Right", center + new Vector3(side * width * 0.28f, height * 0.10f, front), 0.062f, 46,
                new Color(0.86f, 0.90f, 1f), TextAnchor.UpperLeft);
            d.Refresh();
            return d;
        }

        TextMesh Make(string name, Vector3 worldPos, float size, int fontSize, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Board_" + name);
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.Euler(0, _faceNorth ? 0f : 180f, 0);
            var tm = go.AddComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = fontSize;
            tm.characterSize = size;
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.MiddleCenter ? TextAlignment.Center : TextAlignment.Left;
            tm.color = color;
            var mr = go.GetComponent<MeshRenderer>();
            if (tm.font != null) mr.material = tm.font.material;
            return tm;
        }

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 2f;
            Refresh();
        }

        public void Refresh()
        {
            var g = GoalOS.Active;
            if (g == null)
            {
                _head.text = "目 标 看 板";
                _left.text = "还没有目标。\n\n去电脑或目标面板\n写下你要做成的那件事，\n系统会为它生成\n一整条要打的关卡路线。";
                _mid.text = "";
                _right.text = "";
                return;
            }

            _head.text = g.completed ? "★ " + g.title + " · 已达成" : g.title;

            // —— 左：最终目标 ——
            var sb = new StringBuilder();
            sb.Append("【最终目标】\n").Append(Wrap(g.title, 11)).Append('\n');
            if (!string.IsNullOrEmpty(g.why))
                sb.Append("\n【为什么】\n").Append(Wrap(g.why, 11)).Append('\n');
            if (!string.IsNullOrEmpty(g.successCondition))
                sb.Append("\n【怎样算成】\n").Append(Wrap(g.successCondition, 11)).Append('\n');
            var p = GoalJourneyGraph.Progress(g);
            sb.Append("\n里程碑 ").Append(p.milestonesDone).Append('/').Append(p.milestonesTotal);
            sb.Append("   障碍 ").Append(p.obstaclesRemoved).Append('/').Append(p.obstaclesTotal);
            _left.text = sb.ToString();

            // —— 中：当前计划 ——
            sb.Length = 0;
            sb.Append("【当前计划】\n");
            int shown = 0;
            foreach (var m in g.milestones)
            {
                if (shown++ >= 6) break;
                sb.Append(m.done ? "✔ " : "○ ").Append(Cut(m.title, 11)).Append('\n');
            }
            var cur = g.CurrentMilestone();
            if (cur != null)
            {
                sb.Append("\n【正在这一段】\n").Append(Wrap(cur.title, 11)).Append('\n');
                if (!string.IsNullOrEmpty(cur.successHint))
                    sb.Append(Wrap(cur.successHint, 11)).Append('\n');
            }
            sb.Append("\n【下一步行动】\n");
            int acts = 0;
            foreach (var a in g.criticalActions)
            {
                if (a.done || acts >= 3) continue;
                sb.Append("· ").Append(Cut(a.label, 11)).Append('\n');
                acts++;
            }
            if (acts == 0) sb.Append("（暂无待办）\n");
            _mid.text = sb.ToString();

            // —— 右：关卡路线 ——
            sb.Length = 0;
            sb.Append("【关卡路线】\n");
            int done = 0, total = 0, listed = 0;
            foreach (var c in g.chapters)
            {
                total++;
                if (c.cleared) done++;
            }
            bool metNext = false;
            foreach (var c in g.chapters)
            {
                if (listed++ >= 9) break;
                string mark;
                if (c.cleared) mark = "✔ ";
                else if (!metNext) { mark = "▶ "; metNext = true; }
                else mark = "○ ";
                string tag = c.source == ChapterSource.Legacy ? "（经典）" :
                             c.source == ChapterSource.UserPreset ? "（自设）" : "";
                sb.Append(mark).Append(Cut(c.chapterName, 9)).Append(tag).Append('\n');
            }
            if (total == 0) sb.Append("（正在生成……）\n");
            sb.Append("\n通关 ").Append(done).Append(" / ").Append(total);
            if (g.completed) sb.Append("\n\n🏆 这条路已经走完");
            _right.text = sb.ToString();
        }

        /// <summary>按字数硬折行：TextMesh 不会自动换行，长句会一路冲出板子。</summary>
        static string Wrap(string s, int perLine)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                sb.Append(s[i]);
                if ((i + 1) % perLine == 0 && i + 1 < s.Length) sb.Append('\n');
            }
            return sb.ToString();
        }

        static string Cut(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
