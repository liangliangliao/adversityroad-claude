using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Combat;

namespace AdversityRoad.AI
{
    /// <summary>敌方武术类型（方案 10.5 十类）。</summary>
    public enum MartialArchetype
    {
        Fist,        // 拳法型：刺拳、摆拳、肘、短连击 —— 考验格挡与近距离读招
        Leg,         // 腿法型：低扫、侧踢、回旋、距离控制 —— 考验站位与闪避
        Grapple,     // 擒拿型：抓取、摔投、压制 —— 考验反抓与空间管理
        Blade,       // 刀术型：快速斩击、突刺、假动作 —— 考验精准格挡与节奏
        Staff,       // 棍术型：大范围横扫、点刺、压制 —— 考验距离与翻滚方向
        Heavy,       // 重武器型：慢起手、高破防、地面冲击 —— 考验耐心与反击窗口
        Assassin,    // 刺客型：高速突进、侧后切入 —— 考验锁定与听觉 Telegraph
        Counter,     // 防反型：架势、诱导先手、反击 —— 考验假动作识别与耐心
        Coop,        // 协同型：围堵、远近配合、交叉攻击 —— 考验目标优先级与空间
        MindMixed    // 心理混合型：物理招式与 Mental Attack Move 叠加 —— 考验稳定与实时反制
    }

    /// <summary>一类武术的动作语言与调校。</summary>
    public class ArchetypeProfile
    {
        public MartialArchetype kind;
        public string name;
        public string moveLanguage;      // 动作语言
        public string mainTest;          // 主要考验
        public PoseState[] movePool;     // 它常用的招（决定威胁形状）
        public float aggressionDelta;
        public float rangeDelta;
        public float speedDelta;
        public float mentalDelta;
    }

    /// <summary>
    /// 敌方武术 Archetype（方案 10.5）：敌人不该只是"血量不同的同一个人"。
    ///
    /// 每一类有自己的动作语言与自己考验玩家的那一项能力：
    /// 拳法逼你读近身、腿法逼你管站位、重武器逼你等窗口、防反逼你别乱出手。
    /// 玩家学会的不是"怎么打这个敌人"，而是"怎么应对这一类打法"——
    /// 这才是能迁移到下一个敌人身上的能力。
    /// </summary>
    public static class MartialArchetypeCatalog
    {
        static readonly ArchetypeProfile[] Profiles =
        {
            new ArchetypeProfile
            {
                kind = MartialArchetype.Fist, name = "拳法型",
                moveLanguage = "刺拳、摆拳、肘、短连击",
                mainTest = "格挡与近距离读招",
                movePool = new[] { PoseState.PunchCross, PoseState.Attack, PoseState.PunchCross },
                aggressionDelta = 0.15f, rangeDelta = -0.2f, speedDelta = 0.2f, mentalDelta = 0f
            },
            new ArchetypeProfile
            {
                kind = MartialArchetype.Leg, name = "腿法型",
                moveLanguage = "低扫、侧踢、回旋、距离控制",
                mainTest = "站位与闪避",
                movePool = new[] { PoseState.AttackKick, PoseState.SideKick, PoseState.SpinKick },
                aggressionDelta = 0.05f, rangeDelta = 0.4f, speedDelta = 0.15f, mentalDelta = 0f
            },
            new ArchetypeProfile
            {
                kind = MartialArchetype.Grapple, name = "擒拿型",
                moveLanguage = "抓取、摔投、压制",
                mainTest = "反抓与空间管理",
                movePool = new[] { PoseState.PunchCross, PoseState.HeavyAttack },
                aggressionDelta = 0.1f, rangeDelta = -0.3f, speedDelta = -0.1f, mentalDelta = 0f
            },
            new ArchetypeProfile
            {
                kind = MartialArchetype.Blade, name = "刀术型",
                moveLanguage = "快速斩击、突刺、假动作",
                mainTest = "精准格挡与节奏",
                movePool = new[] { PoseState.Attack, PoseState.SwordThrust, PoseState.AttackUp },
                aggressionDelta = 0.12f, rangeDelta = 0.15f, speedDelta = 0.1f, mentalDelta = 0f
            },
            new ArchetypeProfile
            {
                kind = MartialArchetype.Staff, name = "棍术型",
                moveLanguage = "大范围横扫、点刺、压制",
                mainTest = "距离与翻滚方向",
                movePool = new[] { PoseState.Attack, PoseState.AttackSpin, PoseState.SwordThrust },
                aggressionDelta = -0.05f, rangeDelta = 0.5f, speedDelta = -0.05f, mentalDelta = 0f
            },
            new ArchetypeProfile
            {
                kind = MartialArchetype.Heavy, name = "重武器型",
                moveLanguage = "慢起手、高破防、地面冲击",
                mainTest = "耐心与反击窗口",
                movePool = new[] { PoseState.HeavyAttack, PoseState.Attack },
                aggressionDelta = -0.2f, rangeDelta = 0.3f, speedDelta = -0.35f, mentalDelta = 0f
            },
            new ArchetypeProfile
            {
                kind = MartialArchetype.Assassin, name = "刺客型",
                moveLanguage = "高速突进、侧后切入",
                mainTest = "锁定与听觉 Telegraph",
                movePool = new[] { PoseState.SwordThrust, PoseState.JumpKick, PoseState.PunchCross },
                aggressionDelta = 0.2f, rangeDelta = -0.1f, speedDelta = 0.5f, mentalDelta = 0f
            },
            new ArchetypeProfile
            {
                kind = MartialArchetype.Counter, name = "防反型",
                moveLanguage = "架势、诱导先手、反击",
                mainTest = "假动作识别与耐心",
                movePool = new[] { PoseState.Attack, PoseState.SideKick },
                aggressionDelta = -0.3f, rangeDelta = 0.1f, speedDelta = -0.1f, mentalDelta = 0f
            },
            new ArchetypeProfile
            {
                kind = MartialArchetype.Coop, name = "协同型",
                moveLanguage = "围堵、远近配合、交叉攻击",
                mainTest = "目标优先级与空间",
                movePool = new[] { PoseState.Attack, PoseState.AttackKick },
                aggressionDelta = 0.08f, rangeDelta = 0.2f, speedDelta = 0.15f, mentalDelta = 0f
            },
            new ArchetypeProfile
            {
                kind = MartialArchetype.MindMixed, name = "心理混合型",
                moveLanguage = "物理招式与 Mental Attack Move 叠加",
                mainTest = "稳定与实时反制",
                movePool = new[] { PoseState.Attack, PoseState.PunchCross },
                aggressionDelta = -0.05f, rangeDelta = 0.1f, speedDelta = 0f, mentalDelta = 0.2f
            },
        };

        public static ArchetypeProfile Get(MartialArchetype k)
        {
            foreach (var p in Profiles) if (p.kind == k) return p;
            return Profiles[0];
        }

        public static IEnumerable<ArchetypeProfile> All => Profiles;

        /// <summary>
        /// 敌人类型 → 武术类型。内心敌人偏心理混合，外部敌人按它的"打法气质"归类，
        /// Boss 归到最能表达它机制的那一类。
        /// </summary>
        public static MartialArchetype For(EnemyType t)
        {
            switch (t)
            {
                // 刺客型：高速突进、切入
                case EnemyType.CoughAssassin:
                case EnemyType.HungerHound:
                case EnemyType.RuminationSwarm:
                    return MartialArchetype.Assassin;

                // 重武器型：慢起手、高破防
                case EnemyType.ValleyColossus:
                case EnemyType.TomorrowKing:
                case EnemyType.SelfDenialGavel:
                case EnemyType.TotalResponsibilityJudge:
                    return MartialArchetype.Heavy;

                // 刀术型：快斩、突刺、假动作
                case EnemyType.ColdWindBlade:
                case EnemyType.RuleTwister:
                case EnemyType.DebtDodger:
                    return MartialArchetype.Blade;

                // 棍术型：大范围横扫
                case EnemyType.InfinitePayer:
                case EnemyType.GoodPersonCage:
                case EnemyType.QuestionBeast:
                    return MartialArchetype.Staff;

                // 腿法型：距离控制
                case EnemyType.ProvokerPasserby:
                case EnemyType.TauntMirror:
                case EnemyType.ProcrastinationShadow:
                    return MartialArchetype.Leg;

                // 擒拿型：抓取压制
                case EnemyType.RequestExpander:
                case EnemyType.TomorrowMud:
                case EnemyType.DebtShadow:
                    return MartialArchetype.Grapple;

                // 防反型：诱导先手
                case EnemyType.ConceptMazeMaster:
                case EnemyType.DoubtScholar:
                case EnemyType.PerfectPreparer:
                    return MartialArchetype.Counter;

                // 协同型：围堵与远近配合
                case EnemyType.MockingBystander:
                case EnemyType.OverreactGhost:
                case EnemyType.MaskFace:
                    return MartialArchetype.Coop;

                // 心理混合型：台词与招式绑定
                case EnemyType.SelfDoubtWhisper:
                case EnemyType.TomorrowPhantom:
                case EnemyType.OldVoiceRepeater:
                case EnemyType.OldSelf:
                case EnemyType.GazeEye:
                case EnemyType.ThousandEyeJudge:
                case EnemyType.GuiltThrower:
                case EnemyType.QuoteGhost:
                case EnemyType.InfiniteAsker:
                case EnemyType.MedDebtShadow:
                case EnemyType.StimulusAmplifier:
                case EnemyType.GoalForgetter:
                    return MartialArchetype.MindMixed;

                default:
                    return MartialArchetype.Fist;
            }
        }

        /// <summary>
        /// 生成时套用武术类型：只调**公开可读**的参数（出手频率/交战距离/移速/心理压强），
        /// 而且只调一次——绝不在攻击过程中偷偷改判定（方案 7.4 禁止项）。
        /// </summary>
        public static ArchetypeProfile Apply(EnemyProfile profile, EnemyType type)
        {
            var arc = Get(For(type));
            if (profile == null) return arc;

            profile.aggression = Mathf.Clamp01(profile.aggression + arc.aggressionDelta);
            profile.attackRange = Mathf.Clamp(profile.attackRange + arc.rangeDelta, 1.2f, 3.2f);
            profile.moveSpeed = Mathf.Clamp(profile.moveSpeed + arc.speedDelta, 1.2f, 6f);
            if (arc.mentalDelta > 0f)
                profile.mentalDamage *= 1f + arc.mentalDelta;
            return arc;
        }

        /// <summary>玩家侧可读文案：这一类在考验你什么（图鉴/交战提示用）。</summary>
        public static string Describe(MartialArchetype k)
        {
            var p = Get(k);
            return "【" + p.name + "】" + p.moveLanguage + " —— 考验：" + p.mainTest;
        }
    }
}
