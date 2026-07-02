using UnityEngine;
using AdversityRoad.Personalization;

namespace AdversityRoad.AI
{
    public enum EnemyType
    {
        SelfDoubtWhisper,       // 自我怀疑低语（内心）
        TomorrowPhantom,        // 明日幻影（内心·拖延）
        CoughAssassin,          // 咳声刺客（混合·噪声）
        ShameMirror,            // 羞耻镜像（内心·羞耻）
        ProcrastinationShadow   // 拖延影魔（Boss 原型）
    }

    public enum EnemyTier { Novice, Standard, Elite, Chief } // 见习/标准/精英/首领

    /// <summary>敌人目录：类型 × 难度 → 完整 Profile。供章节生成与玩家自由添加共用。</summary>
    public static class EnemyCatalog
    {
        static int _extraCounter;

        public static string TierLabel(EnemyTier t)
        {
            switch (t)
            {
                case EnemyTier.Novice: return "见习";
                case EnemyTier.Elite: return "精英";
                case EnemyTier.Chief: return "首领";
                default: return "标准";
            }
        }

        public static float TierStat(EnemyTier t)
        {
            switch (t)
            {
                case EnemyTier.Novice: return 0.55f;
                case EnemyTier.Elite: return 1.6f;
                case EnemyTier.Chief: return 2.5f;
                default: return 1f;
            }
        }

        public static float TierScale(EnemyTier t)
        {
            switch (t)
            {
                case EnemyTier.Novice: return 0.85f;
                case EnemyTier.Elite: return 1.15f;
                case EnemyTier.Chief: return 1.45f;
                default: return 1f;
            }
        }

        public static string TypeLabel(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.SelfDoubtWhisper: return "自我怀疑低语";
                case EnemyType.TomorrowPhantom: return "明日幻影";
                case EnemyType.CoughAssassin: return "咳声刺客";
                case EnemyType.ShameMirror: return "羞耻镜像";
                default: return "拖延影魔";
            }
        }

        public static Color TypeColor(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.SelfDoubtWhisper: return new Color(0.35f, 0.55f, 0.6f);
                case EnemyType.TomorrowPhantom: return new Color(0.5f, 0.3f, 0.7f);
                case EnemyType.CoughAssassin: return new Color(0.9f, 0.4f, 0.2f);
                case EnemyType.ShameMirror: return new Color(0.75f, 0.6f, 0.85f);
                default: return new Color(0.22f, 0.12f, 0.32f);
            }
        }

        public static string BaseId(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.SelfDoubtWhisper: return "enemy_selfdoubt_whisper";
                case EnemyType.TomorrowPhantom: return "enemy_tomorrow_phantom";
                case EnemyType.CoughAssassin: return "enemy_cough_assassin";
                case EnemyType.ShameMirror: return "enemy_shame_mirror";
                default: return "boss_procrastination_shadow";
            }
        }

        /// <summary>uniqueId=true 时生成独立 id（玩家自由添加的敌人不推进章节任务）。</summary>
        public static EnemyProfile Create(EnemyType type, EnemyTier tier, bool uniqueId = false)
        {
            EnemyProfile p;
            switch (type)
            {
                case EnemyType.SelfDoubtWhisper:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.SelfDoubt, category = EnemyCategory.Internal,
                        maxHealth = 70, posture = 25, physicalDamage = 4, mentalDamage = 14,
                        aggression = 0.5f, defense = 4, moveSpeed = 3f, attackRange = 1.8f, detectRange = 13
                    };
                    break;
                case EnemyType.TomorrowPhantom:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Procrastination, category = EnemyCategory.Internal,
                        maxHealth = 90, posture = 32, physicalDamage = 7, mentalDamage = 10,
                        aggression = 0.45f, defense = 6, moveSpeed = 2.6f, attackRange = 1.8f, detectRange = 14
                    };
                    break;
                case EnemyType.CoughAssassin:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.NoiseSensitivity, category = EnemyCategory.Hybrid,
                        maxHealth = 100, posture = 40, physicalDamage = 12, mentalDamage = 12,
                        aggression = 0.7f, defense = 8, moveSpeed = 4.5f, attackRange = 1.8f, detectRange = 14
                    };
                    break;
                case EnemyType.ShameMirror:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.Internal,
                        maxHealth = 85, posture = 30, physicalDamage = 6, mentalDamage = 15,
                        aggression = 0.55f, defense = 6, moveSpeed = 3.2f, attackRange = 1.8f, detectRange = 13
                    };
                    break;
                default:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Procrastination, category = EnemyCategory.Boss,
                        maxHealth = 130, posture = 40, physicalDamage = 14, mentalDamage = 16,
                        aggression = 0.6f, defense = 8, moveSpeed = 3.2f, attackRange = 2.2f, detectRange = 13
                    };
                    break;
            }

            float k = TierStat(tier);
            p.maxHealth *= k;
            p.posture *= k;
            p.physicalDamage *= Mathf.Lerp(1f, k, 0.8f);
            p.mentalDamage *= Mathf.Lerp(1f, k, 0.8f);
            p.defense *= Mathf.Lerp(1f, k, 0.6f);
            p.displayName = TierLabel(tier) + "·" + TypeLabel(type);
            p.enemyId = uniqueId ? BaseId(type) + "_extra_" + (++_extraCounter) : BaseId(type);
            if (tier == EnemyTier.Chief) p.category = EnemyCategory.Boss;
            return p;
        }
    }
}
