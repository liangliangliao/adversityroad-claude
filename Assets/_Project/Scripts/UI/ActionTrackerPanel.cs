using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 现实行动追踪面板（安全屋·行动）：复盘里写下的「现实里：去做 X」在这里回访——
    /// 「上次你说要做 X，做到了吗？」做到 → 复盘点 +2、连续行动天数 +1。
    /// 这是本作核心命题的落地：把游戏内成长与现实里的一小步绑定起来。
    /// </summary>
    public class ActionTrackerPanel : MonoBehaviour
    {
        GameObject _panel;
        GameObject _content;   // 动态行容器（每次刷新重建）
        Text _statusText;

        public static ActionTrackerPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<ActionTrackerPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "ActionTrackerPanel", new Vector2(1180, 1010),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "现 实 行 动 · 追 踪", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(820, 54));

            _statusText = UiUtil.MakeText(_panel.transform, "Status", "", 25,
                TextAnchor.MiddleCenter, new Color(0.85f, 0.95f, 0.85f));
            UiUtil.SetRect(_statusText, new Vector2(0.5f, 1f), new Vector2(0, -108), new Vector2(1040, 40));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "复盘时写下的「现实里：…」会来到这里等你确认。做到就点「做到了」——不求完美，先做一小步。",
                21, TextAnchor.MiddleCenter, new Color(0.72f, 0.75f, 0.8f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -152), new Vector2(1060, 34));

            _content = new GameObject("Content", typeof(RectTransform));
            _content.transform.SetParent(_panel.transform, false);
            var crt = _content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 1f);
            crt.anchorMax = new Vector2(0.5f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = new Vector2(0, -196);
            crt.sizeDelta = new Vector2(1100, 720);

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 46), new Vector2(260, 72),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        void Rebuild()
        {
            for (int i = _content.transform.childCount - 1; i >= 0; i--)
                Object.Destroy(_content.transform.GetChild(i).gameObject);

            _statusText.text = "连续行动 " + ActionSystem.Streak + " 天　·　累计完成 " +
                ActionSystem.TotalDone + " 件";

            var pending = ActionSystem.Pending();
            float y = 0f;

            var head = UiUtil.MakeText(_content.transform, "PendHead",
                pending.Count > 0 ? "待确认（" + pending.Count + "）" : "暂无待确认的现实行动", 25,
                TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.5f));
            UiUtil.SetRect(head, new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(1060, 34));
            head.fontStyle = FontStyle.Bold;
            y -= 46f;

            int shown = Mathf.Min(pending.Count, 5);
            for (int i = 0; i < shown; i++)
            {
                var c = pending[i];
                MakeRow(c, y);
                y -= 96f;
            }
            if (pending.Count > shown)
            {
                var more = UiUtil.MakeText(_content.transform, "More",
                    "…… 还有 " + (pending.Count - shown) + " 条，逐条确认后显示", 20,
                    TextAnchor.MiddleLeft, new Color(0.7f, 0.72f, 0.76f));
                UiUtil.SetRect(more, new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(1060, 30));
                y -= 42f;
            }

            // 最近已确认
            var recent = ActionSystem.Recent(4);
            if (recent.Count > 0)
            {
                y -= 12f;
                var rHead = UiUtil.MakeText(_content.transform, "RecentHead", "最近记录", 23,
                    TextAnchor.MiddleLeft, new Color(0.7f, 0.85f, 0.95f));
                UiUtil.SetRect(rHead, new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(1060, 32));
                rHead.fontStyle = FontStyle.Bold;
                y -= 40f;
                foreach (var c in recent)
                {
                    string mark = c.status == 1 ? "<color=#7fd48a>✓ 做到</color>" : "<color=#9aa0a8>· 暂缓</color>";
                    var line = UiUtil.MakeText(_content.transform, "R",
                        mark + "　" + Trunc(c.text, 34) + "　<size=16><color=#888>" + c.resolvedDay + "</color></size>",
                        20, TextAnchor.MiddleLeft, new Color(0.82f, 0.84f, 0.88f));
                    UiUtil.SetRect(line, new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(1060, 30));
                    y -= 36f;
                }
            }
        }

        void MakeRow(ActionCommitment c, float y)
        {
            var bg = new GameObject("Row", typeof(Image));
            bg.transform.SetParent(_content.transform, false);
            bg.GetComponent<Image>().color = new Color(0.16f, 0.19f, 0.26f, 0.95f);
            var rt = bg.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(1060, 86);

            var label = UiUtil.MakeText(bg.transform, "Txt",
                Trunc(c.text, 40) + (string.IsNullOrEmpty(c.chapterTitle) ? "" :
                    "\n<size=15><color=#8a8f98>来自：" + Trunc(c.chapterTitle, 26) + "</color></size>"),
                22, TextAnchor.MiddleLeft, new Color(0.92f, 0.92f, 0.95f));
            UiUtil.SetRect(label, new Vector2(0f, 0.5f), new Vector2(348, 0), new Vector2(660, 78));
            label.alignment = TextAnchor.MiddleLeft;

            UiUtil.MakeButton(bg.transform, "做到了", new Vector2(1f, 0.5f), new Vector2(-250, 0),
                new Vector2(170, 62), new Color(0.25f, 0.5f, 0.35f, 0.95f), () => OnDone(c), 24);
            UiUtil.MakeButton(bg.transform, "还没", new Vector2(1f, 0.5f), new Vector2(-70, 0),
                new Vector2(150, 62), new Color(0.34f, 0.34f, 0.4f, 0.95f), () => OnSkip(c), 24);
        }

        void OnDone(ActionCommitment c)
        {
            var pc = FindObjectOfType<PlayerController>();
            ActionSystem.MarkDone(c, pc != null ? pc.Stats : null);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.8f);
            Rebuild();
        }

        void OnSkip(ActionCommitment c)
        {
            ActionSystem.MarkSkipped(c);
            GameAudio.Play(GameAudio.Sfx.Hit, 0.4f);
            Rebuild();
        }

        static string Trunc(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            Rebuild();
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
