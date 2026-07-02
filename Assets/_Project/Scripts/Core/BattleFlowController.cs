using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AdversityRoad.Core
{
    /// <summary>
    /// 战局流程：Boss 被击败 → 胜利结算；玩家倒下 → 复盘并重新挑战。
    /// 失败复盘遵循"改进卡"原则：不惩罚，引导玩家思考策略后重开。
    /// </summary>
    public class BattleFlowController : MonoBehaviour
    {
        public string bossEnemyId;
        public GameObject panel;
        public Text titleText;
        public Text detailText;
        public Text buttonText;

        bool _ended;
        bool _victory;

        void OnEnable()
        {
            GameEvents.OnPlayerDied += HandlePlayerDied;
            GameEvents.OnEnemyKilled += HandleEnemyKilled;
        }

        void OnDisable()
        {
            GameEvents.OnPlayerDied -= HandlePlayerDied;
            GameEvents.OnEnemyKilled -= HandleEnemyKilled;
            Time.timeScale = 1f;
        }

        void HandlePlayerDied(string reason)
        {
            if (_ended) return;
            _ended = true;
            _victory = false;
            Show("你倒下了",
                "失败不是终点，而是复盘的起点。\n这一战是被心理攻击拖垮，还是体力管理失误？\n调整策略，重新站起。",
                "复盘并再战");
        }

        void HandleEnemyKilled(string enemyId)
        {
            if (_ended || enemyId != bossEnemyId) return;
            _ended = true;
            _victory = true;
            Show("拖延影魔已被击败",
                "你没有等待明天，而是现在就出手了。\n训练武馆试炼完成——下一站：独居小屋与噪声街区。",
                "继续修炼");
        }

        void Show(string title, string detail, string button)
        {
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
            if (_victory)
            {
                _ended = false; // 胜利后继续自由修炼
                return;
            }
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
