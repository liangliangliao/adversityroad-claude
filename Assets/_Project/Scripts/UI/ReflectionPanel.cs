using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 战后复盘（安全屋核心）：把这一战整理成四栏——事实 / 感受 / 边界 / 行动。
    /// 「归档此战」清空反刍值并回补心理属性——用复盘代替反刍，把刺痛转成经验。
    /// 内容按当前章节主题生成（抽象化，绝不复刻真实人物/事件）。
    /// </summary>
    public class ReflectionPanel : MonoBehaviour
    {
        GameObject _panel;
        Text _factText, _feelText, _boundText, _actText, _ruminationText;

        // 四栏文案：事实 / 感受 / 边界 / 行动，按章节顺序对应五章主题 + 自由模式兜底。
        static readonly string[][] ChapterReflections =
        {
            new[] {
                "桌上的计划落灰，你在「我行不行」里反复打转，一直没有开始。",
                "不是懒——是怕自己不够好，于是抢先否定了自己。",
                "怀疑可以存在，但它没有资格替我做决定。",
                "现实里：为一个目标写下最小的第一步，今天就做五分钟。" },
            new[] {
                "你一次次把「开始」推给明天，明天却从不到来。",
                "总想等状态好了、准备足了再动，可那一刻不会自己来。",
                "不等动力，先开始；动力是被行动召回的，不是等来的。",
                "现实里：挑一件拖了很久的小事，只做五分钟，不求做完。" },
            new[] {
                "一声咳嗽、一个眼神，就把你的注意力整个拽走了。",
                "你把每个刺激都解释成「针对我」，越想越乱、越乱越耗。",
                "我听见了，但我不跟随；不必回应每一个声音。",
                "现实里：练习一次「听见但不跟随」，把注意力放回手上的事。" },
            new[] {
                "投出去的简历石沉大海，你开始怀疑自己的价值。",
                "没有回音，被你读成了「我不够好」。",
                "没有回应不代表没有价值，它只说明我要继续调整、继续投。",
                "现实里：今天再投递或改进一次，把「下一次」握在自己手里。" },
            new[] {
                "无数个「明天再说」，堆成了一个有你轮廓的巨大旧我。",
                "你害怕成为过去的自己，却又被它拖住脚步。",
                "旧我不是身份——我带着它继续往前，而不是被它定义。",
                "现实里：写下一件过去发生过、但不该继续定义你的事。" },
        };

        static readonly string[] FreeMode =
        {
            "这一战里，对方用言语反复消耗你、试图接管你的方向。",
            "被刺痛是真的，但刺痛不等于事实，也不等于你不行。",
            "我守住我自己的注意力、边界与人生主线。",
            "现实里：为自己做一件五分钟的小事，把主动权拿回来。",
        };

        public static ReflectionPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<ReflectionPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "ReflectionPanel", new Vector2(1200, 900),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "战 后 复 盘", 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(700, 56));

            _factText  = MakeColumn("事实 · 发生了什么", new Color(0.85f, 0.9f, 1f), -120);
            _feelText  = MakeColumn("感受 · 我为什么被刺痛", new Color(1f, 0.8f, 0.8f), -300);
            _boundText = MakeColumn("边界 · 下一次更早守住什么", new Color(0.6f, 0.9f, 0.7f), -480);
            _actText   = MakeColumn("行动 · 下一步现实小任务", new Color(1f, 0.85f, 0.5f), -660);

            _ruminationText = UiUtil.MakeText(_panel.transform, "Rum", "", 22,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.6f, 0.85f));
            UiUtil.SetRect(_ruminationText, new Vector2(0.5f, 1f), new Vector2(0, -790), new Vector2(1000, 30));

            UiUtil.MakeButton(_panel.transform, "归档此战（清空反刍值）", new Vector2(0.5f, 0f),
                new Vector2(-210, 56), new Vector2(560, 74),
                new Color(0.25f, 0.5f, 0.35f, 0.95f), OnArchive, 26);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(300, 56), new Vector2(260, 74),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        Text MakeColumn(string header, Color headColor, float y)
        {
            var h = UiUtil.MakeText(_panel.transform, "H", header, 24,
                TextAnchor.MiddleLeft, headColor);
            UiUtil.SetRect(h, new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(1080, 32));
            h.fontStyle = FontStyle.Bold;

            var body = UiUtil.MakeText(_panel.transform, "B", "", 24,
                TextAnchor.UpperLeft, new Color(0.95f, 0.95f, 0.95f));
            UiUtil.SetRect(body, new Vector2(0.5f, 1f), new Vector2(0, y - 68), new Vector2(1080, 110));
            return body;
        }

        void Refresh()
        {
            string[] r = FreeMode;
            var story = StoryManager.Instance;
            if (story != null && !story.AllCleared)
            {
                int idx = Mathf.Clamp(story.Chapter, 0, ChapterReflections.Length - 1);
                r = ChapterReflections[idx];
            }
            _factText.text = r[0];
            _feelText.text = r[1];
            _boundText.text = r[2];
            _actText.text = r[3];

            var stats = Stats();
            _ruminationText.text = stats != null
                ? $"当前反刍值：{Mathf.RoundToInt(stats.rumination)} / {Mathf.RoundToInt(stats.maxRumination)}"
                : "";
        }

        static PlayerStats Stats()
        {
            var pc = FindObjectOfType<PlayerController>();
            return pc != null ? pc.Stats : null;
        }

        void OnArchive()
        {
            var stats = Stats();
            if (stats != null)
            {
                stats.ReduceRumination(999f);
                stats.RestoreMental(25f);
            }
            GameEvents.RaiseSubtitle("已归档：事实留下，反刍清零。旧事进入档案，不再无限回放。");
            Refresh();
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
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
