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
        ProcrastinationShadow,  // 拖延影魔（Boss 原型）
        NoReplyKing,            // 无回应之王（求职荒原 Boss：拒信飞刃）
        TotalResponsibilityJudge, // 全责法官（责任转嫁法院 Boss：抛掷责任球）

        // ---- 小题大做审判庭（公平与承诺线） ----
        OverreactGhost,         // 小题大做鬼（外部·公平刺痛近战）
        MockingBystander,       // 旁观嘲笑者（内心·羞耻远程）
        SelfDenialGavel,        // 自我否定法槌（审判庭 Boss：标签弹幕/审判冲击波/否定重锤）

        // ---- 一声咳嗽的街道（外界刺激线） ----
        StimulusAmplifier,      // 刺激放大器（街道 Boss：噪声放大/幻影假目标）

        // ---- 拖延沼泽（拖延与目标线） ----
        TomorrowMud,            // 明日泥怪（外部·拖延近战，迟缓但耐打）
        PerfectPreparer,        // 完美准备者（内心·低信心远程："再准备一下"）
        TomorrowKing,           // 明天之王（沼泽 Boss：泥壳护体，点燃三座火种台才能破防）

        // ---- 旧事回声馆（旧我与新我线） ----
        OldVoiceRepeater,       // 旧话复读者（内心·旧事回声远程）
        PastJudge,              // 过去判官（外部·旧事近战审判）
        RuminationSwarm,        // 反刍虫群（内心·小型快速缠身）
        OldSelf,                // 旧我（终局 Boss：旧话复读/身份冻结/失败召回/整合选择）

        // ---- 两元赌桌 / 债务车影（公平与承诺线） ----
        DebtDodger,             // 赖账牌手（外部·公平刺痛近战）
        RuleTwister,            // 规则篡改者（混合·公平远程"改规则"）
        DebtShadow,             // 欠款残影（内心·未结清之事的具象）
        GambleKing,             // 两元赖账王（赌桌 Boss：硬币弹幕/耍赖回血/账本对质破防）
        DebtCarKing,            // 新车债王（停车场 Boss：车灯眩光/召唤残影/欠条护体）

        // ---- 眼神审判走廊 / 陌生挑衅路口（外界刺激线） ----
        GazeEye,                // 凝视眼球（内心·被注视感远程）
        MaskFace,               // 表情面具（外部·假笑之下的评价近战）
        ThousandEyeJudge,       // 万眼审判者（镜厅 Boss：凝视光束/虚假凝视点）
        ProvokerPasserby,       // 挑衅路人（外部·故意找茬近战）
        TauntMirror,            // 挑衅镜像（路口 Boss：挑衅窗口——追打变强、不理破绽）

        // ---- 目标遗忘房（拖延与目标线） ----
        GoalForgetter,          // 目标遗忘者（房间 Boss：守着落灰的目标板，让你忘记为什么出发）

        // ---- 老实人消耗局 / 无限代付走廊（边界与责任线） ----
        RequestExpander,        // 请求膨胀者（外部·"就这一次"近战）
        GuiltThrower,           // 内疚投手（内心·边界远程掷内疚）
        GoodPersonCage,         // 好人牢笼（消耗局 Boss：好人卡附着/困人牢笼）
        InfinitePayer,          // 无限代付者（走廊 Boss：索取冲击/消耗账单/代付之门）

        // ---- 低谷与生存线 ----
        HungerHound,            // 饥饿犬影（外部·快速扑咬，低血量高压迫）
        ColdWindBlade,          // 寒风刃（混合·车库寒夜的风刃远程）
        MedDebtShadow,          // 医药债影（内心·病房回廊的账单低语）
        ValleyColossus,         // 低谷巨像（低谷线 Boss：无力威压/内疚重石/求助破防）

        // ---- 哲学与行动线 ----
        QuoteGhost,             // 引文幽灵（内心·"某某说过"的远程引文弹）
        DoubtScholar,           // 怀疑学者（混合·"你确定吗？"远程质询）
        ConceptMazeMaster,      // 概念迷宫师（图书馆 Boss：引文弹幕/概念迷环/灯台破防）
        QuestionBeast,          // 无限问题兽（大厅 Boss：问题弹幕狂涨反刍/召唤引文幽灵）
        InfiniteAsker           // 无限追问者（断桥 Boss：追问弹幕/崩桥/行动答台破防）
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
                case EnemyTier.Elite: return 1.5f;
                case EnemyTier.Chief: return 2.1f;
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
                case EnemyType.NoReplyKing: return "无回应之王";
                case EnemyType.TotalResponsibilityJudge: return "全责法官";
                case EnemyType.OverreactGhost: return "小题大做鬼";
                case EnemyType.MockingBystander: return "旁观嘲笑者";
                case EnemyType.SelfDenialGavel: return "自我否定法槌";
                case EnemyType.StimulusAmplifier: return "刺激放大器";
                case EnemyType.TomorrowMud: return "明日泥怪";
                case EnemyType.PerfectPreparer: return "完美准备者";
                case EnemyType.TomorrowKing: return "明天之王";
                case EnemyType.OldVoiceRepeater: return "旧话复读者";
                case EnemyType.PastJudge: return "过去判官";
                case EnemyType.RuminationSwarm: return "反刍虫群";
                case EnemyType.OldSelf: return "旧我";
                case EnemyType.DebtDodger: return "赖账牌手";
                case EnemyType.RuleTwister: return "规则篡改者";
                case EnemyType.DebtShadow: return "欠款残影";
                case EnemyType.GambleKing: return "两元赖账王";
                case EnemyType.DebtCarKing: return "新车债王";
                case EnemyType.GazeEye: return "凝视眼球";
                case EnemyType.MaskFace: return "表情面具";
                case EnemyType.ThousandEyeJudge: return "万眼审判者";
                case EnemyType.ProvokerPasserby: return "挑衅路人";
                case EnemyType.TauntMirror: return "挑衅镜像";
                case EnemyType.GoalForgetter: return "目标遗忘者";
                case EnemyType.RequestExpander: return "请求膨胀者";
                case EnemyType.GuiltThrower: return "内疚投手";
                case EnemyType.GoodPersonCage: return "好人牢笼";
                case EnemyType.InfinitePayer: return "无限代付者";
                case EnemyType.HungerHound: return "饥饿犬影";
                case EnemyType.ColdWindBlade: return "寒风刃";
                case EnemyType.MedDebtShadow: return "医药债影";
                case EnemyType.ValleyColossus: return "低谷巨像";
                case EnemyType.QuoteGhost: return "引文幽灵";
                case EnemyType.DoubtScholar: return "怀疑学者";
                case EnemyType.ConceptMazeMaster: return "概念迷宫师";
                case EnemyType.QuestionBeast: return "无限问题兽";
                case EnemyType.InfiniteAsker: return "无限追问者";
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
                case EnemyType.NoReplyKing: return new Color(0.62f, 0.66f, 0.75f);
                case EnemyType.TotalResponsibilityJudge: return new Color(0.55f, 0.16f, 0.2f);
                case EnemyType.OverreactGhost: return new Color(0.85f, 0.55f, 0.25f);
                case EnemyType.MockingBystander: return new Color(0.65f, 0.5f, 0.75f);
                case EnemyType.SelfDenialGavel: return new Color(0.6f, 0.32f, 0.2f);
                case EnemyType.StimulusAmplifier: return new Color(0.9f, 0.55f, 0.15f);
                case EnemyType.TomorrowMud: return new Color(0.35f, 0.28f, 0.16f);
                case EnemyType.PerfectPreparer: return new Color(0.55f, 0.62f, 0.55f);
                case EnemyType.TomorrowKing: return new Color(0.3f, 0.2f, 0.45f);
                case EnemyType.OldVoiceRepeater: return new Color(0.45f, 0.42f, 0.55f);
                case EnemyType.PastJudge: return new Color(0.38f, 0.3f, 0.3f);
                case EnemyType.RuminationSwarm: return new Color(0.5f, 0.22f, 0.45f);
                case EnemyType.OldSelf: return new Color(0.18f, 0.18f, 0.25f);
                case EnemyType.DebtDodger: return new Color(0.6f, 0.45f, 0.2f);
                case EnemyType.RuleTwister: return new Color(0.55f, 0.5f, 0.28f);
                case EnemyType.DebtShadow: return new Color(0.35f, 0.32f, 0.4f);
                case EnemyType.GambleKing: return new Color(0.72f, 0.5f, 0.15f);
                case EnemyType.DebtCarKing: return new Color(0.3f, 0.35f, 0.5f);
                case EnemyType.GazeEye: return new Color(0.7f, 0.65f, 0.85f);
                case EnemyType.MaskFace: return new Color(0.8f, 0.75f, 0.65f);
                case EnemyType.ThousandEyeJudge: return new Color(0.6f, 0.55f, 0.9f);
                case EnemyType.ProvokerPasserby: return new Color(0.85f, 0.35f, 0.3f);
                case EnemyType.TauntMirror: return new Color(0.65f, 0.7f, 0.75f);
                case EnemyType.GoalForgetter: return new Color(0.45f, 0.4f, 0.6f);
                case EnemyType.RequestExpander: return new Color(0.75f, 0.6f, 0.35f);
                case EnemyType.GuiltThrower: return new Color(0.6f, 0.42f, 0.5f);
                case EnemyType.GoodPersonCage: return new Color(0.85f, 0.72f, 0.4f);
                case EnemyType.InfinitePayer: return new Color(0.4f, 0.5f, 0.45f);
                case EnemyType.HungerHound: return new Color(0.42f, 0.3f, 0.22f);
                case EnemyType.ColdWindBlade: return new Color(0.6f, 0.75f, 0.9f);
                case EnemyType.MedDebtShadow: return new Color(0.75f, 0.8f, 0.85f);
                case EnemyType.ValleyColossus: return new Color(0.25f, 0.28f, 0.35f);
                case EnemyType.QuoteGhost: return new Color(0.55f, 0.6f, 0.75f);
                case EnemyType.DoubtScholar: return new Color(0.45f, 0.5f, 0.62f);
                case EnemyType.ConceptMazeMaster: return new Color(0.4f, 0.35f, 0.6f);
                case EnemyType.QuestionBeast: return new Color(0.6f, 0.35f, 0.55f);
                case EnemyType.InfiniteAsker: return new Color(0.35f, 0.4f, 0.65f);
                default: return new Color(0.22f, 0.12f, 0.32f);
            }
        }

        /// <summary>各类型敌人的兵器：不同敌方持不同兵器。</summary>
        public static Combat.WeaponKind WeaponOf(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.SelfDoubtWhisper: return Combat.WeaponKind.None;   // 纯心念远程
                case EnemyType.TomorrowPhantom: return Combat.WeaponKind.Staff;   // 长棍
                case EnemyType.CoughAssassin: return Combat.WeaponKind.Claw;      // 利爪
                case EnemyType.ShameMirror: return Combat.WeaponKind.Sword;       // 镜像之剑
                case EnemyType.NoReplyKing: return Combat.WeaponKind.Sword;       // 拒信之剑
                case EnemyType.TotalResponsibilityJudge: return Combat.WeaponKind.Staff; // 法槌·长杖
                case EnemyType.OverreactGhost: return Combat.WeaponKind.Claw;      // 挑刺之爪
                case EnemyType.MockingBystander: return Combat.WeaponKind.None;    // 纯嘲笑远程
                case EnemyType.SelfDenialGavel: return Combat.WeaponKind.Staff;    // 否定法槌
                case EnemyType.StimulusAmplifier: return Combat.WeaponKind.None;   // 纯噪声远程
                case EnemyType.TomorrowMud: return Combat.WeaponKind.None;         // 泥拳
                case EnemyType.PerfectPreparer: return Combat.WeaponKind.None;     // 计划纸念弹
                case EnemyType.TomorrowKing: return Combat.WeaponKind.Blade;       // 明日大刀
                case EnemyType.OldVoiceRepeater: return Combat.WeaponKind.None;    // 旧话回声
                case EnemyType.PastJudge: return Combat.WeaponKind.Staff;          // 过往裁尺
                case EnemyType.RuminationSwarm: return Combat.WeaponKind.Claw;     // 虫群噬咬
                case EnemyType.OldSelf: return Combat.WeaponKind.Sword;            // 与你同款的旧剑
                case EnemyType.DebtDodger: return Combat.WeaponKind.None;          // 甩牌
                case EnemyType.RuleTwister: return Combat.WeaponKind.None;         // 篡改的规则纸
                case EnemyType.DebtShadow: return Combat.WeaponKind.Claw;          // 残影之爪
                case EnemyType.GambleKing: return Combat.WeaponKind.None;          // 硬币与牌
                case EnemyType.DebtCarKing: return Combat.WeaponKind.Blade;        // 债契大刀
                case EnemyType.GazeEye: return Combat.WeaponKind.None;             // 纯凝视
                case EnemyType.MaskFace: return Combat.WeaponKind.Claw;            // 面具下的利爪
                case EnemyType.ThousandEyeJudge: return Combat.WeaponKind.None;    // 千目凝视
                case EnemyType.ProvokerPasserby: return Combat.WeaponKind.None;    // 寻衅拳脚
                case EnemyType.TauntMirror: return Combat.WeaponKind.Sword;        // 镜像之剑
                case EnemyType.GoalForgetter: return Combat.WeaponKind.Staff;      // 遗忘之杖
                case EnemyType.RequestExpander: return Combat.WeaponKind.None;     // 递不完的请求单
                case EnemyType.GuiltThrower: return Combat.WeaponKind.None;        // 掷内疚
                case EnemyType.GoodPersonCage: return Combat.WeaponKind.None;      // 好人卡
                case EnemyType.InfinitePayer: return Combat.WeaponKind.Staff;      // 账单之杖
                case EnemyType.HungerHound: return Combat.WeaponKind.Claw;         // 饿犬之牙
                case EnemyType.ColdWindBlade: return Combat.WeaponKind.None;       // 风刃
                case EnemyType.MedDebtShadow: return Combat.WeaponKind.None;       // 账单低语
                case EnemyType.ValleyColossus: return Combat.WeaponKind.Staff;     // 重石之柱
                case EnemyType.QuoteGhost: return Combat.WeaponKind.None;          // 引文弹
                case EnemyType.DoubtScholar: return Combat.WeaponKind.Staff;       // 质询之杖
                case EnemyType.ConceptMazeMaster: return Combat.WeaponKind.Staff;  // 概念之杖
                case EnemyType.QuestionBeast: return Combat.WeaponKind.Claw;       // 问题之爪
                case EnemyType.InfiniteAsker: return Combat.WeaponKind.None;       // 纯追问
                default: return Combat.WeaponKind.Blade;                          // 影魔大刀
            }
        }

        /// <summary>该类型是否具备远程攻击（心念弹/拒信飞刃）。</summary>
        public static bool RangedOf(EnemyType t) =>
            t == EnemyType.SelfDoubtWhisper || t == EnemyType.ShameMirror ||
            t == EnemyType.ProcrastinationShadow || t == EnemyType.NoReplyKing ||
            t == EnemyType.MockingBystander || t == EnemyType.StimulusAmplifier ||
            t == EnemyType.PerfectPreparer || t == EnemyType.OldVoiceRepeater ||
            t == EnemyType.SelfDenialGavel || t == EnemyType.OldSelf ||
            t == EnemyType.RuleTwister || t == EnemyType.DebtShadow ||
            t == EnemyType.GazeEye || t == EnemyType.ThousandEyeJudge ||
            t == EnemyType.GambleKing || t == EnemyType.GuiltThrower ||
            t == EnemyType.GoalForgetter || t == EnemyType.InfinitePayer ||
            t == EnemyType.ColdWindBlade || t == EnemyType.MedDebtShadow ||
            t == EnemyType.QuoteGhost || t == EnemyType.DoubtScholar ||
            t == EnemyType.ConceptMazeMaster || t == EnemyType.QuestionBeast ||
            t == EnemyType.InfiniteAsker;

        public static string BaseId(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.SelfDoubtWhisper: return "enemy_selfdoubt_whisper";
                case EnemyType.TomorrowPhantom: return "enemy_tomorrow_phantom";
                case EnemyType.CoughAssassin: return "enemy_cough_assassin";
                case EnemyType.ShameMirror: return "enemy_shame_mirror";
                case EnemyType.NoReplyKing: return "boss_no_reply_king";
                case EnemyType.TotalResponsibilityJudge: return "boss_total_responsibility_judge";
                case EnemyType.OverreactGhost: return "enemy_overreact_ghost";
                case EnemyType.MockingBystander: return "enemy_mocking_bystander";
                case EnemyType.SelfDenialGavel: return "boss_self_denial_gavel";
                case EnemyType.StimulusAmplifier: return "boss_stimulus_amplifier";
                case EnemyType.TomorrowMud: return "enemy_tomorrow_mud";
                case EnemyType.PerfectPreparer: return "enemy_perfect_preparer";
                case EnemyType.TomorrowKing: return "boss_tomorrow_king";
                case EnemyType.OldVoiceRepeater: return "enemy_old_voice_repeater";
                case EnemyType.PastJudge: return "enemy_past_judge";
                case EnemyType.RuminationSwarm: return "enemy_rumination_swarm";
                case EnemyType.OldSelf: return "boss_old_self";
                case EnemyType.DebtDodger: return "enemy_debt_dodger";
                case EnemyType.RuleTwister: return "enemy_rule_twister";
                case EnemyType.DebtShadow: return "enemy_debt_shadow";
                case EnemyType.GambleKing: return "boss_gamble_king";
                case EnemyType.DebtCarKing: return "boss_debt_car_king";
                case EnemyType.GazeEye: return "enemy_gaze_eye";
                case EnemyType.MaskFace: return "enemy_mask_face";
                case EnemyType.ThousandEyeJudge: return "boss_thousand_eye_judge";
                case EnemyType.ProvokerPasserby: return "enemy_provoker_passerby";
                case EnemyType.TauntMirror: return "boss_taunt_mirror";
                case EnemyType.GoalForgetter: return "boss_goal_forgetter";
                case EnemyType.RequestExpander: return "enemy_request_expander";
                case EnemyType.GuiltThrower: return "enemy_guilt_thrower";
                case EnemyType.GoodPersonCage: return "boss_good_person_cage";
                case EnemyType.InfinitePayer: return "boss_infinite_payer";
                case EnemyType.HungerHound: return "enemy_hunger_hound";
                case EnemyType.ColdWindBlade: return "enemy_cold_wind_blade";
                case EnemyType.MedDebtShadow: return "enemy_med_debt_shadow";
                case EnemyType.ValleyColossus: return "boss_valley_colossus";
                case EnemyType.QuoteGhost: return "enemy_quote_ghost";
                case EnemyType.DoubtScholar: return "enemy_doubt_scholar";
                case EnemyType.ConceptMazeMaster: return "boss_concept_maze_master";
                case EnemyType.QuestionBeast: return "boss_question_beast";
                case EnemyType.InfiniteAsker: return "boss_infinite_asker";
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
                case EnemyType.NoReplyKing:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.JobAnxiety, category = EnemyCategory.Hybrid,
                        maxHealth = 120, posture = 45, physicalDamage = 13, mentalDamage = 17,
                        aggression = 0.55f, defense = 10, moveSpeed = 3f, attackRange = 2f, detectRange = 15
                    };
                    break;
                case EnemyType.TotalResponsibilityJudge:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.BoundaryConflict, category = EnemyCategory.Boss,
                        maxHealth = 125, posture = 46, physicalDamage = 12, mentalDamage = 16,
                        aggression = 0.5f, defense = 10, moveSpeed = 3f, attackRange = 2.1f, detectRange = 15
                    };
                    break;
                case EnemyType.OverreactGhost:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FairnessSensitivity, category = EnemyCategory.External,
                        maxHealth = 95, posture = 34, physicalDamage = 9, mentalDamage = 11,
                        aggression = 0.6f, defense = 7, moveSpeed = 3.6f, attackRange = 1.8f, detectRange = 13
                    };
                    break;
                case EnemyType.MockingBystander:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.Internal,
                        maxHealth = 75, posture = 26, physicalDamage = 5, mentalDamage = 14,
                        aggression = 0.5f, defense = 4, moveSpeed = 2.8f, attackRange = 1.8f, detectRange = 14
                    };
                    break;
                case EnemyType.SelfDenialGavel:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.SelfDoubt, category = EnemyCategory.Boss,
                        maxHealth = 130, posture = 48, physicalDamage = 13, mentalDamage = 16,
                        aggression = 0.55f, defense = 10, moveSpeed = 2.9f, attackRange = 2.2f, detectRange = 16
                    };
                    break;
                case EnemyType.StimulusAmplifier:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.NoiseSensitivity, category = EnemyCategory.Boss,
                        maxHealth = 115, posture = 42, physicalDamage = 10, mentalDamage = 18,
                        aggression = 0.5f, defense = 8, moveSpeed = 3.4f, attackRange = 2f, detectRange = 17
                    };
                    break;
                case EnemyType.TomorrowMud:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Procrastination, category = EnemyCategory.External,
                        maxHealth = 120, posture = 45, physicalDamage = 10, mentalDamage = 9,
                        aggression = 0.4f, defense = 12, moveSpeed = 2.2f, attackRange = 1.9f, detectRange = 12
                    };
                    break;
                case EnemyType.PerfectPreparer:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.LowConfidence, category = EnemyCategory.Internal,
                        maxHealth = 80, posture = 28, physicalDamage = 6, mentalDamage = 13,
                        aggression = 0.45f, defense = 5, moveSpeed = 2.9f, attackRange = 1.8f, detectRange = 14
                    };
                    break;
                case EnemyType.TomorrowKing:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Procrastination, category = EnemyCategory.Boss,
                        maxHealth = 140, posture = 50, physicalDamage = 13, mentalDamage = 15,
                        aggression = 0.5f, defense = 10, moveSpeed = 2.8f, attackRange = 2.3f, detectRange = 16
                    };
                    break;
                case EnemyType.OldVoiceRepeater:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FailureFear, category = EnemyCategory.Internal,
                        maxHealth = 85, posture = 30, physicalDamage = 6, mentalDamage = 15,
                        aggression = 0.5f, defense = 5, moveSpeed = 3f, attackRange = 1.8f, detectRange = 14
                    };
                    break;
                case EnemyType.PastJudge:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FailureFear, category = EnemyCategory.External,
                        maxHealth = 105, posture = 38, physicalDamage = 11, mentalDamage = 11,
                        aggression = 0.55f, defense = 9, moveSpeed = 3.3f, attackRange = 1.9f, detectRange = 13
                    };
                    break;
                case EnemyType.RuminationSwarm:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FailureFear, category = EnemyCategory.Internal,
                        maxHealth = 60, posture = 20, physicalDamage = 6, mentalDamage = 10,
                        aggression = 0.75f, defense = 3, moveSpeed = 4.8f, attackRange = 1.6f, detectRange = 15
                    };
                    break;
                case EnemyType.OldSelf:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FailureFear, category = EnemyCategory.Boss,
                        maxHealth = 160, posture = 55, physicalDamage = 14, mentalDamage = 17,
                        aggression = 0.6f, defense = 11, moveSpeed = 3.4f, attackRange = 2.2f, detectRange = 18
                    };
                    break;
                case EnemyType.DebtDodger:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FairnessSensitivity, category = EnemyCategory.External,
                        maxHealth = 90, posture = 32, physicalDamage = 9, mentalDamage = 11,
                        aggression = 0.55f, defense = 6, moveSpeed = 3.4f, attackRange = 1.8f, detectRange = 13
                    };
                    break;
                case EnemyType.RuleTwister:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FairnessSensitivity, category = EnemyCategory.Hybrid,
                        maxHealth = 85, posture = 28, physicalDamage = 7, mentalDamage = 13,
                        aggression = 0.5f, defense = 5, moveSpeed = 3f, attackRange = 1.8f, detectRange = 14
                    };
                    break;
                case EnemyType.DebtShadow:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FairnessSensitivity, category = EnemyCategory.Internal,
                        maxHealth = 80, posture = 26, physicalDamage = 6, mentalDamage = 13,
                        aggression = 0.5f, defense = 4, moveSpeed = 3.1f, attackRange = 1.8f, detectRange = 13
                    };
                    break;
                case EnemyType.GambleKing:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FairnessSensitivity, category = EnemyCategory.Boss,
                        maxHealth = 120, posture = 44, physicalDamage = 11, mentalDamage = 15,
                        aggression = 0.55f, defense = 9, moveSpeed = 3f, attackRange = 2f, detectRange = 15
                    };
                    break;
                case EnemyType.DebtCarKing:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FairnessSensitivity, category = EnemyCategory.Boss,
                        maxHealth = 135, posture = 48, physicalDamage = 13, mentalDamage = 15,
                        aggression = 0.5f, defense = 10, moveSpeed = 3.1f, attackRange = 2.2f, detectRange = 16
                    };
                    break;
                case EnemyType.GazeEye:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.Internal,
                        maxHealth = 75, posture = 24, physicalDamage = 5, mentalDamage = 14,
                        aggression = 0.5f, defense = 4, moveSpeed = 2.9f, attackRange = 1.8f, detectRange = 15
                    };
                    break;
                case EnemyType.MaskFace:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.External,
                        maxHealth = 95, posture = 34, physicalDamage = 10, mentalDamage = 10,
                        aggression = 0.6f, defense = 7, moveSpeed = 3.7f, attackRange = 1.8f, detectRange = 13
                    };
                    break;
                case EnemyType.ThousandEyeJudge:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.Boss,
                        maxHealth = 125, posture = 44, physicalDamage = 11, mentalDamage = 18,
                        aggression = 0.5f, defense = 9, moveSpeed = 3.2f, attackRange = 2f, detectRange = 17
                    };
                    break;
                case EnemyType.ProvokerPasserby:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.NoiseSensitivity, category = EnemyCategory.External,
                        maxHealth = 100, posture = 36, physicalDamage = 11, mentalDamage = 10,
                        aggression = 0.7f, defense = 7, moveSpeed = 4.2f, attackRange = 1.8f, detectRange = 14
                    };
                    break;
                case EnemyType.TauntMirror:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.NoiseSensitivity, category = EnemyCategory.Boss,
                        maxHealth = 130, posture = 46, physicalDamage = 12, mentalDamage = 15,
                        aggression = 0.6f, defense = 10, moveSpeed = 3.6f, attackRange = 2.1f, detectRange = 16
                    };
                    break;
                case EnemyType.GoalForgetter:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Procrastination, category = EnemyCategory.Boss,
                        maxHealth = 110, posture = 40, physicalDamage = 11, mentalDamage = 14,
                        aggression = 0.5f, defense = 8, moveSpeed = 3f, attackRange = 2f, detectRange = 15
                    };
                    break;
                case EnemyType.RequestExpander:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.BoundaryConflict, category = EnemyCategory.External,
                        maxHealth = 95, posture = 34, physicalDamage = 9, mentalDamage = 12,
                        aggression = 0.6f, defense = 6, moveSpeed = 3.6f, attackRange = 1.8f, detectRange = 13
                    };
                    break;
                case EnemyType.GuiltThrower:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.BoundaryConflict, category = EnemyCategory.Internal,
                        maxHealth = 80, posture = 26, physicalDamage = 6, mentalDamage = 14,
                        aggression = 0.5f, defense = 4, moveSpeed = 3f, attackRange = 1.8f, detectRange = 14
                    };
                    break;
                case EnemyType.GoodPersonCage:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.BoundaryConflict, category = EnemyCategory.Boss,
                        maxHealth = 130, posture = 46, physicalDamage = 11, mentalDamage = 16,
                        aggression = 0.5f, defense = 10, moveSpeed = 2.9f, attackRange = 2.1f, detectRange = 16
                    };
                    break;
                case EnemyType.InfinitePayer:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.BoundaryConflict, category = EnemyCategory.Boss,
                        maxHealth = 140, posture = 50, physicalDamage = 12, mentalDamage = 16,
                        aggression = 0.55f, defense = 10, moveSpeed = 3.1f, attackRange = 2.2f, detectRange = 17
                    };
                    break;
                case EnemyType.HungerHound:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.WillpowerCollapse, category = EnemyCategory.External,
                        maxHealth = 70, posture = 22, physicalDamage = 10, mentalDamage = 8,
                        aggression = 0.8f, defense = 4, moveSpeed = 5f, attackRange = 1.7f, detectRange = 16
                    };
                    break;
                case EnemyType.ColdWindBlade:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.WillpowerCollapse, category = EnemyCategory.Hybrid,
                        maxHealth = 90, posture = 30, physicalDamage = 9, mentalDamage = 12,
                        aggression = 0.55f, defense = 6, moveSpeed = 3.6f, attackRange = 1.9f, detectRange = 15
                    };
                    break;
                case EnemyType.MedDebtShadow:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.WillpowerCollapse, category = EnemyCategory.Internal,
                        maxHealth = 85, posture = 26, physicalDamage = 6, mentalDamage = 14,
                        aggression = 0.45f, defense = 5, moveSpeed = 2.8f, attackRange = 1.8f, detectRange = 14
                    };
                    break;
                case EnemyType.ValleyColossus:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.WillpowerCollapse, category = EnemyCategory.Boss,
                        maxHealth = 155, posture = 55, physicalDamage = 14, mentalDamage = 16,
                        aggression = 0.45f, defense = 12, moveSpeed = 2.6f, attackRange = 2.4f, detectRange = 17
                    };
                    break;
                case EnemyType.QuoteGhost:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.SelfDoubt, category = EnemyCategory.Internal,
                        maxHealth = 78, posture = 24, physicalDamage = 5, mentalDamage = 13,
                        aggression = 0.5f, defense = 4, moveSpeed = 2.9f, attackRange = 1.8f, detectRange = 14
                    };
                    break;
                case EnemyType.DoubtScholar:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.SelfDoubt, category = EnemyCategory.Hybrid,
                        maxHealth = 92, posture = 30, physicalDamage = 8, mentalDamage = 13,
                        aggression = 0.5f, defense = 6, moveSpeed = 3.2f, attackRange = 1.9f, detectRange = 15
                    };
                    break;
                case EnemyType.ConceptMazeMaster:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.SelfDoubt, category = EnemyCategory.Boss,
                        maxHealth = 130, posture = 46, physicalDamage = 11, mentalDamage = 16,
                        aggression = 0.5f, defense = 10, moveSpeed = 3f, attackRange = 2.1f, detectRange = 16
                    };
                    break;
                case EnemyType.QuestionBeast:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.SelfDoubt, category = EnemyCategory.Boss,
                        maxHealth = 125, posture = 44, physicalDamage = 12, mentalDamage = 15,
                        aggression = 0.6f, defense = 9, moveSpeed = 3.8f, attackRange = 2f, detectRange = 16
                    };
                    break;
                case EnemyType.InfiniteAsker:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.WillpowerCollapse, category = EnemyCategory.Boss,
                        maxHealth = 145, posture = 52, physicalDamage = 13, mentalDamage = 17,
                        aggression = 0.55f, defense = 10, moveSpeed = 3.2f, attackRange = 2.2f, detectRange = 18
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
            // 等级=智商/身手：级别越高出手越频繁、防御反应越灵、连招越多
            //（EnemyController 按 aggression/category 换算出手间隔/前摇/闪格概率/连击数）
            p.aggression = Mathf.Clamp01(p.aggression * Mathf.Lerp(0.75f, 1.35f,
                Mathf.InverseLerp(0.55f, 2.1f, k)));
            p.displayName = TierLabel(tier) + "·" + TypeLabel(type);
            p.enemyId = uniqueId ? BaseId(type) + "_extra_" + (++_extraCounter) : BaseId(type);
            p.rangedAttack = RangedOf(type);
            if (tier == EnemyTier.Chief) p.category = EnemyCategory.Boss;
            return p;
        }
    }
}
