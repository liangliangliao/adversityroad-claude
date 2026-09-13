using System;
using System.Collections.Generic;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// World Kit（V2.2 第 4 节）：14 套现实底座。
    ///
    /// 90 关不是 90 张孤立地图——它们复用同一批现实场景，心理层只做覆盖。
    /// 这一条既是美术预算，也是设计要求：玩家必须认得出"刚才那场逆境就发生在这里"。
    /// </summary>
    [Serializable]
    public class WorldKitInfo
    {
        public string id;        // WK01..WK14
        public string kit;       // PlayerHome / Apartment / ...
        public string area;      // 主要现实区域
        public string usage;     // 用途
    }

    /// <summary>七类根障碍（V2.2 第 7.1 节）。章节不是课程表，而是挂在根障碍上的模块。</summary>
    [Serializable]
    public class RootObstacleInfo
    {
        public string code;      // A..G
        public string name;
        public string chain;     // 典型控制链
        public string chapters;  // 主要章节
    }

    /// <summary>
    /// DeathType 语义：Boss 怎样才算"失效"，绝大多数不是清空血条。
    ///
    /// 附录 B 收录了 15 个；第 6 节的 Boss 总表另外用到 5 个
    /// （Reconnection / Interruption / Replacement / AutomationShift / RecoveryConfidence），
    /// source 标的就是这一条来自哪里——两处都是 PRD 正文，不是我们自己加的。
    /// </summary>
    [Serializable]
    public class DeathTypeInfo
    {
        public string id;
        public string meaning;
        public string source;
    }

    /// <summary>Mastery 等级 M0-M5（第 8.2 节）。</summary>
    [Serializable]
    public class MasteryLevelInfo
    {
        public string level;
        public string rule;
    }

    /// <summary>
    /// 关卡里的一个内部敌人单位。
    ///
    /// 【为什么这里没有"外部敌人"字段】
    /// 第 9-26 章的 90 关，敌人全部是玩家自身机制的投影：白纸哨兵、校稿幽灵、
    /// 链断者、反例书记员……它们不是路人、不是同事、不是第三方。这不是叙事口味，
    /// 而是这一批章节的定义——外部敌人线在 V1/V2.0 的七大主题里已经讲完了，
    /// 本增补讲的是"挡住自己的是自己"。所以 category 恒为 Internal，
    /// 校验器（<see cref="InternalChapterCatalog.Validate"/>）会把任何非 Internal 的单位判为错误。
    /// </summary>
    [Serializable]
    public class InternalUnitSpec
    {
        public string unitId;
        public string displayName;
        public int count = 1;
        /// <summary>是否可被击杀。大量单位（讨好回声式的投影）不可击杀，只能靠改变行为让它失去作用。</summary>
        public bool killable = true;
        /// <summary>恒为 "Internal"。保留成字段是为了让校验器能在数据层就报出来，而不是等运行时。</summary>
        public string category = "Internal";
        /// <summary>SPN_CH09_L1_Internal_01 —— 第 4.1 节的 Spawn Socket 命名契约。</summary>
        public string spawnSocketId;
    }

    /// <summary>
    /// 一关的灰盒蓝图（第 10.1 节 LevelGreyboxData）。
    ///
    /// 字段全部来自 PRD 的关卡表，一栏一字段：能让美术按它搭 Blockout，
    /// 也能让校验器检查"这一关到底有没有物理机制、有没有 Reality Victory"。
    /// </summary>
    [Serializable]
    public class InternalLevelData
    {
        public string levelId;          // "9-1"
        public string chapterId;        // "chapter09"
        public string name;
        public string tier;             // Standard / Elite / Boss
        public bool isBossLevel;

        public List<string> worldKitIds = new List<string>();
        public string baseKitLabel;
        public string districtId;       // 落地到 ChapterModuleLibrary 的开放世界区域
        /// <summary>没有固定 Base（复用上一关场景 / 开放世界多 Base / 持久世界）。</summary>
        public bool dynamicBase;

        public string greyboxScale;
        public int firstRunMinutesMin;
        public int firstRunMinutesMax;

        public string spawnPoint;
        public string mainPath;
        public string realityToAdversity;
        public string coreMechanic;

        public string enemyNote;
        public List<InternalUnitSpec> internalUnits = new List<InternalUnitSpec>();

        public List<string> triggerIds = new List<string>();
        public string triggerNote;
        public string navMeshCamera;

        /// <summary>通关 / Reality Victory 条件。空 = 这一关不可交付，校验器会拒绝。</summary>
        public string realityVictory;
        public string reusedAssets;

        /// <summary>
        /// HUD 目标行上那句**人话**：现在该去做的一件事，一行读完就知道往哪走。
        ///
        /// 【为什么不直接拿 realityVictory 顶上去】
        /// realityVictory 是写给设计文档看的验收条件，原文长这样：
        /// "把当前目标箱装上货车，车门关闭并生成Reality Evidence。"
        /// 玩家站在仓库里看到这行字，要先解析"Reality Evidence"是什么，
        /// 才知道自己该推哪个箱子——这正是玩家反馈里的"游戏规则不清楚"。
        /// 两者都留着：验收条件仍由 realityVictory 记录，玩家看的是这一栏。
        /// 留空则回落到 realityVictory，不会因为漏填而把目标行清空。
        /// </summary>
        public string playerObjective;

        /// <summary>玩家看的那句话：有 playerObjective 就用它，没有才回落到验收条件。</summary>
        public string Objective =>
            string.IsNullOrEmpty(playerObjective) ? realityVictory : playerObjective;

        public int ChapterNo
        {
            get
            {
                int dash = levelId != null ? levelId.IndexOf('-') : -1;
                int n;
                if (dash > 0 && int.TryParse(levelId.Substring(0, dash), out n)) return n;
                return 0;
            }
        }

        public int SubIndex
        {
            get
            {
                int dash = levelId != null ? levelId.IndexOf('-') : -1;
                int n;
                if (dash > 0 && int.TryParse(levelId.Substring(dash + 1), out n)) return n;
                return 0;
            }
        }

        /// <summary>这一关一共会放出多少个内部单位（不含 Boss 本体）。</summary>
        public int TotalUnitCount()
        {
            int n = 0;
            for (int i = 0; i < internalUnits.Count; i++) n += internalUnits[i].count;
            return n;
        }
    }

    /// <summary>
    /// Boss DNA（第 10.1 节）。
    ///
    /// 【这份数据存在的唯一理由：禁止"清空血条 = 通关"】
    /// 18 个 Boss 里只有极少数可以靠打倒解决。executionGate 与 deathType 才是真正的终局条件——
    /// 提交、出门、撤销裁判权、归档、重新进入、做出决定……
    /// 校验器会检查这两项非空，运行时的 Boss 逻辑必须读它，而不是读血条。
    /// </summary>
    [Serializable]
    public class InternalBossDNA
    {
        public int bossId;              // 28..45
        public string name;
        public string enemyType;        // EnemyCatalog 里对应的枚举名
        public string chapterId;
        public string levelId;

        public string bossType;         // Dependency / Execution Gate ...
        public string coreTest;
        public List<string> attackGrammar = new List<string>();
        public List<string> phases = new List<string>();
        /// <summary>假弱点：玩家最容易当成解法的那几条，打中了也不会推进。</summary>
        public List<string> fakeWeaknesses = new List<string>();
        public string coreMechanism;    // 真命门
        public string executionGate;    // 最终失效条件
        /// <summary>原文写法，可能是复合式（"AuthorityRevocation + Integration"）。</summary>
        public string deathType;
        /// <summary>拆开后的原子 DeathType。校验与判定一律走这一份。</summary>
        public List<string> deathTypeAtoms = new List<string>();
    }

    /// <summary>一个章节（第 9-26 章之一）：5 关 + 1 个 Boss。</summary>
    [Serializable]
    public class InternalChapterInfo
    {
        public string chapterId;        // chapter09..chapter26
        public int chapterNo;
        public string title;
        public string core;             // 章节核心
        public string spaceLanguage;    // 空间语言
        public string axis;             // WeaknessAxis 枚举名
        public string rootObstacle;     // A..G
        public string districtId;
        public List<InternalLevelData> levels = new List<InternalLevelData>();
        public InternalBossDNA boss = new InternalBossDNA();
    }

    /// <summary>internal_chapters_v22.json 的根对象。</summary>
    [Serializable]
    public class InternalChapterBook
    {
        public string version;
        public List<WorldKitInfo> worldKits = new List<WorldKitInfo>();
        public List<RootObstacleInfo> rootObstacles = new List<RootObstacleInfo>();
        public List<DeathTypeInfo> deathTypes = new List<DeathTypeInfo>();
        public List<MasteryLevelInfo> masteryLevels = new List<MasteryLevelInfo>();
        public List<InternalChapterInfo> chapters = new List<InternalChapterInfo>();
    }
}
