using System;
using System.Collections.Generic;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// 错误策略家族（增补章第 4 条）。
    ///
    /// 【为什么干扰项必须分家族】
    /// 两个干扰项如果只是同一句话换个说法，玩家一眼就能排除，三选一退化成二选一。
    /// PRD 的原话是"至少属于不同错误家族，例如 Avoidance 与 Overcontrol"。
    /// 这里把 PRD 举例的两大类展开成实际出现在 90 关里的 12 种——
    /// 分类不是为了给玩家看（它是内部标签），而是为了让校验器能判"这两个干扰项是不是同一招"。
    /// </summary>
    public enum DistractorFamily
    {
        Unknown = 0,
        Reasonable,        // 仅用于 best
        Avoidance,         // 退出、放弃、换一个更容易的
        WaitDependency,    // 等条件、等状态、等别人给许可
        Overcontrol,       // 全部做完、更完整、更细、无限准备
        Overpersistence,   // 不许撤退、硬撑、加倍补回来
        Rumination,        // 继续分析、继续重播、继续比较
        Overcorrection,    // 反向极端：彻底禁止、全部关掉、一律拒绝
        Recklessness,      // 无视关键风险直接冲、不检查就交
        ProofFight,        // 用赢一场、说服对方来解决
        SelfPunishment,    // 骂得更狠一点才会记住
        Denial,            // 告诉自己不会发生
        Conformity,        // 别人都这么选
        Catastrophizing,   // 只盯最坏结果并当成必然
        IdentityFreeze,    // 把一次结果读成"我永远不会变"
    }

    /// <summary>三选一的呈现方式（增补章第 7 节）。</summary>
    public enum MentalPopupMode
    {
        /// <summary>普通探索/阶段切换：不倒计时，世界可暂停或极慢速。</summary>
        ExplorePause,
        /// <summary>Boss 非高速阶段：timeScale 0.15-0.25，1.5-3 秒。</summary>
        BossSlowMotion,
        /// <summary>高速动作战：不完全暂停，选项映射到技能键。</summary>
        HighSpeedHud,
    }

    /// <summary>玩家对一次内部语言攻击的处置结果。</summary>
    public enum MentalChoiceOutcome
    {
        /// <summary>没选：弹框淡出。**不算答错**，攻击原效果继续生效，不额外羞辱（第 7 节）。</summary>
        NoResponse,
        Best,
        Distractor,
    }

    /// <summary>三选一中的一项。</summary>
    [Serializable]
    public class MentalAttackOption
    {
        public string optionId;
        /// <summary>策划稿里的原始位置 A/B/C。**运行时不按它排序**——顺序每次随机（第 5 节）。</summary>
        public string canonicalSlot;
        public string text;
        /// <summary>
        /// PRD 原文——仅当这一条为了压长度被改写过才有值。
        ///
        /// 改写只动措辞长度，机制、术语与它指向的动作一个字都没变；
        /// 留底是为了能随时核对"改的到底是什么"，也让 PRD 那句
        /// 「只增补，不删除、不替换」在数据里仍然成立。
        /// </summary>
        public string canonicalText;
        public string role;             // Best / Distractor
        public int distractorIndex;     // 0=best, 1=干扰项1, 2=干扰项2
        public string distractorFamily; // DistractorFamily 枚举名
        /// <summary>立即状态效果，例如 "StartLatency↑"。这是"语言攻击不能只是字幕"的落点。</summary>
        public string immediateEffect;

        public bool IsBest => role == "Best";

        public DistractorFamily Family()
        {
            DistractorFamily f;
            return Enum.TryParse(distractorFamily ?? "", out f) ? f : DistractorFamily.Unknown;
        }
    }

    /// <summary>
    /// 一次内部语言攻击事件（增补章第 8 节 MentalAttackChoiceEvent）。
    ///
    /// 策划 Canonical 版本存在这里；运行时 AI 只能在同一个 attackIntent 下改写措辞与语境，
    /// **不得改写 best 对应的机制、FollowUpAction、真弱点、命门或 DeathType**。
    /// </summary>
    [Serializable]
    public class MentalAttackChoiceEvent
    {
        public string eventId;
        public string levelId;
        public string levelName;
        public string chapterId;
        public string sourceType;       // InternalVoice
        public string triggerType;      // 典型触发
        public float intensity;         // 0-1，随关卡档位递增

        public string attackIntent;     // 内部标签，不显示给玩家
        public string attackLine;       // 玩家看得到的那一句

        public List<MentalAttackOption> options = new List<MentalAttackOption>();
        public string bestOptionId;

        /// <summary>答对之后仍然必须做的事——答题本身永远不能通关（第 3 条）。</summary>
        public string followUpAction;

        public string popupMode;        // MentalPopupMode 枚举名
        public bool highSpeedMode;

        public bool aiGenerated;
        public float confidence = 1f;
        public List<string> safetyTags = new List<string>();
        public string seedId;
        public string aiTag;

        public MentalPopupMode Mode()
        {
            MentalPopupMode m;
            return Enum.TryParse(popupMode ?? "", out m) ? m : MentalPopupMode.ExplorePause;
        }

        public MentalAttackOption Best()
        {
            for (int i = 0; i < options.Count; i++) if (options[i].IsBest) return options[i];
            return null;
        }

        public MentalAttackOption ById(string optionId)
        {
            for (int i = 0; i < options.Count; i++) if (options[i].optionId == optionId) return options[i];
            return null;
        }
    }

    /// <summary>mental_attacks_v22.json 的根对象。</summary>
    [Serializable]
    public class MentalAttackBook
    {
        public string version;
        public List<MentalAttackChoiceEvent> events = new List<MentalAttackChoiceEvent>();
    }

    /// <summary>
    /// 关卡的 Mental Attack 档案（增补章第 11 节 LevelMentalAttackProfile）。
    /// 由目录按 levelId 现场聚合，不单独存盘。
    /// </summary>
    public class LevelMentalAttackProfile
    {
        public string levelId;
        public List<MentalAttackChoiceEvent> canonicalEvents = new List<MentalAttackChoiceEvent>();
        public List<string> attackIntents = new List<string>();
        public List<string> safetyTags = new List<string>();
        public bool aiGenerationEnabled = true;
        /// <summary>AI 置信度低于此值一律回退策划版本（第 8 节）。</summary>
        public float aiConfidenceThreshold = 0.7f;
        /// <summary>Mastery 越高弹框越少——避免玩家永久依赖系统答题（第 12 条）。</summary>
        public int masterySuppressionFrom = 3;   // M3 起开始压缩
    }

    /// <summary>
    /// 一次弹框的历史记录（增补章第 11 节 MentalAttackHistory）。
    ///
    /// 它存在的目的只有一个：复盘时能回溯"攻击语句 → 玩家选择 → 后续行为 → 结果"的因果链。
    /// 所以 followUpCompleted 与 laterFailureLinked 比"对错"本身更重要。
    /// </summary>
    [Serializable]
    public class MentalAttackHistoryEntry
    {
        public string levelId;
        public string eventId;
        public string shownAt;
        public int attackLineHash;
        /// <summary>本次实际呈现的顺序，例如 "B,A,C"。用来证明 best 的位置确实是随机的。</summary>
        public string optionOrder;
        public string selectedOptionRole;   // Best / Distractor / NoResponse
        public string selectedFamily;
        public string immediateStateChange;
        public bool followUpCompleted;
        public bool laterFailureLinked;
        public bool laterSuccessLinked;
        public int replayCount;
        public int masteryAtEvent;
    }

    [Serializable]
    public class MentalAttackHistoryData
    {
        public List<MentalAttackHistoryEntry> entries = new List<MentalAttackHistoryEntry>();
    }
}
