using System.Collections;
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
            // 延迟弹面板：先让倒地动作完整播完（之前立即 timeScale=0，
            // 倒地第一帧就被冻结——"死亡动作没有完整呈现"的根因）
            StartCoroutine(ShowDeathDelayed());
        }

        IEnumerator ShowDeathDelayed()
        {
            yield return new WaitForSecondsRealtime(2.4f);

            // 个性化失败诊断：为什么这次输了 + 针对性策略（围绕心理数值轴）
            var pc = FindObjectOfType<Player.PlayerController>();
            var diag = FailureAnalyzer.Diagnose(pc != null ? pc.Stats : null);

            // 致死心魔归因（近 8 秒内的最后来袭者，解析显示名）
            string killerId = FailureLog.LastAttackerId;
            string killerLabel = ResolveKillerLabel(killerId);
            int falls = FailureLog.RecordDeath(killerId, killerLabel);

            string killerLine = string.IsNullOrEmpty(killerLabel)
                ? "" : "\n败给了【" + killerLabel + "】" +
                       (falls >= 3 ? "——已是第 " + falls + " 次。去安全屋『图鉴』查它的破解方式。"
                                   : (falls == 2 ? "（第 2 次）。" : "。"));

            string detail =
                "失败不是终点，而是复盘的起点。\n\n" +
                "◎ 诊断：" + diag.cause + killerLine + "\n" +
                "◎ 下一次：" + diag.tip + "\n\n" +
                "章节进度不会丢失——失败是事实，不是身份。";

            // 复盘种子：把诊断带进再战后的「复盘」面板（感受/行动两栏预填）
            FailureLog.SetSeed(
                "这一战" + diag.cause,
                "下一次：" + diag.tip);

            Show("你倒下了", detail, "复盘并再战", ConfirmAction.Reload);
        }

        static string ResolveKillerLabel(string killerId)
        {
            if (string.IsNullOrEmpty(killerId)) return "";
            foreach (var e in FindObjectsOfType<AI.EnemyController>())
                if (e != null && e.profile != null && e.profile.enemyId == killerId)
                    return e.profile.displayName;
            return "";
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
