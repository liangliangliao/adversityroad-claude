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
            fovBias = -1f, centerBias = 0.34f, damping = 1.15f
        };

        static readonly ShotProfile Crowd = new ShotProfile
        {
            name = "群战·拉远看局势",
            distanceMult = 1.24f, heightBias = 0.28f, pitchBias = 6f,
            fovBias = 4f, centerBias = 0.14f, damping = 1.25f
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
            fovBias = -5f, centerBias = 0.42f, damping = 1.35f
        };

        /// <summary>
        /// 选镜。roomAround = 镜头四周的可用空间（米），用于识别狭窄场地；
        /// nearbyEnemies = 近身敌人数；hasTarget = 是否有交战对象；impact = 大招/处决演出中。
        /// </summary>
        public static ShotProfile Pick(bool impact, int nearbyEnemies, bool hasTarget, float roomAround)
        {
            if (impact) return Impact;
            // 狭窄优先于群战：地方都不够站，拉远只会把镜头顶进墙里
            if (roomAround < 2.6f) return Tight;
            if (nearbyEnemies >= 3) return Crowd;
            if (hasTarget || nearbyEnemies >= 1) return Duel;
            return Explore;
        }

        /// <summary>景别切换的过渡速率（1/秒）：越大越快。
        /// 推近特写要果断（戏剧性），拉远与回归常态要从容（不惊扰）。</summary>
        public static float BlendRate(ShotProfile from, ShotProfile to)
        {
            if (to.name == Impact.name) return 4.5f;      // 进特写：果断
            if (from.name == Impact.name) return 2.2f;    // 出特写：从容回稳
            if (to.name == Tight.name) return 3.5f;       // 进狭窄：要快，否则已经顶墙了
            return 1.6f;                                   // 其余：缓慢推轨，几乎察觉不到
        }
    }
}
