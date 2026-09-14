namespace AdversityRoad.Core
{
    /// <summary>
    /// 全局通关规则：**打倒这一关的大 BOSS，就具备通关条件。**
    ///
    /// 玩家原话："不管是经典关卡还是新增关卡或其他任何关卡，也不管是属于外部敌人关卡
    /// 还是属于内部敌人关卡，统一规则只需要消灭大BOSS就算具备通关条件，
    /// 无必须打死其他小BOSS敌人。"
    ///
    /// 【这条规则改了什么、没改什么】
    /// 查下来，"必须打死小 BOSS"这件事本来就不存在：每一章只有一个目标敌人
    /// （ChapterInfo.enemyId），生成章节只给 tier=="chief" 的那一个挂 ChapterGateEnemy，
    /// 精英与杂兵从来不是通关条件。这一条因此主要是把**反过来的那一面**补齐——
    /// 有几处"打死了大 BOSS 也不给通关"：
    ///
    ///   · 第八章两关：advanceOnKill=false，打死 Boss 不推进；
    ///   · 第 9-26 章：靠 Execution Gate 判，DeathType 决定打死算不算；
    ///   · 第 9-26 章的 Boss 关：打死 chief 只写了 GoalOS.ChapterCleared，
    ///     没有设 runner.Cleared——出口门不放人、下一关不解锁（这本身是个 bug）。
    ///
    /// 现在统一：**打倒大 BOSS 一律满足通关条件。**
    ///
    /// 【原有的非战斗路线一条都不拆】
    /// 外部心魔的"穿过去就算"、第八章的"走进广播室自行陈述"、
    /// 第 9-26 章的"交付（Execution Gate）"全部照旧。
    /// 这一条是**多给一条路**，不是把别的路换掉——谁先到算谁。
    ///
    /// 【要说清楚的代价】
    /// 对第 9-26 章，这一条和 PRD 第 3.4 节「不以清怪定义胜利」以及 DeathType
    /// （9-5 的完美审判官写的是 DeathType=Publish：按下 Submit 即失效，HP 未清零也算）
    /// 是有出入的：按 PRD，那种 Boss 本来就不该靠打死来结束。
    /// 产品要统一，这里就按统一的来；想改回按 DeathType 走，把下面这个开关翻成 false，
    /// 两个判定点（StoryManager.HandleEnemyKilled、InternalChapterBridge.BossDefeated）
    /// 会一起回到原来的判法。
    ///
    /// 第三处改动**不受这个开关影响**，因为它是 bug 不是规则：
    /// ChapterGateEnemy 打倒 chief 之后现在会调 InternalLevelRunner.ExecutionGate，
    /// 让关卡自己知道通关了。原来只写 GoalOS.ChapterCleared，
    /// runner.Cleared 没置位——玩家打完 Boss 被关在里面出不去、下一关也不解锁。
    /// 那一条无论规则怎么定都得修。
    /// </summary>
    public static class BossClearRule
    {
        /// <summary>打倒大 BOSS 是否一律满足通关条件。</summary>
        public const bool KillBossClears = true;
    }
}
