using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Goals;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 客厅那面墙上的大屏目标看板——**一整块屏幕，字要看得清**。
    ///
    /// 上一版用 TextMesh 直接往墙上贴字：字号只能靠 characterSize 缩放，
    /// 放大就糊、缩小就看不见，而且没有背板，远看像一堆浮字。
    /// 这一版改成**世界空间 Canvas**：屏幕是一块真的板子，字是 UI Text，
    /// 按 1 单位 = 1 米、200 像素/米的密度渲染——放大到 17 米宽依然是锐利的。
    ///
    /// 三栏常驻：
    ///   左：最终目标（标题 / 动机 / 完成条件 / 四维进度）
    ///   中：当前计划（里程碑清单 + 正在这一段 + 下一步行动）
    ///   右：关卡路线（每一关带 ✔ ▶ ○ 状态与来源）
    /// 每 2 秒刷新一次；走近仍可交互打开完整面板。
    /// </summary>
    public class GoalBoardDisplay : MonoBehaviour
    {
        /// <summary>
        /// 每米多少像素：越高越锐利，代价是这块 Canvas 的分辨率与字体图集占用。
        ///
        /// 150 → 260：150 的密度下正文字号只有 31 像素，站在两三米外够用，
        /// 但玩家走到板子跟前（室内镜头被家具挡住时会一路回缩到 0.5 米，
        /// 脸几乎贴在屏幕上）时，31 像素的字被放大到占满半个屏幕——那就是
        /// 截图里那种糊成一团的样子。260 下正文 54 像素、标题 120 像素，
        /// 贴脸看也还是清楚的。
        /// </summary>
        const float PixelsPerMeter = 260f;

        Text _head, _left, _mid, _right;
        float _next;

        public static GoalBoardDisplay Attach(GameObject board, Vector3 center, float width,
            float height, bool faceNorth = false)
        {
            var d = board.AddComponent<GoalBoardDisplay>();

            // 【不能挂在板子下面】那块板的 localScale 是 (17, 4.6, 0.34)，
            // Canvas 一旦成为它的子物体就会被这个非等比缩放整个拉歪。
            // 挂到别墅根节点（scale 恒为 1）上，屏幕才是方正的。
            var go = new GameObject("GoalScreen", typeof(Canvas));
            if (VillaKit.Root != null) go.transform.SetParent(VillaKit.Root, true);
            go.transform.position = center;
            go.transform.rotation = Quaternion.Euler(0, faceNorth ? 180f : 0f, 0);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvas.transform as RectTransform;
            rt.sizeDelta = new Vector2(width * PixelsPerMeter, height * PixelsPerMeter);
            rt.localScale = Vector3.one / PixelsPerMeter;

            // 屏幕底：深色玻璃感的背板 + 一圈发光边
            var bg = d.Panel(rt, "Bg", Vector2.zero, new Vector2(width, height) * PixelsPerMeter,
                new Color(0.05f, 0.08f, 0.13f, 0.98f));
            d.Panel(bg.transform as RectTransform, "Edge", Vector2.zero,
                new Vector2(width * PixelsPerMeter, 6f), new Color(0.30f, 0.62f, 0.92f, 0.9f))
                .GetComponent<RectTransform>().anchoredPosition =
                new Vector2(0, height * PixelsPerMeter / 2f - 3f);

            float w = width * PixelsPerMeter, hgt = height * PixelsPerMeter;
            d._head = d.Label(rt, "Head", new Vector2(0, hgt * 0.40f), new Vector2(w * 0.94f, hgt * 0.16f),
                Mathf.RoundToInt(hgt * 0.115f), new Color(1f, 0.86f, 0.42f), TextAnchor.MiddleCenter);
            d._left = d.Label(rt, "Left", new Vector2(-w * 0.325f, -hgt * 0.06f),
                new Vector2(w * 0.30f, hgt * 0.72f), Mathf.RoundToInt(hgt * 0.052f),
                new Color(0.95f, 0.96f, 0.99f), TextAnchor.UpperLeft);
            d._mid = d.Label(rt, "Mid", new Vector2(0, -hgt * 0.06f),
                new Vector2(w * 0.30f, hgt * 0.72f), Mathf.RoundToInt(hgt * 0.052f),
                new Color(0.78f, 0.95f, 0.85f), TextAnchor.UpperLeft);
            d._right = d.Label(rt, "Right", new Vector2(w * 0.325f, -hgt * 0.06f),
                new Vector2(w * 0.30f, hgt * 0.72f), Mathf.RoundToInt(hgt * 0.052f),
                new Color(0.86f, 0.90f, 1f), TextAnchor.UpperLeft);

            // 分栏竖线：三栏之间要有分隔，否则一屏字挤在一起没法读
            for (int i = -1; i <= 1; i += 2)
                d.Panel(rt, "Divider", new Vector2(i * w * 0.163f, -hgt * 0.06f),
                    new Vector2(2.5f, hgt * 0.70f), new Color(0.25f, 0.45f, 0.65f, 0.55f));

            d.Refresh();
            return d;
        }

        Image Panel(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        Text Label(RectTransform parent, string name, Vector2 pos, Vector2 size, int fontSize,
            Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = Mathf.Max(12, fontSize);
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.lineSpacing = 1.15f;
            t.raycastTarget = false;
            return t;
        }

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 2f;
            Refresh();
        }

        public void Refresh()
        {
            if (_head == null) return;
            var g = GoalOS.Active;
            if (g == null)
            {
                _head.text = "目 标 看 板";
                _left.text = "还没有目标。\n\n用办公室的电脑，或打开目标面板，\n写下你要做成的那件事。";
                _mid.text = "系统会为它预测最可能挡路的障碍，\n并为每条障碍现场生成一关。";
                _right.text = "关卡路线会显示在这里：\n哪一关打过了、下一关是哪个、\n一共还剩几关。";
                return;
            }

            _head.text = g.completed ? "★ " + g.title + " · 已达成" : g.title;

            var sb = new StringBuilder();
            sb.Append("【最终目标】\n").Append(g.title).Append("\n\n");
            if (!string.IsNullOrEmpty(g.why))
                sb.Append("【为什么】\n").Append(g.why).Append("\n\n");
            if (!string.IsNullOrEmpty(g.successCondition))
                sb.Append("【怎样算成】\n").Append(g.successCondition).Append("\n\n");
            var p = GoalJourneyGraph.Progress(g);
            sb.Append("里程碑  ").Append(p.milestonesDone).Append(" / ").Append(p.milestonesTotal).Append('\n');
            sb.Append("障碍    ").Append(p.obstaclesRemoved).Append(" / ").Append(p.obstaclesTotal).Append('\n');
            sb.Append("行动    ").Append(p.actionsDone).Append(" / ").Append(p.actionsTotal);
            _left.text = sb.ToString();

            sb.Length = 0;
            sb.Append("【当前计划】\n");
            int shown = 0;
            foreach (var m in g.milestones)
            {
                if (shown++ >= 7) break;
                sb.Append(m.done ? "✔ " : "○ ").Append(m.title).Append('\n');
            }
            var cur = g.CurrentMilestone();
            if (cur != null)
            {
                sb.Append("\n【正在这一段】\n").Append(cur.title).Append('\n');
                if (!string.IsNullOrEmpty(cur.successHint)) sb.Append(cur.successHint).Append('\n');
            }
            sb.Append("\n【下一步行动】\n");
            int acts = 0;
            foreach (var a in g.criticalActions)
            {
                if (a.done || acts >= 4) continue;
                sb.Append("· ").Append(a.label).Append('\n');
                acts++;
            }
            if (acts == 0) sb.Append("（暂无待办）");
            _mid.text = sb.ToString();

            sb.Length = 0;
            sb.Append("【关卡路线】\n");
            int done = 0, total = 0, listed = 0;
            foreach (var c in g.chapters) { total++; if (c.cleared) done++; }
            bool metNext = false;
            foreach (var c in g.chapters)
            {
                if (listed++ >= 10) break;
                string mark;
                if (c.cleared) mark = "✔ ";
                else if (!metNext) { mark = "▶ "; metNext = true; }
                else mark = "○ ";
                string tag = c.source == ChapterSource.Legacy ? "（经典）"
                    : c.source == ChapterSource.UserPreset ? "（自设）" : "";
                sb.Append(mark).Append(c.chapterName).Append(tag).Append('\n');
            }
            if (total == 0) sb.Append("（正在生成……）\n");
            sb.Append("\n通关  ").Append(done).Append(" / ").Append(total);
            if (g.completed) sb.Append("\n\n🏆 这条路已经走完");
            _right.text = sb.ToString();
        }
    }
}
