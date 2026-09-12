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
        InfiniteAsker,          // 无限追问者（断桥 Boss：追问弹幕/崩桥/行动答台破防）

        // ---- 第八章 羞耻与污名线（V2.1 增补）----
        // 这一线的敌人有一个共同点：它们大多**不靠打赢玩家取胜**。
        // 指认、注视、低语、追问都不是伤害手段，是让玩家停下来的手段。
        DebtMessenger,          // 欠条使者（外部·递条与追加条目的近战）
        NewHandle,              // 新的把柄（混合·每次隐瞒后生成，专攻上一条隐瞒）
        WeeklyInquirer,         // 每周追问者（外部·限制移动范围的提问链）
        AppeaseEcho,            // 讨好回声（内心·玩家自身讨好行为的具象化，不可直接击杀）
        BystanderWhisper,       // 旁观耳语者（外部·背景压力单位，不攻击，只抬暴露增速）
        SideGlancer,            // 侧目者（外部·静止单位，头部朝向生成视线锥）
        MagnifierOnlooker,      // 放大镜围观者（外部·把一次失误放大成全场事件）
        BackRowWhisperPair,     // 后排低语组（外部·双人交替发声，倒一个另一个接管）
        NailAccuser,            // 身份钉兵（混合·专职施放指认招式，truthTag = true）
        GuiltProjection,        // 心虚投影（内心·预判并抢占玩家最常用的回避路线，不可击杀）
        DisguisedClassmate,     // 伪装同学（外部·接近后转为敌对，必须先给出识别信号）
        PendingJudge,           // 悬案法官（8-1 Boss：改期/追加/要求当众/身份钉·轻，不可击杀）
        BackRowWhisperer,       // 后排低语者（8-2 Boss / T5 宿敌候选：看、说、指）

        // ---- 第 9-26 章 18 个新增章节 Boss（V2.2 增补，Boss 28-45）----
        // 这一批全部是【内部敌人】：它们不是路人、同事或第三方，而是玩家自己那套机制
        // 在逆境层里的实体化。所以它们几乎都不靠打倒解决——真正的失效条件写在
        // InternalBossDNA.executionGate 里（提交、出门、归档、撤销裁判权、重新进入……）。
        PerfectionJudge,        // 完美审判官（9 章 Boss 28：瑕疵扫描/标准漂移，Submit 即失效）
        FrozenKing,             // 冻结之王（10 章 Boss 29：伪准备与等待，跨出门槛即失效）
        RejectionGatekeeper,    // 拒绝守门人（11 章 Boss 30：身体不是命门，撤销终审权）
        PowerlessProphet,       // 无力预言家（12 章 Boss 31：预测权，靠行为证据校准）
        OnceBrokenEnder,        // 一次中断即结束者（13 章 Boss 32：断了就作废，重新进入即破）
        AbsoluteCertainty,      // 绝对确定者（14 章 Boss 33：把决定锁在信息里）
        RankThrone,             // 等级王座（15 章 Boss 34：比较依赖，解除依赖即失效）
        EternalReplayer,        // 永恒重播机（16 章 Boss 35：注意力所有权，归档即失效）
        OverloadedRadar,        // 过载雷达（17 章 Boss 36：不是恶意，是保护系统失准）
        RetreatKing,            // 撤退之王（18 章 Boss 37：能力长过它之后自然失去统治力）
        MeaningDisconnector,    // 意义断线者（19 章 Boss 38：无直接攻击，重连价值即失效）
        HabitHijacker,          // 习惯劫持者（20 章 Boss 39：触发结构，替换动作即失效）
        DisasterProphetDragon,  // 灾难预言龙（21 章 Boss 40：不可恢复幻觉）
        InnerTyrant,            // 内在暴君（22 章 Boss 41：撤销羞辱对行动的指挥权）
        AssimilationFog,        // 同化迷雾（23 章 Boss 42：规范权，靠改变暴露结构取胜）
        NeverFinisher,          // 永不完成者（24 章 Boss 43：范围边界，冻结版本并发布）
        KnowNotDoer,            // 知而不行者（25 章 Boss 44：行为迁移，知识必须落到现实动作）
        OldDestiny              // 旧命运（26 章 Boss 45：终局，撤销未来判决权并整合）
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

        /// <summary>
        /// 全局生命调校系数（只乘生命，不动伤害与韧性）。
        ///
        /// 【为什么需要它】目录里那张 60~160 的生命表，是在
        /// GameDebug.TankyEnemies 默认为 true（全工程伤害 ×0.1）的年代写下的——
        /// 也就是说这张表从来没有在真实伤害下被验证过。把那个调试开关关掉之后，
        /// 表里的数字第一次原样生效，于是一套完整剑连（4 段合计 5.8 倍基础伤害
        /// ≈ 107 点，链取消下约 0.9 秒）就能打空一个 100 血的标准杂兵。
        /// 玩家的原话是"敌人很容易就被打死"——不是通关规则改了，是这张表
        /// 一直缺一个从没有人算过的换算。
        ///
        /// 【怎么定的】按"打倒需要几套完整剑连"反推，不是拍脑袋：
        ///   见习 ≈1 套、标准 ≈2 套、精英 ≈3.5 套、首领 ≈5.5 套。
        /// 换算成秒是 0.9 / 1.9 / 3.4 / 5.2 秒的**不间断输出**——实战里还要闪、
        /// 要拉开、要读招，真实时长是这个数的三到五倍。
        /// 首领没有定得更高，是因为"血条一击只掉 2%"读起来就是"打不动"，
        /// 而那正是上一轮"敌人有无限的生命"的观感来源。首领该靠削韧破防、
        /// 抓破绽处决（重击 ×2.8）来缩短，而不是靠堆一条磨不完的血条。
        ///
        /// 这一行的实际结果由 CI 每次构建打成一张表（见 CIDiagnostics 的「平衡」段），
        /// 改这个数就能在下一次构建的日志里直接看到几套连招——不必靠猜，也不必靠手感回忆。
        /// </summary>
        public static float HealthTuning(EnemyTier t)
        {
            switch (t)
            {
                case EnemyTier.Novice: return 2.0f;
                case EnemyTier.Elite: return 2.5f;
                case EnemyTier.Chief: return 2.8f;
                default: return 2.2f;
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
                case EnemyType.DebtMessenger: return "欠条使者";
                case EnemyType.NewHandle: return "新的把柄";
                case EnemyType.WeeklyInquirer: return "每周追问者";
                case EnemyType.AppeaseEcho: return "讨好回声";
                case EnemyType.BystanderWhisper: return "旁观耳语者";
                case EnemyType.SideGlancer: return "侧目者";
                case EnemyType.MagnifierOnlooker: return "放大镜围观者";
                case EnemyType.BackRowWhisperPair: return "后排低语组";
                case EnemyType.NailAccuser: return "身份钉兵";
                case EnemyType.GuiltProjection: return "心虚投影";
                case EnemyType.DisguisedClassmate: return "伪装同学";
                case EnemyType.PendingJudge: return "悬案法官";
                case EnemyType.BackRowWhisperer: return "后排低语者";
                case EnemyType.PerfectionJudge: return "完美审判官";
                case EnemyType.FrozenKing: return "冻结之王";
                case EnemyType.RejectionGatekeeper: return "拒绝守门人";
                case EnemyType.PowerlessProphet: return "无力预言家";
                case EnemyType.OnceBrokenEnder: return "一次中断即结束者";
                case EnemyType.AbsoluteCertainty: return "绝对确定者";
                case EnemyType.RankThrone: return "等级王座";
                case EnemyType.EternalReplayer: return "永恒重播机";
                case EnemyType.OverloadedRadar: return "过载雷达";
                case EnemyType.RetreatKing: return "撤退之王";
                case EnemyType.MeaningDisconnector: return "意义断线者";
                case EnemyType.HabitHijacker: return "习惯劫持者";
                case EnemyType.DisasterProphetDragon: return "灾难预言龙";
                case EnemyType.InnerTyrant: return "内在暴君";
                case EnemyType.AssimilationFog: return "同化迷雾";
                case EnemyType.NeverFinisher: return "永不完成者";
                case EnemyType.KnowNotDoer: return "知而不行者";
                case EnemyType.OldDestiny: return "旧命运";
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
                case EnemyType.DebtMessenger: return new Color(0.62f, 0.55f, 0.38f);
                case EnemyType.NewHandle: return new Color(0.5f, 0.42f, 0.34f);
                case EnemyType.WeeklyInquirer: return new Color(0.48f, 0.5f, 0.58f);
                case EnemyType.AppeaseEcho: return new Color(0.72f, 0.66f, 0.55f);
                case EnemyType.BystanderWhisper: return new Color(0.58f, 0.56f, 0.62f);
                case EnemyType.SideGlancer: return new Color(0.68f, 0.66f, 0.5f);
                case EnemyType.MagnifierOnlooker: return new Color(0.8f, 0.72f, 0.45f);
                case EnemyType.BackRowWhisperPair: return new Color(0.55f, 0.5f, 0.68f);
                case EnemyType.NailAccuser: return new Color(0.6f, 0.58f, 0.56f);
                case EnemyType.GuiltProjection: return new Color(0.24f, 0.22f, 0.3f);
                case EnemyType.DisguisedClassmate: return new Color(0.6f, 0.64f, 0.62f);
                case EnemyType.PendingJudge: return new Color(0.4f, 0.36f, 0.32f);
                case EnemyType.BackRowWhisperer: return new Color(0.46f, 0.42f, 0.58f);
                case EnemyType.PerfectionJudge: return new Color(0.72f, 0.70f, 0.62f);
                case EnemyType.FrozenKing: return new Color(0.55f, 0.70f, 0.80f);
                case EnemyType.RejectionGatekeeper: return new Color(0.45f, 0.45f, 0.52f);
                case EnemyType.PowerlessProphet: return new Color(0.40f, 0.46f, 0.58f);
                case EnemyType.OnceBrokenEnder: return new Color(0.50f, 0.38f, 0.42f);
                case EnemyType.AbsoluteCertainty: return new Color(0.62f, 0.62f, 0.72f);
                case EnemyType.RankThrone: return new Color(0.78f, 0.66f, 0.35f);
                case EnemyType.EternalReplayer: return new Color(0.52f, 0.40f, 0.62f);
                case EnemyType.OverloadedRadar: return new Color(0.85f, 0.50f, 0.30f);
                case EnemyType.RetreatKing: return new Color(0.42f, 0.52f, 0.48f);
                case EnemyType.MeaningDisconnector: return new Color(0.46f, 0.46f, 0.50f);
                case EnemyType.HabitHijacker: return new Color(0.60f, 0.35f, 0.55f);
                case EnemyType.DisasterProphetDragon: return new Color(0.35f, 0.30f, 0.45f);
                case EnemyType.InnerTyrant: return new Color(0.58f, 0.22f, 0.26f);
                case EnemyType.AssimilationFog: return new Color(0.62f, 0.66f, 0.68f);
                case EnemyType.NeverFinisher: return new Color(0.55f, 0.50f, 0.30f);
                case EnemyType.KnowNotDoer: return new Color(0.50f, 0.58f, 0.55f);
                case EnemyType.OldDestiny: return new Color(0.20f, 0.20f, 0.28f);
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
                // 羞耻线：这一线基本不持械——它们的武器是话、是眼睛、是一句判词
                case EnemyType.DebtMessenger: return Combat.WeaponKind.Staff;      // 递条的长杆
                case EnemyType.NewHandle: return Combat.WeaponKind.Claw;           // 抓住不放的手
                case EnemyType.WeeklyInquirer: return Combat.WeaponKind.None;
                case EnemyType.AppeaseEcho: return Combat.WeaponKind.None;
                case EnemyType.BystanderWhisper: return Combat.WeaponKind.None;
                case EnemyType.SideGlancer: return Combat.WeaponKind.None;
                case EnemyType.MagnifierOnlooker: return Combat.WeaponKind.None;
                case EnemyType.BackRowWhisperPair: return Combat.WeaponKind.None;
                case EnemyType.NailAccuser: return Combat.WeaponKind.Claw;         // 钉子
                case EnemyType.GuiltProjection: return Combat.WeaponKind.None;
                case EnemyType.DisguisedClassmate: return Combat.WeaponKind.None;
                case EnemyType.PendingJudge: return Combat.WeaponKind.Staff;       // 合上的账本
                case EnemyType.BackRowWhisperer: return Combat.WeaponKind.None;
                case EnemyType.PerfectionJudge: return Combat.WeaponKind.Staff;   // 长杖·审判与压制
                case EnemyType.FrozenKing: return Combat.WeaponKind.None;   // 纯内部语言，不持械
                case EnemyType.RejectionGatekeeper: return Combat.WeaponKind.Sword;   // 裁决之剑
                case EnemyType.PowerlessProphet: return Combat.WeaponKind.None;   // 纯内部语言，不持械
                case EnemyType.OnceBrokenEnder: return Combat.WeaponKind.Claw;   // 抓取·缠住不放
                case EnemyType.AbsoluteCertainty: return Combat.WeaponKind.Staff;   // 长杖·审判与压制
                case EnemyType.RankThrone: return Combat.WeaponKind.Sword;   // 裁决之剑
                case EnemyType.EternalReplayer: return Combat.WeaponKind.None;   // 纯内部语言，不持械
                case EnemyType.OverloadedRadar: return Combat.WeaponKind.None;   // 纯内部语言，不持械
                case EnemyType.RetreatKing: return Combat.WeaponKind.Blade;   // 快斩·退路
                case EnemyType.MeaningDisconnector: return Combat.WeaponKind.None;   // 纯内部语言，不持械
                case EnemyType.HabitHijacker: return Combat.WeaponKind.Claw;   // 抓取·缠住不放
                case EnemyType.DisasterProphetDragon: return Combat.WeaponKind.Staff;   // 长杖·审判与压制
                case EnemyType.InnerTyrant: return Combat.WeaponKind.Staff;   // 长杖·审判与压制
                case EnemyType.AssimilationFog: return Combat.WeaponKind.None;   // 纯内部语言，不持械
                case EnemyType.NeverFinisher: return Combat.WeaponKind.Claw;   // 抓取·缠住不放
                case EnemyType.KnowNotDoer: return Combat.WeaponKind.Sword;   // 裁决之剑
                case EnemyType.OldDestiny: return Combat.WeaponKind.None;   // 纯内部语言，不持械
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
            t == EnemyType.InfiniteAsker ||
            // 羞耻线的"远程"不是弹幕，是隔着半个房间也能落到身上的一句话
            t == EnemyType.NewHandle || t == EnemyType.AppeaseEcho ||
            t == EnemyType.MagnifierOnlooker || t == EnemyType.BackRowWhisperPair ||
            t == EnemyType.NailAccuser || t == EnemyType.BackRowWhisperer ||
            // 第 9-26 章：这一批的"远程"是那句话本身——它不需要靠近就能让人停下
            t == EnemyType.PerfectionJudge || t == EnemyType.FrozenKing ||
            t == EnemyType.PowerlessProphet || t == EnemyType.RankThrone ||
            t == EnemyType.EternalReplayer || t == EnemyType.OverloadedRadar ||
            t == EnemyType.MeaningDisconnector || t == EnemyType.InnerTyrant ||
            t == EnemyType.AssimilationFog || t == EnemyType.OldDestiny;

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
                case EnemyType.DebtMessenger: return "enemy_debt_messenger";
                case EnemyType.NewHandle: return "enemy_new_handle";
                case EnemyType.WeeklyInquirer: return "enemy_weekly_inquirer";
                case EnemyType.AppeaseEcho: return "enemy_appease_echo";
                case EnemyType.BystanderWhisper: return "enemy_bystander_whisper";
                case EnemyType.SideGlancer: return "enemy_side_glancer";
                case EnemyType.MagnifierOnlooker: return "enemy_magnifier_onlooker";
                case EnemyType.BackRowWhisperPair: return "enemy_back_row_pair";
                case EnemyType.NailAccuser: return "enemy_nail_accuser";
                case EnemyType.GuiltProjection: return "enemy_guilt_projection";
                case EnemyType.DisguisedClassmate: return "enemy_disguised_classmate";
                case EnemyType.PendingJudge: return "boss_pending_judge";
                case EnemyType.BackRowWhisperer: return "boss_back_row_whisperer";
                case EnemyType.PerfectionJudge: return "boss_perfection_judge";
                case EnemyType.FrozenKing: return "boss_frozen_king";
                case EnemyType.RejectionGatekeeper: return "boss_rejection_gatekeeper";
                case EnemyType.PowerlessProphet: return "boss_powerless_prophet";
                case EnemyType.OnceBrokenEnder: return "boss_once_broken_ender";
                case EnemyType.AbsoluteCertainty: return "boss_absolute_certainty";
                case EnemyType.RankThrone: return "boss_rank_throne";
                case EnemyType.EternalReplayer: return "boss_eternal_replayer";
                case EnemyType.OverloadedRadar: return "boss_overloaded_radar";
                case EnemyType.RetreatKing: return "boss_retreat_king";
                case EnemyType.MeaningDisconnector: return "boss_meaning_disconnector";
                case EnemyType.HabitHijacker: return "boss_habit_hijacker";
                case EnemyType.DisasterProphetDragon: return "boss_disaster_dragon";
                case EnemyType.InnerTyrant: return "boss_inner_tyrant";
                case EnemyType.AssimilationFog: return "boss_assimilation_fog";
                case EnemyType.NeverFinisher: return "boss_never_finisher";
                case EnemyType.KnowNotDoer: return "boss_know_not_doer";
                case EnemyType.OldDestiny: return "boss_old_destiny";
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
                // ================= 第八章 羞耻与污名线 =================
                // 【Physical 维度被主动压低】（8.7 预算表：物理只占 15%）
                // 这一线的物理伤害普遍很低，压力全部压在 Mental 与 Environmental 上。
                // 这是主题决定的，不是难度取巧——本章禁止靠堆 Boss 血量制造难度。
                case EnemyType.DebtMessenger:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.External,
                        maxHealth = 80, posture = 26, physicalDamage = 8, mentalDamage = 7,
                        aggression = 0.5f, defense = 5, moveSpeed = 3.2f, attackRange = 2f, detectRange = 13
                    };
                    break;
                case EnemyType.NewHandle:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.Hybrid,
                        maxHealth = 95, posture = 32, physicalDamage = 6, mentalDamage = 14,
                        aggression = 0.5f, defense = 6, moveSpeed = 3.3f, attackRange = 1.9f, detectRange = 15
                    };
                    break;
                case EnemyType.WeeklyInquirer:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.External,
                        maxHealth = 100, posture = 34, physicalDamage = 5, mentalDamage = 13,
                        aggression = 0.45f, defense = 7, moveSpeed = 3f, attackRange = 2f, detectRange = 16
                    };
                    break;
                case EnemyType.AppeaseEcho:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.Internal,
                        maxHealth = 110, posture = 38, physicalDamage = 5, mentalDamage = 15,
                        aggression = 0.5f, defense = 8, moveSpeed = 3.1f, attackRange = 1.9f, detectRange = 15
                    };
                    break;
                case EnemyType.BystanderWhisper:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.External,
                        maxHealth = 60, posture = 18, physicalDamage = 0, mentalDamage = 0,
                        aggression = 0.05f, defense = 4, moveSpeed = 2.4f, attackRange = 1.6f, detectRange = 18
                    };
                    break;
                case EnemyType.SideGlancer:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.External,
                        maxHealth = 70, posture = 22, physicalDamage = 0, mentalDamage = 0,
                        aggression = 0.05f, defense = 5, moveSpeed = 0.6f, attackRange = 1.6f, detectRange = 20
                    };
                    break;
                case EnemyType.MagnifierOnlooker:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.External,
                        maxHealth = 90, posture = 30, physicalDamage = 4, mentalDamage = 12,
                        aggression = 0.4f, defense = 6, moveSpeed = 2.8f, attackRange = 1.9f, detectRange = 18
                    };
                    break;
                case EnemyType.BackRowWhisperPair:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.External,
                        maxHealth = 85, posture = 28, physicalDamage = 4, mentalDamage = 11,
                        aggression = 0.45f, defense = 6, moveSpeed = 3f, attackRange = 1.9f, detectRange = 17
                    };
                    break;
                case EnemyType.NailAccuser:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.Hybrid,
                        maxHealth = 115, posture = 40, physicalDamage = 7, mentalDamage = 16,
                        aggression = 0.4f, defense = 9, moveSpeed = 3.1f, attackRange = 2f, detectRange = 17
                    };
                    break;
                case EnemyType.GuiltProjection:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.Internal,
                        maxHealth = 130, posture = 46, physicalDamage = 6, mentalDamage = 14,
                        aggression = 0.55f, defense = 10, moveSpeed = 4.2f, attackRange = 1.9f, detectRange = 20
                    };
                    break;
                case EnemyType.DisguisedClassmate:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.External,
                        maxHealth = 95, posture = 32, physicalDamage = 9, mentalDamage = 8,
                        aggression = 0.55f, defense = 7, moveSpeed = 3.5f, attackRange = 1.9f, detectRange = 14
                    };
                    break;
                // 两个 Boss 的血量与攻击性按"真的要打一场"重给：
                // 原来是 130/120，和一个精英杂兵一样——那是按"不可击杀、只做压迫"设计的，
                // 现在它们要能被打死，就得有 Boss 该有的厚度和存在感，否则两刀就没了。
                case EnemyType.PendingJudge:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.Boss,
                        maxHealth = 340, posture = 96, physicalDamage = 9, mentalDamage = 16,
                        aggression = 0.6f, defense = 14, moveSpeed = 3.4f, attackRange = 2.2f, detectRange = 22
                    };
                    break;
                case EnemyType.BackRowWhisperer:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.Boss,
                        maxHealth = 300, posture = 88, physicalDamage = 8, mentalDamage = 18,
                        aggression = 0.55f, defense = 12, moveSpeed = 3.3f, attackRange = 2.1f, detectRange = 24
                    };
                    break;

                // ---- 第 9-26 章 Boss 28-45（V2.2）----
                // 血量在 290-380 之间：它们要能被打一场，但打倒本身通常不结束战斗——
                // 结束条件由 InternalBossDNA.executionGate 决定，见 InternalChapterBridge.BossDefeated。
                // 心理伤害普遍高于物理：这一批的主要攻击手段是那句内部语言。
                case EnemyType.PerfectionJudge:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FailureFear, category = EnemyCategory.Boss,
                        maxHealth = 330, posture = 94, physicalDamage = 10, mentalDamage = 17,
                        aggression = 0.55f, defense = 14, moveSpeed = 3.2f, attackRange = 2.2f, detectRange = 22
                    };
                    break;
                case EnemyType.FrozenKing:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Procrastination, category = EnemyCategory.Boss,
                        maxHealth = 310, posture = 90, physicalDamage = 7, mentalDamage = 18,
                        aggression = 0.45f, defense = 13, moveSpeed = 2.8f, attackRange = 2.0f, detectRange = 24
                    };
                    break;
                case EnemyType.RejectionGatekeeper:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.JobAnxiety, category = EnemyCategory.Boss,
                        maxHealth = 320, posture = 92, physicalDamage = 11, mentalDamage = 15,
                        aggression = 0.60f, defense = 14, moveSpeed = 3.3f, attackRange = 2.1f, detectRange = 22
                    };
                    break;
                case EnemyType.PowerlessProphet:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.LowConfidence, category = EnemyCategory.Boss,
                        maxHealth = 300, posture = 86, physicalDamage = 7, mentalDamage = 19,
                        aggression = 0.50f, defense = 12, moveSpeed = 3.0f, attackRange = 2.0f, detectRange = 24
                    };
                    break;
                case EnemyType.OnceBrokenEnder:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.WillpowerCollapse, category = EnemyCategory.Boss,
                        maxHealth = 315, posture = 90, physicalDamage = 11, mentalDamage = 15,
                        aggression = 0.58f, defense = 13, moveSpeed = 3.3f, attackRange = 2.1f, detectRange = 21
                    };
                    break;
                case EnemyType.AbsoluteCertainty:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.SelfDoubt, category = EnemyCategory.Boss,
                        maxHealth = 305, posture = 88, physicalDamage = 9, mentalDamage = 17,
                        aggression = 0.50f, defense = 13, moveSpeed = 3.0f, attackRange = 2.2f, detectRange = 23
                    };
                    break;
                case EnemyType.RankThrone:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.Boss,
                        maxHealth = 335, posture = 95, physicalDamage = 10, mentalDamage = 18,
                        aggression = 0.55f, defense = 15, moveSpeed = 3.2f, attackRange = 2.2f, detectRange = 23
                    };
                    break;
                case EnemyType.EternalReplayer:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FairnessSensitivity, category = EnemyCategory.Boss,
                        maxHealth = 300, posture = 86, physicalDamage = 8, mentalDamage = 19,
                        aggression = 0.50f, defense = 12, moveSpeed = 3.1f, attackRange = 2.0f, detectRange = 24
                    };
                    break;
                case EnemyType.OverloadedRadar:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.NoiseSensitivity, category = EnemyCategory.Boss,
                        maxHealth = 290, posture = 84, physicalDamage = 8, mentalDamage = 18,
                        aggression = 0.62f, defense = 12, moveSpeed = 3.2f, attackRange = 2.0f, detectRange = 26
                    };
                    break;
                case EnemyType.RetreatKing:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Procrastination, category = EnemyCategory.Boss,
                        maxHealth = 310, posture = 88, physicalDamage = 10, mentalDamage = 16,
                        aggression = 0.50f, defense = 13, moveSpeed = 3.4f, attackRange = 2.1f, detectRange = 22
                    };
                    break;
                case EnemyType.MeaningDisconnector:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.WillpowerCollapse, category = EnemyCategory.Boss,
                        maxHealth = 295, posture = 84, physicalDamage = 6, mentalDamage = 19,
                        aggression = 0.42f, defense = 12, moveSpeed = 3.0f, attackRange = 2.0f, detectRange = 24
                    };
                    break;
                case EnemyType.HabitHijacker:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Procrastination, category = EnemyCategory.Boss,
                        maxHealth = 300, posture = 86, physicalDamage = 12, mentalDamage = 15,
                        aggression = 0.70f, defense = 12, moveSpeed = 3.6f, attackRange = 2.0f, detectRange = 22
                    };
                    break;
                case EnemyType.DisasterProphetDragon:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FailureFear, category = EnemyCategory.Boss,
                        maxHealth = 345, posture = 98, physicalDamage = 13, mentalDamage = 17,
                        aggression = 0.55f, defense = 15, moveSpeed = 3.1f, attackRange = 2.3f, detectRange = 24
                    };
                    break;
                case EnemyType.InnerTyrant:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.Shame, category = EnemyCategory.Boss,
                        maxHealth = 340, posture = 96, physicalDamage = 12, mentalDamage = 18,
                        aggression = 0.60f, defense = 15, moveSpeed = 3.2f, attackRange = 2.2f, detectRange = 22
                    };
                    break;
                case EnemyType.AssimilationFog:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.BoundaryConflict, category = EnemyCategory.Boss,
                        maxHealth = 295, posture = 84, physicalDamage = 7, mentalDamage = 18,
                        aggression = 0.48f, defense = 12, moveSpeed = 3.0f, attackRange = 2.0f, detectRange = 25
                    };
                    break;
                case EnemyType.NeverFinisher:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.BoundaryConflict, category = EnemyCategory.Boss,
                        maxHealth = 320, posture = 92, physicalDamage = 11, mentalDamage = 16,
                        aggression = 0.58f, defense = 14, moveSpeed = 3.3f, attackRange = 2.1f, detectRange = 22
                    };
                    break;
                case EnemyType.KnowNotDoer:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.SelfDoubt, category = EnemyCategory.Boss,
                        maxHealth = 305, posture = 88, physicalDamage = 9, mentalDamage = 17,
                        aggression = 0.50f, defense = 13, moveSpeed = 3.1f, attackRange = 2.1f, detectRange = 23
                    };
                    break;
                case EnemyType.OldDestiny:
                    p = new EnemyProfile
                    {
                        targetWeakness = WeaknessAxis.FailureFear, category = EnemyCategory.Boss,
                        maxHealth = 380, posture = 108, physicalDamage = 12, mentalDamage = 20,
                        aggression = 0.58f, defense = 16, moveSpeed = 3.3f, attackRange = 2.2f, detectRange = 26
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
            // 生命另乘一道全局调校：目录里那张表是在"伤害 ×0.1"的年代写的，
            // 从没在真实伤害下算过。理由与取值见 HealthTuning。
            p.maxHealth *= k * HealthTuning(tier);
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
