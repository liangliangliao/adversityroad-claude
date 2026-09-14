using UnityEngine;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// 场上每一件带字的东西，走近时要回答的**同样三个问题**。
    ///
    /// 【为什么要把它收成一份】
    /// 玩家的要求很具体："玩家站立在所有带有文字牌子旁边自动或者点击牌子显示解释，
    /// 这是什么？为什么出现在这里？如何用它？而这些解释需要和产品方案中的
    /// 核心思想内容一致。"
    ///
    /// 在这一版之前，解释散在四个地方、格式各不相同：
    ///   · SiteSignExplain 一句话（入口/出口/路线牌）
    ///   · InternalProp.Hint 一句话（关键物）
    ///   · ZoneRoute 一句话（地面分段）
    ///   · EditCard / ProofCard 自己拼的一句（修改项）
    /// 每一句都只回答了三个问题里的某一个，而且谁也没说"它凭什么在这儿"——
    /// 那一层恰恰是 PRD 的核心：这一关的东西不是布景，是那条**控制链**的零件。
    ///
    /// 所以这里定死一个格式，四段：
    ///   What —— 这是什么（看得见的那件东西）
    ///   Why  —— 为什么出现在这里（接到这一章的根障碍与这一关的核心机制上）
    ///   How  —— 怎么用（玩家现在能做的那个动作）
    ///   Core —— 这一章的核心命题（chapter.core 原文，不改写）
    ///
    /// Why 与 Core 一律取自关卡表的字段（chapter.core / rootObstacle.chain /
    /// level.coreMechanic / level.realityToAdversity），不是我另编的说辞——
    /// "和产品方案核心思想一致"这条要求，只有直接引用才守得住。
    ///
    /// 【长度是硬约束】
    /// 卡片摆在屏幕左侧 620×250 的一块地方（上边是心法行，下边是虚拟摇杆，
    /// 中间只有这么宽）。每段超过 <see cref="MaxPart"/> 个字就会被挤出去，
    /// 所以 CI 会逐条量（见 CIDiagnostics「说明卡三段体检」）。
    /// </summary>
    public struct CodexEntry
    {
        public string title;
        public string what;
        public string why;
        public string how;
        public string core;

        public bool Valid => !string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(what)
                          && !string.IsNullOrEmpty(why) && !string.IsNullOrEmpty(how);
    }

    public static class SignCodex
    {
        /// <summary>每一段的字数上限。卡片宽 620、字号 18，一行约装 34 个汉字，两行封顶。</summary>
        public const int MaxPart = 68;

        /// <summary>
        /// 截到卡片装得下为止。
        ///
        /// 关卡表里有些栏目是**写给文档看的**（"……；DeathType=Interruption"），
        /// 把它原样拼进说明卡，最长的一条有 83 字；chapter.core 最长的一条也有 69 字。
        /// 卡片是定高的，多出来的不会换页，会被截成半句话。
        /// 宁可自己截并且显式加省略号，也不要让玩家看见一句没头没尾的话。
        /// </summary>
        public static string Clip(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
            return text.Substring(0, Mathf.Max(1, max - 1)) + "…";
        }

        /// <summary>这一章的核心命题原文（PRD chapter.core）。</summary>
        public static string CoreOf(InternalLevelData lv)
        {
            var ch = lv != null ? InternalChapterCatalog.Chapter(lv.chapterId) : null;
            return ch != null && !string.IsNullOrEmpty(ch.core) ? ch.core : "";
        }

        /// <summary>这一章的根障碍控制链（PRD rootObstacles 表的 chain 栏）。</summary>
        public static string ChainOf(InternalLevelData lv)
        {
            var ch = lv != null ? InternalChapterCatalog.Chapter(lv.chapterId) : null;
            if (ch == null) return "";
            var ro = InternalChapterCatalog.RootObstacle(ch.rootObstacle);
            return ro != null ? ro.chain : "";
        }

        /// <summary>这一章的根障碍名（"失败被解释为身份威胁"这一类）。</summary>
        public static string ObstacleOf(InternalLevelData lv)
        {
            var ch = lv != null ? InternalChapterCatalog.Chapter(lv.chapterId) : null;
            if (ch == null) return "";
            var ro = InternalChapterCatalog.RootObstacle(ch.rootObstacle);
            return ro != null ? ro.name : "";
        }

        static CodexEntry Make(InternalLevelData lv, string title,
            string what, string why, string how)
        {
            return new CodexEntry
            {
                title = title, what = what, why = why, how = how,
                core = Clip(CoreOf(lv), MaxPart)
            };
        }

        // ==================== 关键物 ====================

        /// <summary>
        /// 一件关键物的三段说明。
        ///
        /// Why 这一栏全部落在同一句话上：**它是那条控制链上的一个可操作点**。
        /// 这一批关卡的设计前提就是这个（PRD 第 11.3 节：心理机制要通过机关表达），
        /// 所以每件东西的"为什么在这儿"都该说得出它卡在链条的哪一环。
        /// </summary>
        public static CodexEntry ForProp(InternalPropKind kind, InternalLevelData lv, bool replay)
        {
            string obstacle = ObstacleOf(lv);
            string title, what, why, how;

            switch (kind)
            {
                case InternalPropKind.GateConsole:
                    title = "交付台";
                    what = "这一关唯一能按下「做完了」的地方。";
                    why = "本章的根障碍是「" + obstacle + "」：最后一步迟迟不发生。所以它必须是一个动作，不是一种感觉。";
                    how = "条件够了就按【用】/ R。没够会告诉你还差什么。";
                    break;
                case InternalPropKind.DoneLock:
                    title = "完成标准锁";
                    what = "把「做到什么算完」钉死在这儿的一把锁。";
                    why = "标准会随着你看它的时间往上漂。锁住它，交付才有一个不动的靶子。";
                    how = "按【用】/ R 锁定。锁过之后，标准不再往上抬。";
                    break;
                case InternalPropKind.Checkpoint:
                    title = "检查点";
                    what = "记下你走到这儿了的一个点。";
                    why = "「全或无」把一次失手读成全盘失败。有它，失手只毁掉这一段。";
                    how = "路过按【用】/ R 记一下。之后跌倒从这儿继续。";
                    break;
                case InternalPropKind.DisengageGate:
                    title = "脱离门";
                    what = "合法退出这一关的门。";
                    why = "撤退不是失败，但没有重返条件的撤退就是拖延。这道门要你把条件说出来。";
                    how = "按【用】/ R 撤离，并给出「什么时候回来」。";
                    break;
                case InternalPropKind.Evidence:
                    title = "证据台";
                    what = "把「我以为会怎样」和「实际怎样」并排摆出来的台子。";
                    why = "不尝试就没有证据，没有证据只好继续用恐惧当结论。";
                    how = "按【用】/ R 把这一次的实际结果记上去。";
                    break;
                case InternalPropKind.DecisionBoard:
                    title = "判据板";
                    what = "先写下「按什么选」的一块板。";
                    why = "判据没定就选，选完一定反悔——反悔又变成新一轮分析。";
                    how = "按【用】/ R 定下判据，再去做选择。";
                    break;
                case InternalPropKind.TriggerObject:
                    title = "触发物";
                    what = "那条自动行为链的起点。";
                    why = "控制链是「" + ChainOf(lv) + "」。看清起点，你才有得选。";
                    how = "按【用】/ R 认下它。认过之后它就不再是背景。";
                    break;
                case InternalPropKind.ArchiveVault:
                    title = "归档柜";
                    what = "把「还能更好」收起来的柜子。";
                    why = "必要的事做完之后，剩下的不是没处理，是到此为止。";
                    how = "按【用】/ R 归档。归档不等于放弃，等于结束。";
                    break;
                case InternalPropKind.Decoy:
                    title = "诱饵";
                    what = "一件看起来也该做的事。";
                    why = "它不推进这一关，只把时间花掉——这正是本章要你认出来的东西。";
                    how = "可以按【用】/ R 做它。做完你会看到进度没动。";
                    break;
                case InternalPropKind.Cargo:
                    title = "目标箱";
                    what = "今天真正要交出去的那一箱。";
                    why = "堆着的旧箱会遮住视线。一次只锁定一个目标，其余的不归今天管。";
                    how = "按【用】/ R 带上它，再送到装车月台。";
                    break;
                default:
                    title = "工作台";
                    what = "在这儿做出第一版的地方。";
                    why = "本章的核心是让现实检验发生。没有第一版，就没有东西可被检验。";
                    how = "按【用】/ R 做出第一版。粗糙没关系，有就行。";
                    break;
            }

            if (replay)
            {
                // 回访：同一件东西，说的不该还是"你要做什么"
                how = kind == InternalPropKind.GateConsole
                    ? "按【用】/ R 交现实回执：现实里做到没有、换个场合又做到没有。"
                    : "这一件你上一趟做过了，这次不用再做。";
                why = kind == InternalPropKind.GateConsole
                    ? "游戏里的胜利只是排练。Mastery 靠现实与迁移两层往上走。"
                    : why;
            }
            return Make(lv, title, what, why, how);
        }

        // ==================== 地面分段 ====================

        /// <summary>一条带的三段说明。index 与 InternalLayout.ZoneNames 对齐。</summary>
        public static CodexEntry ForZone(int index, InternalLevelData lv, bool replay)
        {
            string name = index >= 0 && index < InternalLayout.ZoneNames.Length
                ? InternalLayout.ZoneNames[index] : "分段";
            string what, why, how;

            switch (index)
            {
                case 0:
                    what = "一块特意空出来的地，没有家具。";
                    why = "敌人是你自己内部的阻力。它们拦路，但这一关不靠清怪通关。";
                    how = "在这儿打开一条路。打不打得过，和过不过关是两回事。";
                    break;
                case 1:
                    what = "这一关第一件要动手的东西所在的一段。";
                    why = "先有东西，再谈它够不够好——顺序反过来，这一关就永远开不了头。";
                    how = "走到那件东西跟前，按【用】/ R。";
                    break;
                case 2:
                    what = "要读的东西分两排立在过道两侧。";
                    why = "本关要练的是分辨：哪些真的挡住交付，哪些只是「还能更好」。";
                    how = "走近读内容，读完自己决定改不改。读不等于做。";
                    break;
                case 3:
                    what = "余下要按的关键物排在这一段。";
                    why = "读出来的判断，要在这里变成一个真的按下去的动作才算数。";
                    how = "沿路依次走过去，按【用】/ R。";
                    break;
                default:
                    what = "这一关算完成的那个点。";
                    why = "交付是一个动作，不是一种「觉得可以了」的状态。";
                    how = "条件够了按【用】/ R 交付，然后走到出口离开。";
                    break;
            }

            if (replay)
            {
                how = "这一趟不用再做一遍。往前走到交付台交回执就行。";
                why = "你已经在游戏里做到过了。还没记上的是现实那一层。";
            }
            return Make(lv, "【" + name + "】", what, why, how);
        }

        // ==================== 修改项卡 ====================

        /// <summary>一张修改项阅读台。text 是这一条的具体内容。</summary>
        public static CodexEntry ForCard(InternalLevelData lv, string label, string text)
        {
            return Make(lv, "〔" + label + "〕",
                string.IsNullOrEmpty(text) ? "一处可以改的地方。" : Clip(text, MaxPart),
                "它们长得一模一样：分得出轻重要靠读，不靠标签——这正是本关要练的。",
                "读完再决定：要改按【用】/ R，不改就走开。");
        }

        // ==================== 入口 / 出口 / 路线牌 ====================

        /// <summary>
        /// 经典章节 / AI 生成章节里的普通牌子（房间名、机制物件）。
        ///
        /// 这一类没有关卡表可引，但玩家的要求是"**所有**带有文字牌子"——
        /// 一块只写着名字的牌子，和一块没有字的板子对玩家是一回事。
        /// 所以照样给三段：名字说是什么，原有的那句 purpose 说为什么在这儿，
        /// 怎么用则如实说明它是不是可操作的。**不编**它没有的功能。
        /// </summary>
        public static CodexEntry ForPlainSign(string title, string explain, bool interactive)
        {
            return new CodexEntry
            {
                title = string.IsNullOrEmpty(title) ? "牌子" : title,
                what = "一块写着「" + (string.IsNullOrEmpty(title) ? "…" : title) + "」的牌子。",
                why = string.IsNullOrEmpty(explain) ? "它标出这一块地方是做什么用的。" : explain,
                how = interactive
                    ? "走到跟前按【用】/ R 使用它。"
                    : "它只用来认路，没有可操作的东西——记住方向就行。",
                core = "",
            };
        }

        public static CodexEntry ForEntrance(InternalLevelData lv, string siteName)
        {
            return Make(lv, "◀ 入口",
                "你进来的那扇门，也是原路退回城里的门。",
                "撤退是合法结局，但走这一头不算通关——这一关的事没有做完。",
                "想离开就从这儿走。要通关，得走到另一头的出口。");
        }

        public static CodexEntry ForExit(InternalLevelData lv)
        {
            return Make(lv, "▲ 出口",
                "这一关的终点门。",
                "结构和经典关卡一致：入口进、必经区做完事、出口走出去。",
                "先交付，再走出这扇门，这一关才结束。");
        }

        public static CodexEntry ForRouteBoard(InternalLevelData lv, bool replay)
        {
            string how = replay
                ? "这一趟不必重做。走到交付台交回执，或直接离开。"
                : "顺着主轴往前走，一段做一件事，走到头就走完了。";
            return Make(lv, "【路线牌】",
                "全场唯一一块说明牌，刻着这一关的走法。",
                "地上的颜色线把这块地分成几段，顺序就是这一关的做事顺序。",
                how);
        }
    }
}
