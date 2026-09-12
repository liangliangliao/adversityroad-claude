namespace AdversityRoad.AI
{
    /// <summary>
    /// 敌人的**来处**：这个心魔是世界里的东西，还是自己心里的东西。
    ///
    /// 【为什么不能直接用 EnemyCategory】
    /// EnemyCategory 有四个值（External / Internal / Hybrid / Boss），而且
    /// `EnemyCatalog.Create` 的最后一行是：
    ///     if (tier == EnemyTier.Chief) p.category = EnemyCategory.Boss;
    /// ——只要挂成首领，它原本是外部还是内心就被**抹掉**了。目录里二十几个
    /// 关卡首领因此全都是 Boss，一个都读不出来处。战斗节奏（出手间隔/闪格概率/
    /// 连招数）确实只关心"是不是 Boss"，抹掉没问题；但关卡规则关心的恰恰是
    /// 被抹掉的那一半。
    ///
    /// 所以来处单独存一份，**与 category 正交**：category 说"它在战斗里有多重"，
    /// origin 说"它是谁的东西"。Hybrid 也必须在这里二选一——关卡规则是一道
    /// 非此即彼的门，没有"一半算通关"这种东西。
    /// </summary>
    public enum EnemyOrigin
    {
        /// <summary>外部：别人、处境、环境。它在世界里，不在你心里。</summary>
        External,
        /// <summary>内心：自我怀疑、拖延、羞耻、旧我。它跟着你走，走到哪都在。</summary>
        Internal
    }

    /// <summary>
    /// 敌人来处表——**关卡规则的唯一来源**。
    ///
    /// 每个类型在这里只有一行答案：外部，还是内心。它决定了这个敌人所在关卡
    /// 的通关方式（见 <see cref="AdversityRoad.Core.LevelRules"/>）：
    ///   · 外部敌人的关卡 → 从入口进、从出口出就算通关，可以完全不交战；
    ///   · 内心敌人的关卡 → 仍然要打倒它才算通关。
    ///
    /// 【判断口径】只问一句话：**这东西是不是就算你走开了也还在？**
    ///   · 赖账的人、追着你咳嗽的人、盯着你的目光、逼你代付的人、饿和冷——
    ///     你走出那扇门，它们留在那个地方。这是外部。
    ///   · 自我怀疑、拖延、自我否定、旧我、反刍——
    ///     你走出那扇门，它们跟着你出来。这是内心。
    ///
    /// 混合型（目录里标 Hybrid 的那几个）按"**这件事是谁起的头**"归边：
    /// 咳嗽是别人咳的、寒风是天气、追问是别人问的 → 外部；
    /// "新的把柄"是玩家自己隐瞒生成的 → 内心。
    /// </summary>
    public static class EnemyOrigins
    {
        public static EnemyOrigin Of(EnemyType type)
        {
            switch (type)
            {
                // ---------- 外部：世界里的人与处境 ----------
                case EnemyType.OverreactGhost:          // 小题大做鬼：别人替你评价你的感受
                case EnemyType.DebtDodger:              // 赖账牌手
                case EnemyType.RuleTwister:             // 规则篡改者：改规则的是对方
                case EnemyType.GambleKing:              // 两元赖账王
                case EnemyType.DebtCarKing:             // 新车债王
                case EnemyType.CoughAssassin:           // 咳声刺客：咳嗽是别人咳的
                case EnemyType.StimulusAmplifier:       // 刺激放大器：外界刺激线的终战
                case EnemyType.MaskFace:                // 表情面具：假笑之下的评价
                case EnemyType.ThousandEyeJudge:        // 万眼审判者：别人的目光
                case EnemyType.ProvokerPasserby:        // 挑衅路人
                case EnemyType.TauntMirror:             // 挑衅镜像：整关的主题就是"不接招"
                case EnemyType.TomorrowMud:             // 明日泥怪：拖住脚的是外面的泥
                case EnemyType.NoReplyKing:             // 无回应之王：沉默的是招聘方
                case EnemyType.RequestExpander:         // 请求膨胀者：提要求的是别人
                case EnemyType.GoodPersonCage:          // 好人牢笼：牢笼由别人的索取砌成
                case EnemyType.TotalResponsibilityJudge:// 全责法官：把责任推过来的是对方
                case EnemyType.InfinitePayer:           // 无限代付者
                case EnemyType.HungerHound:             // 饥饿犬影：饿是处境
                case EnemyType.ColdWindBlade:           // 寒风刃：冷是天气
                case EnemyType.DoubtScholar:            // 怀疑学者："你确定吗"是别人问的
                case EnemyType.PastJudge:               // 过去判官：拿旧事审你的是别人
                case EnemyType.DebtMessenger:           // 欠条使者
                case EnemyType.WeeklyInquirer:          // 每周追问者
                case EnemyType.BystanderWhisper:        // 旁观耳语者
                case EnemyType.SideGlancer:             // 侧目者
                case EnemyType.MagnifierOnlooker:       // 放大镜围观者
                case EnemyType.BackRowWhisperPair:      // 后排低语组
                case EnemyType.NailAccuser:             // 身份钉兵：指认你的是别人
                case EnemyType.DisguisedClassmate:      // 伪装同学
                case EnemyType.PendingJudge:            // 悬案法官：把柄在别人手里
                case EnemyType.BackRowWhisperer:        // 后排低语者
                    return EnemyOrigin.External;

                // ---------- 内心：走出那扇门也跟着你出来的那部分 ----------
                case EnemyType.SelfDoubtWhisper:        // 自我怀疑低语
                case EnemyType.TomorrowPhantom:         // 明日幻影
                case EnemyType.ShameMirror:             // 羞耻镜像
                case EnemyType.MockingBystander:        // 旁观嘲笑者：听见的是自己的羞耻
                case EnemyType.SelfDenialGavel:         // 自我否定法槌：敲槌的是你自己
                case EnemyType.PerfectPreparer:         // 完美准备者
                case EnemyType.TomorrowKing:            // 明天之王
                case EnemyType.ProcrastinationShadow:   // 拖延影魔
                case EnemyType.GoalForgetter:           // 目标遗忘者
                case EnemyType.DebtShadow:              // 欠款残影：未结清之事在心里的形状
                case EnemyType.GazeEye:                 // 凝视眼球：被注视"感"
                case EnemyType.GuiltThrower:            // 内疚投手
                case EnemyType.MedDebtShadow:           // 医药债影
                case EnemyType.ValleyColossus:          // 低谷巨像：无力感
                case EnemyType.QuoteGhost:              // 引文幽灵
                case EnemyType.ConceptMazeMaster:       // 概念迷宫师
                case EnemyType.QuestionBeast:           // 无限问题兽
                case EnemyType.InfiniteAsker:           // 无限追问者
                case EnemyType.OldVoiceRepeater:        // 旧话复读者
                case EnemyType.RuminationSwarm:         // 反刍虫群
                case EnemyType.OldSelf:                 // 旧我
                case EnemyType.NewHandle:               // 新的把柄：由玩家自己的隐瞒生成
                case EnemyType.AppeaseEcho:             // 讨好回声：玩家自身讨好行为的具象
                case EnemyType.GuiltProjection:         // 心虚投影
                    return EnemyOrigin.Internal;

                // 新增敌人若忘了归边，一律按内心处理——那是**旧规则**（要打倒才通关），
                // 漏归边最多是少一条捷径，不会把一关变成走两步就通关。
                default:
                    return EnemyOrigin.Internal;
            }
        }

        /// <summary>玩家可读的来处标签（"外部" / "内心"）。</summary>
        public static string Label(EnemyOrigin origin) =>
            origin == EnemyOrigin.External ? "外部" : "内心";
    }
}
