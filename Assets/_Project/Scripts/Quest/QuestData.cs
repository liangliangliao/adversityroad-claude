using System;
using System.Collections.Generic;
using AdversityRoad.Personalization;

namespace AdversityRoad.Quest
{
    public enum QuestType { Main, Adversity, Training, Reflection, Personalized }

    [Serializable]
    public class QuestObjective
    {
        public string description;
        public string targetEnemyId;   // 需击杀的敌人（可空）
        public int requiredCount = 1;
        public int currentCount = 0;
        public bool IsComplete => currentCount >= requiredCount;
    }

    [Serializable]
    public class QuestData
    {
        public string questId;
        public string title;
        public QuestType type;
        public string sceneId;
        public List<QuestObjective> objectives = new List<QuestObjective>();
        public List<string> rewardSkillIds = new List<string>();
        public WeaknessAxis relatedWeakness;
        public bool completed;
    }
}
