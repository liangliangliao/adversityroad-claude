using UnityEngine;
using AdversityRoad.AI;

namespace AdversityRoad.Core
{
    [System.Serializable]
    public class ChapterInfo
    {
        public int zoneIndex;
        public EnemyType enemyType;
        public EnemyTier enemyTier;
        public string enemyId;
        public string title;
        public string intro;
        public string victory;
        /// <summary>Boss 出生点覆盖（相对区域原点）。零向量 = 使用区域默认敌人出生点。</summary>
        public UnityEngine.Vector3 spawnOffset;
    }

    /// <summary>
    /// 章节剧情管理：独居小屋 → 训练武馆 → 噪声街区 → 城市广场 的逆袭主线。
    /// 击败当前章节的心魔即推进章节并解锁下一区域。进度本地持久化，死亡重开不丢。
    /// </summary>
    public class StoryManager : MonoBehaviour
    {
        public static StoryManager Instance { get; private set; }

        const string SaveKey = "adversity_chapter";

        public static readonly ChapterInfo[] Chapters =
        {
            new ChapterInfo
            {
                zoneIndex = 0, enemyType = EnemyType.SelfDoubtWhisper, enemyTier = EnemyTier.Novice,
                enemyId = "enemy_selfdoubt_whisper",
                title = "第一章 · 独居小屋",
                intro = "深夜，独居的房间。桌上的计划落满灰尘，角落里传来熟悉的低语：\n「你真的觉得自己可以？」\n\n这一次，你决定不再躺回床上。\n击败【见习·自我怀疑低语】，推开那扇门。",
                victory = "低语散去，房间安静下来。\n你推开门，门外是清晨的街道。\n下一站：训练武馆——把决心练成实力。"
            },
            new ChapterInfo
            {
                zoneIndex = 1, enemyType = EnemyType.TomorrowPhantom, enemyTier = EnemyTier.Standard,
                enemyId = "enemy_tomorrow_phantom",
                title = "第二章 · 训练武馆",
                intro = "武馆的木地板吱呀作响。\n一个熟悉的身影挡在训练柱之间——它总是劝你「明天再来」。\n\n击败【标准·明日幻影】，证明你今天就能开始。",
                victory = "幻影消散。你的拳脚第一次有了重量。\n武馆外，噪声街区的喧嚣正在等你。"
            },
            new ChapterInfo
            {
                zoneIndex = 2, enemyType = EnemyType.CoughAssassin, enemyTier = EnemyTier.Elite,
                enemyId = "enemy_cough_assassin",
                title = "第三章 · 噪声街区",
                intro = "行人、车辆、议论声、咳嗽声……\n每一个声音都在拉扯你的注意力。\n\n在干扰中保持专注，击败【精英·咳声刺客】。\n提示：专注值被打空时锁定会失灵，用「定心格挡」反制心理攻击。",
                victory = "街道依旧喧嚣，但那些声音再也钻不进你的心里。\n街区尽头是一片荒原——漫天飘着无人回复的简历。"
            },
            new ChapterInfo
            {
                zoneIndex = 3, enemyType = EnemyType.NoReplyKing, enemyTier = EnemyTier.Chief,
                enemyId = "boss_no_reply_king",
                title = "第四章 · 求职荒原",
                intro = "荒原上漫天飘着简历纸片，五扇面试之门紧闭，只有一扇透着光。\n审判台上坐着一个不说话的王——它的武器是沉默，它的刀刃是拒信。\n\n击败【首领·无回应之王】，夺回「下一次投递」的勇气。\n提示：它会远程掷出拒信飞刃，靠近它、别停下脚步。",
                victory = "王座崩塌，沉默被打破。\n没有回应不代表没有价值——你还能再投一次。\n最后一站：城市广场，你所有的拖延凝成的旧我在等你。"
            },
            new ChapterInfo
            {
                zoneIndex = 4, enemyType = EnemyType.ProcrastinationShadow, enemyTier = EnemyTier.Chief,
                enemyId = "boss_procrastination_shadow",
                title = "第五章 · 城市广场",
                intro = "华灯初上的广场中央，站着一个巨大的影子。\n它有你的轮廓——那是无数个「明天再说」堆成的旧我。\n\n击败【首领·拖延影魔】，把现在夺回来。",
                victory = "影子碎裂成漫天光点。\n你没有变成另一个人——你只是终于成为了自己。\n\n可广场东侧的法院还亮着灯——那里，有人正把不属于你的责任，一件件推到你身上。\n最后一站：责任转嫁法院。"
            },
            new ChapterInfo
            {
                zoneIndex = 5, enemyType = EnemyType.TotalResponsibilityJudge, enemyTier = EnemyTier.Chief,
                enemyId = "boss_total_responsibility_judge",
                title = "第六章 · 责任转嫁法院",
                intro = "高大的法院里，责任天平永远倾向你这一侧。\n审判席上的【全责法官】不停宣判：「这也是你的责任。」\n它会把一个个「责任球」抛向你——红色的不属于你，绿色的才是你的本分。\n\n提示：举起边界盾（格挡）把红球挡回去，就是「责任归还」；绿球别推开，接下它。\n击败【首领·全责法官】，学会准确承担属于自己的部分。",
                victory = "责任天平终于回正。\n真正的负责不是什么都背，而是准确承担属于自己的那部分——多一分是内耗，少一分是逃避。\n\n可法院深处还有一间更小、更歪的审判庭——\n那里审判的不是责任，而是你的感受本身。\n下一站：小题大做审判庭。"
            },
            new ChapterInfo
            {
                zoneIndex = 6, enemyType = EnemyType.SelfDenialGavel, enemyTier = EnemyTier.Chief,
                enemyId = "boss_self_denial_gavel",
                title = "第七章 · 小题大做审判庭",
                intro = "倾斜的法官席上悬着一柄巨大的法槌。\n墙上漂浮着一个个标签：「太敏感」「小题大做」「你也有问题」。\n每一次你想说出事实，就有人敲响法槌：「这么小的事，你也要说？」\n\n提示：先去证据桌前看清事实——此后浮动标签一击即碎；\n击碎标签回补自尊，破碎的镜子能让你看清被扭曲的倒影。\n击败【首领·自我否定法槌】，守住自己的判断。",
                victory = "法槌落地，标签散尽。\n我可以感受强烈，但这不等于没有理由——事实先于评价。\n\n可就在这时，远方传来被放大十倍的咳嗽声——\n熟悉的噪声街区出事了。\n下一站：回到噪声街区的街心广场（提示：打开「传送」面板可直达已解锁区域）。"
            },
            new ChapterInfo
            {
                zoneIndex = 2, enemyType = EnemyType.StimulusAmplifier, enemyTier = EnemyTier.Chief,
                enemyId = "boss_stimulus_amplifier",
                title = "第八章 · 噪声放大",
                spawnOffset = new UnityEngine.Vector3(0, 1.1f, 9),
                intro = "熟悉的街道，陌生的震动。\n有什么东西把每一声咳嗽、每一次转头、每一句低语都放大了十倍——\n街心广场上，【刺激放大器】正在把整条街变成针对你的证据。\n\n提示：它会制造酷似威胁的幻影——攻击幻影只会消耗你自己；\n用「不读心盾」（键5/盾）让幻影显形，用「注意力回收」（键6/收）清场后猛攻真身。\n回到噪声街区，击败【首领·刺激放大器】。",
                victory = "放大器碎裂，街道恢复成普通的街道。\n咳嗽只是咳嗽，眼神只是眼神——你不需要证明每个刺激是不是针对你，\n你只需要先把注意力拿回来。\n\n街道尽头东南侧，一条湿地小径已经打开——\n下一站：拖延沼泽（也可用「传送」面板直达）。"
            },
            new ChapterInfo
            {
                zoneIndex = 7, enemyType = EnemyType.TomorrowKing, enemyTier = EnemyTier.Chief,
                enemyId = "boss_tomorrow_king",
                title = "第九章 · 拖延沼泽",
                intro = "泥沼中漂着床垫、手机屏幕和写满计划的纸。\n巨大的泥台上坐着【明天之王】——它的泥壳由无数个「明天再说」凝成，寻常刀剑打不动。\n\n提示：泥壳只有一个弱点——已经开始的行动。\n点燃场边三座「五分钟火种台」（走近即点燃），火种齐燃时泥壳崩裂；\n陷进深泥或被冻住时，按「五分钟火种」（键4/火）脱身。\n击败【首领·明天之王】，把今天夺回来。",
                victory = "泥壳崩裂，王座沉入沼底。\n不是等有动力才行动，而是行动召回动力——五分钟就够点燃开始。\n\n沼泽尽头立着一座安静的建筑：旧事回声馆。\n那里陈列着你所有的过去——最后一战，不是杀死谁，而是整合谁。"
            },
            new ChapterInfo
            {
                zoneIndex = 8, enemyType = EnemyType.OldSelf, enemyTier = EnemyTier.Chief,
                enemyId = "boss_old_self",
                title = "终章 · 旧事回声馆",
                intro = "博物馆里陈列着你的失败记录、旧标签和未说出口的话。\n靠近展柜，旧事就开始循环播放——站定完成「归档」，让它们安静下来（归档 3 座开启终局大门）。\n\n镜面平台中央站着【旧我】。它有你的轮廓——那是过去为了保护你而形成的旧模式。\n它会复读旧话、冻结你的脚步、召回你打败过的旧敌。\n\n记住：旧我不能也不需要被杀死。\n打到它停手时，走进整合圆环，完成「旧我整合式」。",
                victory = "旧我化为影子护卫，站到了你身后。\n「你曾经保护过我，但现在我要继续往前。」\n\n过去发生过，但不是你的全部；失败是事实，不是身份。\n\n【主线完结】自由修炼模式已开启：\n可用「敌人+」在任意区域添加不同类型与难度的心魔挑战；\n安全屋各面板（复盘/成长/装备/图鉴/档案）持续可用。",
                spawnOffset = new UnityEngine.Vector3(0, 1.1f, 30)
            }
        };

        public int Chapter { get; private set; }

        public bool AllCleared => Chapter >= Chapters.Length;

        public ChapterInfo Current =>
            Chapter < Chapters.Length ? Chapters[Chapter] : null;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Chapter = PlayerPrefs.GetInt(SaveKey, 0);
        }

        void OnEnable() => GameEvents.OnEnemyKilled += HandleEnemyKilled;
        void OnDisable() => GameEvents.OnEnemyKilled -= HandleEnemyKilled;

        void HandleEnemyKilled(string enemyId)
        {
            var cur = Current;
            if (cur == null || enemyId != cur.enemyId) return;
            Chapter++;
            PlayerPrefs.SetInt(SaveKey, Chapter);
            PlayerPrefs.Save();
            GameEvents.RaiseChapterAdvanced(Chapter);
        }

        /// <summary>
        /// 区域是否解锁：主线到过的区域集合（章节顺序不再单调对应区域顺序——
        /// 例如第八章回到噪声街区打刺激放大器）。安全屋（0 区）永远可回。
        /// </summary>
        public bool ZoneUnlocked(int zoneIndex)
        {
            if (AllCleared || zoneIndex == 0) return true;
            for (int i = 0; i <= Chapter && i < Chapters.Length; i++)
                if (Chapters[i].zoneIndex == zoneIndex) return true;
            return false;
        }

        /// <summary>重置主线（调试/新周目用）。</summary>
        public void ResetStory()
        {
            Chapter = 0;
            PlayerPrefs.SetInt(SaveKey, 0);
            PlayerPrefs.Save();
        }
    }
}
