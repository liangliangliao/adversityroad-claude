using UnityEngine;
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
        /// <summary>
        /// 移动速度（m/s）。**读的时候一律走 MoveSpeed，不要直接读这个字段**。
        /// </summary>
        public float moveSpeed = 3f;

        /// <summary>敌人移速的硬上限（m/s）。两条理由，缺一不可：</summary>
        /// <remarks>
        /// ① 敌人和玩家共用同一批 Mixamo 跑动片段，Running 的实测自然速度是
        ///    3.63 m/s。超过 1.15 倍（≈4.2）就是肉眼可见的快放，"滑步"不只
        ///    发生在玩家身上。
        /// ② 玩家的 runSpeed 已按同一条依据定到 4.2。目录里原有 4.2/4.5/4.8/5.0
        ///    四档，若照原值跑，这四种敌人会**追得上甚至跑得过玩家**——
        ///    "打不过还跑不掉"是把速度锚到动画上时最容易顺手引入的回归。
        /// 上限取 3.9：留出 0.3 m/s 的余量，玩家全速时总能脱离，
        /// 而目录里各档之间的相对快慢原样保留（只压顶部那几档）。
        /// </remarks>
        public const float MoveSpeedCap = 3.9f;

        /// <summary>实际生效的移动速度（目录值封顶后的结果）。</summary>
        public float MoveSpeed => Mathf.Min(moveSpeed, MoveSpeedCap);
        public float attackRange = 1.8f;
        public float detectRange = 12f;
        public bool rangedAttack = false;     // 中距离发射心念弹（远程攻击）
        public List<string> skillIds = new List<string>();
        public List<string> dialogueTags = new List<string>();
        public string prefabAddress;
    }
}
