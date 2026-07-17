using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 旧事档案（安全屋）：历史复盘记录——旧事不再作为脑内回声无限播放，
    /// 而是整理成"事实/感受/边界/行动"四栏躺在档案里。支持翻页浏览。
    /// </summary>
    public class ArchivePanel : MonoBehaviour
    {
        const int PerPage = 3;

        GameObject _panel;
        Text _bodyText, _pageText;
        int _page;

        public static ArchivePanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<ArchivePanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "ArchivePanel", new Vector2(1240, 980),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "旧 事 档 案", 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(700, 56));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "归档过的战斗复盘都在这里——事实留下了，回放停止了。", 24,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.85f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -96), new Vector2(1000, 34));

            _bodyText = UiUtil.MakeText(_panel.transform, "Body", "", 22,
                TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.95f));
            UiUtil.SetRect(_bodyText, new Vector2(0.5f, 1f), new Vector2(0, -500), new Vector2(1140, 720));
            _bodyText.lineSpacing = 1.12f;

            _pageText = UiUtil.MakeText(_panel.transform, "Page", "", 24,
                TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.75f));
            UiUtil.SetRect(_pageText, new Vector2(0.5f, 0f), new Vector2(0, 130), new Vector2(300, 34));

            UiUtil.MakeButton(_panel.transform, "上一页", new Vector2(0.5f, 0f),
                new Vector2(-330, 54), new Vector2(240, 72),
                new Color(0.25f, 0.3f, 0.4f, 0.95f), () => Flip(-1), 26);
            UiUtil.MakeButton(_panel.transform, "下一页", new Vector2(0.5f, 0f),
                new Vector2(-60, 54), new Vector2(240, 72),
                new Color(0.25f, 0.3f, 0.4f, 0.95f), () => Flip(1), 26);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(280, 54), new Vector2(240, 72),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        int PageCount()
        {
            int n = GrowthSystem.Reflections().Count;
            return Mathf.Max(1, (n + PerPage - 1) / PerPage);
        }

        void Flip(int dir)
        {
            _page = Mathf.Clamp(_page + dir, 0, PageCount() - 1);
            Refresh();
        }

        void Refresh()
        {
            var entries = GrowthSystem.Reflections();
            if (entries.Count == 0)
            {
                _bodyText.text = "档案还是空的。\n\n战斗之后打开「复盘」面板，把这一战整理成四栏并归档——\n旧事会从脑内回声，变成这里的一页档案。";
                _pageText.text = "";
                return;
            }

            // 最新的在最前
            var sb = new System.Text.StringBuilder();
            int start = entries.Count - 1 - _page * PerPage;
            for (int i = start; i > start - PerPage && i >= 0; i--)
            {
                var e = entries[i];
                sb.Append("─── ").Append(e.chapterTitle).Append("  ").Append(e.savedAt).Append(" ───\n");
                sb.Append("事实：").Append(e.fact).Append('\n');
                sb.Append("感受：").Append(e.feeling).Append('\n');
                sb.Append("边界：").Append(e.boundary).Append('\n');
                sb.Append("行动：").Append(e.action).Append("\n\n");
            }
            _bodyText.text = sb.ToString();
            _pageText.text = (_page + 1) + " / " + PageCount();
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            _page = 0;
            Refresh();
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
