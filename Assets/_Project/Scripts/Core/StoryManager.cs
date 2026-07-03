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
                victory = "街道依旧喧嚣，但那些声音再也钻不进你的心里。\n城市广场上，最后的影子在等你——那是你所有的拖延凝成的形状。"
            },
            new ChapterInfo
            {
                zoneIndex = 3, enemyType = EnemyType.ProcrastinationShadow, enemyTier = EnemyTier.Chief,
                enemyId = "boss_procrastination_shadow",
                title = "终章 · 城市广场",
                intro = "华灯初上的广场中央，站着一个巨大的影子。\n它有你的轮廓——那是无数个「明天再说」堆成的旧我。\n\n击败【首领·拖延影魔】，把现在夺回来。",
                victory = "影子碎裂成漫天光点。\n你没有变成另一个人——你只是终于成为了自己。\n\n【主线完结】自由修炼模式已开启：\n可用「敌人+」在任意区域添加不同类型与难度的心魔挑战。"
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

        /// <summary>区域是否解锁：已通关章节的区域 + 当前章节区域。</summary>
        public bool ZoneUnlocked(int zoneIndex) =>
            AllCleared || zoneIndex <= Chapters[Chapter].zoneIndex;

        /// <summary>重置主线（调试/新周目用）。</summary>
        public void ResetStory()
        {
            Chapter = 0;
            PlayerPrefs.SetInt(SaveKey, 0);
            PlayerPrefs.Save();
        }
    }
}
