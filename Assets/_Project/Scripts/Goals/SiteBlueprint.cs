using System;
using System.Collections.Generic;
using AdversityRoad.Personalization;

namespace AdversityRoad.Goals
{
    /// <summary>一个房间/区块的描述（AI 只填语义，坐标由引擎决定）。</summary>
    [Serializable]
    public class SiteRoom
    {
        public string name = "";          // 「需求会议室」「堆满故障单的工位区」
        public string purpose = "";       // 这个房间在关卡规则里承担什么
        public string sizeHint = "medium";// small / medium / large
        public List<string> props = new List<string>();   // 只能来自已批准道具库
    }

    /// <summary>
    /// 一条敌人编成（AI 决定"这一关放谁、放几个、放在哪、怎么行动"）。
    ///
    /// 以前敌人只有三个扁平字段（externalEnemies / internalEnemies / bossArchetype），
    /// 引擎拿到之后一律往同一批出生点上摆——于是每一关的战斗排布都一样，
    /// 而且玩家常常在空地上找不到人。现在把"编成"也交给 AI：
    /// 同样是"需求膨胀"，可以是门口两个小兵 + 深处一个首领，
    /// 也可以是一路巡逻的三人组，战斗节奏因此才真的按关卡走。
    ///
    /// 坐标仍然不给 AI：placement 只说**远近层次**（门口/中段/深处），
    /// 具体落点由引擎在已烘焙的导航面上算。
    /// </summary>
    [Serializable]
    public class ChapterEnemySpec
    {
        public string enemyType = "";      // 已批准敌人库里的英文名
        public string tier = "standard";   // minion / standard / elite / chief
        public int count = 1;              // 1-4
        public string placement = "middle";// entrance / middle / deep
        public string role = "guard";      // guard 守点 / patrol 巡逻 / ambush 伏击
    }

    /// <summary>场景里的 NPC（只用角色类型，绝不使用真实姓名）。</summary>
    [Serializable]
    public class SiteNpc
    {
        public string roleType = "passerby";
        public int count = 1;
        public string behavior = "wander";   // wander / station / patrol
        public string line = "";             // 一句环境台词（非攻击性）
    }

    /// <summary>
    /// Site Blueprint：AI 章节的**场景生成数据**。
    ///
    /// 这是"目标专属"能不能成立的关键——如果每个 AI 章节都只能落在预先做好的
    /// 24 个场景里，那系统做的仍然是心理标签推荐，而不是为这个目标造一条路。
    /// 所以 AI 必须能描述出一个**这个目标专属的地方**：它长什么样、有几个房间、
    /// 里面摆着什么、有哪些人、灯光什么样、规则是什么、它会对玩家说什么。
    ///
    /// 但 AI 仍然拿不到坐标、拿不到 Prefab、写不了代码：
    /// 它只能从已批准的 Kit / 道具库 / 角色类型库里挑，
    /// 由 SiteBuilder 按 assemblySeed 确定性地把它建成一个真的能走进去的场景。
    /// </summary>
    [Serializable]
    public class SiteBlueprint
    {
        public string siteName = "";        // 场景名（显示给玩家）
        public string siteKind = "office_floor";
        public string layout = "rooms";     // rooms / corridor / maze / hall / openblock / courtyard
        public string ambience = "indoor_cold";
        public string sizeHint = "medium";

        public List<SiteRoom> rooms = new List<SiteRoom>();
        public List<SiteNpc> npcs = new List<SiteNpc>();

        /// <summary>关卡规则（玩家进场时按条显示；每条都应对应一个已声明的机制）。</summary>
        public List<string> rules = new List<string>();

        /// <summary>外部言语攻击：敌人会喊的话（会过安全过滤与禁用主题检查）。</summary>
        public List<string> externalLines = new List<string>();
        /// <summary>内部言语攻击：玩家脑内回声（同样过滤）。</summary>
        public List<string> internalLines = new List<string>();

        /// <summary>场景里可交互的关键物（对应机制的世界化身）。</summary>
        public List<string> interactables = new List<string>();

        public bool IsEmpty => rooms.Count == 0 && string.IsNullOrEmpty(siteName);
    }

    /// <summary>
    /// 已批准的世界 Kit（方案 19.4）：AI 只能引用这里的 id。
    /// 任何库外 id 都会被 Validator 换成同类默认值——绝不允许 AI 凭空造几何。
    /// </summary>
    public static class SiteKitCatalog
    {
        /// <summary>场景类型 → 它属于哪个 Kit、默认落在哪个开放世界区域。</summary>
        public class SiteKindInfo
        {
            public string id;
            public string name;
            public string kit;
            public string districtId;
            public bool indoor;
        }

        public static readonly SiteKindInfo[] Kinds =
        {
            // Office Kit
            new SiteKindInfo { id = "office_floor", name = "办公层", kit = "Office", districtId = "office", indoor = true },
            new SiteKindInfo { id = "meeting_room", name = "会议区", kit = "Office", districtId = "office", indoor = true },
            new SiteKindInfo { id = "recruit_hall", name = "招聘大厅", kit = "Office", districtId = "office", indoor = true },
            new SiteKindInfo { id = "server_room", name = "机房", kit = "Office", districtId = "office", indoor = true },
            new SiteKindInfo { id = "archive", name = "档案室", kit = "Office", districtId = "office", indoor = true },
            // Residential Kit
            new SiteKindInfo { id = "apartment", name = "公寓单元", kit = "Residential", districtId = "residential", indoor = true },
            new SiteKindInfo { id = "stairwell", name = "楼梯间", kit = "Residential", districtId = "residential", indoor = true },
            new SiteKindInfo { id = "studio", name = "工作间", kit = "Residential", districtId = "residential", indoor = true },
            // Street Kit
            new SiteKindInfo { id = "street_block", name = "街区", kit = "Street", districtId = "commercial", indoor = false },
            new SiteKindInfo { id = "market", name = "市集", kit = "Street", districtId = "commercial", indoor = false },
            new SiteKindInfo { id = "mall", name = "商场中庭", kit = "Street", districtId = "commercial", indoor = true },
            new SiteKindInfo { id = "shop", name = "店铺", kit = "Street", districtId = "commercial", indoor = true },
            new SiteKindInfo { id = "rooftop", name = "天台", kit = "Street", districtId = "commercial", indoor = false },
            new SiteKindInfo { id = "park", name = "公园", kit = "Street", districtId = "service", indoor = false },
            // Hospital Kit
            new SiteKindInfo { id = "hospital_ward", name = "病房区", kit = "Hospital", districtId = "service", indoor = true },
            new SiteKindInfo { id = "clinic", name = "诊室", kit = "Hospital", districtId = "service", indoor = true },
            new SiteKindInfo { id = "waiting_area", name = "等候区", kit = "Hospital", districtId = "service", indoor = true },
            // Transit Kit
            new SiteKindInfo { id = "subway", name = "地铁站", kit = "Transit", districtId = "transit", indoor = true },
            new SiteKindInfo { id = "parking", name = "停车场", kit = "Transit", districtId = "transit", indoor = true },
            new SiteKindInfo { id = "crossroad", name = "十字路口", kit = "Transit", districtId = "transit", indoor = false },
            // Edge / Industrial
            new SiteKindInfo { id = "warehouse", name = "仓库", kit = "Industrial", districtId = "edge", indoor = true },
            new SiteKindInfo { id = "factory", name = "车间", kit = "Industrial", districtId = "edge", indoor = true },
            new SiteKindInfo { id = "alley", name = "小巷", kit = "Street", districtId = "edge", indoor = false },
            new SiteKindInfo { id = "abandoned", name = "废弃楼", kit = "Industrial", districtId = "edge", indoor = true },
            // 学习/创作
            new SiteKindInfo { id = "classroom", name = "教室", kit = "Office", districtId = "service", indoor = true },
            new SiteKindInfo { id = "library_room", name = "阅览室", kit = "Office", districtId = "service", indoor = true },
        };

        public static SiteKindInfo Kind(string id)
        {
            foreach (var k in Kinds) if (k.id == id) return k;
            return Kinds[0];
        }

        public static bool IsKind(string id)
        {
            foreach (var k in Kinds) if (k.id == id) return true;
            return false;
        }

        public static readonly string[] Layouts =
            { "rooms", "corridor", "maze", "hall", "openblock", "courtyard" };

        public static bool IsLayout(string id)
        {
            foreach (var l in Layouts) if (l == id) return true;
            return false;
        }

        public static readonly string[] Ambiences =
            { "day", "dusk", "night", "rain", "indoor_cold", "indoor_warm", "flicker", "fog" };

        public static bool IsAmbience(string id)
        {
            foreach (var a in Ambiences) if (a == id) return true;
            return false;
        }

        /// <summary>已批准道具库：每个 id 都有对应的程序化构件（SiteBuilder.BuildProp）。</summary>
        public static readonly string[] Props =
        {
            "desk", "chair", "table", "shelf", "cabinet", "locker", "server_rack", "monitor",
            "whiteboard", "printer", "counter", "sofa", "bed", "curtain", "crate", "barrier",
            "trashbin", "plant", "pillar", "sign", "bench", "vending", "cart", "pipe",
            "fence", "billboard", "stall", "car", "lamp", "door_frame", "stairs", "papers"
        };

        public static bool IsProp(string id)
        {
            foreach (var p in Props) if (p == id) return true;
            return false;
        }

        /// <summary>
        /// 兜底房间：模型没写房间、或者房间里的道具全写错被清空时用这一套。
        ///
        /// 三间房而不是一间：入口（看清处境）→ 阻力所在（这一关真正要打的地方）→
        /// 深处（Boss / 结算）。一间空房不构成关卡，玩家走进去会立刻看出这是敷衍的。
        /// 中间那间按弱点轴换陈设——考验专注的地方摆的是工位与显示器，
        /// 考验边界的地方摆的是柜台与队列，玩家一眼能看出这关在针对什么。
        /// </summary>
        public static List<SiteRoom> DefaultRooms(string siteKind, WeaknessAxis axis)
        {
            var mid = new SiteRoom { name = "阻力所在", sizeHint = "large" };
            switch (axis)
            {
                case WeaknessAxis.BoundaryConflict:
                    mid.purpose = "一个个请求在这里排队递到你面前";
                    mid.props.AddRange(new[] { "counter", "barrier", "chair", "papers" });
                    break;
                case WeaknessAxis.NoiseSensitivity:
                    mid.purpose = "声音与目光从四面挤过来，专注被持续拉扯";
                    mid.props.AddRange(new[] { "pillar", "bench", "vending", "sign" });
                    break;
                case WeaknessAxis.JobAnxiety:
                    mid.purpose = "投出去的东西堆在这里，没有一条回音";
                    mid.props.AddRange(new[] { "counter", "printer", "papers", "chair" });
                    break;
                case WeaknessAxis.Shame:
                    mid.purpose = "所有人都能看见你在这里做得怎么样";
                    mid.props.AddRange(new[] { "billboard", "bench", "monitor", "plant" });
                    break;
                case WeaknessAxis.WillpowerCollapse:
                    mid.purpose = "撑不住的时候，这里连个能坐下的地方都要找";
                    mid.props.AddRange(new[] { "crate", "pipe", "trashbin", "bench" });
                    break;
                case WeaknessAxis.FairnessSensitivity:
                    mid.purpose = "该谁承担的事在这里被反复推来推去";
                    mid.props.AddRange(new[] { "table", "papers", "cabinet", "sign" });
                    break;
                case WeaknessAxis.FailureFear:
                    mid.purpose = "以前失手的东西被一件件摆出来展示";
                    mid.props.AddRange(new[] { "shelf", "sign", "pillar", "papers" });
                    break;
                default:   // 拖延 / 自我怀疑 / 低信心
                    mid.purpose = "该做的事都在这里，但每一件都可以再等一等";
                    mid.props.AddRange(new[] { "desk", "monitor", "papers", "whiteboard" });
                    break;
            }

            // 室内外用不同的入口陈设：露天场景摆一道门框反而穿帮
            bool indoor = Kind(siteKind).indoor;
            var entry = new SiteRoom
            {
                name = "入口", purpose = "先看清这地方是什么样子", sizeHint = "medium",
                props = indoor
                    ? new List<string> { "door_frame", "sign", "plant" }
                    : new List<string> { "fence", "sign", "bench" }
            };

            return new List<SiteRoom>
            {
                entry,
                mid,
                new SiteRoom
                {
                    name = "深处", purpose = "挡在最后的那一个在这里等你", sizeHint = "large",
                    props = new List<string> { "pillar", "crate", "lamp" }
                },
            };
        }

        /// <summary>已批准 NPC 角色类型：只有类型，没有姓名（方案 8.2 隐私红线）。</summary>
        public static readonly string[] NpcRoles =
        {
            "passerby", "clerk", "colleague", "manager", "patient", "nurse",
            "guard", "applicant", "customer", "cleaner", "student", "driver", "neighbor"
        };

        public static bool IsNpcRole(string id)
        {
            foreach (var r in NpcRoles) if (r == id) return true;
            return false;
        }

        public static string NpcRoleName(string id)
        {
            switch (id)
            {
                case "clerk": return "店员";
                case "colleague": return "同事";
                case "manager": return "主管";
                case "patient": return "病人";
                case "nurse": return "护士";
                case "guard": return "保安";
                case "applicant": return "求职者";
                case "customer": return "顾客";
                case "cleaner": return "保洁";
                case "student": return "学生";
                case "driver": return "司机";
                case "neighbor": return "邻居";
                default: return "路人";
            }
        }

        public static string LayoutName(string id)
        {
            switch (id)
            {
                case "corridor": return "长廊";
                case "maze": return "迷宫";
                case "hall": return "大厅";
                case "openblock": return "开放街区";
                case "courtyard": return "围合院落";
                default: return "房间群";
            }
        }

        public static List<string> AllKindIds()
        {
            var l = new List<string>();
            foreach (var k in Kinds) l.Add(k.id);
            return l;
        }
    }
}
