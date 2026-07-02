using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.Quest
{
    /// <summary>
    /// 任务管理器：监听击杀事件推进目标；失败任务转为"复盘任务"（改进卡机制：失败不惩罚，给经验）。
    /// </summary>
    public class QuestManager : MonoBehaviour
    {
        public static QuestManager Instance { get; private set; }
        public List<QuestData> activeQuests = new List<QuestData>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnEnable()
        {
            GameEvents.OnEnemyKilled += HandleEnemyKilled;
            GameEvents.OnPlayerDied += HandlePlayerDied;
        }

        void OnDisable()
        {
            GameEvents.OnEnemyKilled -= HandleEnemyKilled;
            GameEvents.OnPlayerDied -= HandlePlayerDied;
        }

        public void AddQuest(QuestData quest)
        {
            activeQuests.Add(quest);
            GameEvents.RaiseQuestUpdated(quest.questId);
        }

        void HandleEnemyKilled(string enemyId)
        {
            foreach (var q in activeQuests)
            {
                if (q.completed) continue;
                bool changed = false;
                foreach (var o in q.objectives)
                    if (o.targetEnemyId == enemyId && !o.IsComplete) { o.currentCount++; changed = true; }
                if (!changed) continue;

                bool allDone = true;
                foreach (var o in q.objectives) if (!o.IsComplete) { allDone = false; break; }
                if (allDone)
                {
                    q.completed = true;
                    foreach (var s in q.rewardSkillIds) GameEvents.RaiseSkillUnlocked(s);
                }
                GameEvents.RaiseQuestUpdated(q.questId);
            }
        }

        /// <summary>失败复盘：死亡不清空进度，而是自动生成一条复盘任务（改进卡）。</summary>
        void HandlePlayerDied(string reason)
        {
            var review = new QuestData
            {
                questId = "review_" + System.DateTime.Now.Ticks,
                title = "复盘这次失败：找到一条可以改进的策略",
                type = QuestType.Reflection,
                objectives = new List<QuestObjective>
                {
                    new QuestObjective { description = "回到安全房间，在目标板上选择下一次策略" }
                }
            };
            AddQuest(review);
        }
    }
}
