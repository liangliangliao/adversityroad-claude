using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AdversityRoad.Core
{
    /// <summary>
    /// 战局与剧情流程面板：章节开场 / 章节通关 / 玩家倒下复盘再战。
    /// 失败复盘遵循"改进卡"原则：不惩罚，引导玩家思考策略后重开（章节进度不丢）。
    /// </summary>
    public class BattleFlowController : MonoBehaviour
    {
        public GameObject panel;
        public Text titleText;
        public Text detailText;
        public Text buttonText;

        enum ConfirmAction { Close, Reload }
        ConfirmAction _action = ConfirmAction.Close;
        bool _deathShown;

        void OnEnable()
        {
            GameEvents.OnPlayerDied += HandlePlayerDied;
            GameEvents.OnChapterAdvanced += HandleChapterAdvanced;
        }

        void OnDisable()
        {
            GameEvents.OnPlayerDied -= HandlePlayerDied;
            GameEvents.OnChapterAdvanced -= HandleChapterAdvanced;
            Time.timeScale = 1f;
        }

        void HandlePlayerDied(string reason)
        {
            if (_deathShown) return;
            _deathShown = true;
            Show("你倒下了",
                "失败不是终点，而是复盘的起点。\n这一战是被心理攻击拖垮，还是体力管理失误？\n调整策略，重新站起——章节进度不会丢失。",
                "复盘并再战", ConfirmAction.Reload);
        }

        void HandleChapterAdvanced(int newChapter)
        {
            var chapters = StoryManager.Chapters;
            int cleared = newChapter - 1;
            if (cleared < 0 || cleared >= chapters.Length) return;
            string title = newChapter >= chapters.Length ? "主线完结" : chapters[cleared].title + " · 通关";
            Show(title, chapters[cleared].victory, "继续", ConfirmAction.Close);
        }

        /// <summary>展示剧情/提示面板（章节开场等）。</summary>
        public void ShowStory(string title, string detail, string button) =>
            Show(title, detail, button, ConfirmAction.Close);

        void Show(string title, string detail, string button, ConfirmAction action)
        {
            _action = action;
            if (titleText != null) titleText.text = title;
            if (detailText != null) detailText.text = detail;
            if (buttonText != null) buttonText.text = button;
            if (panel != null) panel.SetActive(true);
            Time.timeScale = 0f;
        }

        public void OnConfirm()
        {
            Time.timeScale = 1f;
            if (panel != null) panel.SetActive(false);
            if (_action == ConfirmAction.Reload)
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
