using AdversityRoad.Combat;

namespace AdversityRoad.AI
{
    /// <summary>一串连招：名字 + 逐段的招式。每一段的前摇时长由 TelegraphTable 按族给定。</summary>
    public struct AttackString
    {
        public string name;
        public PoseState[] stages;
    }

    /// <summary>
    /// 按武学流派给敌人配招串。
    ///
    /// 【为什么要有这个文件】MartialArchetype 的九个流派在枚举注释里写得很清楚
    /// （拳法型「刺拳、摆拳、肘、短连击」、腿法型「低扫、侧踢、回旋、距离控制」……），
    /// 但它此前**只被用来决定要不要显示武器**——每个敌人，无论拳法还是重武器，
    /// 都从同一个 BasicMoves/EliteMoves 里随机抓一招。
    /// 于是"敌人招式单一、变化太少"是字面属实的：十招一个随机池，流派形同虚设。
    ///
    /// 【设计取向：变化来自招串，规律来自招式族】
    /// 这是一款考验判断力的游戏，所以变化和规律必须同时成立，不能靠随机凑：
    ///   · 变化 —— 每个流派有自己的几串连招，出手时整串打完，节奏各不相同；
    ///   · 规律 —— 每一段仍然属于六个招式族之一，而**同族的前摇时长全场恒定**
    ///     （劈 0.78s / 横 0.70s / 刺 0.62s / 扫 0.66s / 踢 0.58s / 旋 0.82s）。
    /// 玩家要学的是"看身体认族、按族决定怎么躲"，而不是背下每个敌人的随机数。
    /// 招串让同一个族以不同顺序、不同长度反复出现，这正是练判断力的材料。
    ///
    /// 【六个族都必须有人用】扫腿族（唯一"挡不住、只能跳"的答案）此前没有任何
    /// 敌人会用——一条玩家永远学不到的规律等于不存在。腿法/棍术/重武器里都排了它。
    /// </summary>
    public static class EnemyMoveSet
    {
        static AttackString S(string name, params PoseState[] stages) =>
            new AttackString { name = name, stages = stages };

        // ---- 拳法型：短、快、密；答案基本是格挡，贴身时侧移 ----
        static readonly AttackString[] Fist =
        {
            S("刺拳·二连",   PoseState.PunchJab, PoseState.PunchCross),
            S("刺拳·三连",   PoseState.PunchJab, PoseState.PunchJab, PoseState.PunchCross),
            S("下潜·重拳",   PoseState.Sweep, PoseState.PunchCross),
            S("摆拳·正踢",   PoseState.PunchCross, PoseState.AttackKick),
        };
        static readonly AttackString[] FistElite =
        {
            S("连打·收招回旋", PoseState.PunchJab, PoseState.PunchCross, PoseState.SpinKick),
            S("突进·贴身连",   PoseState.JumpKick, PoseState.PunchJab, PoseState.PunchCross),
        };

        // ---- 腿法型：距离控制；扫腿与飞踢是它的招牌，答案是跳与拉开 ----
        static readonly AttackString[] Leg =
        {
            S("低扫·起腿",   PoseState.Sweep, PoseState.AttackKick),
            S("侧踢·二连",   PoseState.SideKick, PoseState.SideKick),
            S("正踢·回旋",   PoseState.AttackKick, PoseState.SpinKick),
        };
        static readonly AttackString[] LegElite =
        {
            S("扫·踢·旋 三段", PoseState.Sweep, PoseState.SideKick, PoseState.SpinKick),
            S("飞踢·追击",     PoseState.JumpKick, PoseState.AttackKick),
        };

        // ---- 刀术型：快斩与突刺交替，节奏最碎，考验精准格挡 ----
        static readonly AttackString[] Blade =
        {
            S("横斩·二连",   PoseState.Attack, PoseState.Attack),
            S("横斩·撩斩",   PoseState.Attack, PoseState.AttackUp),
            S("突刺",        PoseState.SwordThrust),
            S("撩斩·突刺",   PoseState.AttackUp, PoseState.SwordThrust),
        };
        static readonly AttackString[] BladeElite =
        {
            S("横·横·劈 三段", PoseState.Attack, PoseState.Attack, PoseState.HeavyAttack),
            S("突刺·回旋斩",   PoseState.SwordThrust, PoseState.AttackSpin),
        };

        // ---- 棍术型：大范围横扫与点刺，站位不对就一定吃到 ----
        static readonly AttackString[] Staff =
        {
            S("横扫·点刺",   PoseState.Attack, PoseState.SwordThrust),
            S("扫腿·横扫",   PoseState.Sweep, PoseState.Attack),
            S("回旋·横扫",   PoseState.AttackSpin, PoseState.Attack),
        };
        static readonly AttackString[] StaffElite =
        {
            S("旋·扫·刺 三段", PoseState.AttackSpin, PoseState.Sweep, PoseState.SwordThrust),
        };

        // ---- 重武器型：慢起手、高破防；给的是耐心与反击窗口 ----
        static readonly AttackString[] Heavy =
        {
            S("劈斩",        PoseState.HeavyAttack),
            S("横斩·劈斩",   PoseState.Attack, PoseState.HeavyAttack),
            S("扫腿·劈斩",   PoseState.Sweep, PoseState.HeavyAttack),
        };
        static readonly AttackString[] HeavyElite =
        {
            S("劈·旋·劈 三段", PoseState.HeavyAttack, PoseState.AttackSpin, PoseState.HeavyAttack),
        };

        // ---- 刺客型：突进切入，起手最短，答案偏向闪避 ----
        static readonly AttackString[] Assassin =
        {
            S("突进·突刺",   PoseState.JumpKick, PoseState.SwordThrust),
            S("突刺·二连",   PoseState.SwordThrust, PoseState.SwordThrust),
            S("撩斩·侧踢",   PoseState.AttackUp, PoseState.SideKick),
        };
        static readonly AttackString[] AssassinElite =
        {
            S("刺·刺·旋 三段", PoseState.SwordThrust, PoseState.SwordThrust, PoseState.AttackSpin),
        };

        // ---- 防反型：单发为主、间隔长——它要的是你先出手 ----
        static readonly AttackString[] Counter =
        {
            S("横斩",        PoseState.Attack),
            S("突刺",        PoseState.SwordThrust),
            S("正踢",        PoseState.AttackKick),
        };
        static readonly AttackString[] CounterElite =
        {
            S("横斩·劈斩",   PoseState.Attack, PoseState.HeavyAttack),
        };

        /// <summary>这个流派的招串表。elite=精英/首领那一档（更长、更凶的串）。</summary>
        public static AttackString[] For(MartialArchetype a, bool elite)
        {
            switch (a)
            {
                case MartialArchetype.Fist:     return elite ? FistElite : Fist;
                case MartialArchetype.Leg:      return elite ? LegElite : Leg;
                case MartialArchetype.Blade:    return elite ? BladeElite : Blade;
                case MartialArchetype.Staff:    return elite ? StaffElite : Staff;
                case MartialArchetype.Heavy:    return elite ? HeavyElite : Heavy;
                case MartialArchetype.Assassin: return elite ? AssassinElite : Assassin;
                case MartialArchetype.Counter:  return elite ? CounterElite : Counter;
                // 擒拿/协同暂无专属动作素材，走拳法型（短连击）——
                // 宁可共用一套说得清的招串，也不要退回"十招随机抓一招"。
                default:                        return elite ? FistElite : Fist;
            }
        }

        /// <summary>全部流派（CI 逐个核对用）。</summary>
        public static readonly MartialArchetype[] AllArchetypes =
        {
            MartialArchetype.Fist, MartialArchetype.Leg, MartialArchetype.Grapple,
            MartialArchetype.Blade, MartialArchetype.Staff, MartialArchetype.Heavy,
            MartialArchetype.Assassin, MartialArchetype.Counter, MartialArchetype.Coop,
        };
    }
}
