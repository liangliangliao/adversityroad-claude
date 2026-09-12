namespace AdversityRoad.Core
{
    /// <summary>
    /// 调试开关（全局静态，运行时可切换）。设置面板里有一个开关驱动它（见 SettingsPanel）。
    ///
    /// 【TankyEnemies 默认必须是 false】
    /// 这个开关把敌人受到的伤害乘以 0.1——**全工程每一个敌人，每一关**。
    /// 它原本默认是 true，注释里写着"正式发布把 TankyEnemies 设为 false 即可"，
    /// 而这一步从来没有做过。于是每一个新装的包，敌人实际血量都是设计值的十倍。
    ///
    /// 连续几轮实机反馈"敌人打不死""敌人有无限的生命"，根因就在这一行。
    /// 我此前几轮一直在第八章的 Boss 血线里找原因，方向从一开始就是错的：
    /// 那两个 Boss 确实有问题（血条不该存在，已另行修掉），但"所有敌人都打不死"
    /// 这件事和第八章无关，是这个默认值。
    ///
    /// 要调试时在设置面板里手动打开，不要再改这个默认值。
    /// </summary>
    public static class GameDebug
    {
        /// <summary>
        /// 动作库优先级：关＝主库优先（默认），开＝通用动作库（UAL）优先。
        ///
        /// 【为什么需要这个开关】主库 84 条几乎盖满了所有姿态，而映射表是"先注册的赢"，
        /// 所以 UAL 的姿态条目在默认顺序下**一条都轮不到**——加载了、每个角色的
        /// Playable 图里各占一个槽位、每帧都要过一遍，却永远播不到。
        /// 那不叫"接进去了"，那叫"接了但看不见"。
        /// 打开这个开关，两张表的先后对调，33 个动作全都能在游戏里直接看到，
        /// 也能和主库逐个对比——调动画本来就需要 A/B。
        ///
        /// 存本机：调动画要反复重启对比，每次重开都要重设一遍没有意义。
        /// </summary>
        public static bool PreferUalClips
        {
            get => UnityEngine.PlayerPrefs.GetInt("dbg_prefer_ual", 0) != 0;
            set => UnityEngine.PlayerPrefs.SetInt("dbg_prefer_ual", value ? 1 : 0);
        }

        /// <summary>
        /// 包体标记。每改一次会影响手感的东西就 +1，并显示在设置面板标题上。
        ///
        /// 【为什么要有它】"改了，但装上去没什么变化"这句话有两种可能：
        /// 改错了地方，或者装的还是上一个包。这两者要分开，否则下一轮会去修
        /// 一个根本不存在的问题——这个项目里已经发生过一次（招式表在烘焙前打的那次）。
        /// 面板上写着几号包，一眼就能排除掉后一种可能。
        /// </summary>
        public const int BuildTag = 66;

        /// <summary>
        /// 敌人强度：在调好的基准生命之上再乘一道，**玩家自己在设置面板里调**。
        ///
        /// 【为什么做成设置项而不是我改一个常量】这个数是纯手感，只有实机打过才知道
        /// 合不合适；而我这边改一个数要等一次二十多分钟的构建，一来一回就是一小时，
        /// 还只试得了一个值。做成开关之后，同一局里就能把 ×1 到 ×5 全试一遍，
        /// 定下来的那个值告诉我，我再把它设成默认。
        ///
        /// 与 TankyEnemies 的区别：那个是把伤害乘 0.1 的调试开关（十倍血、一档到底）；
        /// 这个是连续可调的生命倍率，档位之间是能玩的差别，不是能不能打死的差别。
        /// </summary>
        public static float EnemyToughness
        {
            get
            {
                float v = UnityEngine.PlayerPrefs.GetFloat("dbg_tough", 2f);
                return v < 0.1f ? 2f : v;
            }
            set => UnityEngine.PlayerPrefs.SetFloat("dbg_tough", UnityEngine.Mathf.Clamp(value, 0.25f, 12f));
        }

        /// <summary>敌人强度的可选档位（设置面板按这张表轮换）。</summary>
        public static readonly float[] ToughnessSteps = { 1f, 2f, 3f, 5f, 8f };

        /// <summary>敌人耐揍模式：大幅削减敌人受到的伤害（仅供调试，默认关闭）。</summary>
        public static bool TankyEnemies;

        /// <summary>耐揍时敌人实际承受的伤害系数（越小越耐揍）。</summary>
        public const float TankyDamageScale = 0.1f;
    }
}
