using System;
using System.Collections.Generic;

namespace AdversityRoad.Goals
{
    /// <summary>
    /// Structured Chapter Blueprint（方案 5.4 / 20.2）：
    /// AI 只输出这份结构化数据，永不生成 Unity 代码、Prefab 或 3D 资产；
    /// Unity 侧只允许从已批准的模块库 / 机制库 / 敌人库里确定性组装。
    /// 这样才能同时拿到「AI 个性化」和「可测试、可复现、可验收」。
    /// </summary>
    [Serializable]
    public class GoalChapterData
    {
        public string chapterId;
        public ChapterSource source = ChapterSource.AIRequired;

        public string linkedGoalId = "";
        public string linkedMilestoneId = "";

        public string chapterName;
        public string worldDistrictId = "residential";   // 开放世界落地区域（DistrictCatalog）

        public string primaryObstacle = "";
        public string secondaryObstacle = "";

        /// <summary>外部敌人 / 内心敌人：必须是 EnemyCatalog 里已批准的类型名。</summary>
        public List<string> externalEnemies = new List<string>();
        public List<string> internalEnemies = new List<string>();
        public string bossArchetype = "";

        /// <summary>物理机制标签（探索/战斗/地形/资源/潜行）——必须至少一个，否则不可玩。</summary>
        public List<string> physicalMechanics = new List<string>();
        /// <summary>心理机制标签（心理攻击轴与实时防御）。</summary>
        public List<string> mentalMechanics = new List<string>();

        public string successCondition = "";
        public string failureConsequence = "";
        public List<string> safetyTags = new List<string>();

        /// <summary>可复现的组装种子：相同 Seed + 版本必须组装出相同关卡。</summary>
        public int assemblySeed;

        /// <summary>
        /// 场景蓝图：这个章节**自己的那个地方**长什么样。
        /// 有它，AI 章节才不是"在固定关卡里换几个敌人"，而是为这个目标现造一处场景。
        /// </summary>
        public SiteBlueprint site = new SiteBlueprint();

        /// <summary>
        /// 敌人编成（AI 给：谁 / 几个 / 放在门口还是深处 / 守点还是巡逻）。
        /// 为空时由校验器按上面三个扁平字段合成一份，老存档因此仍然能用。
        /// </summary>
        public List<ChapterEnemySpec> enemyPlan = new List<ChapterEnemySpec>();

        // ---------- 校验与评分（方案 5.5 / 5.6） ----------
        public bool validated;
        public float goalRelevance;
        public float playability;
        public float playerFit;
        public float novelty;
        public float narrativeCoherence;
        public float safetyCompliance;
        public float cqs;                      // Chapter Quality Score
        public string rejectReason = "";
        /// <summary>入库前被就近修复了哪些字段（玩家可查：这一关有多少是模型给的、多少是补的）。</summary>
        public string repairLog = "";

        // ---------- 运行时状态 ----------
        public bool cleared;
        public int attempts;
        public string clearedAtUtc = "";
        /// <summary>Legacy 章节映射到的 V1 区域索引（-1 = 非 Legacy）。</summary>
        public int legacyZoneIndex = -1;

        public string SourceLabel()
        {
            switch (source)
            {
                case ChapterSource.Legacy: return "经典章节";
                case ChapterSource.UserPreset: return "玩家预设";
                default: return "AI 专属";
            }
        }
    }
}
