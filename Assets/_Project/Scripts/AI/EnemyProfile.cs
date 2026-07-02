using System;
using System.Collections.Generic;
using AdversityRoad.Personalization;

namespace AdversityRoad.AI
{
    public enum EnemyCategory { External, Internal, Hybrid, Boss }

    [Serializable]
    public class EnemyProfile
    {
        public string enemyId;
        public string displayName;
        public EnemyCategory category;
        public WeaknessAxis targetWeakness;   // 该敌人主攻的弱点轴
        public float maxHealth = 100;
        public float posture = 50;            // 韧性
        public float physicalDamage = 10;
        public float mentalDamage = 8;
        public float aggression = 0.5f;       // 0-1 出手频率
        public float defense = 10;
        public float moveSpeed = 3f;
        public float attackRange = 1.8f;
        public float detectRange = 12f;
        public List<string> skillIds = new List<string>();
        public List<string> dialogueTags = new List<string>();
        public string prefabAddress;
    }
}
