using System.Collections.Generic;
using AdversityRoad.AI;
using AdversityRoad.Personalization;

namespace AdversityRoad.Goals
{
    /// <summary>开放世界区域（方案 6.2）：AI 章节只能落地到这些已批准的区域。</summary>
    public class DistrictInfo
    {
        public string id;
        public string name;
        public string environment;
        public string play;
        /// <summary>该区域在连续城区里的锚点索引（OpenWorldBuilder 提供坐标）。</summary>
        public int anchor;
    }

    /// <summary>物理机制模块（方案 5.6 第 2 条：必须有可玩的移动/探索/战斗/决策/资源/交互）。</summary>
    public class MechanicInfo
    {
        public string id;
        public string name;
        public string description;
        public bool physical;
    }

    /// <summary>
    /// 已批准模块库（方案 5.4 / 18.3）：
    /// AI 不直接生成 Unity 代码、Prefab 或 3D 资产，只能引用这里的 id。
    /// Validator 会拒绝任何引用库外 id 的蓝图——这是"AI 个性化"与"可测试性"能并存的前提。
    /// </summary>
    public static class ChapterModuleLibrary
    {
        public static readonly DistrictInfo[] Districts =
        {
            new DistrictInfo { id = "residential", name = "住宅区", anchor = 0,
                environment = "玩家住宅、公寓、楼道、小区、便利店",
                play = "生活、准备、恢复、随机社会事件" },
            new DistrictInfo { id = "commercial", name = "商业区", anchor = 1,
                environment = "商场、店铺、广场、写字楼",
                play = "人群压力、交易、任务、机会" },
            new DistrictInfo { id = "office", name = "工作区", anchor = 2,
                environment = "办公室、工厂、会议室、招聘中心",
                play = "目标专属任务、评价压力、资源/时间挑战" },
            new DistrictInfo { id = "transit", name = "交通区", anchor = 3,
                environment = "道路、公交、地铁、停车场、十字路口",
                play = "路线选择、时间压力、刺激与追逐" },
            new DistrictInfo { id = "service", name = "生活服务区", anchor = 4,
                environment = "医院、银行、餐馆、超市、公园",
                play = "资源、支持、恢复、生活事件" },
            new DistrictInfo { id = "edge", name = "边缘区", anchor = 5,
                environment = "小巷、车库、废弃建筑、荒地",
                play = "高风险遭遇、宿敌、低谷线" },
        };

        public static DistrictInfo District(string id)
        {
            foreach (var d in Districts) if (d.id == id) return d;
            return Districts[0];
        }

        public static bool IsDistrict(string id)
        {
            foreach (var d in Districts) if (d.id == id) return true;
            return false;
        }

        // ================= 机制库 =================

        public static readonly MechanicInfo[] Mechanics =
        {
            new MechanicInfo { id = "explore_branching", physical = true, name = "分叉探索",
                description = "在不断分叉的路线中选择必须做与暂缓做，错误选择增加时间债务" },
            new MechanicInfo { id = "evidence_collect", physical = true, name = "证据收集",
                description = "收集散落的事实碎片，集齐后击穿对方的护体叙事" },
            new MechanicInfo { id = "source_hunt", physical = true, name = "追踪故障源",
                description = "顺着连锁反应找到真正的源头，而不是一路救火" },
            new MechanicInfo { id = "protect_zone", physical = true, name = "保护稳定区",
                description = "守住已经跑通的部分，不让连锁故障把它一起拖垮" },
            new MechanicInfo { id = "timed_actions", physical = true, name = "限时关键动作",
                description = "有限时间内完成关键动作；低价值打磨会偷走时间" },
            new MechanicInfo { id = "resource_scavenge", physical = true, name = "资源搜集",
                description = "寻找食物、补给、情报与安全点，而不是只战斗" },
            new MechanicInfo { id = "beacon_ignite", physical = true, name = "点燃火种台",
                description = "走近点燃分散的行动灯台，齐燃时破除对方护体" },
            new MechanicInfo { id = "stealth_observe", physical = true, name = "潜行观察",
                description = "蹲伏绕行，先看清阵型再决定从哪一侧介入" },
            new MechanicInfo { id = "terrain_climb", physical = true, name = "地形攀越",
                description = "跳跃、攀爬、破障逐阶推进，限制不是全部" },
            new MechanicInfo { id = "route_cost", physical = true, name = "路线成本",
                description = "不同路线消耗不同的时间与体力，绕远可能更快" },
            new MechanicInfo { id = "escort_protect", physical = true, name = "护送保护",
                description = "在压力下保护一个会被针对的目标" },
            new MechanicInfo { id = "boss_phase", physical = true, name = "多阶段 Boss",
                description = "阶段边界改变战术，全部关键攻击带可学习 Telegraph" },

            new MechanicInfo { id = "mental_doubt", physical = false, name = "怀疑波",
                description = "「你根本做不到」——台词与攻击动作绑定的实时心理攻击" },
            new MechanicInfo { id = "mental_compare", physical = false, name = "社会比较",
                description = "拿别人的进度压你，诱导你放弃自己的节奏" },
            new MechanicInfo { id = "mental_shift_blame", physical = false, name = "责任倒置",
                description = "把对方的问题倒扣到你头上，用内疚换取默认代付" },
            new MechanicInfo { id = "mental_provoke", physical = false, name = "挑衅诱敌",
                description = "挑衅-后撤-伏击：追打变强，不接招则露出破绽" },
            new MechanicInfo { id = "mental_freeze", physical = false, name = "身份冻结",
                description = "「你就是这样的人」——把失败说成身份，冻结行动" },
            new MechanicInfo { id = "mental_noise", physical = false, name = "噪声干扰",
                description = "咳嗽、低语、凝视持续拉扯专注，但镜头不强转" },
            new MechanicInfo { id = "mental_urgency", physical = false, name = "虚假紧迫",
                description = "用「现在不做就来不及」制造错误优先级" },
        };

        public static MechanicInfo Mechanic(string id)
        {
            foreach (var m in Mechanics) if (m.id == id) return m;
            return null;
        }

        public static bool IsMechanic(string id) => Mechanic(id) != null;

        public static bool IsPhysicalMechanic(string id)
        {
            var m = Mechanic(id);
            return m != null && m.physical;
        }

        /// <summary>
        /// 按弱点轴给出两个物理机制 + 一个心理机制（供 Prompt、Fallback 与"就近修复"共用）。
        ///
        /// 模型写错机制 id 时不该整章作废：它已经说清了这一关要考验什么（弱点轴），
        /// 那就按轴取一套默认机制补上——玩到的还是这一关，只是玩法回到了已批准的那几种。
        /// </summary>
        public static void SuggestMechanics(WeaknessAxis axis,
            out string physical1, out string physical2, out string mental)
        {
            switch (axis)
            {
                case WeaknessAxis.BoundaryConflict:
                    physical1 = "protect_zone"; physical2 = "route_cost";
                    mental = "mental_shift_blame"; return;
                case WeaknessAxis.FairnessSensitivity:
                    physical1 = "evidence_collect"; physical2 = "boss_phase";
                    mental = "mental_provoke"; return;
                case WeaknessAxis.NoiseSensitivity:
                    physical1 = "stealth_observe"; physical2 = "route_cost";
                    mental = "mental_noise"; return;
                case WeaknessAxis.Shame:
                    physical1 = "escort_protect"; physical2 = "evidence_collect";
                    mental = "mental_compare"; return;
                case WeaknessAxis.JobAnxiety:
                    physical1 = "timed_actions"; physical2 = "explore_branching";
                    mental = "mental_doubt"; return;
                case WeaknessAxis.SelfDoubt:
                    physical1 = "beacon_ignite"; physical2 = "terrain_climb";
                    mental = "mental_freeze"; return;
                case WeaknessAxis.LowConfidence:
                    physical1 = "beacon_ignite"; physical2 = "timed_actions";
                    mental = "mental_urgency"; return;
                case WeaknessAxis.FailureFear:
                    physical1 = "source_hunt"; physical2 = "explore_branching";
                    mental = "mental_freeze"; return;
                case WeaknessAxis.WillpowerCollapse:
                    physical1 = "resource_scavenge"; physical2 = "terrain_climb";
                    mental = "mental_urgency"; return;
                default:   // Procrastination 及其它
                    physical1 = "timed_actions"; physical2 = "explore_branching";
                    mental = "mental_urgency"; return;
            }
        }

        // ================= 敌人库 =================

        /// <summary>敌人名是否在已批准的 EnemyCatalog 里。</summary>
        public static bool TryEnemy(string name, out EnemyType type)
        {
            type = EnemyType.TomorrowPhantom;
            if (string.IsNullOrEmpty(name)) return false;
            foreach (EnemyType t in System.Enum.GetValues(typeof(EnemyType)))
            {
                if (t.ToString() == name) { type = t; return true; }
                if (EnemyCatalog.TypeLabel(t) == name) { type = t; return true; }
            }
            return false;
        }

        /// <summary>
        /// 敌人名的**就近映射**：模型写的名字对不上库里任何一条时，按语义找最接近的一条。
        ///
        /// 为什么必须有这一层：模型面对「完成并发布一个自己的作品」这样的目标，
        /// 写出来的敌人叫「截止倒计时」「需求膨胀者」「便签风暴」——它在用玩家的语言
        /// 描述玩家的处境，这正是我们要的。要求它逐字写出 `TomorrowPhantom`，
        /// 等于要求它背下内部枚举表；对不上就整章作废，等于因为措辞不合规而没收整关。
        ///
        /// 三级匹配：包含关系 → 关键词归轴 → 失败（由调用方按轴取默认）。
        /// </summary>
        public static bool TryEnemyFuzzy(string name, out EnemyType type)
        {
            type = EnemyType.TomorrowPhantom;
            if (string.IsNullOrWhiteSpace(name)) return false;
            string n = name.Trim();

            // ① 包含关系：「拖延影魔王」↔「拖延影魔」、「TomorrowPhantom 明日幻影」↔ 枚举名
            foreach (EnemyType t in System.Enum.GetValues(typeof(EnemyType)))
            {
                string en = t.ToString(), cn = EnemyCatalog.TypeLabel(t);
                if (n.Contains(en) || en.Contains(n)) { type = t; return true; }
                if (!string.IsNullOrEmpty(cn) && (n.Contains(cn) || cn.Contains(n)))
                { type = t; return true; }
            }

            // ② 关键词归轴：认不出这一条具体是谁，但认得出它在考验哪一项
            foreach (var kv in EnemyKeywordAxis)
                if (n.Contains(kv.Key))
                {
                    SuggestEnemies(kv.Value, out var ext, out _, out _);
                    type = ext;
                    return true;
                }

            return false;
        }

        /// <summary>敌人名关键词 → 弱点轴（只用于就近映射，不参与正式匹配）。</summary>
        static readonly Dictionary<string, WeaknessAxis> EnemyKeywordAxis =
            new Dictionary<string, WeaknessAxis>
            {
                { "拖延", WeaknessAxis.Procrastination },
                { "明天", WeaknessAxis.Procrastination },
                { "倒计时", WeaknessAxis.Procrastination },
                { "截止", WeaknessAxis.Procrastination },
                { "分心", WeaknessAxis.NoiseSensitivity },
                { "噪声", WeaknessAxis.NoiseSensitivity },
                { "干扰", WeaknessAxis.NoiseSensitivity },
                { "挑衅", WeaknessAxis.NoiseSensitivity },
                { "眼神", WeaknessAxis.Shame },
                { "议论", WeaknessAxis.Shame },
                { "羞耻", WeaknessAxis.Shame },
                { "评价", WeaknessAxis.Shame },
                { "比较", WeaknessAxis.Shame },
                { "怀疑", WeaknessAxis.SelfDoubt },
                { "否定", WeaknessAxis.SelfDoubt },
                { "低语", WeaknessAxis.SelfDoubt },
                { "完美", WeaknessAxis.LowConfidence },
                { "追问", WeaknessAxis.LowConfidence },
                { "虚无", WeaknessAxis.LowConfidence },
                { "膨胀", WeaknessAxis.BoundaryConflict },
                { "需求", WeaknessAxis.BoundaryConflict },
                { "请求", WeaknessAxis.BoundaryConflict },
                { "代付", WeaknessAxis.BoundaryConflict },
                { "内疚", WeaknessAxis.BoundaryConflict },
                { "责任", WeaknessAxis.FairnessSensitivity },
                { "不公", WeaknessAxis.FairnessSensitivity },
                { "赖账", WeaknessAxis.FairnessSensitivity },
                { "债", WeaknessAxis.FairnessSensitivity },
                { "沉默", WeaknessAxis.JobAnxiety },
                { "无回", WeaknessAxis.JobAnxiety },
                { "拒信", WeaknessAxis.JobAnxiety },
                { "面试", WeaknessAxis.JobAnxiety },
                { "失败", WeaknessAxis.FailureFear },
                { "旧我", WeaknessAxis.FailureFear },
                { "过去", WeaknessAxis.FailureFear },
                { "回声", WeaknessAxis.FailureFear },
                { "疲惫", WeaknessAxis.WillpowerCollapse },
                { "低谷", WeaknessAxis.WillpowerCollapse },
                { "饥饿", WeaknessAxis.WillpowerCollapse },
                { "寒", WeaknessAxis.WillpowerCollapse },
            };

        /// <summary>按弱点轴给出该轴的外部敌人 / 内心敌人 / Boss 原型建议（供 Prompt 与 Fallback 用）。</summary>
        public static void SuggestEnemies(WeaknessAxis axis,
            out EnemyType external, out EnemyType internalEnemy, out EnemyType boss)
        {
            switch (axis)
            {
                case WeaknessAxis.BoundaryConflict:
                    external = EnemyType.RequestExpander;
                    internalEnemy = EnemyType.GuiltThrower;
                    boss = EnemyType.GoodPersonCage;
                    return;
                case WeaknessAxis.FairnessSensitivity:
                    external = EnemyType.OverreactGhost;
                    internalEnemy = EnemyType.DebtShadow;
                    boss = EnemyType.SelfDenialGavel;
                    return;
                case WeaknessAxis.NoiseSensitivity:
                    external = EnemyType.ProvokerPasserby;
                    internalEnemy = EnemyType.GazeEye;
                    boss = EnemyType.StimulusAmplifier;
                    return;
                case WeaknessAxis.Shame:
                    external = EnemyType.MaskFace;
                    internalEnemy = EnemyType.MockingBystander;
                    boss = EnemyType.ThousandEyeJudge;
                    return;
                case WeaknessAxis.JobAnxiety:
                    external = EnemyType.RuleTwister;
                    internalEnemy = EnemyType.PerfectPreparer;
                    boss = EnemyType.NoReplyKing;
                    return;
                case WeaknessAxis.FailureFear:
                    external = EnemyType.PastJudge;
                    internalEnemy = EnemyType.OldVoiceRepeater;
                    boss = EnemyType.GoalForgetter;
                    return;
                case WeaknessAxis.SelfDoubt:
                    external = EnemyType.DoubtScholar;
                    internalEnemy = EnemyType.SelfDoubtWhisper;
                    boss = EnemyType.ConceptMazeMaster;
                    return;
                case WeaknessAxis.LowConfidence:
                    external = EnemyType.QuoteGhost;
                    internalEnemy = EnemyType.PerfectPreparer;
                    boss = EnemyType.QuestionBeast;
                    return;
                case WeaknessAxis.WillpowerCollapse:
                    external = EnemyType.ColdWindBlade;
                    internalEnemy = EnemyType.MedDebtShadow;
                    boss = EnemyType.ValleyColossus;
                    return;
                default:
                    external = EnemyType.TomorrowMud;
                    internalEnemy = EnemyType.TomorrowPhantom;
                    boss = EnemyType.TomorrowKing;
                    return;
            }
        }

        /// <summary>弱点轴 → 建议落地区域（AI 未指定或指定非法时的确定性回退）。</summary>
        public static string DistrictFor(WeaknessAxis axis)
        {
            switch (axis)
            {
                case WeaknessAxis.JobAnxiety: return "office";
                case WeaknessAxis.NoiseSensitivity: return "transit";
                case WeaknessAxis.Shame: return "commercial";
                case WeaknessAxis.BoundaryConflict: return "office";
                case WeaknessAxis.FairnessSensitivity: return "service";
                case WeaknessAxis.WillpowerCollapse: return "edge";
                case WeaknessAxis.FailureFear: return "edge";
                default: return "residential";
            }
        }

        /// <summary>心理攻击轴 → 建议心理机制 id。</summary>
        public static string MentalMechanicFor(WeaknessAxis axis)
        {
            switch (axis)
            {
                case WeaknessAxis.BoundaryConflict: return "mental_shift_blame";
                case WeaknessAxis.NoiseSensitivity: return "mental_noise";
                case WeaknessAxis.Shame: return "mental_compare";
                case WeaknessAxis.FailureFear: return "mental_freeze";
                case WeaknessAxis.FairnessSensitivity: return "mental_provoke";
                case WeaknessAxis.Procrastination: return "mental_urgency";
                default: return "mental_doubt";
            }
        }

        public static List<string> AllDistrictIds()
        {
            var l = new List<string>();
            foreach (var d in Districts) l.Add(d.id);
            return l;
        }

        public static List<string> AllMechanicIds(bool physicalOnly)
        {
            var l = new List<string>();
            foreach (var m in Mechanics)
                if (!physicalOnly || m.physical) l.Add(m.id);
            return l;
        }

        /// <summary>
        /// 已批准敌人清单，写成「英文名(中文)」发给模型。
        ///
        /// 英文名是它必须逐字填回来的键，中文只是让它挑得准——
        /// 光给一串 PascalCase 英文，模型很难判断 GuiltThrower 和 DebtShadow
        /// 哪个更适合"被人情绑架"的那一关。
        /// </summary>
        public static List<string> AllEnemyNames()
        {
            var l = new List<string>();
            foreach (EnemyType t in System.Enum.GetValues(typeof(EnemyType)))
            {
                string cn = EnemyCatalog.TypeLabel(t);
                l.Add(string.IsNullOrEmpty(cn) ? t.ToString() : t + "(" + cn + ")");
            }
            return l;
        }
    }
}
