using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// HUD：血条 + 五维心理条 + 任务提示。
    /// 订阅 GameEvents，与游戏逻辑完全解耦。
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        public StatBar hpBar;
        public StatBar willBar;
        public StatBar focusBar;
        public StatBar selfWorthBar;
        public StatBar boundaryBar;
        public StatBar resolveBar;
        public UnityEngine.UI.Text questText;

        void OnEnable()
        {
            GameEvents.OnPlayerHpChanged += OnHp;
            GameEvents.OnMentalStatChanged += OnMental;
            GameEvents.OnQuestUpdated += OnQuest;
        }

        void OnDisable()
        {
            GameEvents.OnPlayerHpChanged -= OnHp;
            GameEvents.OnMentalStatChanged -= OnMental;
            GameEvents.OnQuestUpdated -= OnQuest;
        }

        void OnHp(float cur, float max) { if (hpBar != null) hpBar.SetValue(cur, max); }

        void OnMental(string stat, float cur, float max)
        {
            switch (stat)
            {
                case "will": if (willBar != null) willBar.SetValue(cur, max); break;
                case "focus": if (focusBar != null) focusBar.SetValue(cur, max); break;
                case "selfWorth": if (selfWorthBar != null) selfWorthBar.SetValue(cur, max); break;
                case "boundary": if (boundaryBar != null) boundaryBar.SetValue(cur, max); break;
                case "resolve": if (resolveBar != null) resolveBar.SetValue(cur, max); break;
            }
        }

        void OnQuest(string questId)
        {
            if (questText == null || Quest.QuestManager.Instance == null) return;
            foreach (var q in Quest.QuestManager.Instance.activeQuests)
                if (!q.completed) { questText.text = q.title; return; }
            questText.text = "";
        }
    }
}
