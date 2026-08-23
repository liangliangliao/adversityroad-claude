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

        /// <summary>兜底看门狗：只要玩家已经没血，就一定要把复盘面板弹出来。
        ///
        /// 「倒地后一直卡住、过不下去」的成因是死亡面板依赖 `OnPlayerDied` 这一个事件，
        /// 而该事件只在 `PlayerStats.TakePhysicalDamage` 里抛。任何别的途径把血打到 0
        /// （道具/机制/未来新增的伤害源直接改 hp），事件就不会来；而 `CombatState.Death`
        /// 是**不可退出**的终态（`RequestState` 对 Death 直接 return false），
        /// 于是角色永远躺在那里，玩家只能杀进程。
        /// 状态（死了）比事件（死亡那一刻）可靠——这里按状态兜底。</summary>
        Player.PlayerController _pc;
        float _nextDeathCheck;

        void Update()
        {
            if (_deathShown || Time.unscaledTime < _nextDeathCheck) return;
            _nextDeathCheck = Time.unscaledTime + 0.5f;   // 每秒两次足够，别每帧全场搜
            if (_pc == null) _pc = AdversityRoad.Core.ActorRegistry.Player;
            if (_pc != null && _pc.Stats != null && _pc.Stats.IsDead) HandlePlayerDied("watchdog");
        }

        void HandlePlayerDied(string reason)
        {
            if (_deathShown) return;
            _deathShown = true;
            // 延迟弹面板：先让倒地动作完整播完（之前立即 timeScale=0，
            // 倒地第一帧就被冻结——"死亡动作没有完整呈现"的根因）
            StartCoroutine(ShowDeathDelayed());
        }

        /// <summary>倒地死亡动作实际需要多久播完（秒）。
        ///
        /// 之前这里写死 2.4 秒。而动作库里的 `Dying` 片段本身就有 3~4 秒，
        /// 于是 2.4 秒一到面板弹出、`Time.timeScale = 0` 把整个世界冻住——
        /// 角色正好停在倒了一半的姿势上。玩家看到的"倒地死亡卡住、没有完整倒地动画、
        /// 复盘界面提早弹出"其实是同一件事：**面板比动作先到**。
        /// 现在直接问动画系统这段要播多久（ActionClipLength 返回的就是裁剪与倍速之后
        /// 的实际时长），播完再留半秒让画面沉一下。没有片段时（程序化倒地）沿用旧的 2.4 秒。
        /// </summary>
        static float DeathAnimSeconds()
        {
            var pc = AdversityRoad.Core.ActorRegistry.Player;
            var anim = pc != null ? pc.GetComponent<Combat.HumanoidAnimator>() : null;
            float len = anim != null ? anim.ActionClipLength(Combat.PoseState.Death) : 0f;
            return Mathf.Clamp(len > 0.1f ? len + 0.6f : 2.4f, 2.0f, 6.5f);
        }

        IEnumerator ShowDeathDelayed()
        {
            // 用非缩放时间等待：期间 timeScale 仍是 1，倒地动作照常播完
            yield return new WaitForSecondsRealtime(DeathAnimSeconds());

            // 个性化失败诊断：为什么这次输了 + 针对性策略（围绕心理数值轴）
            var pc = AdversityRoad.Core.ActorRegistry.Player;
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
            foreach (var e in AdversityRoad.Core.ActorRegistry.Enemies)
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
