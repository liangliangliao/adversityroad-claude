using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 一套「景别」参数：镜头导演选定的机位语汇。
    /// 摄影指导与学徒的差别不在于会不会调参数，而在于**有没有景别的词汇表**，
    /// 以及知不知道此刻该用哪一种、该用多久、怎么过渡过去。
    /// </summary>
    public struct ShotProfile
    {
        public string name;
        public float distanceMult;   // 吊杆长度倍率（>1 拉远看局势，<1 推近看细节）
        public float heightBias;     // 取景点抬高（俯瞰局势 / 压低贴地增强临场）
        public float pitchBias;      // 俯仰偏置（正=略俯视，负=略仰视）
        public float fovBias;        // 视野角偏置（负=长焦压缩、更有分量；正=广角、更开阔）
        public float centerBias;     // 取景点偏向「玩家↔目标中点」的比例（双人同框）
        public float damping;        // 位置阻尼倍率（>1 更稳如三脚架，<1 更跟手）

        public static ShotProfile Lerp(ShotProfile a, ShotProfile b, float t) => new ShotProfile
        {
            name = t > 0.5f ? b.name : a.name,
            distanceMult = Mathf.Lerp(a.distanceMult, b.distanceMult, t),
            heightBias = Mathf.Lerp(a.heightBias, b.heightBias, t),
            pitchBias = Mathf.Lerp(a.pitchBias, b.pitchBias, t),
            fovBias = Mathf.Lerp(a.fovBias, b.fovBias, t),
            centerBias = Mathf.Lerp(a.centerBias, b.centerBias, t),
            damping = Mathf.Lerp(a.damping, b.damping, t),
        };
    }

    /// <summary>
    /// 镜头导演：按当前战况从景别词汇表里选镜，而不是永远用同一个机位微调。
    ///
    /// 选镜逻辑（对齐动作游戏摄影指导的判断顺序）：
    ///   · 大招/处决  → 推近特写：看清这一击的分量（最短、最低、长焦）
    ///   · 群战包围   → 拉远抬高：看清包围态势与空位（最长、略俯、广角）
    ///   · 单挑对峙   → 双人同框：玩家与对手都在画面里，近水平低机位
    ///   · 狭窄空间   → 收紧贴身：走廊/贴墙时缩短吊杆，避免顶墙塌视野
    ///   · 探索行进   → 标准跟随：中景，留出前方引导空间
    ///
    /// 关键不是"选得对"，而是**过渡**：所有景别参数都做时间常数插值，
    /// 镜头是"推轨"过去的，不是切过去的——这是"不会让人感到不适"的核心。
    /// </summary>
    public static class CameraDirector
    {
        // ---- 景别词汇表 ----
        static readonly ShotProfile Explore = new ShotProfile
        {
            name = "行进·标准中景",
            distanceMult = 1f, heightBias = 0f, pitchBias = 0f,
            fovBias = 0f, centerBias = 0f, damping = 1f
        };

        static readonly ShotProfile Duel = new ShotProfile
        {
            name = "对峙·双人同框",
            distanceMult = 1.02f, heightBias = -0.05f, pitchBias = 1f,
            fovBias = 1f, centerBias = 0.34f, damping = 1.15f
        };

        static readonly ShotProfile Crowd = new ShotProfile
        {
            // 拉远幅度 1.12→1.22：被包围时"看清有几个人、从哪来、往哪躲"压倒一切，
            // 而 12% 在 4.6m 吊杆上只有 0.55m，实测读不出"局势展开"的感觉。
            // 22%（+1.0m）配合 +0.34 抬高与 7° 俯角，才真的把包围圈纳入画面。
            // 对照：战神/鬼泣/猎天使魔女/蝙蝠侠阿卡姆在多敌围攻时都会明显后拉抬高。
            name = "群战·拉远看局势",
            distanceMult = 1.22f, heightBias = 0.34f, pitchBias = 7f,
            fovBias = 4f, centerBias = 0.12f, damping = 1.25f
        };

        /// <summary>
        /// 腾空/坠落：拉远 + 略俯，把【落点】纳入画面。
        /// 这是大作里最一致的一条自动拉远——塞尔达（滑翔伞）、原神（滑翔）、
        /// 神秘海域与刺客信条（长距离跳跃/信仰之跃）、马力欧奥德赛都会在离地后
        /// 明显后拉并压低视角。道理很实在：**空中你无法再改变落点，
        /// 唯一能做的就是看清它**；而落地点在角色下方，不俯一点就在画面外。
        /// </summary>
        static readonly ShotProfile Airborne = new ShotProfile
        {
            name = "腾空·拉远看落点",
            distanceMult = 1.20f, heightBias = 0.22f, pitchBias = 8f,
            fovBias = 2f, centerBias = 0f, damping = 1.1f
        };

        /// <summary>
        /// 潜行蹲伏：推近 + 压低，画面收紧、临场感强。
        /// 《最后生还者》《对马岛之魂》《刺客信条》《合金装备》在蹲伏潜行时都会
        /// 收紧景别——一半是戏剧（紧张感来自看不到全貌），一半是实用
        /// （角色压低后，长吊杆会把大量地面塞进画面，纯属浪费）。
        /// </summary>
        static readonly ShotProfile Stealth = new ShotProfile
        {
            name = "潜行·收紧压低",
            distanceMult = 0.86f, heightBias = -0.04f, pitchBias = -2f,
            fovBias = 1f, centerBias = 0f, damping = 1.1f
        };

        static readonly ShotProfile Tight = new ShotProfile
        {
            name = "狭窄·收紧贴身",
            distanceMult = 0.82f, heightBias = 0.06f, pitchBias = -1f,
            fovBias = 5f, centerBias = 0.2f, damping = 0.9f
        };

        static readonly ShotProfile Impact = new ShotProfile
        {
            name = "决胜·推近特写",
            distanceMult = 0.78f, heightBias = -0.12f, pitchBias = 2f,
            fovBias = -2f, centerBias = 0.42f, damping = 1.35f
        };

        /// <summary>
        /// 选镜。roomAround = 镜头四周的可用空间（米），用于识别狭窄场地；
        /// engagedEnemies = 真的在打你的敌人数（待机/巡逻的不算，路过一个不搭理你的
        /// 敌人不该把镜头切成群战）；hasTarget = 是否有交战对象；
        /// impact = 大招/处决演出中；falling = 离地下坠中；crouched = 蹲伏潜行中。
        ///
        /// 顺序即优先级，从最不容妥协的往下排：
        ///   演出 > 没地方（拉远只会顶墙）> 腾空（落点必须看见）> 群战 > 对峙 > 潜行 > 行进
        /// </summary>
        public static ShotProfile Pick(bool impact, int engagedEnemies, bool hasTarget,
                                       float roomAround, bool falling, bool crouched)
        {
            if (impact) return Impact;
            // 狭窄优先于一切拉远：地方都不够站，拉远只会把镜头顶进墙里
            if (roomAround < 2.6f) return Tight;
            // 腾空优先于群战：人在空中已经改不了落点，唯一能做的就是看清它
            if (falling) return Airborne;
            if (engagedEnemies >= 3) return Crowd;
            if (hasTarget || engagedEnemies >= 1) return Duel;
            if (crouched) return Stealth;
            return Explore;
        }

        /// <summary>景别切换的过渡速率（1/秒）：越大越快。
        /// 推近特写要果断（戏剧性），拉远与回归常态要从容（不惊扰）。</summary>
        public static float BlendRate(ShotProfile from, ShotProfile to)
        {
            if (to.name == Impact.name) return 4.5f;      // 进特写：果断
            if (from.name == Impact.name) return 2.2f;    // 出特写：从容回稳
            if (to.name == Tight.name) return 3.5f;       // 进狭窄：要快，否则已经顶墙了
            // 腾空要快进快出：跳跃往往不到一秒，1.6/s 的慢推轨等于整段跳跃都没生效；
            // 落地则要立刻收回来，否则"落地后镜头还在远处慢慢飘"
            if (to.name == Airborne.name || from.name == Airborne.name) return 4f;
            return 1.6f;                                   // 其余：缓慢推轨，几乎察觉不到
        }

        /// <summary>换景别的最短驻留（秒）：换景别是分镜级的决定，不该每秒发生一次。
        /// 群战人数在阈值上下浮动、跑过一段窄道，都会让选择反复横跳，
        /// 而每一次横跳都是一段可见的推拉。演出与腾空豁免（它们本身就是短节拍）。</summary>
        public const float Dwell = 2.5f;

        public static bool Urgent(ShotProfile to) =>
            to.name == Impact.name || to.name == Airborne.name || to.name == Tight.name;
    }
}
