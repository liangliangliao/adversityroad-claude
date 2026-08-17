using System;
using System.Collections.Generic;
using UnityEngine;
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

        // ===== 物理世界的差异化描述（V2.1 新增）=====
        //
        // 【为什么必须加这一组】玩家反复反馈"AI 生成的每一关场景基本雷同，
        // 像是共用同一个物理世界"。查下来确实如此：AI 能决定的只有
        // "哪类地方 + 什么布局 + 什么灯光"，而地面、墙色、边界、场地中央有什么、
        // 有没有高低差、什么天气——这些**一眼就能看出差别**的东西全部写死在引擎里。
        // 于是十关下来都是同一块灰地板、同一圈米色墙、同一片空地。
        //
        // 所以把"这地方长什么样"真正交给 AI 描述，引擎按描述现场搭：
        // 每一项都对应一套已实现的程序化构件，AI 只能从已批准清单里选
        // （和道具、敌人、机制同一套约束——它描述，引擎建，坐标永远不由它给）。
        public string palette = "";          // 色板：地面/墙体/装饰的主色调
        public string groundSurface = "";    // 地面：混凝土/瓷砖/沥青/木地板/地毯/草地/沙地/碎石/积水/钢格栅
        public string boundary = "";         // 场地边界：墙/围栏/绿篱/临街楼/水岸/山崖/幕布/集装箱
        public string landmark = "";         // 场地中央的标志物：舞台/喷泉/钟塔/大屏/雕像/塔吊/篝火/讲台/帐篷/巴士/擂台/货架山
        public string verticality = "flat";  // 高低差：flat / platform / split / pit / balcony
        public string weather = "clear";     // 天气：clear / rain / fog / snow / dust / wind
        public int clutter = 2;              // 杂物密度 0-3

        /// <summary>散落在场地各处的道具（不属于任何房间，用来把空地填成"有人用过的地方"）。</summary>
        public List<string> scatterProps = new List<string>();

        /// <summary>一段"这地方长什么样"的描述：进场时给玩家看，也是差异化的自检项。</summary>
        public string sceneDescription = "";

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

        // ================= 物理世界差异化词表 =================
        //
        // 这几张表是"AI 描述 → 引擎搭建"的接口：每一个 id 背后都有一段已实现的
        // 程序化构件（见 SiteBuilder）。AI 只能从这里选，但选出来的组合足够多——
        // 12 色板 × 10 地面 × 8 边界 × 14 标志物 × 5 高低差 × 6 天气，
        // 再叠上 26 类场所与 6 种布局，两关撞成一个样子的概率低到可以忽略。

        /// <summary>色板：一整套地面/墙体/装饰/点缀色。这是"一眼看出不是同一个地方"的第一层。</summary>
        public class PaletteInfo
        {
            public string id, name;
            public Color floor, wall, trim, accent;
        }

        public static readonly PaletteInfo[] Palettes =
        {
            new PaletteInfo { id = "concrete_gray", name = "水泥灰",
                floor = new Color(0.46f, 0.45f, 0.44f), wall = new Color(0.70f, 0.68f, 0.63f),
                trim = new Color(0.32f, 0.33f, 0.36f), accent = new Color(0.85f, 0.72f, 0.30f) },
            new PaletteInfo { id = "office_blue", name = "办公冷蓝",
                floor = new Color(0.34f, 0.36f, 0.40f), wall = new Color(0.78f, 0.82f, 0.86f),
                trim = new Color(0.22f, 0.30f, 0.40f), accent = new Color(0.35f, 0.62f, 0.85f) },
            new PaletteInfo { id = "clinic_white", name = "诊室白",
                floor = new Color(0.80f, 0.82f, 0.83f), wall = new Color(0.92f, 0.94f, 0.94f),
                trim = new Color(0.55f, 0.70f, 0.72f), accent = new Color(0.40f, 0.75f, 0.72f) },
            new PaletteInfo { id = "warm_wood", name = "暖木色",
                floor = new Color(0.52f, 0.37f, 0.23f), wall = new Color(0.80f, 0.70f, 0.55f),
                trim = new Color(0.36f, 0.25f, 0.16f), accent = new Color(0.90f, 0.66f, 0.32f) },
            new PaletteInfo { id = "rust_industrial", name = "工业锈",
                floor = new Color(0.33f, 0.31f, 0.29f), wall = new Color(0.52f, 0.40f, 0.31f),
                trim = new Color(0.42f, 0.24f, 0.14f), accent = new Color(0.92f, 0.52f, 0.18f) },
            new PaletteInfo { id = "neon_night", name = "霓虹夜",
                floor = new Color(0.17f, 0.18f, 0.24f), wall = new Color(0.24f, 0.24f, 0.34f),
                trim = new Color(0.12f, 0.13f, 0.18f), accent = new Color(0.95f, 0.25f, 0.65f) },
            new PaletteInfo { id = "moss_green", name = "苔藓绿",
                floor = new Color(0.30f, 0.40f, 0.28f), wall = new Color(0.62f, 0.68f, 0.55f),
                trim = new Color(0.25f, 0.32f, 0.24f), accent = new Color(0.75f, 0.85f, 0.40f) },
            new PaletteInfo { id = "sunset_brick", name = "落日砖红",
                floor = new Color(0.45f, 0.33f, 0.30f), wall = new Color(0.72f, 0.42f, 0.34f),
                trim = new Color(0.40f, 0.22f, 0.18f), accent = new Color(0.98f, 0.72f, 0.40f) },
            new PaletteInfo { id = "sand_beige", name = "沙土米",
                floor = new Color(0.72f, 0.64f, 0.48f), wall = new Color(0.85f, 0.79f, 0.66f),
                trim = new Color(0.52f, 0.45f, 0.34f), accent = new Color(0.60f, 0.72f, 0.85f) },
            new PaletteInfo { id = "steel_cold", name = "钢青",
                floor = new Color(0.38f, 0.42f, 0.45f), wall = new Color(0.55f, 0.62f, 0.66f),
                trim = new Color(0.24f, 0.28f, 0.32f), accent = new Color(0.60f, 0.90f, 0.95f) },
            new PaletteInfo { id = "paper_archive", name = "旧纸黄",
                floor = new Color(0.58f, 0.53f, 0.42f), wall = new Color(0.84f, 0.78f, 0.62f),
                trim = new Color(0.45f, 0.38f, 0.28f), accent = new Color(0.72f, 0.35f, 0.28f) },
            new PaletteInfo { id = "deep_violet", name = "深紫夜",
                floor = new Color(0.24f, 0.20f, 0.30f), wall = new Color(0.38f, 0.32f, 0.48f),
                trim = new Color(0.16f, 0.13f, 0.22f), accent = new Color(0.72f, 0.55f, 0.95f) },
        };

        public static PaletteInfo Palette(string id)
        {
            foreach (var p in Palettes) if (p.id == id) return p;
            return Palettes[0];
        }

        public static bool IsPalette(string id)
        {
            foreach (var p in Palettes) if (p.id == id) return true;
            return false;
        }

        /// <summary>地面材质：决定地板颜色微调与地面上的纹理构件（车道线/砖缝/草簇/积水…）。</summary>
        public static readonly string[] Surfaces =
            { "concrete", "tile", "asphalt", "wood", "carpet", "grass", "sand", "gravel", "puddle", "grate" };

        public static bool IsSurface(string id)
        {
            foreach (var s in Surfaces) if (s == id) return true;
            return false;
        }

        public static string SurfaceName(string id)
        {
            switch (id)
            {
                case "tile": return "瓷砖";
                case "asphalt": return "沥青";
                case "wood": return "木地板";
                case "carpet": return "地毯";
                case "grass": return "草地";
                case "sand": return "沙地";
                case "gravel": return "碎石";
                case "puddle": return "积水地面";
                case "grate": return "钢格栅";
                default: return "水泥地";
            }
        }

        /// <summary>场地边界：室外场景的"这块地到哪儿为止"，也是远景的主要构成。</summary>
        public static readonly string[] Boundaries =
            { "wall", "fence", "hedge", "buildings", "water", "cliff", "curtain", "containers" };

        public static bool IsBoundary(string id)
        {
            foreach (var b in Boundaries) if (b == id) return true;
            return false;
        }

        public static string BoundaryName(string id)
        {
            switch (id)
            {
                case "fence": return "围栏";
                case "hedge": return "绿篱";
                case "buildings": return "临街楼";
                case "water": return "水岸";
                case "cliff": return "断崖";
                case "curtain": return "帷幕";
                case "containers": return "集装箱墙";
                default: return "围墙";
            }
        }

        /// <summary>
        /// 场地标志物：中央那个**一眼就记得住**的东西。
        /// 两关就算同为"街区·开放布局"，一个中间立着舞台、一个中间是塔吊，
        /// 玩家也绝不会觉得是同一个地方——这是差异化里性价比最高的一项。
        /// </summary>
        public static readonly string[] Landmarks =
        {
            "none", "stage", "fountain", "clock_tower", "big_screen", "statue", "crane",
            "bonfire", "podium", "tent", "bus", "ring", "shelf_maze", "scaffold"
        };

        public static bool IsLandmark(string id)
        {
            foreach (var l in Landmarks) if (l == id) return true;
            return false;
        }

        public static string LandmarkName(string id)
        {
            switch (id)
            {
                case "stage": return "舞台";
                case "fountain": return "喷泉";
                case "clock_tower": return "钟塔";
                case "big_screen": return "巨幕";
                case "statue": return "雕像";
                case "crane": return "塔吊";
                case "bonfire": return "篝火堆";
                case "podium": return "讲台";
                case "tent": return "帐篷";
                case "bus": return "废弃巴士";
                case "ring": return "擂台";
                case "shelf_maze": return "货架阵";
                case "scaffold": return "脚手架";
                default: return "";
            }
        }

        /// <summary>高低差：平地 / 中央高台 / 半层落差 / 下沉坑 / 二层挑台。</summary>
        public static readonly string[] Verticalities =
            { "flat", "platform", "split", "pit", "balcony" };

        public static bool IsVerticality(string id)
        {
            foreach (var v in Verticalities) if (v == id) return true;
            return false;
        }

        public static string VerticalityName(string id)
        {
            switch (id)
            {
                case "platform": return "中央高台";
                case "split": return "半层落差";
                case "pit": return "下沉坑";
                case "balcony": return "二层挑台";
                default: return "平地";
            }
        }

        public static readonly string[] Weathers =
            { "clear", "rain", "fog", "snow", "dust", "wind" };

        public static bool IsWeather(string id)
        {
            foreach (var w in Weathers) if (w == id) return true;
            return false;
        }

        public static string WeatherName(string id)
        {
            switch (id)
            {
                case "rain": return "下着雨";
                case "fog": return "起雾";
                case "snow": return "落雪";
                case "dust": return "扬尘";
                case "wind": return "刮风";
                default: return "晴";
            }
        }

        public static List<string> AllPaletteIds()
        {
            var l = new List<string>();
            foreach (var p in Palettes) l.Add(p.id);
            return l;
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
