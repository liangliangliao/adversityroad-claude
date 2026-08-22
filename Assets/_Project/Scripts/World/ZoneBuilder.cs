using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AdversityRoad.Combat;

namespace AdversityRoad.World
{
    public struct CarRoute
    {
        public Vector3 a;
        public Vector3 b;
    }

    /// <summary>世界构建上下文：材质、区域坐标、出生点、行人/车辆生成数据。</summary>
    public class WorldContext
    {
        public Material mat;
        public DayNightCycle dayNight;
        public Vector3[] zoneOrigins;
        public Vector3[] playerSpawns;
        public Vector3[] enemySpawns;
        public readonly List<Vector3> pedestrianSpawns = new List<Vector3>();
        public readonly List<CarRoute> carRoutes = new List<CarRoute>();
    }

    /// <summary>
    /// 运行时程序化世界：独居小屋（室内）→ 训练武馆 → 噪声街区（房屋/路灯/车辆/行人）
    /// → 城市广场（决战地）。四个区域横向排布，传送门相连，共享一次 NavMesh 烘焙。
    /// </summary>
    public static class ZoneBuilder
    {
        public static string CurrentZoneId = "home";

        // V2.0：区域表改为可在运行时追加——AI 生成的场景会作为**动态区域**注册进来，
        // 于是传送、雾色、剧情锁、存档里的当前区域都能像对待原生区域一样对待它。
        static readonly List<string> ZoneIds = new List<string>
            { "home", "dojo", "street", "job", "plaza", "court", "judgment", "swamp", "echo",
              "gamble", "carpark", "gazehall", "crossroad", "goalroom", "favorhall", "paycorridor",
              "alley", "garage", "ward",
              "library", "hall", "bridge", "exhibit", "tower",
              "city",
              "corridor", "classroom" };
        static readonly List<string> ZoneNames = new List<string>
            { "独居小屋", "训练武馆", "噪声街区", "求职荒原", "城市广场", "责任转嫁法院",
              "小题大做审判庭", "拖延沼泽", "旧事回声馆",
              "两元赌桌", "债务车影", "眼神审判走廊", "陌生挑衅路口",
              "目标遗忘房", "老实人消耗局", "无限代付走廊",
              "饥饿荒巷", "车库寒夜", "病房回廊",
              "哲学虚无图书馆", "无限追问大厅", "意志断桥", "失败展览馆", "意志塔",
              "开放城区",
              "欠条长廊·未播出的广播室", "二十元回声教室" };

        /// <summary>静态区域数量（含开放城区与第八章两关）。索引 ≥ 此值的都是运行时生成的动态场景。</summary>
        public const int StaticZoneCount = 27;

        public static string ZoneIdOf(int index) =>
            index >= 0 && index < ZoneIds.Count ? ZoneIds[index] : "home";

        public static string ZoneNameOf(int index) =>
            index >= 0 && index < ZoneNames.Count ? ZoneNames[index] : "";

        public static int ZoneCount => ZoneIds.Count;

        /// <summary>是不是运行时生成的动态场景（AI 章节现造的地方）。</summary>
        public static bool IsDynamicZone(int index) => index >= StaticZoneCount;

        /// <summary>
        /// 按区域 id 反查索引，**查不到返回 -1**。
        ///
        /// 【这里曾经是"打着打着穿越回独居小屋"的源头】
        /// 老版本查不到就回退 0，而 0 号区正是独居小屋。于是任何一次查不到——
        /// 生成场景被卸载后 id 改成了 `xxx_closed`、或区域表还没注册上——
        /// 都会被静默翻译成"你在独居小屋"，下游的落点计算照单全收，
        /// 把正在别处打架的玩家直接送回小屋。
        /// 查不到就是查不到：返回 -1，让调用方自己决定该怎么兜底。
        /// 需要"查不到当独居小屋"的老语义时用 <see cref="IndexOfZoneOrHome"/>，
        /// 但那只该用在显示上，绝不该用来算传送落点。
        /// </summary>
        public static int IndexOfZone(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < ZoneIds.Count; i++)
                if (ZoneIds[i] == id) return i;
            return -1;
        }

        /// <summary>同上，但查不到时回退到 0 号区（仅供显示用途）。</summary>
        public static int IndexOfZoneOrHome(string id)
        {
            int i = IndexOfZone(id);
            return i < 0 ? 0 : i;
        }

        // 各区域玩家出生点的静态副本（BuildAll 时填充）：关卡选择面板传送用
        static readonly List<Vector3> _spawnTable = new List<Vector3>();

        public static Vector3 PlayerSpawnOf(int index) =>
            index >= 0 && index < _spawnTable.Count ? _spawnTable[index] : Vector3.up * 1.1f;

        /// <summary>
        /// 注册一个运行时生成的场景为新区域，返回它的索引。
        /// 同名场景重复注册时复用原索引并更新出生点（避免区域表无限增长）。
        /// </summary>
        public static int RegisterDynamicZone(string id, string name, Vector3 playerSpawn)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            for (int i = StaticZoneCount; i < ZoneIds.Count; i++)
                if (ZoneIds[i] == id)
                {
                    ZoneNames[i] = name;
                    while (_spawnTable.Count <= i) _spawnTable.Add(Vector3.up * 1.1f);
                    _spawnTable[i] = playerSpawn;
                    return i;
                }

            ZoneIds.Add(id);
            ZoneNames.Add(string.IsNullOrEmpty(name) ? id : name);
            while (_spawnTable.Count < ZoneIds.Count - 1) _spawnTable.Add(Vector3.up * 1.1f);
            _spawnTable.Add(playerSpawn);
            return ZoneIds.Count - 1;
        }

        /// <summary>卸载动态场景时注销（保留占位以免既有索引错位，改名为已关闭）。</summary>
        public static void UnregisterDynamicZone(string id)
        {
            for (int i = StaticZoneCount; i < ZoneIds.Count; i++)
                if (ZoneIds[i] == id)
                {
                    ZoneIds[i] = id + "_closed";
                    ZoneNames[i] = ZoneNames[i] + "（已关闭）";
                    return;
                }
        }

        public static void BuildAll(WorldContext ctx)
        {
            Player.CameraOcclusionFade.ClearOccluders();   // 重建世界前清空旧遮挡登记
            ctx.zoneOrigins = new[]
            {
                new Vector3(0, 0, 0),
                new Vector3(300, 0, 0),
                new Vector3(600, 0, 0),
                new Vector3(900, 0, 0),
                new Vector3(1200, 0, 0),
                new Vector3(1500, 0, 0),
                new Vector3(1800, 0, 0),
                new Vector3(2100, 0, 0),
                new Vector3(2400, 0, 0),
                new Vector3(2700, 0, 0),
                new Vector3(3000, 0, 0),
                new Vector3(3300, 0, 0),
                new Vector3(3600, 0, 0),
                new Vector3(3900, 0, 0),
                new Vector3(4200, 0, 0),
                new Vector3(4500, 0, 0),
                new Vector3(4800, 0, 0),
                new Vector3(5100, 0, 0),
                new Vector3(5400, 0, 0),
                new Vector3(5700, 0, 0),
                new Vector3(6000, 0, 0),
                new Vector3(6300, 0, 0),
                new Vector3(6600, 0, 0),
                new Vector3(6900, 0, 0),
                new Vector3(7400, 0, 0),     // 24 开放城区（连续城市，占地更大，间距拉到 500）
                // 第八章两关排在城区之后：城区地面横跨 ±140，7800 起才不会压到它
                new Vector3(7800, 0, 0),     // 25 欠条长廊 / 未播出的广播室
                new Vector3(8100, 0, 0)      // 26 二十元回声教室
            };
            ctx.playerSpawns = new[]
            {
                ctx.zoneOrigins[0] + new Vector3(0, 1.1f, -5),
                ctx.zoneOrigins[1] + new Vector3(-18, 1.1f, 0),
                ctx.zoneOrigins[2] + new Vector3(-40, 1.1f, 8),
                ctx.zoneOrigins[3] + new Vector3(-38, 1.1f, 0),
                ctx.zoneOrigins[4] + new Vector3(-48, 1.1f, 0),
                ctx.zoneOrigins[5] + new Vector3(0, 1.1f, -40),
                ctx.zoneOrigins[6] + new Vector3(0, 1.1f, -32),
                ctx.zoneOrigins[7] + new Vector3(0, 1.1f, -42),
                ctx.zoneOrigins[8] + new Vector3(0, 1.1f, -38),
                ctx.zoneOrigins[9] + new Vector3(0, 1.1f, -9),
                ctx.zoneOrigins[10] + new Vector3(0, 1.1f, -24),
                ctx.zoneOrigins[11] + new Vector3(0, 1.1f, -30),
                ctx.zoneOrigins[12] + new Vector3(0, 1.1f, -30),
                ctx.zoneOrigins[13] + new Vector3(0, 1.1f, -13),
                ctx.zoneOrigins[14] + new Vector3(0, 1.1f, -22),
                ctx.zoneOrigins[15] + new Vector3(0, 1.1f, -34),
                ctx.zoneOrigins[16] + new Vector3(0, 1.1f, -30),
                ctx.zoneOrigins[17] + new Vector3(0, 1.1f, -26),
                ctx.zoneOrigins[18] + new Vector3(0, 1.1f, -30),
                ctx.zoneOrigins[19] + new Vector3(0, 1.1f, -30),
                ctx.zoneOrigins[20] + new Vector3(0, 1.1f, -32),
                ctx.zoneOrigins[21] + new Vector3(0, 1.1f, -42),
                ctx.zoneOrigins[22] + new Vector3(0, 1.1f, -28),
                ctx.zoneOrigins[23] + new Vector3(0, 1.1f, -30),
                ctx.zoneOrigins[24] + new Vector3(-96, 1.1f, 20),  // 开放城区：从家中醒来
                ctx.zoneOrigins[25] + new Vector3(0, 1.1f, -46),   // 欠条长廊：从小商店门口起步
                ctx.zoneOrigins[26] + new Vector3(0, 1.1f, -14)    // 回声教室：从教室门内起步
            };
            ctx.enemySpawns = new[]
            {
                ctx.zoneOrigins[0] + new Vector3(4, 1.1f, 5),
                ctx.zoneOrigins[1] + new Vector3(8, 1.1f, 8),
                ctx.zoneOrigins[2] + new Vector3(15, 1.1f, 9),
                ctx.zoneOrigins[3] + new Vector3(10, 1.1f, 5),
                ctx.zoneOrigins[4] + new Vector3(0, 1.1f, 8),
                ctx.zoneOrigins[5] + new Vector3(0, 1.1f, 28),
                ctx.zoneOrigins[6] + new Vector3(0, 1.1f, 26),
                ctx.zoneOrigins[7] + new Vector3(0, 1.1f, 28),
                ctx.zoneOrigins[8] + new Vector3(0, 1.1f, 30),
                ctx.zoneOrigins[9] + new Vector3(0, 1.1f, 5),
                ctx.zoneOrigins[10] + new Vector3(0, 1.1f, 16),
                ctx.zoneOrigins[11] + new Vector3(0, 1.1f, 26),
                ctx.zoneOrigins[12] + new Vector3(0, 1.1f, 0),
                ctx.zoneOrigins[13] + new Vector3(0, 1.1f, 12),
                ctx.zoneOrigins[14] + new Vector3(0, 1.1f, 12),
                ctx.zoneOrigins[15] + new Vector3(0, 1.1f, 30),
                ctx.zoneOrigins[16] + new Vector3(0, 1.1f, 14),
                ctx.zoneOrigins[17] + new Vector3(0, 1.1f, 16),
                ctx.zoneOrigins[18] + new Vector3(0, 1.1f, 28),
                ctx.zoneOrigins[19] + new Vector3(0, 1.1f, 16),
                ctx.zoneOrigins[20] + new Vector3(0, 1.1f, 26),
                ctx.zoneOrigins[21] + new Vector3(0, 1.1f, 34),
                ctx.zoneOrigins[22] + new Vector3(0, 1.1f, 16),
                ctx.zoneOrigins[23] + new Vector3(0, 4.8f, 26),
                ctx.zoneOrigins[24] + new Vector3(88, 1.1f, 35),   // 开放城区：边缘区小巷
                ctx.zoneOrigins[25] + new Vector3(0, 1.1f, 6),     // 悬案法官：站在长廊中段
                ctx.zoneOrigins[26] + new Vector3(6, 1.1f, 14)     // 后排低语者：教室最后一排
            };

            _spawnTable.Clear();
            _spawnTable.AddRange(ctx.playerSpawns);

            BuildHome(ctx);
            BuildDojo(ctx);
            BuildStreet(ctx);
            BuildJobSquare(ctx);
            BuildPlaza(ctx);
            BuildResponsibilityCourt(ctx);
            BuildJudgmentCourt(ctx);
            BuildProcrastinationSwamp(ctx);
            BuildEchoMuseum(ctx);
            BuildGambleDen(ctx);
            BuildDebtCarPark(ctx);
            BuildGazeHall(ctx);
            BuildCrossroad(ctx);
            BuildGoalRoom(ctx);
            BuildFavorHall(ctx);
            BuildPayCorridor(ctx);
            BuildHungerAlley(ctx);
            BuildColdGarage(ctx);
            BuildWardCorridor(ctx);
            BuildPhilLibrary(ctx);
            BuildQuestionHall(ctx);
            BuildWillBridge(ctx);
            BuildFailureExhibit(ctx);
            BuildWillTower(ctx);

            BuildDebtCorridor(ctx);      // V2.1 第八章 8-1
            BuildEchoClassroom(ctx);     // V2.1 第八章 8-2

            OpenWorld.OpenWorldBuilder.Build(ctx, 24);   // V2.0：一片连续可自由移动的城市

            EnsureZoneLighting(ctx);
            EnsureSpawnPads(ctx);
        }

        /// <summary>
        /// 落点保底：确保每个区域的玩家出生点脚下有实地。
        ///
        /// 传送的落点统一取该区的出生点，而 24 个区的地板是各自手工摆的——
        /// 只要有一个区的地板没覆盖到它自己的出生点坐标，玩家一进这一关就直接下坠。
        /// 玩家反馈的"通关进入下一关掉进深渊"就是这么来的，而且这种问题
        /// 靠肉眼审 24 个建场函数根本查不全。
        ///
        /// 这里改成**建完世界统一验一遍**：从出生点上方往下探，探不到地面就补一块
        /// 6×6 的落地台。已经有地板的区域一块都不会多加，所以对现有关卡零影响。
        /// </summary>
        static void EnsureSpawnPads(WorldContext ctx)
        {
            if (ctx.playerSpawns == null) return;
            Physics.SyncTransforms();   // 刚建出来的碰撞体要先同步，否则射线打不到
            for (int z = 0; z < ctx.playerSpawns.Length; z++)
            {
                Vector3 c = ctx.playerSpawns[z];
                // 从出生点上方 3 米往下探 8 米：够穿过角色胶囊高度，也够发现低一点的台阶
                if (Physics.Raycast(c + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f,
                        ~0, QueryTriggerInteraction.Ignore)
                    && hit.point.y > c.y - 4f)
                    continue;
                Box(ctx, "GroundPad_Spawn_" + ZoneIdOf(z), new Vector3(c.x, -0.25f, c.z),
                    new Vector3(6f, 0.5f, 6f), new Color(0.34f, 0.36f, 0.42f));
            }
        }

        /// <summary>
        /// 补灯：24 个区域里只有 6 个手工摆了路灯，其余 18 个——包括拖延沼泽之后
        /// 的一整串关卡——照明全靠两三盏 range 很大但强度写死 1.0 的顶灯，
        /// 夜里等于没有。玩家反馈的"拖延线之后的场景都太黑、看不清人和物"就是这个。
        ///
        /// 【判据从"附近有没有灯"换成"这块地到底亮不亮"】
        /// 旧版只看 60 米内有没有登记在册的路灯，于是：
        ///   ① 顶灯不登记在 lamps 里 → 有顶灯的关卡照样被补一圈路灯（白补）；
        ///   ② 有一盏很弱的灯 → 判定"有灯"直接跳过（该补的没补）。
        /// 两种错法方向相反，共同点是**都没有真的量过亮度**。
        /// 现在按网格采样实际照度，暗的格子才补灯——这是可验证的，不靠猜。
        /// </summary>
        static void EnsureZoneLighting(WorldContext ctx)
        {
            if (ctx.dayNight == null || ctx.playerSpawns == null) return;
            Physics.SyncTransforms();   // 刚建好的墙/屋顶要先同步，否则下面的探顶射线打空
            int total = 0;
            for (int z = 0; z < ctx.playerSpawns.Length; z++)
            {
                Vector3 c = ctx.playerSpawns[z];
                c.y = 0f;
                // 每区以出生点为中心审一片 90×90。
                // 30m 半径只覆盖出生点周围一小块——而这些关卡的战场、传送门、
                // Boss 台大多在三四十米开外，正好落在审计范围之外，于是"补过灯了"
                // 和"走过去还是黑的"同时成立。半径按区域实际尺度放大。
                total += SceneLighting.EnsureLit(c, 45f, p =>
                {
                    // 出生点跟前 6 米内不补：灯柱正好糊住出门那一眼
                    if ((p - c).sqrMagnitude < 36f) return false;
                    // 【室内 / 露天要用不同的灯】
                    // 头顶有屋顶的地方补的是**常亮吊灯**：那儿一天到晚都晒不到太阳，
                    // 补一盏"天黑才亮"的路灯等于白天照样黑（审判庭/走廊/图书馆全是这种）。
                    // 露天才补路灯，交给昼夜循环开关。
                    if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.up,
                            out RaycastHit roof, 22f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        // 吊灯离地不超过 3.4m：贴着屋顶装会把顶低的走廊照爆
                        AddCeilingLight(
                            new Vector3(p.x, SceneLighting.CeilingLightY(p.y, roof.point.y), p.z),
                            new Color(0.95f, 0.93f, 0.88f), SceneLighting.CeilingLightRange);
                        return true;
                    }
                    // 露天才立灯柱；被墙/家具占住的位置跳过（灯柱插进墙里比暗更难看）
                    if (Physics.CheckSphere(p + Vector3.up * 1.6f, 0.7f, ~0,
                            QueryTriggerInteraction.Ignore)) return false;
                    Lamp(ctx, p);
                    return true;
                }, cell: 14f, minLux: SceneLighting.MinLux, maxLamps: 14);
            }
            if (total > 0) Debug.Log("[ZoneBuilder] 照明审计补灯 " + total + " 盏");
        }

        // ================= 第一区：独居小屋（室内） =================

        static void BuildHome(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[0];

            Box(ctx, "Home_Floor", o + new Vector3(0, -0.1f, 0), new Vector3(22, 0.2f, 22),
                new Color(0.5f, 0.42f, 0.34f));
            // 四面墙
            Box(ctx, "Wall", o + new Vector3(0, 1.5f, 11), new Vector3(22, 3, 0.4f), WallColor);
            Box(ctx, "Wall", o + new Vector3(-11, 1.5f, 0), new Vector3(0.4f, 3, 22), WallColor);
            Box(ctx, "Wall", o + new Vector3(11, 1.5f, 0), new Vector3(0.4f, 3, 22), WallColor);
            // 南墙留门洞
            Box(ctx, "Wall", o + new Vector3(-7, 1.5f, -11), new Vector3(8, 3, 0.4f), WallColor);
            Box(ctx, "Wall", o + new Vector3(7, 1.5f, -11), new Vector3(8, 3, 0.4f), WallColor);
            Box(ctx, "Wall", o + new Vector3(0, 2.6f, -11), new Vector3(6, 0.8f, 0.4f), WallColor);

            // 家具：床 / 书桌 / 屏幕 / 椅子 / 书架 / 地毯 / 窗
            Box(ctx, "Bed", o + new Vector3(-7.5f, 0.35f, 7), new Vector3(3, 0.7f, 5),
                new Color(0.35f, 0.42f, 0.6f));
            Box(ctx, "Pillow", o + new Vector3(-7.5f, 0.8f, 8.8f), new Vector3(2, 0.25f, 1),
                new Color(0.9f, 0.9f, 0.92f));
            Box(ctx, "Desk", o + new Vector3(6.5f, 0.5f, 8.5f), new Vector3(4, 1, 1.6f),
                new Color(0.42f, 0.3f, 0.2f));
            Box(ctx, "Screen", o + new Vector3(6.5f, 1.45f, 9f), new Vector3(1.6f, 0.9f, 0.1f),
                new Color(0.55f, 0.75f, 1f));
            Box(ctx, "Chair", o + new Vector3(6.5f, 0.35f, 6.8f), new Vector3(1, 0.7f, 1),
                new Color(0.25f, 0.25f, 0.28f));
            Box(ctx, "Shelf", o + new Vector3(10.5f, 1.2f, 3), new Vector3(0.6f, 2.4f, 4),
                new Color(0.45f, 0.33f, 0.22f));
            Decoration(ctx, "Rug", o + new Vector3(0, 0.02f, 0), new Vector3(6, 0.04f, 4),
                new Color(0.5f, 0.28f, 0.25f));
            Decoration(ctx, "Window", o + new Vector3(-11f + 0.25f, 1.8f, -3), new Vector3(0.1f, 1.6f, 3),
                new Color(0.65f, 0.8f, 1f));

            // 室内暖灯（常亮）
            var lampGo = new GameObject("Home_Lamp");
            lampGo.transform.position = o + new Vector3(0, 2.7f, 0);
            var lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 14;
            lamp.intensity = 1.1f;
            lamp.color = new Color(1f, 0.9f, 0.7f);

            // 目标板（恢复点）与拖延泥潭
            var board = Box(ctx, "GoalBoard", o + new Vector3(2.5f, 1.5f, 10.6f),
                new Vector3(3, 1.6f, 0.2f), new Color(0.95f, 0.8f, 0.25f));
            board.AddComponent<GoalBoard>();
            Mire(ctx, o + new Vector3(-3, 0, -5), new Vector3(7, 2, 5));

            // 出门传送门 → 训练武馆
            // 序章第一关只有一扇门：往前去训练武馆（没有上一关，所以没有回头门）
            MakePortal(ctx, o + new Vector3(0, 0, -10.5f), PortalRole.Forward);
            // V2.0：北墙的另一扇门 → 开放城区（走出去就是连续的真实城市，不再回菜单选关）。
            // 城区不在章节序列里——它一直开着，也不参与"上一关/下一关"的推进，
            // 所以这扇门用显式目标，不走章节解析。
            MakePortal(ctx, o + new Vector3(-4, 0, 10.4f), 24, "开放城区");
        }

        // ================= 第二区：训练武馆 =================

        static void BuildDojo(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[1];

            Box(ctx, "Dojo_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(50, 0.5f, 50),
                new Color(0.55f, 0.45f, 0.32f));
            Ring(ctx, o, 25, 3, new Color(0.4f, 0.28f, 0.2f));

            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Box(ctx, "Pillar", o + new Vector3(9 * x, 1.5f, 9 * z),
                        new Vector3(1.5f, 3, 1.5f), new Color(0.5f, 0.36f, 0.25f));

            // 训练木桩装饰
            for (int i = 0; i < 3; i++)
                Box(ctx, "TrainingPost", o + new Vector3(-14 + i * 3, 1f, -14),
                    new Vector3(0.5f, 2f, 0.5f), new Color(0.6f, 0.45f, 0.3f));

            // 门往里收到 ±21（地板边缘在 ±25）：加上门前地台后，门口前后左右都还有余量，
            // 站定等待期间怎么走都不会走出地板。
            MakePortal(ctx, o + new Vector3(-21f, 0, 0), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(21f, 0, 0), PortalRole.Forward);
        }

        // ================= 第三区：噪声街区 =================

        static void BuildStreet(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[2];

            Box(ctx, "Street_Ground", o + new Vector3(0, -0.25f, 0), new Vector3(104, 0.5f, 44),
                new Color(0.42f, 0.42f, 0.44f));
            // 马路
            Decoration(ctx, "Road", o + new Vector3(0, 0.02f, 0), new Vector3(104, 0.04f, 8),
                new Color(0.2f, 0.2f, 0.22f));
            for (int x = -48; x <= 48; x += 6)
                Decoration(ctx, "Lane", o + new Vector3(x, 0.05f, 0), new Vector3(2.2f, 0.04f, 0.3f),
                    Color.white);
            // 人行道
            Decoration(ctx, "Sidewalk", o + new Vector3(0, 0.04f, 9), new Vector3(104, 0.08f, 10),
                new Color(0.55f, 0.55f, 0.56f));
            Decoration(ctx, "Sidewalk", o + new Vector3(0, 0.04f, -9), new Vector3(104, 0.08f, 10),
                new Color(0.55f, 0.55f, 0.56f));

            Ring(ctx, o, new Vector2(52, 22), 4, new Color(0.3f, 0.3f, 0.33f));

            // 两侧房屋
            var rng = new System.Random(42);
            for (int i = 0; i < 6; i++)
            {
                float x = -42 + i * 17;
                Building(ctx, o + new Vector3(x, 0, 17.5f), 12, 8 + (float)rng.NextDouble() * 8, 8, rng);
                Building(ctx, o + new Vector3(x + 6, 0, -17.5f), 12, 8 + (float)rng.NextDouble() * 8, 8, rng);
            }

            // 路灯
            for (int x = -40; x <= 40; x += 20)
            {
                Lamp(ctx, o + new Vector3(x, 0, 5.2f));
                Lamp(ctx, o + new Vector3(x + 10, 0, -5.2f));
            }

            // 行道树 / 长椅 / 垃圾桶 / 斑马线 / 公交站
            for (int x = -35; x <= 45; x += 20)
            {
                Tree(ctx, o + new Vector3(x, 0, 11.5f));
                Tree(ctx, o + new Vector3(x - 8, 0, -11.5f));
            }
            Bench(ctx, o + new Vector3(-15, 0, 10), 0);
            Bench(ctx, o + new Vector3(20, 0, -10), 180);
            TrashBin(ctx, o + new Vector3(-12, 0, 10.5f));
            TrashBin(ctx, o + new Vector3(24, 0, -10.5f));
            Crosswalk(ctx, o + new Vector3(0, 0, 0), false);
            BusStop(ctx, o + new Vector3(32, 0, 9.5f));

            // 刺激放大器 Boss 场地：街心人行道上的小广场——四周是广告牌与噪声源
            var arenaMark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(arenaMark.GetComponent<Collider>());
            arenaMark.name = "StreetBossArena";
            arenaMark.transform.position = o + new Vector3(0, 0.05f, 9);
            arenaMark.transform.localScale = new Vector3(14f, 0.02f, 9f);
            Paint(ctx, arenaMark, new Color(0.48f, 0.46f, 0.5f));
            AdBoard(ctx, o + new Vector3(-8, 0, 13.4f), 0);
            AdBoard(ctx, o + new Vector3(8, 0, 13.4f), 0);
            AdBoard(ctx, o + new Vector3(14, 0, -13.4f), 180);

            // 噪声区（方案·环境机制）：广告牌下与公交站旁的环境噪声——
            // 站入专注缓慢流失，定心姿态减免、不读心盾免疫；影响很轻（防晕原则）
            MakeZoneTrigger<Combat.NoiseZone>(o + new Vector3(0, 1, 11), new Vector3(20, 2.5f, 6));
            MakeZoneTrigger<Combat.NoiseZone>(o + new Vector3(32, 1, 9.5f), new Vector3(8, 2.5f, 6));
            MakeZoneTrigger<Combat.NoiseZone>(o + new Vector3(14, 1, -12), new Vector3(8, 2.5f, 6));

            // 车辆路线（双向两车道）
            ctx.carRoutes.Add(new CarRoute { a = o + new Vector3(-46, 0.55f, -2), b = o + new Vector3(46, 0.55f, -2) });
            ctx.carRoutes.Add(new CarRoute { a = o + new Vector3(46, 0.55f, 2), b = o + new Vector3(-46, 0.55f, 2) });

            // 行人出生点（人行道上）
            for (int i = 0; i < 3; i++)
            {
                ctx.pedestrianSpawns.Add(o + new Vector3(-30 + i * 22, 1f, 9));
                ctx.pedestrianSpawns.Add(o + new Vector3(-20 + i * 22, 1f, -9));
            }

            MakePortal(ctx, o + new Vector3(-50f, 0, 8), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(50f, 0, 8), PortalRole.Forward);
        }

        // ================= 第四区：求职荒原 =================

        static void BuildJobSquare(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[3];

            Box(ctx, "Job_Ground", o + new Vector3(0, -0.25f, 0), new Vector3(84, 0.5f, 64),
                new Color(0.55f, 0.5f, 0.42f));
            Ring(ctx, o, new Vector2(42, 32), 4, new Color(0.4f, 0.36f, 0.3f));

            // 漫天简历纸片：悬浮的白色纸页
            var rng = new System.Random(88);
            for (int i = 0; i < 34; i++)
            {
                var paper = Decoration(ctx, "Resume",
                    o + new Vector3(-36 + (float)rng.NextDouble() * 72,
                        0.4f + (float)rng.NextDouble() * 3.4f,
                        -28 + (float)rng.NextDouble() * 56),
                    new Vector3(0.5f, 0.02f, 0.7f), new Color(0.93f, 0.93f, 0.9f));
                paper.transform.rotation = Quaternion.Euler(
                    (float)rng.NextDouble() * 50 - 25, (float)rng.NextDouble() * 360,
                    (float)rng.NextDouble() * 50 - 25);
            }

            // 面试之门：一排紧闭的高门，唯一一扇透光
            for (int i = 0; i < 5; i++)
            {
                float x = -24 + i * 12;
                Box(ctx, "DoorPillar", o + new Vector3(x - 1.6f, 2.4f, 22), new Vector3(0.8f, 4.8f, 0.8f),
                    new Color(0.35f, 0.33f, 0.3f));
                Box(ctx, "DoorPillar", o + new Vector3(x + 1.6f, 2.4f, 22), new Vector3(0.8f, 4.8f, 0.8f),
                    new Color(0.35f, 0.33f, 0.3f));
                Decoration(ctx, "DoorTop", o + new Vector3(x, 5f, 22), new Vector3(4.4f, 0.6f, 1f),
                    new Color(0.3f, 0.28f, 0.26f));
                Decoration(ctx, "DoorPanel", o + new Vector3(x, 2.3f, 22.1f), new Vector3(2.6f, 4.4f, 0.15f),
                    i == 2 ? new Color(1f, 0.9f, 0.55f) : new Color(0.16f, 0.15f, 0.15f));
            }

            // 审判台：高台上的空椅——无回应的化身
            Box(ctx, "JudgeStand", o + new Vector3(10, 0.8f, 8), new Vector3(8, 1.6f, 6),
                new Color(0.42f, 0.4f, 0.38f));
            Decoration(ctx, "JudgeChair", o + new Vector3(10, 2.4f, 9.5f), new Vector3(1.6f, 1.6f, 0.3f),
                new Color(0.2f, 0.2f, 0.24f));
            Decoration(ctx, "JudgeSeat", o + new Vector3(10, 1.9f, 8.8f), new Vector3(1.6f, 0.25f, 1.4f),
                new Color(0.25f, 0.25f, 0.28f));

            // 枯树
            for (int i = 0; i < 4; i++)
            {
                var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                trunk.name = "DeadTree";
                trunk.transform.position = o + new Vector3(-30 + i * 18, 1.7f, -22 + (i % 2) * 40);
                trunk.transform.localScale = new Vector3(0.28f, 1.7f, 0.28f);
                trunk.transform.rotation = Quaternion.Euler(0, 0, (i % 2 == 0 ? 6f : -8f));
                Paint(ctx, trunk, new Color(0.32f, 0.26f, 0.2f));
            }

            Lamp(ctx, o + new Vector3(-16, 0, 12));
            Lamp(ctx, o + new Vector3(20, 0, -12));

            MakePortal(ctx, o + new Vector3(-40f, 0, 0), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(40f, 0, 0), PortalRole.Forward);
        }

        // ================= 第五区：城市广场 =================

        static void BuildPlaza(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[4];

            Box(ctx, "Plaza_Ground", o + new Vector3(0, -0.25f, 0), new Vector3(110, 0.5f, 110),
                new Color(0.5f, 0.5f, 0.52f));
            Decoration(ctx, "PlazaCenter", o + new Vector3(0, 0.02f, 0), new Vector3(36, 0.04f, 36),
                new Color(0.62f, 0.58f, 0.5f));
            // 十字马路
            Decoration(ctx, "RoadX", o + new Vector3(0, 0.03f, 30), new Vector3(110, 0.04f, 8),
                new Color(0.2f, 0.2f, 0.22f));
            Decoration(ctx, "RoadZ", o + new Vector3(-30, 0.03f, 0), new Vector3(8, 0.04f, 110),
                new Color(0.2f, 0.2f, 0.22f));

            // 喷泉（底座碰撞体换成薄盒：Cylinder 胶囊碰撞体非均匀缩放会成 r=3 隐形球）
            var basin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(basin.GetComponent<Collider>());
            basin.name = "Fountain";
            basin.transform.position = o + new Vector3(0, 0.3f, -10);
            basin.transform.localScale = new Vector3(6, 0.3f, 6);
            Paint(ctx, basin, new Color(0.4f, 0.55f, 0.65f));
            var basinCol = new GameObject("FountainCollider");
            basinCol.transform.position = o + new Vector3(0, 0.3f, -10);
            basinCol.AddComponent<BoxCollider>().size = new Vector3(5.2f, 0.6f, 5.2f);
            var jet = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            jet.name = "FountainJet";
            Object.DestroyImmediate(jet.GetComponent<Collider>());
            jet.transform.position = o + new Vector3(0, 1.4f, -10);
            jet.transform.localScale = new Vector3(0.6f, 1.1f, 0.6f);
            Paint(ctx, jet, new Color(0.7f, 0.9f, 1f));

            Ring(ctx, o, 55, 4, new Color(0.32f, 0.32f, 0.36f));

            var rng = new System.Random(7);
            for (int i = 0; i < 5; i++)
            {
                float t = -44 + i * 22;
                Building(ctx, o + new Vector3(t, 0, 48), 14, 12 + (float)rng.NextDouble() * 14, 10, rng);
                Building(ctx, o + new Vector3(t, 0, -48), 14, 12 + (float)rng.NextDouble() * 14, 10, rng);
                // 东侧中央（i==2）留出通往责任转嫁法院的大门缺口，不建楼
                if (i != 2)
                    Building(ctx, o + new Vector3(48, 0, t), 10, 10 + (float)rng.NextDouble() * 12, 14, rng);
            }

            for (int i = 0; i < 4; i++)
            {
                float ang = i * Mathf.PI / 2f + Mathf.PI / 4f;
                Lamp(ctx, o + new Vector3(Mathf.Cos(ang) * 20, 0, Mathf.Sin(ang) * 20));
            }
            Lamp(ctx, o + new Vector3(40, 0, 26));
            Lamp(ctx, o + new Vector3(-40, 0, 34));

            // 广场绿化与休憩设施
            for (int i = 0; i < 8; i++)
            {
                float ang = i * Mathf.PI / 4f;
                Tree(ctx, o + new Vector3(Mathf.Cos(ang) * 26, 0, Mathf.Sin(ang) * 26));
            }
            Bench(ctx, o + new Vector3(6, 0, -14), 0);
            Bench(ctx, o + new Vector3(-6, 0, -14), 0);
            Bench(ctx, o + new Vector3(0, 0, -3), 180);
            TrashBin(ctx, o + new Vector3(9, 0, -14));
            Crosswalk(ctx, o + new Vector3(0, 0, 30), false);
            Crosswalk(ctx, o + new Vector3(-30, 0, 0), true);

            ctx.carRoutes.Add(new CarRoute { a = o + new Vector3(-52, 0.55f, 28), b = o + new Vector3(52, 0.55f, 28) });
            ctx.carRoutes.Add(new CarRoute { a = o + new Vector3(-28, 0.55f, -52), b = o + new Vector3(-28, 0.55f, 52) });

            for (int i = 0; i < 4; i++)
                ctx.pedestrianSpawns.Add(o + new Vector3(-18 + i * 12, 1f, 12 - i * 8));

            MakePortal(ctx, o + new Vector3(-53f, 0, 0), PortalRole.Back);
            // 边界与责任线入口：广场东门先入「老实人消耗局」，再到法院
            MakePortal(ctx, o + new Vector3(44f, 0, 0), PortalRole.Forward);
        }

        // ================= 第六区：责任转嫁法院 =================

        static void BuildResponsibilityCourt(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[5];
            Combat.CourtState.Reset();

            Color courtWall = new Color(0.26f, 0.24f, 0.28f);
            Color stone = new Color(0.34f, 0.32f, 0.36f);

            // 一条自南向北纵深推进的大厅：门厅 → 文件走廊 → 责任天平大厅 → 审判席
            Box(ctx, "Court_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(52, 0.5f, 100), stone);
            Decoration(ctx, "CourtAisle", o + new Vector3(0, 0.02f, 0), new Vector3(9, 0.04f, 96),
                new Color(0.48f, 0.42f, 0.4f));

            // 外墙：南墙中央留出法院大门缺口（x -6~+6）
            Box(ctx, "CourtWall_N", o + new Vector3(0, 3, 48), new Vector3(52, 6, 1), courtWall);
            Box(ctx, "CourtWall_W", o + new Vector3(-26, 3, 0), new Vector3(1, 6, 100), courtWall);
            Box(ctx, "CourtWall_E", o + new Vector3(26, 3, 0), new Vector3(1, 6, 100), courtWall);
            Box(ctx, "CourtWall_S", o + new Vector3(-16, 3, -48), new Vector3(20, 6, 1), courtWall);
            Box(ctx, "CourtWall_S", o + new Vector3(16, 3, -48), new Vector3(20, 6, 1), courtWall);
            Decoration(ctx, "CourtDoorTop", o + new Vector3(0, 5.2f, -48), new Vector3(13, 1.6f, 1),
                new Color(0.2f, 0.18f, 0.22f));
            Decoration(ctx, "CourtDoorSign", o + new Vector3(0, 6.4f, -47.4f), new Vector3(9, 1.0f, 0.2f),
                new Color(0.5f, 0.16f, 0.2f));

            // 冷白顶灯（走廊 + 审判席各一盏）：法院的压迫式照明
            AddCeilingLight(o + new Vector3(0, 9f, -22), new Color(0.82f, 0.86f, 1f), 44);
            AddCeilingLight(o + new Vector3(0, 9f, 30), new Color(0.9f, 0.9f, 1f), 40);

            // ===== 第一段·文件走廊（含证词走廊侧廊 + 陪审团阴影 + 可击碎文件山 + 锁链闸）=====
            Box(ctx, "CorridorWall_L", o + new Vector3(-8, 2, -23), new Vector3(0.6f, 4, 24), courtWall);
            Box(ctx, "CorridorWall_R", o + new Vector3(8, 2, -23), new Vector3(0.6f, 4, 24), courtWall);

            // 证词走廊：两侧侧廊里排列陪审团阴影剪影（被围观的压迫感）
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 5; i++)
                    Decoration(ctx, "JurorShadow",
                        o + new Vector3(side * 15, 1.1f, -32 + i * 8),
                        new Vector3(1.0f, 2.2f, 0.6f), new Color(0.11f, 0.11f, 0.15f));

            // 可击碎文件山：击碎露出事实证据（事实之刃）——交错摆放，逼玩家穿行
            var filePos = new[]
            {
                new Vector3(-5.3f, 0, -30), new Vector3(5.3f, 0, -25),
                new Vector3(-5.3f, 0, -17), new Vector3(5.3f, 0, -12)
            };
            foreach (var fp in filePos)
            {
                float h = 2.4f;
                var fm = Box(ctx, "FileMountain", o + fp + new Vector3(0, h / 2, 0),
                    new Vector3(2.4f, h, 2.4f), new Color(0.6f, 0.56f, 0.46f));
                var bp = fm.AddComponent<Combat.BreakableProp>();
                bp.kind = Combat.CourtPropKind.FileMountain;
                bp.hitsToBreak = 3;
            }

            // 锁链闸：横跨走廊的责任锁链——链未断时站入减速，击碎任一即"破念"解除
            var chainZone = new GameObject("ChainBindZone");
            chainZone.transform.position = o + new Vector3(0, 1.2f, -21);
            var czCol = chainZone.AddComponent<BoxCollider>();
            czCol.size = new Vector3(13, 2.4f, 3);
            var cz = chainZone.AddComponent<Combat.ChainBindZone>();
            for (int i = 0; i < 5; i++)
            {
                float x = -5.6f + i * 2.8f;
                var chain = Box(ctx, "Chain", o + new Vector3(x, 2.0f, -21),
                    new Vector3(0.18f, 3.6f, 0.18f), new Color(0.4f, 0.4f, 0.46f));
                var bp = chain.AddComponent<Combat.BreakableProp>();
                bp.kind = Combat.CourtPropKind.Chain;
                bp.hitsToBreak = 1;
                bp.linkedChain = cz;
            }

            // ===== 第二段·责任天平大厅（责任天平判断归属 + 责任归属台 + 边界圈安全区）=====
            var scaleRoot = new GameObject("ResponsibilityScale");
            scaleRoot.transform.position = o + new Vector3(0, 0, -2);
            var scale = scaleRoot.AddComponent<Combat.ResponsibilityScale>();
            Box(ctx, "ScalePillar", o + new Vector3(0, 1.4f, -2), new Vector3(0.5f, 2.8f, 0.5f),
                new Color(0.55f, 0.5f, 0.3f));
            Decoration(ctx, "ScaleBeam", o + new Vector3(0, 2.7f, -2), new Vector3(6, 0.18f, 0.18f),
                new Color(0.6f, 0.55f, 0.32f));
            scale.panLeft = ScalePan(ctx, scaleRoot.transform, new Vector3(-2.8f, 2.1f, 0));
            scale.panRight = ScalePan(ctx, scaleRoot.transform, new Vector3(2.8f, 2.4f, 0));

            // 责任归属台：天平旁的低台（象征"在这里判断归属"）
            Box(ctx, "AttributionStand", o + new Vector3(-9, 0.4f, -2), new Vector3(4, 0.8f, 4),
                new Color(0.4f, 0.36f, 0.3f));

            // 边界圈安全区
            var circle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(circle.GetComponent<Collider>());
            circle.name = "BoundaryCircle";
            circle.transform.position = o + new Vector3(10, 0.04f, -2);
            circle.transform.localScale = new Vector3(5f, 0.02f, 5f);
            Paint(ctx, circle, new Color(0.3f, 0.75f, 0.5f));
            var bcZone = new GameObject("BoundaryCircleZone");
            bcZone.transform.position = o + new Vector3(10, 1f, -2);
            bcZone.AddComponent<BoxCollider>().size = new Vector3(5f, 2f, 5f);
            bcZone.AddComponent<Combat.BoundaryCircle>();

            // ===== 第三段·审判席（最高法官台 + 法槌，Boss 在此出现）=====
            Box(ctx, "BenchStep", o + new Vector3(0, 0.35f, 30), new Vector3(12, 0.7f, 2.5f),
                new Color(0.34f, 0.26f, 0.22f));
            Box(ctx, "JudgeBench", o + new Vector3(0, 1.3f, 36), new Vector3(20, 2.6f, 6),
                new Color(0.3f, 0.22f, 0.18f));
            Box(ctx, "JudgeBenchTop", o + new Vector3(0, 2.75f, 36), new Vector3(21, 0.3f, 6.6f),
                new Color(0.24f, 0.17f, 0.14f));
            Decoration(ctx, "GavelHead", o + new Vector3(0, 5.4f, 38.6f), new Vector3(3.6f, 1.5f, 1.5f),
                new Color(0.5f, 0.4f, 0.24f));
            Decoration(ctx, "GavelHandle", o + new Vector3(0, 3.6f, 38.6f), new Vector3(0.5f, 3f, 0.5f),
                new Color(0.42f, 0.32f, 0.2f));

            // 返回老实人消耗局（边界线来路）/ 通往无限代付走廊（边界线其三）
            MakePortal(ctx, o + new Vector3(15f, 0, -42), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(-18f, 0, 42), PortalRole.Forward);
        }

        // ================= 第七区：小题大做审判庭 =================

        static void BuildJudgmentCourt(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[6];
            Combat.JudgmentState.Reset();

            Color wall = new Color(0.24f, 0.2f, 0.26f);
            Color stone = new Color(0.32f, 0.29f, 0.34f);

            // 畸形审判庭：狭长大厅，中央通道通向倾斜的法官席
            Box(ctx, "Judgment_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(44, 0.5f, 78), stone);
            Decoration(ctx, "JudgmentAisle", o + new Vector3(0, 0.02f, 0), new Vector3(8, 0.04f, 74),
                new Color(0.45f, 0.38f, 0.38f));
            Ring(ctx, o, new Vector2(22, 39), 6, wall);

            // 压迫式冷光
            AddCeilingLight(o + new Vector3(0, 10f, -18), new Color(0.85f, 0.85f, 1f), 40);
            AddCeilingLight(o + new Vector3(0, 10f, 20), new Color(0.9f, 0.85f, 1f), 40);

            // 两侧旁观者席：高台 + 一排排旁观者阴影（被围观的压迫感）
            for (int side = -1; side <= 1; side += 2)
            {
                Box(ctx, "GalleryStand", o + new Vector3(side * 16, 0.6f, -6),
                    new Vector3(8, 1.2f, 40), new Color(0.28f, 0.24f, 0.3f));
                for (int i = 0; i < 6; i++)
                    Decoration(ctx, "BystanderShadow",
                        o + new Vector3(side * 16, 2.3f, -24 + i * 7),
                        new Vector3(1.0f, 2.2f, 0.6f), new Color(0.1f, 0.1f, 0.14f));
            }

            // 浮动标签：悬浮在通道上空的否定之词（事实之刃可击碎）
            var labels = new[]
            {
                ("太敏感", new Vector3(-4, 2.4f, -18)),
                ("小题大做", new Vector3(4.5f, 2.6f, -10)),
                ("你也有问题", new Vector3(-4.5f, 2.3f, -2)),
                ("不值得计较", new Vector3(4, 2.5f, 6)),
                ("想太多了", new Vector3(-3.5f, 2.7f, 14)),
                ("就你事多", new Vector3(3.5f, 2.4f, 20)),
            };
            foreach (var (text, pos) in labels)
                MakeFloatingLabel(ctx, o + pos, text);

            // 证据桌：中庭——走近即"看清事实"，此后标签一击即碎
            var table = Box(ctx, "EvidenceTable", o + new Vector3(-7, 0.55f, -6),
                new Vector3(3.4f, 1.1f, 2.2f), new Color(0.42f, 0.3f, 0.2f));
            Decoration(ctx, "EvidencePapers", o + new Vector3(-7, 1.16f, -6),
                new Vector3(2.4f, 0.06f, 1.4f), new Color(0.93f, 0.9f, 0.8f));
            table.AddComponent<Combat.EvidenceTable>();

            // 破碎镜子：西侧墙边——看清被扭曲的倒影，回补自尊
            var mirror = Box(ctx, "BrokenMirror", o + new Vector3(-20.5f, 1.8f, 10),
                new Vector3(0.3f, 3.2f, 2.4f), new Color(0.72f, 0.78f, 0.9f));
            for (int i = 0; i < 3; i++)
                Decoration(ctx, "MirrorCrack", o + new Vector3(-20.3f, 1.2f + i * 0.9f, 9.4f + i * 0.5f),
                    new Vector3(0.08f, 1.4f, 0.1f), new Color(0.2f, 0.22f, 0.3f));
            mirror.AddComponent<Combat.BrokenMirror>();

            // 审判席（Boss 平台）：倾斜的高台 + 巨大法槌
            Box(ctx, "GavelBenchStep", o + new Vector3(0, 0.35f, 24), new Vector3(12, 0.7f, 2.5f),
                new Color(0.3f, 0.22f, 0.2f));
            var bench = Box(ctx, "GavelBench", o + new Vector3(0, 1.4f, 31), new Vector3(18, 2.8f, 6),
                new Color(0.26f, 0.18f, 0.16f));
            bench.transform.rotation = Quaternion.Euler(0, 0, 3.5f);   // 倾斜的法官席：这里的公平是歪的
            Decoration(ctx, "GiantGavelHead", o + new Vector3(0, 6.2f, 33.5f),
                new Vector3(4.2f, 1.8f, 1.8f), new Color(0.5f, 0.36f, 0.2f));
            Decoration(ctx, "GiantGavelHandle", o + new Vector3(0, 3.9f, 33.5f),
                new Vector3(0.6f, 3.4f, 0.6f), new Color(0.4f, 0.3f, 0.18f));

            // 传送门：回债务车影（公平线来路）/ 通往责任转嫁法院（边界线时代的通道）
            MakePortal(ctx, o + new Vector3(-14f, 0, -35), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(14f, 0, -35), PortalRole.Forward);
        }

        /// <summary>浮动标签：漂浮的否定之词——立牌 + 文字 + 触发判定（事实之刃击碎）。</summary>
        static void MakeFloatingLabel(WorldContext ctx, Vector3 pos, string text)
        {
            var root = new GameObject("FloatingLabel_" + text);
            root.transform.position = pos;

            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "LabelBoard";
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.transform.SetParent(root.transform, false);
            board.transform.localScale = new Vector3(2.2f, 1.0f, 0.15f);
            Paint(ctx, board, new Color(0.65f, 0.5f, 0.3f));

            var tmGo = new GameObject("LabelText");
            tmGo.transform.SetParent(root.transform, false);
            tmGo.transform.localPosition = new Vector3(0, 0, -0.1f);
            // 会被遮挡的字（内置字体材质 ZTest Always，字会穿墙，见 WorldText）
            WorldText.Attach(tmGo, text, 48, 0.045f, new Color(0.15f, 0.1f, 0.08f));

            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(2.4f, 1.4f, 1.2f);

            var label = root.AddComponent<Combat.FloatingLabel>();
            label.labelText = text;
        }

        // ================= 第八区：拖延沼泽 =================

        static void BuildProcrastinationSwamp(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[7];
            Combat.SwampState.Reset();

            // 沼泽底色：暗绿褐的湿地
            Box(ctx, "Swamp_Ground", o + new Vector3(0, -0.25f, 0), new Vector3(92, 0.5f, 100),
                new Color(0.24f, 0.26f, 0.18f));
            Ring(ctx, o, new Vector2(46, 50), 4, new Color(0.2f, 0.22f, 0.16f));

            // 昏沉雾灯
            AddCeilingLight(o + new Vector3(0, 12f, -20), new Color(0.75f, 0.8f, 0.6f), 55);
            AddCeilingLight(o + new Vector3(0, 12f, 25), new Color(0.7f, 0.75f, 0.6f), 50);

            // 浅泥带（轻微减速）与深泥潭（大幅减速+行动力流失）：交错铺在主路线上
            MireZone(ctx, o + new Vector3(-8, 0, -30), new Vector3(16, 2, 10), 0.72f, 2f, false);
            MireZone(ctx, o + new Vector3(10, 0, -16), new Vector3(18, 2, 12), 0.72f, 2f, false);
            MireZone(ctx, o + new Vector3(-6, 0, 0), new Vector3(14, 2, 10), 0.45f, 5f, true);
            MireZone(ctx, o + new Vector3(12, 0, 12), new Vector3(12, 2, 10), 0.45f, 5f, true);
            MireZone(ctx, o + new Vector3(-14, 0, 20), new Vector3(12, 2, 8), 0.72f, 2f, false);

            // 干地平台：泥中的落脚点（可绕行路线）
            var dryColor = new Color(0.5f, 0.44f, 0.32f);
            Box(ctx, "DryPlatform", o + new Vector3(-18, 0.15f, -12), new Vector3(6, 0.3f, 6), dryColor);
            Box(ctx, "DryPlatform", o + new Vector3(2, 0.15f, -8), new Vector3(5, 0.3f, 5), dryColor);
            Box(ctx, "DryPlatform", o + new Vector3(20, 0.15f, 2), new Vector3(6, 0.3f, 6), dryColor);
            Box(ctx, "DryPlatform", o + new Vector3(-4, 0.15f, 14), new Vector3(5, 0.3f, 5), dryColor);

            // 目标石板：站上恢复行动力（明确的目标是干地）
            var goal1 = Box(ctx, "GoalStone", o + new Vector3(-20, 0.2f, 6), new Vector3(4.4f, 0.4f, 4.4f),
                new Color(0.75f, 0.68f, 0.45f));
            MakeZoneTrigger<Combat.GoalStoneZone>(goal1.transform.position + Vector3.up,
                new Vector3(4.4f, 2f, 4.4f));
            var goal2 = Box(ctx, "GoalStone", o + new Vector3(16, 0.2f, 22), new Vector3(4.4f, 0.4f, 4.4f),
                new Color(0.75f, 0.68f, 0.45f));
            MakeZoneTrigger<Combat.GoalStoneZone>(goal2.transform.position + Vector3.up,
                new Vector3(4.4f, 2f, 4.4f));

            // 手机光点区：诱人偏离主路线的幽蓝光（站入即流失专注与行动力）
            PhoneLight(ctx, o + new Vector3(22, 0, -24));
            PhoneLight(ctx, o + new Vector3(-24, 0, -2));
            PhoneLight(ctx, o + new Vector3(26, 0, 12));

            // 床铺藤蔓与漂浮计划纸：沼泽里的拖延残骸
            var rng = new System.Random(64);
            for (int i = 0; i < 4; i++)
            {
                var vine = Box(ctx, "BedVine", o + new Vector3(-30 + i * 17, 0.6f, -34 + (i % 2) * 10),
                    new Vector3(3.2f, 1.2f, 1.4f), new Color(0.24f, 0.34f, 0.22f));
                vine.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 90f, 0);
            }
            for (int i = 0; i < 18; i++)
            {
                var paper = Decoration(ctx, "PlanPaper",
                    o + new Vector3(-36 + (float)rng.NextDouble() * 72,
                        0.5f + (float)rng.NextDouble() * 2.6f,
                        -40 + (float)rng.NextDouble() * 76),
                    new Vector3(0.5f, 0.02f, 0.7f), new Color(0.9f, 0.9f, 0.85f));
                paper.transform.rotation = Quaternion.Euler(
                    (float)rng.NextDouble() * 40 - 20, (float)rng.NextDouble() * 360, 0);
            }
            // 破碎日历：拖延的时间残骸
            Decoration(ctx, "BrokenCalendar", o + new Vector3(6, 1.2f, -36),
                new Vector3(2.2f, 2.4f, 0.15f), new Color(0.85f, 0.82f, 0.75f));
            Decoration(ctx, "CalendarTear", o + new Vector3(6.4f, 0.4f, -35.6f),
                new Vector3(1.2f, 0.8f, 0.1f), new Color(0.8f, 0.76f, 0.7f));

            // Boss 泥台：北端圆形场地标识（纯装饰贴地圆盘）。
            // 注意：Cylinder 原始体自带胶囊碰撞体，非均匀缩放(26,0.15,26)会退化成
            // 半径 13 米的隐形巨球——把整个 Boss 区罩死（玩家进不去、Boss 出不来、
            // NavMesh 被隔断）。必须销毁碰撞体，战斗发生在平地上。
            var mudStage = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(mudStage.GetComponent<Collider>());
            mudStage.name = "TomorrowKingStage";
            mudStage.transform.position = o + new Vector3(0, 0.05f, 30);
            mudStage.transform.localScale = new Vector3(26f, 0.04f, 26f);
            Paint(ctx, mudStage, new Color(0.3f, 0.26f, 0.2f));
            SparkAltar(ctx, o + new Vector3(-11, 0, 24));
            SparkAltar(ctx, o + new Vector3(11, 0, 24));
            SparkAltar(ctx, o + new Vector3(0, 0, 40));

            // 传送门：回目标遗忘房（拖延线来路）/ 通往旧事回声馆（旧我线开启即解锁）
            MakePortal(ctx, o + new Vector3(-10f, 0, -46), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(28f, 0, 44), PortalRole.Forward);
        }

        /// <summary>沼泽泥地：视觉贴片 + 减速/耗行动力触发区。深泥颜色更深。</summary>
        static void MireZone(WorldContext ctx, Vector3 basePos, Vector3 size,
            float speedMult, float drainPerSec, bool deep)
        {
            Decoration(ctx, deep ? "DeepMud" : "ShallowMud", basePos + new Vector3(0, 0.03f, 0),
                new Vector3(size.x, 0.06f, size.z),
                deep ? new Color(0.1f, 0.07f, 0.14f) : new Color(0.17f, 0.15f, 0.12f));
            var zone = new GameObject(deep ? "DeepMudZone" : "ShallowMudZone");
            zone.transform.position = basePos + new Vector3(0, 1, 0);
            var col = zone.AddComponent<BoxCollider>();
            col.size = size;
            var mire = zone.AddComponent<Combat.ProcrastinationMire>();
            mire.speedMultiplier = speedMult;
            mire.actionDrainPerSec = drainPerSec;
        }

        /// <summary>手机光点区：幽蓝的诱惑光圈 + 消耗触发区。</summary>
        static void PhoneLight(WorldContext ctx, Vector3 basePos)
        {
            var glow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(glow.GetComponent<Collider>());
            glow.name = "PhoneGlow";
            glow.transform.position = basePos + new Vector3(0, 0.04f, 0);
            glow.transform.localScale = new Vector3(7f, 0.02f, 7f);
            Paint(ctx, glow, new Color(0.35f, 0.6f, 0.95f));

            var phone = Decoration(ctx, "PhoneScreen", basePos + new Vector3(0, 0.5f, 0),
                new Vector3(0.8f, 0.06f, 1.6f), new Color(0.6f, 0.8f, 1f));
            phone.transform.rotation = Quaternion.Euler(0, 30f, 0);

            var lightGo = new GameObject("PhoneLightGlow");
            lightGo.transform.position = basePos + new Vector3(0, 1.6f, 0);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 9;
            l.intensity = 1.1f;
            l.color = new Color(0.5f, 0.7f, 1f);

            MakeZoneTrigger<Combat.PhoneLightZone>(basePos + Vector3.up, new Vector3(7f, 2f, 7f));
        }

        /// <summary>五分钟火种台：石座+火盆（走近点燃，三座齐燃 Boss 破防）。</summary>
        static void SparkAltar(WorldContext ctx, Vector3 basePos)
        {
            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "SparkAltar";
            pedestal.transform.position = basePos + new Vector3(0, 0.5f, 0);
            pedestal.transform.localScale = new Vector3(1.6f, 0.5f, 1.6f);
            Paint(ctx, pedestal, new Color(0.45f, 0.4f, 0.35f));

            var bowl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(bowl.GetComponent<Collider>());
            bowl.name = "SparkBowl";
            bowl.transform.position = basePos + new Vector3(0, 1.1f, 0);
            bowl.transform.localScale = new Vector3(1.2f, 0.18f, 1.2f);
            Paint(ctx, bowl, new Color(0.3f, 0.26f, 0.22f));

            var root = new GameObject("SparkAltarRoot");
            root.transform.position = basePos + new Vector3(0, 1.1f, 0);
            root.AddComponent<Combat.FireSparkAltar>();
        }

        /// <summary>通用触发区辅助：在指定位置放一个带触发碰撞盒的组件。</summary>
        static T MakeZoneTrigger<T>(Vector3 pos, Vector3 size) where T : Component
        {
            var zone = new GameObject(typeof(T).Name + "Zone");
            zone.transform.position = pos;
            var col = zone.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = size;
            return zone.AddComponent<T>();
        }

        /// <summary>街边广告牌：噪声与视觉刺激源（刺激放大器场地装饰）。</summary>
        static void AdBoard(WorldContext ctx, Vector3 basePos, float yRot)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "AdPole";
            pole.transform.position = basePos + new Vector3(0, 1.5f, 0);
            pole.transform.localScale = new Vector3(0.18f, 1.5f, 0.18f);
            Paint(ctx, pole, new Color(0.3f, 0.3f, 0.34f));

            var board = Decoration(ctx, "AdBoard", basePos + new Vector3(0, 3.4f, 0),
                new Vector3(3.2f, 1.8f, 0.15f), new Color(0.95f, 0.75f, 0.35f));
            board.transform.rotation = Quaternion.Euler(0, yRot, 0);
        }

        // ================= 第九区：旧事回声馆 =================

        static void BuildEchoMuseum(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[8];
            Combat.EchoState.Reset();

            Color wall = new Color(0.16f, 0.16f, 0.22f);

            // 记忆博物馆：昏暗大厅，展柜长廊 → 大门 → 终局镜面平台
            Box(ctx, "Echo_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(52, 0.5f, 92),
                new Color(0.2f, 0.2f, 0.26f));
            Decoration(ctx, "EchoCarpet", o + new Vector3(0, 0.02f, -14), new Vector3(7, 0.04f, 56),
                new Color(0.3f, 0.24f, 0.32f));
            Ring(ctx, o, new Vector2(26, 46), 6, wall);

            // 幽蓝顶光（博物馆的旧梦色）
            AddCeilingLight(o + new Vector3(0, 10f, -24), new Color(0.6f, 0.65f, 0.9f), 42);
            AddCeilingLight(o + new Vector3(0, 10f, 4), new Color(0.6f, 0.65f, 0.9f), 40);
            AddCeilingLight(o + new Vector3(0, 10f, 30), new Color(0.75f, 0.78f, 1f), 42);

            // 旧事展柜长廊：四座展柜（靠近触发回声 → 站定归档；归档 3 座开终局大门）
            var cases = new[]
            {
                ("失败记录", new Vector3(-9, 0, -28)),
                ("旧标签", new Vector3(9, 0, -22)),
                ("未说出口的话", new Vector3(-9, 0, -12)),
                ("被放大的批评", new Vector3(9, 0, -4)),
            };
            foreach (var (label, pos) in cases)
                MakeEchoCase(ctx, o + pos, label);

            // 失败展廊：两侧的旧照片墙与碎镜框（氛围装饰）
            for (int i = 0; i < 5; i++)
            {
                Decoration(ctx, "OldPhotoFrame", o + new Vector3(-25.4f, 2.2f, -34 + i * 9),
                    new Vector3(0.15f, 1.6f, 2.0f), new Color(0.5f, 0.45f, 0.35f));
                Decoration(ctx, "OldPhoto", o + new Vector3(-25.25f, 2.2f, -34 + i * 9),
                    new Vector3(0.08f, 1.2f, 1.5f), new Color(0.65f, 0.6f, 0.5f));
                Decoration(ctx, "MirrorShardWall", o + new Vector3(25.4f, 2.0f, -30 + i * 9),
                    new Vector3(0.15f, 1.3f + (i % 3) * 0.4f, 1.1f), new Color(0.6f, 0.68f, 0.82f));
            }

            // 终局大门：归档满 3 座自动沉入地面
            var gate = Box(ctx, "EchoBossGate", o + new Vector3(0, 2.5f, 12), new Vector3(14, 5, 1.2f),
                new Color(0.32f, 0.3f, 0.42f));
            gate.AddComponent<Combat.EchoBossGate>();
            Box(ctx, "GateWall", o + new Vector3(-20, 2.5f, 12), new Vector3(26, 5, 1.2f), wall);
            Box(ctx, "GateWall", o + new Vector3(20, 2.5f, 12), new Vector3(26, 5, 1.2f), wall);
            Decoration(ctx, "GateSign", o + new Vector3(0, 5.6f, 11.6f), new Vector3(10, 0.9f, 0.2f),
                new Color(0.5f, 0.4f, 0.6f));

            // 终局镜面平台：圆形镜面 + 环绕的章节场景碎片（纯装饰贴地圆盘——
            // 同泥台：Cylinder 的胶囊碰撞体在非均匀缩放下会变成隐形巨球，必须销毁）
            var mirrorStage = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(mirrorStage.GetComponent<Collider>());
            mirrorStage.name = "FinalMirrorArena";
            mirrorStage.transform.position = o + new Vector3(0, 0.05f, 30);
            mirrorStage.transform.localScale = new Vector3(26f, 0.04f, 26f);
            Paint(ctx, mirrorStage, new Color(0.55f, 0.62f, 0.78f));
            // 环绕平台的记忆碎片柱：一路走来的章节残影
            var shardColors = new[]
            {
                new Color(0.6f, 0.55f, 0.45f),   // 赌桌/审判庭
                new Color(0.42f, 0.42f, 0.44f),  // 街道
                new Color(0.24f, 0.26f, 0.18f),  // 沼泽
                new Color(0.26f, 0.24f, 0.28f),  // 法院
                new Color(0.5f, 0.42f, 0.34f),   // 小屋
                new Color(0.55f, 0.45f, 0.32f),  // 武馆
            };
            for (int i = 0; i < 6; i++)
            {
                float ang = i * Mathf.PI / 3f;
                var shard = Box(ctx, "MemoryShard",
                    o + new Vector3(Mathf.Cos(ang) * 15f, 1.6f, 30 + Mathf.Sin(ang) * 15f),
                    new Vector3(1.6f, 3.2f, 0.4f), shardColors[i]);
                shard.transform.rotation = Quaternion.Euler(0, ang * Mathf.Rad2Deg + 90f, 8f);
            }

            // 传送门：回拖延沼泽 / 回独居小屋（安全屋——终局之后回家）
            MakePortal(ctx, o + new Vector3(-14f, 0, -42), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(14f, 0, -42), PortalRole.Forward);
        }

        /// <summary>旧事展柜：底座+半透明玻璃罩+内容物+回声光+标签，归档交互。</summary>
        static void MakeEchoCase(WorldContext ctx, Vector3 basePos, string label)
        {
            var pedestal = Box(ctx, "EchoCasePedestal", basePos + new Vector3(0, 0.5f, 0),
                new Vector3(2.2f, 1.0f, 2.2f), new Color(0.3f, 0.28f, 0.36f));

            var glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(glass.GetComponent<Collider>());
            glass.name = "EchoCaseGlass";
            glass.transform.position = basePos + new Vector3(0, 1.9f, 0);
            glass.transform.localScale = new Vector3(1.9f, 1.8f, 1.9f);
            glass.GetComponent<MeshRenderer>().sharedMaterial =
                Combat.CombatFeedback.EnergyMaterial(new Color(0.6f, 0.7f, 0.9f), 0.18f);

            Decoration(ctx, "EchoRelic", basePos + new Vector3(0, 1.5f, 0),
                new Vector3(0.7f, 0.7f, 0.7f), new Color(0.55f, 0.5f, 0.42f));

            // 回声光：未归档时亮着的不安之光（归档后熄灭）
            var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(glow.GetComponent<Collider>());
            glow.name = "EchoGlow";
            glow.transform.position = basePos + new Vector3(0, 3.2f, 0);
            glow.transform.localScale = Vector3.one * 0.5f;
            var glowR = glow.GetComponent<MeshRenderer>();
            glowR.sharedMaterial =
                Combat.CombatFeedback.EnergyMaterial(new Color(0.75f, 0.5f, 0.9f), 0.8f);

            var tmGo = new GameObject("EchoCaseLabel");
            tmGo.transform.position = basePos + new Vector3(0, 3.9f, 0);
            WorldText.Attach(tmGo, label, 44, 0.05f, new Color(0.8f, 0.82f, 0.95f));
            tmGo.AddComponent<FaceCamera>();

            var echoCase = pedestal.AddComponent<Combat.EchoDisplayCase>();
            echoCase.memoryLabel = label;
            echoCase.SetGlow(glowR);
        }

        // ================= 第十区：两元赌桌（公平与承诺线 其一） =================

        static void BuildGambleDen(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[9];

            Color wall = new Color(0.3f, 0.26f, 0.22f);

            // 狭小棋牌室：压迫感来自空间小 + 围观者多
            Box(ctx, "Gamble_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(28, 0.5f, 28),
                new Color(0.4f, 0.33f, 0.26f));
            Ring(ctx, o, 14, 4, wall);

            // 破旧灯泡：昏黄低光
            var bulb = new GameObject("GambleBulb");
            bulb.transform.position = o + new Vector3(0, 3.6f, 2);
            var bl = bulb.AddComponent<Light>();
            bl.type = LightType.Point;
            bl.range = 18;
            bl.intensity = 1.0f;
            bl.color = new Color(1f, 0.8f, 0.5f);
            Decoration(ctx, "BulbWire", o + new Vector3(0, 4.0f, 2), new Vector3(0.05f, 0.9f, 0.05f),
                new Color(0.15f, 0.15f, 0.15f));

            // 中央赌桌（视觉中心 + 可绕行掩体）
            var table = Box(ctx, "GambleTable", o + new Vector3(0, 0.5f, 2), new Vector3(3.6f, 1.0f, 2.4f),
                new Color(0.16f, 0.35f, 0.22f));
            Decoration(ctx, "TableTrim", o + new Vector3(0, 1.02f, 2), new Vector3(3.7f, 0.06f, 2.5f),
                new Color(0.35f, 0.24f, 0.16f));
            // 桌上散落的硬币与纸牌
            var rng = new System.Random(21);
            for (int i = 0; i < 6; i++)
                Decoration(ctx, "Coin", o + new Vector3(-1.2f + (float)rng.NextDouble() * 2.4f, 1.1f,
                    1.2f + (float)rng.NextDouble() * 1.6f),
                    new Vector3(0.16f, 0.03f, 0.16f), new Color(0.95f, 0.85f, 0.4f));
            for (int i = 0; i < 4; i++)
                Decoration(ctx, "Card", o + new Vector3(-1f + i * 0.7f, 1.08f, 2.4f),
                    new Vector3(0.3f, 0.02f, 0.42f), new Color(0.92f, 0.92f, 0.88f));

            // 椅子（可撞倒的障碍感——低矮碰撞体）
            Box(ctx, "GambleChair", o + new Vector3(-2.8f, 0.35f, 2), new Vector3(0.9f, 0.7f, 0.9f),
                new Color(0.3f, 0.26f, 0.22f));
            Box(ctx, "GambleChair", o + new Vector3(2.8f, 0.35f, 2), new Vector3(0.9f, 0.7f, 0.9f),
                new Color(0.3f, 0.26f, 0.22f));
            Box(ctx, "GambleChair", o + new Vector3(0, 0.35f, 4.4f), new Vector3(0.9f, 0.7f, 0.9f),
                new Color(0.3f, 0.26f, 0.22f));

            // 账本对质（核心机制）：桌角的账本——走近即令赖账王语塞破绽
            var ledger = Box(ctx, "Ledger", o + new Vector3(1.5f, 1.12f, 1.3f),
                new Vector3(0.7f, 0.14f, 0.5f), new Color(0.55f, 0.42f, 0.25f));
            Object.DestroyImmediate(ledger.GetComponent<Collider>());
            var ledgerRoot = new GameObject("LedgerRoot");
            ledgerRoot.transform.position = o + new Vector3(1.5f, 1.0f, 1.3f);
            ledgerRoot.AddComponent<Combat.LedgerProp>();

            // 四周围观者阴影（被围观的压迫感）
            for (int i = 0; i < 8; i++)
            {
                float ang = i * Mathf.PI / 4f;
                Decoration(ctx, "OnlookerShadow",
                    o + new Vector3(Mathf.Cos(ang) * 11f, 1.1f, Mathf.Sin(ang) * 11f),
                    new Vector3(1.0f, 2.2f, 0.6f), new Color(0.1f, 0.1f, 0.12f));
            }

            // 传送门：回训练武馆（南）/ 通往债务车影（北，击败赖账王章节推进即解锁）
            MakePortal(ctx, o + new Vector3(-8f, 0, -12), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(8f, 0, 12), PortalRole.Forward);
        }

        // ================= 第十一区：债务车影（公平与承诺线 其二） =================

        static void BuildDebtCarPark(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[10];
            Combat.DebtState.Reset();

            // 夜晚停车场：冷灰地面 + 立柱 + 车阵窄路
            Box(ctx, "CarPark_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(72, 0.5f, 62),
                new Color(0.3f, 0.3f, 0.33f));
            Ring(ctx, o, new Vector2(36, 31), 4, new Color(0.24f, 0.24f, 0.27f));

            // 停车位标线
            for (int i = 0; i < 8; i++)
            {
                Decoration(ctx, "ParkLine", o + new Vector3(-28 + i * 8, 0.03f, -14),
                    new Vector3(0.25f, 0.04f, 10), new Color(0.8f, 0.8f, 0.75f));
                Decoration(ctx, "ParkLine", o + new Vector3(-28 + i * 8, 0.03f, 2),
                    new Vector3(0.25f, 0.04f, 10), new Color(0.8f, 0.8f, 0.75f));
            }

            // 立柱阵（遮挡与绕位）
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    pillar.name = "ParkPillar";
                    pillar.transform.position = o + new Vector3(x * 16, 2f, z * 12 - 4);
                    pillar.transform.localScale = new Vector3(1.1f, 2f, 1.1f);
                    Paint(ctx, pillar, new Color(0.42f, 0.42f, 0.45f));
                    Player.CameraOcclusionFade.RegisterOccluder(pillar.GetComponent<Renderer>());
                }

            // 停放的车（车阵窄路：追逐与闪避空间）
            var rng = new System.Random(37);
            for (int i = 0; i < 7; i++)
            {
                float x = -24 + i * 8 + (i % 2) * 1.5f;
                float z = (i % 2 == 0) ? -14 : 2;
                var car = Box(ctx, "ParkedCar", o + new Vector3(x, 0.55f, z), new Vector3(1.9f, 1.1f, 4.2f),
                    new Color(0.25f + (float)rng.NextDouble() * 0.3f,
                              0.25f + (float)rng.NextDouble() * 0.3f,
                              0.3f + (float)rng.NextDouble() * 0.3f));
                var cabin = Decoration(ctx, "CarCabin", o + new Vector3(x, 1.35f, z - 0.1f),
                    new Vector3(1.6f, 0.5f, 2.1f), new Color(0.55f, 0.65f, 0.75f));
                cabin.transform.SetParent(car.transform, true);
            }

            // 地面积水（夜色反光）
            Decoration(ctx, "Puddle", o + new Vector3(-10, 0.02f, 8), new Vector3(5, 0.03f, 3.2f),
                new Color(0.35f, 0.42f, 0.55f));
            Decoration(ctx, "Puddle", o + new Vector3(14, 0.02f, -6), new Vector3(4, 0.03f, 2.6f),
                new Color(0.35f, 0.42f, 0.55f));

            // 中央幻影车：被强光照亮的"未结清的故事"
            var ghostCar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(ghostCar.GetComponent<Collider>());
            ghostCar.name = "PhantomCar";
            ghostCar.transform.position = o + new Vector3(0, 0.65f, 20);
            ghostCar.transform.localScale = new Vector3(2.1f, 1.3f, 4.6f);
            ghostCar.GetComponent<MeshRenderer>().sharedMaterial =
                Combat.CombatFeedback.EnergyMaterial(new Color(0.7f, 0.8f, 1f), 0.35f);
            var spot = new GameObject("PhantomCarLight");
            spot.transform.position = o + new Vector3(0, 6f, 20);
            var sl = spot.AddComponent<Light>();
            sl.type = LightType.Point;
            sl.range = 20;
            sl.intensity = 1.6f;
            sl.color = new Color(0.85f, 0.9f, 1f);
            // Boss 场地标识（贴地圆盘，无碰撞）
            var arena = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(arena.GetComponent<Collider>());
            arena.name = "DebtArena";
            arena.transform.position = o + new Vector3(0, 0.04f, 18);
            arena.transform.localScale = new Vector3(22f, 0.03f, 22f);
            Paint(ctx, arena, new Color(0.34f, 0.34f, 0.38f));

            // 三张欠条残片：集齐即碎债王护体
            Combat.DebtNoteProp.Spawn(o + new Vector3(-22, 1.2f, 12));
            Combat.DebtNoteProp.Spawn(o + new Vector3(24, 1.2f, 6));
            Combat.DebtNoteProp.Spawn(o + new Vector3(-4, 1.2f, -22));

            // 冷光灯
            AddCeilingLight(o + new Vector3(-14, 7f, -6), new Color(0.8f, 0.85f, 1f), 30);
            AddCeilingLight(o + new Vector3(14, 7f, 6), new Color(0.8f, 0.85f, 1f), 30);

            // 传送门：回两元赌桌（南）/ 通往小题大做审判庭（东北，公平线其三）
            MakePortal(ctx, o + new Vector3(-10f, 0, -28), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(30f, 0, 26), PortalRole.Forward);
        }

        // ================= 第十二区：眼神审判走廊（外界刺激线 其二） =================

        static void BuildGazeHall(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[11];

            Color wall = new Color(0.2f, 0.18f, 0.26f);

            // 狭长走廊（z -35..15）+ 尽头圆形镜厅（z 15..39）
            Box(ctx, "GazeHall_Corridor", o + new Vector3(0, -0.25f, -10), new Vector3(18, 0.5f, 50),
                new Color(0.26f, 0.24f, 0.32f));
            Box(ctx, "GazeHall_Hall", o + new Vector3(0, -0.25f, 27), new Vector3(30, 0.5f, 24),
                new Color(0.3f, 0.28f, 0.38f));

            // 走廊两壁
            Box(ctx, "GazeWall_W", o + new Vector3(-9, 2.5f, -10), new Vector3(0.8f, 5, 50), wall);
            Box(ctx, "GazeWall_E", o + new Vector3(9, 2.5f, -10), new Vector3(0.8f, 5, 50), wall);
            Box(ctx, "GazeWall_S", o + new Vector3(0, 2.5f, -35), new Vector3(18, 5, 0.8f), wall);
            // 镜厅围墙 + 走廊-镜厅接口墙
            Box(ctx, "GazeHallWall_W", o + new Vector3(-15, 2.5f, 27), new Vector3(0.8f, 5, 24), wall);
            Box(ctx, "GazeHallWall_E", o + new Vector3(15, 2.5f, 27), new Vector3(0.8f, 5, 24), wall);
            Box(ctx, "GazeHallWall_N", o + new Vector3(0, 2.5f, 39), new Vector3(30, 5, 0.8f), wall);
            Box(ctx, "GazeJoint_W", o + new Vector3(-12, 2.5f, 15), new Vector3(6.8f, 5, 0.8f), wall);
            Box(ctx, "GazeJoint_E", o + new Vector3(12, 2.5f, 15), new Vector3(6.8f, 5, 0.8f), wall);

            // 两壁的眼睛灯与镜面（被注视感）
            for (int i = 0; i < 7; i++)
            {
                float z = -30 + i * 7;
                for (int side = -1; side <= 1; side += 2)
                {
                    var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(eye.GetComponent<Collider>());
                    eye.name = "WallEye";
                    eye.transform.position = o + new Vector3(side * 8.4f, 2.6f, z);
                    eye.transform.localScale = new Vector3(0.5f, 0.5f, 0.25f);
                    Paint(ctx, eye, new Color(0.85f, 0.82f, 0.95f));
                    var pupil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(pupil.GetComponent<Collider>());
                    pupil.name = "WallEyePupil";
                    pupil.transform.position = o + new Vector3(side * 8.2f, 2.6f, z);
                    pupil.transform.localScale = Vector3.one * 0.2f;
                    Paint(ctx, pupil, new Color(0.2f, 0.15f, 0.3f));
                }
                if (i % 2 == 0)
                    Decoration(ctx, "CorridorMirror", o + new Vector3((i % 4 == 0 ? -8.3f : 8.3f), 1.9f, z + 3),
                        new Vector3(0.12f, 2.6f, 1.8f), new Color(0.6f, 0.68f, 0.82f));
            }

            // 凝视压力区：走廊中后段（定心姿态减免、不读心盾免疫）
            MakeZoneTrigger<Combat.NoiseZone>(o + new Vector3(0, 1, -14), new Vector3(16, 2.5f, 12));
            MakeZoneTrigger<Combat.NoiseZone>(o + new Vector3(0, 1, 4), new Vector3(16, 2.5f, 12));

            // 镜厅：环形镜面 + 中央凝视之台
            for (int i = 0; i < 8; i++)
            {
                float ang = i * Mathf.PI / 4f;
                var mirror = Decoration(ctx, "HallMirror",
                    o + new Vector3(Mathf.Cos(ang) * 12f, 2.2f, 27 + Mathf.Sin(ang) * 9f),
                    new Vector3(1.8f, 3.6f, 0.15f), new Color(0.62f, 0.7f, 0.85f));
                mirror.transform.rotation = Quaternion.Euler(0, ang * Mathf.Rad2Deg + 90f, 0);
            }
            var gazeDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(gazeDisc.GetComponent<Collider>());
            gazeDisc.name = "GazeArena";
            gazeDisc.transform.position = o + new Vector3(0, 0.04f, 27);
            gazeDisc.transform.localScale = new Vector3(18f, 0.03f, 18f);
            Paint(ctx, gazeDisc, new Color(0.36f, 0.34f, 0.46f));

            // 幽紫顶光
            AddCeilingLight(o + new Vector3(0, 8f, -14), new Color(0.7f, 0.65f, 0.9f), 34);
            AddCeilingLight(o + new Vector3(0, 8f, 27), new Color(0.75f, 0.7f, 1f), 32);

            // 传送门：回噪声街区（南）/ 通往陌生挑衅路口（镜厅东北角）
            MakePortal(ctx, o + new Vector3(0f, 0, -31), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(11f, 0, 35), PortalRole.Forward);
        }

        // ================= 第十三区：陌生挑衅路口（外界刺激线 其三） =================

        static void BuildCrossroad(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[12];

            Box(ctx, "Crossroad_Ground", o + new Vector3(0, -0.25f, 0), new Vector3(80, 0.5f, 80),
                new Color(0.45f, 0.45f, 0.47f));
            Ring(ctx, o, 40, 4, new Color(0.3f, 0.3f, 0.34f));

            // 十字马路（四条臂 = 车流幻影危险区；中心路口 = 战场）
            Decoration(ctx, "RoadX", o + new Vector3(0, 0.02f, 0), new Vector3(80, 0.04f, 9),
                new Color(0.2f, 0.2f, 0.22f));
            Decoration(ctx, "RoadZ", o + new Vector3(0, 0.03f, 0), new Vector3(9, 0.04f, 80),
                new Color(0.2f, 0.2f, 0.22f));
            // 危险区红色警示条 + 车流幻影伤害区（中心 |x|,|z|<8 为安全战场）
            var armDefs = new[]
            {
                (new Vector3(-24, 1, 0), new Vector3(30, 2f, 8)),
                (new Vector3(24, 1, 0), new Vector3(30, 2f, 8)),
                (new Vector3(0, 1, -24), new Vector3(8, 2f, 30)),
                (new Vector3(0, 1, 24), new Vector3(8, 2f, 30)),
            };
            foreach (var (pos, size) in armDefs)
            {
                var z = MakeZoneTrigger<Combat.TrafficPhantomZone>(o + pos, size);
                Decoration(ctx, "TrafficWarn", o + new Vector3(pos.x, 0.05f, pos.z),
                    new Vector3(size.x, 0.03f, size.z), new Color(0.55f, 0.22f, 0.2f));
            }
            // 中心路口铺装（安全战场标识）
            Decoration(ctx, "CrossCenter", o + new Vector3(0, 0.06f, 0), new Vector3(15, 0.03f, 15),
                new Color(0.55f, 0.53f, 0.5f));
            Crosswalk(ctx, o + new Vector3(0, 0.03f, 10.5f), false);
            Crosswalk(ctx, o + new Vector3(0, 0.03f, -10.5f), false);
            Crosswalk(ctx, o + new Vector3(10.5f, 0.03f, 0), true);
            Crosswalk(ctx, o + new Vector3(-10.5f, 0.03f, 0), true);

            // 红绿灯柱（四角）
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                float sz = (i < 2) ? -1f : 1f;
                Vector3 basePos = o + new Vector3(sx * 10f, 0, sz * 10f);
                var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pole.name = "TrafficPole";
                pole.transform.position = basePos + new Vector3(0, 2f, 0);
                pole.transform.localScale = new Vector3(0.18f, 2f, 0.18f);
                Paint(ctx, pole, new Color(0.25f, 0.25f, 0.28f));
                Decoration(ctx, "TrafficLightBox", basePos + new Vector3(0, 4.1f, 0),
                    new Vector3(0.4f, 1.0f, 0.4f), new Color(0.18f, 0.18f, 0.2f));
                Decoration(ctx, "TrafficRed", basePos + new Vector3(0, 4.4f, 0.22f),
                    new Vector3(0.22f, 0.22f, 0.06f), new Color(0.9f, 0.2f, 0.15f));
                Decoration(ctx, "TrafficGreen", basePos + new Vector3(0, 3.9f, 0.22f),
                    new Vector3(0.22f, 0.22f, 0.06f), new Color(0.2f, 0.85f, 0.3f));
            }

            // 街角围观人群阴影（挑衅的观众）
            var rng = new System.Random(53);
            for (int i = 0; i < 10; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                float sz = (i % 4 < 2) ? -1f : 1f;
                Decoration(ctx, "CrowdShadow",
                    o + new Vector3(sx * (14 + (float)rng.NextDouble() * 10),
                        1.1f, sz * (14 + (float)rng.NextDouble() * 10)),
                    new Vector3(1.0f, 2.2f, 0.6f), new Color(0.12f, 0.12f, 0.15f));
            }
            // 护栏（路口四角的短栏，可作掩体）
            Box(ctx, "GuardRail", o + new Vector3(12, 0.5f, 12), new Vector3(6, 1f, 0.3f),
                new Color(0.6f, 0.6f, 0.65f));
            Box(ctx, "GuardRail", o + new Vector3(-12, 0.5f, -12), new Vector3(6, 1f, 0.3f),
                new Color(0.6f, 0.6f, 0.65f));

            Lamp(ctx, o + new Vector3(16, 0, -16));
            Lamp(ctx, o + new Vector3(-16, 0, 16));

            // 传送门：回眼神审判走廊（西南）/ 回噪声街区（东南，刺激线终战方向）
            MakePortal(ctx, o + new Vector3(-30f, 0, -30), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(30f, 0, -30), PortalRole.Forward);
        }

        // ================= 第十四区：目标遗忘房（拖延与目标线 其一） =================

        static void BuildGoalRoom(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[13];

            Color wall = new Color(0.42f, 0.38f, 0.34f);

            // 杂乱房间展开成的小迷宫：目标板在最深处
            Box(ctx, "GoalRoom_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(36, 0.5f, 34),
                new Color(0.45f, 0.4f, 0.33f));
            Ring(ctx, o, new Vector2(18, 17), 4, wall);

            // 迷宫隔墙（三道展开的墙：逼玩家绕行，路上全是干扰物）
            Box(ctx, "MazeWall", o + new Vector3(-5, 1.5f, -7), new Vector3(20, 3, 0.6f), wall);
            Box(ctx, "MazeWall", o + new Vector3(6, 1.5f, 0), new Vector3(22, 3, 0.6f), wall);
            Box(ctx, "MazeWall", o + new Vector3(-6, 1.5f, 7), new Vector3(20, 3, 0.6f), wall);

            // 杂乱家具：床（藤蔓化）/衣物堆/未完成任务箱
            Box(ctx, "MessyBed", o + new Vector3(-13, 0.35f, -12), new Vector3(3, 0.7f, 4.6f),
                new Color(0.32f, 0.42f, 0.34f));
            Decoration(ctx, "BedVineWrap", o + new Vector3(-13, 0.8f, -12), new Vector3(3.2f, 0.2f, 4.8f),
                new Color(0.24f, 0.36f, 0.22f));
            Box(ctx, "ClothesPile", o + new Vector3(10, 0.4f, -12), new Vector3(2.6f, 0.8f, 2.2f),
                new Color(0.5f, 0.42f, 0.5f));
            Box(ctx, "TaskBox", o + new Vector3(14, 0.5f, 3), new Vector3(1.8f, 1f, 1.8f),
                new Color(0.5f, 0.38f, 0.24f));
            Decoration(ctx, "TaskBoxLabel", o + new Vector3(14, 1.05f, 3), new Vector3(1.5f, 0.05f, 1.5f),
                new Color(0.85f, 0.82f, 0.7f));

            // 床铺藤蔓减速带 + 手机光点（干扰物：拖住你、吸走你）
            MireZone(ctx, o + new Vector3(-10, 0, -10), new Vector3(10, 2, 6), 0.6f, 3f, false);
            PhoneLight(ctx, o + new Vector3(12, 0, -4));

            // 便利贴与计划纸（散落的"想做但没做"）
            var rng = new System.Random(77);
            for (int i = 0; i < 12; i++)
                Decoration(ctx, "StickyNote",
                    o + new Vector3(-14 + (float)rng.NextDouble() * 28, 0.05f,
                        -14 + (float)rng.NextDouble() * 26),
                    new Vector3(0.4f, 0.02f, 0.4f),
                    new Color(0.95f, 0.9f, 0.5f + (float)rng.NextDouble() * 0.3f));

            // 闹钟（一次性恢复行动力）与窗户（一次性恢复专注）
            var clock = Box(ctx, "AlarmClock", o + new Vector3(-14, 0.9f, 5), new Vector3(0.8f, 0.8f, 0.5f),
                new Color(0.85f, 0.3f, 0.25f));
            MakeZoneTrigger<Combat.GoalStoneZone>(o + new Vector3(-14, 1, 5), new Vector3(3f, 2f, 3f));
            Decoration(ctx, "RoomWindow", o + new Vector3(17.6f, 2f, 8), new Vector3(0.15f, 1.8f, 3),
                new Color(0.65f, 0.8f, 1f));

            // 最深处：落灰的目标板（击败目标遗忘者后在此恢复并被提示钉目标）
            var board = Box(ctx, "DustyGoalBoard", o + new Vector3(0, 1.6f, 15.4f),
                new Vector3(3.4f, 1.8f, 0.2f), new Color(0.75f, 0.65f, 0.35f));
            Decoration(ctx, "BoardDust", o + new Vector3(0, 1.6f, 15.25f), new Vector3(3.0f, 1.4f, 0.05f),
                new Color(0.6f, 0.56f, 0.48f));
            board.AddComponent<Combat.GoalBoard>();

            // 暖光（房间该有的温度，只是落了灰）
            var lampGo = new GameObject("GoalRoom_Lamp");
            lampGo.transform.position = o + new Vector3(0, 3.4f, 2);
            var lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 24;
            lamp.intensity = 1.0f;
            lamp.color = new Color(1f, 0.88f, 0.68f);

            // 传送门：回噪声街区（南）/ 通往拖延沼泽（东北，拖延线其二）
            MakePortal(ctx, o + new Vector3(-9f, 0, -14), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(13f, 0, 14), PortalRole.Forward);
        }

        // ================= 第十五区：老实人消耗局（边界与责任线 其一） =================

        static void BuildFavorHall(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[14];

            Color wall = new Color(0.36f, 0.33f, 0.3f);

            // 不断扩张的办公大厅：四周是"请求入口"，中央是边界圈
            Box(ctx, "FavorHall_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(56, 0.5f, 52),
                new Color(0.5f, 0.47f, 0.42f));
            Ring(ctx, o, new Vector2(28, 26), 5, wall);

            // 四个请求入口（门洞装饰 + 门口的请求单堆）
            var gateDefs = new[]
            {
                (new Vector3(-24, 0, 0), 90f, "时间"),
                (new Vector3(24, 0, 0), 90f, "金钱"),
                (new Vector3(0, 0, 22), 0f, "精力"),
                (new Vector3(-14, 0, 22), 0f, "情绪"),
            };
            foreach (var (pos, rot, res) in gateDefs)
            {
                var frame = Decoration(ctx, "RequestGate_" + res, o + pos + new Vector3(0, 2.2f, 0),
                    new Vector3(3.4f, 4.4f, 0.5f), new Color(0.28f, 0.25f, 0.3f));
                frame.transform.rotation = Quaternion.Euler(0, rot, 0);
                var paper = Decoration(ctx, "RequestPile", o + pos + new Vector3(0, 0.3f, 0),
                    new Vector3(1.8f, 0.6f, 1.4f), new Color(0.9f, 0.88f, 0.8f));
                paper.transform.rotation = Quaternion.Euler(0, rot + 15f, 0);
            }

            // 文件堆与电话（被请求淹没的办公桌景）
            for (int i = 0; i < 5; i++)
            {
                var desk = Box(ctx, "FavorDesk", o + new Vector3(-16 + i * 8, 0.5f, -14),
                    new Vector3(3, 1, 1.6f), new Color(0.42f, 0.32f, 0.22f));
                Decoration(ctx, "DeskFiles", o + new Vector3(-16 + i * 8, 1.15f, -14),
                    new Vector3(1.6f, 0.3f, 1.0f), new Color(0.85f, 0.83f, 0.75f));
            }
            Decoration(ctx, "OfficePhone", o + new Vector3(-16, 1.15f, -13.6f),
                new Vector3(0.5f, 0.2f, 0.35f), new Color(0.15f, 0.15f, 0.18f));

            // 中央边界圈：守住的区域（站入恢复边界、清过度负责）
            var circle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(circle.GetComponent<Collider>());
            circle.name = "FavorBoundaryCircle";
            circle.transform.position = o + new Vector3(0, 0.04f, 2);
            circle.transform.localScale = new Vector3(6f, 0.02f, 6f);
            Paint(ctx, circle, new Color(0.3f, 0.75f, 0.5f));
            MakeZoneTrigger<Combat.BoundaryCircle>(o + new Vector3(0, 1f, 2), new Vector3(6f, 2f, 6f));

            // 好人卡装饰（散落的金色卡片：曾经的"你人真好"）
            var rng = new System.Random(91);
            for (int i = 0; i < 10; i++)
                Decoration(ctx, "GoodCard",
                    o + new Vector3(-20 + (float)rng.NextDouble() * 40, 0.04f,
                        -20 + (float)rng.NextDouble() * 40),
                    new Vector3(0.5f, 0.02f, 0.75f), new Color(0.95f, 0.8f, 0.4f));

            AddCeilingLight(o + new Vector3(0, 8f, 0), new Color(0.95f, 0.92f, 0.85f), 44);

            // 传送门：回城市广场（南）/ 通往责任转嫁法院（东北，边界线其二）
            MakePortal(ctx, o + new Vector3(-10f, 0, -24), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(22f, 0, 22), PortalRole.Forward);
        }

        // ================= 第十六区：无限代付走廊（边界与责任线 其三） =================

        static void BuildPayCorridor(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[15];

            Color wall = new Color(0.28f, 0.3f, 0.28f);

            // 长走廊：两侧无数资源之门，尽头是无限代付者的圆厅
            Box(ctx, "PayCorridor_Floor", o + new Vector3(0, -0.25f, -8), new Vector3(16, 0.5f, 60),
                new Color(0.38f, 0.4f, 0.38f));
            Box(ctx, "PayHall_Floor", o + new Vector3(0, -0.25f, 30), new Vector3(30, 0.5f, 20),
                new Color(0.42f, 0.44f, 0.42f));
            Box(ctx, "PayWall_W", o + new Vector3(-8, 2.5f, -8), new Vector3(0.8f, 5, 60), wall);
            Box(ctx, "PayWall_E", o + new Vector3(8, 2.5f, -8), new Vector3(0.8f, 5, 60), wall);
            Box(ctx, "PayWall_S", o + new Vector3(0, 2.5f, -38), new Vector3(16, 5, 0.8f), wall);
            Box(ctx, "PayHallWall_W", o + new Vector3(-15, 2.5f, 30), new Vector3(0.8f, 5, 20), wall);
            Box(ctx, "PayHallWall_E", o + new Vector3(15, 2.5f, 30), new Vector3(0.8f, 5, 20), wall);
            Box(ctx, "PayHallWall_N", o + new Vector3(0, 2.5f, 40), new Vector3(30, 5, 0.8f), wall);
            Box(ctx, "PayJoint_W", o + new Vector3(-11.5f, 2.5f, 22), new Vector3(7.8f, 5, 0.8f), wall);
            Box(ctx, "PayJoint_E", o + new Vector3(11.5f, 2.5f, 22), new Vector3(7.8f, 5, 0.8f), wall);

            // 两侧资源之门：时间/金钱/精力/情绪/注意力/责任/同情/解释
            string[] doors = { "时间", "金钱", "精力", "情绪", "注意力", "责任", "同情", "解释" };
            for (int i = 0; i < doors.Length; i++)
            {
                float z = -32 + i * 7;
                float side = (i % 2 == 0) ? -1f : 1f;
                Decoration(ctx, "PayDoor_" + doors[i], o + new Vector3(side * 7.4f, 2f, z),
                    new Vector3(0.3f, 4f, 2.6f), new Color(0.22f, 0.2f, 0.24f));
                var tmGo = new GameObject("PayDoorSign");
                tmGo.transform.position = o + new Vector3(side * 6.9f, 3.2f, z);
                WorldText.Attach(tmGo, doors[i], 44, 0.045f, new Color(0.85f, 0.9f, 0.85f));
                tmGo.AddComponent<FaceCamera>();
            }

            // 六个「代付请求区」：举盾通过=明确拒绝（边界回补），空手通过=默认代付（被扣资源）
            for (int i = 0; i < 6; i++)
            {
                float z = -30 + i * 8.6f;
                Decoration(ctx, "PayRequestStrip", o + new Vector3(0, 0.03f, z),
                    new Vector3(14, 0.03f, 2.6f), new Color(0.5f, 0.42f, 0.3f));
                MakeZoneTrigger<Combat.PayRequestZone>(o + new Vector3(0, 1, z), new Vector3(14, 2.2f, 2.6f));
            }

            // 消耗账单雨（装饰）
            var rng = new System.Random(19);
            for (int i = 0; i < 14; i++)
                Decoration(ctx, "BillPaper",
                    o + new Vector3(-6 + (float)rng.NextDouble() * 12,
                        0.6f + (float)rng.NextDouble() * 2.8f, -34 + (float)rng.NextDouble() * 56),
                    new Vector3(0.4f, 0.02f, 0.55f), new Color(0.88f, 0.9f, 0.85f));

            // Boss 圆厅场地标识
            var payArena = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(payArena.GetComponent<Collider>());
            payArena.name = "PayArena";
            payArena.transform.position = o + new Vector3(0, 0.04f, 30);
            payArena.transform.localScale = new Vector3(18f, 0.03f, 18f);
            Paint(ctx, payArena, new Color(0.34f, 0.4f, 0.36f));

            AddCeilingLight(o + new Vector3(0, 8f, -14), new Color(0.85f, 0.9f, 0.85f), 36);
            AddCeilingLight(o + new Vector3(0, 8f, 30), new Color(0.9f, 0.95f, 0.9f), 32);

            // 传送门：回责任转嫁法院（南）/ 通往饥饿荒巷（圆厅东北，低谷线开启即解锁）
            MakePortal(ctx, o + new Vector3(0f, 0, -35), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(11f, 0, 36), PortalRole.Forward);
        }

        // ================= 第十七区：饥饿荒巷（低谷与生存线 其一） =================

        static void BuildHungerAlley(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[16];

            Color wall = new Color(0.24f, 0.22f, 0.2f);

            // 夜晚城市小巷：窄长，资源稀少，尽头是温暖灯光区
            Box(ctx, "Alley_Floor", o + new Vector3(0, -0.25f, -6), new Vector3(20, 0.5f, 56),
                new Color(0.3f, 0.29f, 0.28f));
            Box(ctx, "Alley_EndFloor", o + new Vector3(0, -0.25f, 28), new Vector3(32, 0.5f, 14),
                new Color(0.36f, 0.33f, 0.3f));
            Box(ctx, "AlleyWall_W", o + new Vector3(-10, 3f, -6), new Vector3(0.8f, 6, 56), wall);
            Box(ctx, "AlleyWall_E", o + new Vector3(10, 3f, -6), new Vector3(0.8f, 6, 56), wall);
            Box(ctx, "AlleyWall_S", o + new Vector3(0, 3f, -34), new Vector3(20, 6, 0.8f), wall);
            Box(ctx, "AlleyEnd_W", o + new Vector3(-16, 3f, 28), new Vector3(0.8f, 6, 14), wall);
            Box(ctx, "AlleyEnd_E", o + new Vector3(16, 3f, 28), new Vector3(0.8f, 6, 14), wall);
            Box(ctx, "AlleyEnd_N", o + new Vector3(0, 3f, 35), new Vector3(32, 6, 0.8f), wall);
            Box(ctx, "AlleyJoint_W", o + new Vector3(-13, 3f, 21), new Vector3(7f, 6, 0.8f), wall);
            Box(ctx, "AlleyJoint_E", o + new Vector3(13, 3f, 21), new Vector3(7f, 6, 0.8f), wall);

            // 巷内杂物：垃圾桶/空纸箱/破墙涂鸦/雨水洼
            TrashBin(ctx, o + new Vector3(-8, 0, -26));
            TrashBin(ctx, o + new Vector3(7, 0, -10));
            Box(ctx, "CardboardBox", o + new Vector3(-7, 0.45f, -16), new Vector3(1.4f, 0.9f, 1.1f),
                new Color(0.55f, 0.45f, 0.3f));
            Box(ctx, "CardboardBox", o + new Vector3(8, 0.35f, 2), new Vector3(1.1f, 0.7f, 1.0f),
                new Color(0.5f, 0.42f, 0.28f));
            Decoration(ctx, "RainPuddle", o + new Vector3(2, 0.02f, -20), new Vector3(4, 0.03f, 2.4f),
                new Color(0.32f, 0.38f, 0.48f));
            Decoration(ctx, "RainPuddle", o + new Vector3(-4, 0.02f, 6), new Vector3(3, 0.03f, 2),
                new Color(0.32f, 0.38f, 0.48f));

            // 资源拾取：水瓶与食物包（低谷线核心：先解决生存）
            Combat.SupplyPickup.Spawn(o + new Vector3(-7, 0.8f, -22), "水瓶", new Color(0.5f, 0.75f, 0.95f));
            Combat.SupplyPickup.Spawn(o + new Vector3(7.5f, 0.8f, -4), "食物包", new Color(0.85f, 0.7f, 0.4f));
            Combat.SupplyPickup.Spawn(o + new Vector3(-6, 0.8f, 12), "食物包", new Color(0.85f, 0.7f, 0.4f));

            // 路灯安全区（光下敢停留）与求助电话亭
            Lamp(ctx, o + new Vector3(-7, 0, -2));
            Lamp(ctx, o + new Vector3(8, 0, 16));
            var booth = Box(ctx, "PhoneBooth", o + new Vector3(12, 1.3f, 30), new Vector3(1.6f, 2.6f, 1.6f),
                new Color(0.2f, 0.45f, 0.4f));
            booth.AddComponent<Combat.HelpPhoneBooth>();

            // 尽头温暖灯光区：远处餐馆灯牌
            Decoration(ctx, "DinerSign", o + new Vector3(0, 4.2f, 34.4f), new Vector3(8, 1.2f, 0.2f),
                new Color(1f, 0.7f, 0.35f));
            var warmGo = new GameObject("Alley_WarmLight");
            warmGo.transform.position = o + new Vector3(0, 4f, 30);
            var wl = warmGo.AddComponent<Light>();
            wl.type = LightType.Point;
            wl.range = 20;
            wl.intensity = 1.5f;
            wl.color = new Color(1f, 0.75f, 0.45f);

            // 传送门：回无限代付走廊（南）/ 通往车库寒夜（尽头西侧）
            MakePortal(ctx, o + new Vector3(0f, 0, -31), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(-12f, 0, 30), PortalRole.Forward);
        }

        // ================= 第十八区：车库寒夜（低谷与生存线 其二） =================

        static void BuildColdGarage(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[17];

            Color wall = new Color(0.3f, 0.32f, 0.36f);

            // 寒冷地下车库：大部分区域是寒冷区，取暖点是生命线
            Box(ctx, "Garage_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(64, 0.5f, 56),
                new Color(0.36f, 0.38f, 0.42f));
            Ring(ctx, o, new Vector2(32, 28), 4, wall);

            // 水泥柱阵
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    pillar.name = "GaragePillar";
                    pillar.transform.position = o + new Vector3(x * 14, 2f, z * 12);
                    pillar.transform.localScale = new Vector3(1.2f, 2f, 1.2f);
                    Paint(ctx, pillar, new Color(0.44f, 0.46f, 0.5f));
                    Player.CameraOcclusionFade.RegisterOccluder(pillar.GetComponent<Renderer>());
                }

            // 废弃车辆与纸箱
            Box(ctx, "AbandonedCar", o + new Vector3(-20, 0.55f, -14), new Vector3(1.9f, 1.1f, 4.2f),
                new Color(0.3f, 0.32f, 0.3f));
            Box(ctx, "AbandonedCar", o + new Vector3(18, 0.55f, 8), new Vector3(1.9f, 1.1f, 4.2f),
                new Color(0.32f, 0.3f, 0.34f));
            Box(ctx, "SleepBox", o + new Vector3(-24, 0.4f, 10), new Vector3(2.2f, 0.8f, 1.4f),
                new Color(0.5f, 0.42f, 0.3f));

            // 寒冷区覆盖全场（取暖点给暖意豁免）
            MakeZoneTrigger<Combat.ColdZone>(o + new Vector3(0, 1, 0), new Vector3(62, 2.4f, 54));

            // 三个取暖点：火盆（橙光）——寒夜里的生命线
            var warmDefs = new[] { new Vector3(-18, 0, -4), new Vector3(6, 0, -18), new Vector3(14, 0, 18) };
            foreach (var wpos in warmDefs)
            {
                var basin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                basin.name = "WarmBasin";
                basin.transform.position = o + wpos + new Vector3(0, 0.3f, 0);
                basin.transform.localScale = new Vector3(1.1f, 0.3f, 1.1f);
                Paint(ctx, basin, new Color(0.4f, 0.3f, 0.22f));
                var flame = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(flame.GetComponent<Collider>());
                flame.name = "WarmFlame";
                flame.transform.position = o + wpos + new Vector3(0, 1.0f, 0);
                flame.transform.localScale = new Vector3(0.6f, 0.8f, 0.6f);
                flame.GetComponent<MeshRenderer>().sharedMaterial =
                    Combat.CombatFeedback.EnergyMaterial(new Color(1f, 0.55f, 0.2f), 0.8f);
                var lg = new GameObject("WarmLight");
                lg.transform.position = o + wpos + new Vector3(0, 2f, 0);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 12;
                l.intensity = 1.5f;
                l.color = new Color(1f, 0.6f, 0.3f);
                MakeZoneTrigger<Combat.WarmSpot>(o + wpos + Vector3.up, new Vector3(6f, 2.4f, 6f));
            }

            // 冷光灯（车库的惨白）
            AddCeilingLight(o + new Vector3(0, 6.5f, 0), new Color(0.75f, 0.8f, 0.95f), 40);

            // 资源与求助电话
            Combat.SupplyPickup.Spawn(o + new Vector3(22, 0.8f, -18), "热水", new Color(0.9f, 0.6f, 0.4f));
            var booth = Box(ctx, "GaragePhoneBooth", o + new Vector3(-26, 1.3f, 22), new Vector3(1.6f, 2.6f, 1.6f),
                new Color(0.2f, 0.45f, 0.4f));
            booth.AddComponent<Combat.HelpPhoneBooth>();

            // 传送门：回饥饿荒巷（南）/ 通往病房回廊（东北）
            MakePortal(ctx, o + new Vector3(-8f, 0, -25), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(26f, 0, 24), PortalRole.Forward);
        }

        // ================= 第十九区：病房回廊（低谷与生存线 终战） =================

        static void BuildWardCorridor(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[18];

            Color wall = new Color(0.6f, 0.62f, 0.62f);

            // 医院走廊：低战斗强度，重在情绪承受——白色灯光、病房门、账单雨
            Box(ctx, "Ward_Floor", o + new Vector3(0, -0.25f, -8), new Vector3(18, 0.5f, 56),
                new Color(0.68f, 0.7f, 0.7f));
            Box(ctx, "WardHall_Floor", o + new Vector3(0, -0.25f, 30), new Vector3(32, 0.5f, 20),
                new Color(0.64f, 0.66f, 0.66f));
            Box(ctx, "WardWall_W", o + new Vector3(-9, 2.5f, -8), new Vector3(0.8f, 5, 56), wall);
            Box(ctx, "WardWall_E", o + new Vector3(9, 2.5f, -8), new Vector3(0.8f, 5, 56), wall);
            Box(ctx, "WardWall_S", o + new Vector3(0, 2.5f, -36), new Vector3(18, 5, 0.8f), wall);
            Box(ctx, "WardHallWall_W", o + new Vector3(-16, 2.5f, 30), new Vector3(0.8f, 5, 20), wall);
            Box(ctx, "WardHallWall_E", o + new Vector3(16, 2.5f, 30), new Vector3(0.8f, 5, 20), wall);
            Box(ctx, "WardHallWall_N", o + new Vector3(0, 2.5f, 40), new Vector3(32, 5, 0.8f), wall);
            Box(ctx, "WardJoint_W", o + new Vector3(-12.5f, 2.5f, 20), new Vector3(7.8f, 5, 0.8f), wall);
            Box(ctx, "WardJoint_E", o + new Vector3(12.5f, 2.5f, 20), new Vector3(7.8f, 5, 0.8f), wall);

            // 病房门与安静提示牌
            for (int i = 0; i < 5; i++)
            {
                float z = -30 + i * 10;
                float side = (i % 2 == 0) ? -1f : 1f;
                Decoration(ctx, "WardDoor", o + new Vector3(side * 8.4f, 1.6f, z),
                    new Vector3(0.25f, 3.2f, 2.2f), new Color(0.45f, 0.55f, 0.6f));
                Decoration(ctx, "WardDoorSign", o + new Vector3(side * 8.2f, 3.1f, z),
                    new Vector3(0.15f, 0.5f, 1.0f), new Color(0.85f, 0.88f, 0.9f));
            }
            Decoration(ctx, "QuietSign", o + new Vector3(0, 3.6f, -35.4f), new Vector3(4, 0.9f, 0.2f),
                new Color(0.35f, 0.6f, 0.85f));

            // 长椅与护士站
            Bench(ctx, o + new Vector3(-6, 0, -12), 90);
            Bench(ctx, o + new Vector3(6, 0, 4), 270);
            Box(ctx, "NurseStation", o + new Vector3(-6, 0.7f, 14), new Vector3(4, 1.4f, 2),
                new Color(0.75f, 0.78f, 0.8f));

            // 账单雨（漂浮的医药账单）
            var rng = new System.Random(33);
            for (int i = 0; i < 16; i++)
                Decoration(ctx, "MedBill",
                    o + new Vector3(-7 + (float)rng.NextDouble() * 14,
                        0.8f + (float)rng.NextDouble() * 2.8f, -32 + (float)rng.NextDouble() * 66),
                    new Vector3(0.4f, 0.02f, 0.55f), new Color(0.92f, 0.94f, 0.96f));

            // 情绪恢复点（长椅旁的暖灯）与两座求助电话亭（Boss 破防机制）
            var warm = new GameObject("WardWarmLight");
            warm.transform.position = o + new Vector3(-6, 2.5f, -12);
            var wl2 = warm.AddComponent<Light>();
            wl2.type = LightType.Point;
            wl2.range = 8;
            wl2.intensity = 1.2f;
            wl2.color = new Color(1f, 0.85f, 0.6f);
            var booth1 = Box(ctx, "WardPhoneBooth", o + new Vector3(-13, 1.3f, 26), new Vector3(1.6f, 2.6f, 1.6f),
                new Color(0.2f, 0.45f, 0.4f));
            booth1.AddComponent<Combat.HelpPhoneBooth>();
            var booth2 = Box(ctx, "WardPhoneBooth", o + new Vector3(13, 1.3f, 34), new Vector3(1.6f, 2.6f, 1.6f),
                new Color(0.2f, 0.45f, 0.4f));
            booth2.AddComponent<Combat.HelpPhoneBooth>();

            // 白色顶光（医院的冷静）
            AddCeilingLight(o + new Vector3(0, 7f, -12), new Color(0.95f, 0.97f, 1f), 36);
            AddCeilingLight(o + new Vector3(0, 7f, 30), new Color(0.95f, 0.97f, 1f), 34);

            // 传送门：回车库寒夜（南）/ 通往哲学虚无图书馆（大厅东北，哲学线开启即解锁）
            MakePortal(ctx, o + new Vector3(0f, 0, -33), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(12f, 0, 36), PortalRole.Forward);
        }

        // ================= 第二十区：哲学虚无图书馆（哲学与行动线 其一） =================

        static void BuildPhilLibrary(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[19];
            Combat.PhilState.Reset();   // 重建世界即清空灯台/行动之门计数

            Color wall = new Color(0.34f, 0.3f, 0.26f);
            Color shelf = new Color(0.42f, 0.32f, 0.2f);

            // 昏暗的大图书馆：无穷书架间弥漫"读了这么多还是不知道怎么活"的雾
            Box(ctx, "Library_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(66, 0.5f, 74),
                new Color(0.35f, 0.32f, 0.28f));
            Ring(ctx, o, new Vector2(33, 37), 6, wall);

            // 书架阵（留出中央走道与灯台空地）
            for (int row = 0; row < 4; row++)
                for (int col = -2; col <= 2; col++)
                {
                    if (col == 0) continue; // 中央走道
                    float z = -22 + row * 12;
                    var shelfGo = Box(ctx, "BookShelf", o + new Vector3(col * 11, 2.2f, z),
                        new Vector3(6.5f, 4.4f, 1.6f), shelf);
                    Player.CameraOcclusionFade.RegisterOccluder(shelfGo.GetComponent<Renderer>());
                    // 书脊色带
                    Decoration(ctx, "BookSpines", o + new Vector3(col * 11, 2.2f, z + 0.85f),
                        new Vector3(6.1f, 3.6f, 0.1f),
                        new Color(0.3f + (row * 0.08f), 0.25f, 0.2f + (col + 2) * 0.05f));
                }

            // 散落的书堆与阅读桌
            var rng = new System.Random(47);
            for (int i = 0; i < 10; i++)
                Decoration(ctx, "BookPile",
                    o + new Vector3(-26 + (float)rng.NextDouble() * 52, 0.25f,
                        -30 + (float)rng.NextDouble() * 60),
                    new Vector3(0.9f, 0.5f, 0.7f), new Color(0.55f, 0.5f, 0.4f));
            Box(ctx, "ReadingDesk", o + new Vector3(-8, 0.7f, -28), new Vector3(3.5f, 1.4f, 1.6f),
                new Color(0.45f, 0.36f, 0.24f));
            Box(ctx, "ReadingDesk", o + new Vector3(8, 0.7f, -28), new Vector3(3.5f, 1.4f, 1.6f),
                new Color(0.45f, 0.36f, 0.24f));

            // 三座行动灯台（三座齐亮 → 概念迷宫师引文护体崩碎）
            var lampDefs = new[] { new Vector3(-22, 0, 6), new Vector3(22, 0, 6), new Vector3(0, 0, 30) };
            foreach (var lpos in lampDefs)
            {
                var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pedestal.name = "ActionLamp";
                pedestal.transform.position = o + lpos + new Vector3(0, 0.6f, 0);
                pedestal.transform.localScale = new Vector3(0.9f, 0.6f, 0.9f);
                Paint(ctx, pedestal, new Color(0.5f, 0.42f, 0.28f));
                var root = new GameObject("ActionLampRoot");
                root.transform.position = o + lpos;
                root.AddComponent<Combat.ActionLampAltar>();
            }

            // 昏黄吊灯（图书馆的沉郁）
            AddCeilingLight(o + new Vector3(0, 7f, -14), new Color(0.85f, 0.75f, 0.55f), 38);
            AddCeilingLight(o + new Vector3(0, 7f, 18), new Color(0.85f, 0.75f, 0.55f), 38);

            // 传送门：回病房回廊（南）/ 通往无限追问大厅（北，其二开启即解锁）
            MakePortal(ctx, o + new Vector3(0f, 0, -34), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(0f, 0, 34), PortalRole.Forward);
        }

        // ================= 第二十一区：无限追问大厅（哲学与行动线 其二） =================

        static void BuildQuestionHall(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[20];

            Color wall = new Color(0.3f, 0.3f, 0.38f);

            // 环形大厅：墙上挂满问题之门（诱饵），只有发亮的行动之门通向前方
            Box(ctx, "Hall_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(60, 0.5f, 76),
                new Color(0.32f, 0.32f, 0.38f));
            Ring(ctx, o, new Vector2(30, 38), 6, wall);

            // 三道隔断墙把大厅分成四段，每段留一个行动之门缺口（其余是问题之门）
            float[] gateZ = { -18, 0, 18 };
            float[] gapX = { -18, 12, -6 };
            string[] questions = { "为什么偏偏是我？", "如果又失败了怎么办？", "人生到底有什么意义？" };
            for (int i = 0; i < 3; i++)
            {
                float z = gateZ[i];
                float gx = gapX[i];
                // 隔断墙（缺口两侧）
                float leftW = (gx - 2.2f) - (-30);
                float rightW = 30 - (gx + 2.2f);
                Box(ctx, "HallDivider_L", o + new Vector3(-30 + leftW / 2f, 2.5f, z),
                    new Vector3(leftW, 5, 1), wall);
                Box(ctx, "HallDivider_R", o + new Vector3(30 - rightW / 2f, 2.5f, z),
                    new Vector3(rightW, 5, 1), wall);

                // 缺口即"行动之门"：发亮门框 + 恢复触发器
                Decoration(ctx, "ActionDoorGlow", o + new Vector3(gx, 2.2f, z),
                    new Vector3(4.2f, 4.4f, 0.2f), new Color(1f, 0.8f, 0.35f));
                MakeZoneTrigger<Combat.ActionDoorZone>(o + new Vector3(gx, 1.5f, z),
                    new Vector3(4.2f, 3f, 2.4f));

                // 同一段墙上的问题之门（诱饵）：暗紫色门 + 反刍触发器
                float[] fakeXs = { gx - 16f, gx + 16f };
                foreach (var fx in fakeXs)
                {
                    if (fx < -27 || fx > 27) continue;
                    Decoration(ctx, "QuestionDoor", o + new Vector3(fx, 2f, z - 0.8f),
                        new Vector3(3.2f, 4f, 0.3f), new Color(0.4f, 0.25f, 0.55f));
                    var q = MakeZoneTrigger<Combat.QuestionDoorZone>(o + new Vector3(fx, 1.5f, z - 1.2f),
                        new Vector3(3.4f, 3f, 2f));
                    q.question = questions[i];
                }
            }

            // 悬浮问号雕塑（大厅的压迫感）
            var rng = new System.Random(53);
            for (int i = 0; i < 12; i++)
                Decoration(ctx, "FloatingQuestion",
                    o + new Vector3(-24 + (float)rng.NextDouble() * 48,
                        3f + (float)rng.NextDouble() * 2.5f, -30 + (float)rng.NextDouble() * 60),
                    new Vector3(0.5f, 1.1f, 0.3f), new Color(0.55f, 0.45f, 0.75f));

            // 冷紫顶光
            AddCeilingLight(o + new Vector3(0, 7.5f, -20), new Color(0.75f, 0.7f, 0.95f), 36);
            AddCeilingLight(o + new Vector3(0, 7.5f, 12), new Color(0.75f, 0.7f, 0.95f), 36);

            // 传送门：回图书馆（南）/ 通往意志断桥（北，其三开启即解锁）
            MakePortal(ctx, o + new Vector3(0f, 0, -35), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(0f, 0, 35), PortalRole.Forward);
        }

        // ================= 第二十二区：意志断桥（哲学与行动线 终战） =================

        static void BuildWillBridge(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[21];

            // 深渊之上的断桥：桥面有缺口，缺口旁只有一条窄板可以绕行——
            // 失足坠入虚无会被送回桥头（不惩罚死亡，再走一次）。
            // 桥头平台
            Box(ctx, "BridgeHead", o + new Vector3(0, -0.25f, -44), new Vector3(20, 0.5f, 14),
                new Color(0.4f, 0.4f, 0.46f));
            // 深渊底（纯视觉，坠落由回送区处理）
            Decoration(ctx, "AbyssFloor", o + new Vector3(0, -9f, 0), new Vector3(70, 0.2f, 100),
                new Color(0.05f, 0.04f, 0.1f));

            // 桥段与缺口：四段桥面，段间 2.6m 缺口，缺口旁交替伸出窄板
            float[] segZ = { -30, -14, 2, 18 };
            for (int i = 0; i < segZ.Length; i++)
            {
                Box(ctx, "BridgeSeg", o + new Vector3(0, -0.25f, segZ[i]), new Vector3(6, 0.5f, 13),
                    new Color(0.44f, 0.44f, 0.5f));
                // 缺口旁的窄板（交替左右）：可以小心走过去
                float side = (i % 2 == 0) ? -1f : 1f;
                Box(ctx, "BridgePlank", o + new Vector3(side * 3.6f, -0.25f, segZ[i] + 8f),
                    new Vector3(1.1f, 0.5f, 4.4f), new Color(0.5f, 0.42f, 0.3f));
                // 断裂的栏杆残段
                Decoration(ctx, "BrokenRail", o + new Vector3(-3.2f, 0.7f, segZ[i] - 3),
                    new Vector3(0.15f, 0.9f, 5f), new Color(0.3f, 0.3f, 0.36f));
                Decoration(ctx, "BrokenRail", o + new Vector3(3.2f, 0.7f, segZ[i] + 2),
                    new Vector3(0.15f, 0.9f, 4f), new Color(0.3f, 0.3f, 0.36f));
            }
            // 桥头连接段
            Box(ctx, "BridgeSeg", o + new Vector3(0, -0.25f, -37.5f), new Vector3(6, 0.5f, 3),
                new Color(0.44f, 0.44f, 0.5f));

            // Boss 决战平台（桥的尽头）
            Box(ctx, "BridgeArena", o + new Vector3(0, -0.25f, 34), new Vector3(34, 0.5f, 22),
                new Color(0.42f, 0.42f, 0.48f));
            Box(ctx, "BridgeArenaLink", o + new Vector3(0, -0.25f, 24.5f), new Vector3(6, 0.5f, 1),
                new Color(0.44f, 0.44f, 0.5f));

            // 深渊回送区：覆盖整座桥下方，摔下去传回桥头
            var fall = MakeZoneTrigger<Combat.VoidFallZone>(o + new Vector3(0, -6f, -4),
                new Vector3(66, 3f, 96));
            fall.respawnPoint = ctx.playerSpawns[21];

            // 行动答台（Boss 平台一角）：用"做"回答无限追问 → Boss 大破绽
            var answer = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            answer.name = "ActionAnswerAltar";
            answer.transform.position = o + new Vector3(-12, 0.6f, 30);
            answer.transform.localScale = new Vector3(1.0f, 0.6f, 1.0f);
            Paint(ctx, answer, new Color(0.55f, 0.45f, 0.28f));
            var answerRoot = new GameObject("ActionAnswerRoot");
            answerRoot.transform.position = o + new Vector3(-12, 0, 30);
            answerRoot.AddComponent<Combat.ActionAnswerAltar>();

            // 幽蓝雾灯（深渊的冷）
            AddCeilingLight(o + new Vector3(0, 6f, -20), new Color(0.6f, 0.7f, 0.95f), 34);
            AddCeilingLight(o + new Vector3(0, 6f, 30), new Color(0.6f, 0.7f, 0.95f), 36);

            // 传送门：回追问大厅（桥头）/ 通往失败展览馆（Boss 平台北，旧我线开启即解锁）
            MakePortal(ctx, o + new Vector3(-7f, 0, -46), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(12f, 0, 42), PortalRole.Forward);
        }

        // ================= 第二十三区：失败展览馆（旧我与新我线 其一） =================

        static void BuildFailureExhibit(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[22];

            Color wall = new Color(0.28f, 0.26f, 0.3f);

            // 展览馆：过去的失败被裱起来打上射灯——旧审判官逼你在每件展品前停留
            Box(ctx, "Exhibit_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(58, 0.5f, 66),
                new Color(0.3f, 0.28f, 0.32f));
            Ring(ctx, o, new Vector2(29, 33), 6, wall);

            // 展品内容：优先用玩家真实的失败记录（败给过哪些心魔、各几次）——
            // 让这一关成为"面对自己真实失败史"的个人化体验；无记录则退回通用主题文案。
            var real = Core.FailureLog.Breakdown();
            bool personal = real.Count > 0;
            string introLine = "";
            var exhibits = new List<(string label, string plaque, string reframe)>();
            if (personal)
            {
                int falls = Core.FailureLog.TotalDeaths;
                foreach (var r in real)
                {
                    exhibits.Add((
                        r.label,
                        "败给「" + r.label + "」× " + r.count,
                        "你在这里败给「" + r.label + "」" + r.count + " 次——但你也一次次站了起来，它没有定义你。"));
                    if (exhibits.Count >= 6) break;
                }
                introLine = "这些不是别人的失败——是你真实倒下过的地方（累计 " + falls + " 次）。看，然后走过去。";
            }
            else
            {
                string[] generic = { "搞砸的演讲", "亏掉的积蓄", "断掉的关系", "放弃的计划", "错过的机会", "失败的创业" };
                foreach (var g in generic)
                    exhibits.Add((g, g, "「" + g + "」被裱在这里——但发生过，不等于就是你。"));
            }

            // 展品基座 + 裱框"失败"（红色射灯）+ 可读铭牌 + 走近重构触发
            for (int i = 0; i < exhibits.Count; i++)
            {
                float x = (i % 2 == 0) ? -16f : 16f;
                float z = -22 + (i / 2) * 18;
                Box(ctx, "ExhibitBase", o + new Vector3(x, 0.5f, z), new Vector3(3f, 1f, 3f),
                    new Color(0.4f, 0.38f, 0.42f));
                Decoration(ctx, "ExhibitFrame", o + new Vector3(x, 2.2f, z), new Vector3(2.4f, 2f, 0.25f),
                    new Color(0.55f, 0.45f, 0.25f));
                Decoration(ctx, "ExhibitCanvas", o + new Vector3(x, 2.2f, z + 0.05f),
                    new Vector3(2f, 1.6f, 0.18f), new Color(0.2f, 0.18f, 0.24f));
                Plaque(o + new Vector3(x, 2.2f, z + 0.2f), exhibits[i].plaque, new Color(0.92f, 0.6f, 0.5f));
                Plaque(o + new Vector3(x, 1.05f, z + 1.6f), "展品 " + (i + 1), new Color(0.75f, 0.72f, 0.7f));

                var spot = new GameObject("ExhibitSpot");
                spot.transform.position = o + new Vector3(x, 5f, z);
                var sl = spot.AddComponent<Light>();
                sl.type = LightType.Spot;
                sl.range = 9;
                sl.spotAngle = 46;
                sl.intensity = 1.8f;
                sl.color = new Color(0.95f, 0.4f, 0.35f);
                sl.transform.rotation = Quaternion.Euler(90, 0, 0);

                // 走近展品：把"这就是你"的审判，重构成"发生过，但不是你的全部"
                var reframe = MakeZoneTrigger<ExhibitReframeZone>(o + new Vector3(x, 1.5f, z + 2.2f),
                    new Vector3(5f, 3f, 4f));
                reframe.line = exhibits[i].reframe;
            }

            // 一次性入场提示（仅当有真实失败记录时）：进门告诉玩家这是他自己的失败史
            if (introLine.Length > 0)
            {
                var intro = MakeZoneTrigger<ExhibitReframeZone>(o + new Vector3(0, 1.5f, -8),
                    new Vector3(40, 3f, 8f));
                intro.line = introLine;
                intro.once = true;
            }

            // 中央"荣誉"展台（其实是空的——失败没有资格定义你）
            Box(ctx, "CenterPlinth", o + new Vector3(0, 0.6f, 6), new Vector3(4f, 1.2f, 4f),
                new Color(0.45f, 0.42f, 0.48f));
            Decoration(ctx, "EmptyFrame", o + new Vector3(0, 2.6f, 6), new Vector3(2.8f, 2.2f, 0.25f),
                new Color(0.6f, 0.55f, 0.35f));
            Plaque(o + new Vector3(0, 2.6f, 6.2f), "（空）", new Color(0.7f, 0.85f, 0.75f));
            Plaque(o + new Vector3(0, 1.15f, 4.1f),
                "失败可以陈列，但没资格坐上「定义你是谁」的位置。", new Color(0.75f, 0.9f, 0.8f));

            // 长椅（复盘席）
            Bench(ctx, o + new Vector3(-6, 0, -8), 90);
            Bench(ctx, o + new Vector3(6, 0, 16), 270);

            // 冷白展灯
            AddCeilingLight(o + new Vector3(0, 7f, -12), new Color(0.9f, 0.9f, 0.95f), 36);
            AddCeilingLight(o + new Vector3(0, 7f, 16), new Color(0.9f, 0.9f, 0.95f), 36);

            // 传送门：回意志断桥（南）/ 通往意志塔（北，其二开启即解锁）
            MakePortal(ctx, o + new Vector3(0f, 0, -30), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(0f, 0, 30), PortalRole.Forward);
        }

        // ================= 第二十四区：意志塔（旧我与新我线 其二） =================

        static void BuildWillTower(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[23];

            Color wall = new Color(0.32f, 0.3f, 0.38f);

            // 塔底广场 + 三层递升的塔台：旧声音复读机在最高层等你，
            // 每登一层，塔壁上的旧话就淡一分。
            Box(ctx, "Tower_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(56, 0.5f, 70),
                new Color(0.34f, 0.32f, 0.4f));
            Ring(ctx, o, new Vector2(28, 35), 7, wall);

            // 三层塔台（坡道相连，NavMesh 可走）
            float[] tierY = { 1.2f, 2.4f, 3.6f };
            float[] tierZ = { 2, 14, 26 };
            float[] tierHalf = { 13, 10, 8 };
            for (int i = 0; i < 3; i++)
            {
                Box(ctx, "TowerTier", o + new Vector3(0, tierY[i] - 0.25f, tierZ[i]),
                    new Vector3(tierHalf[i] * 2, 0.5f, 11), new Color(0.42f, 0.4f, 0.48f));
                // 坡道（从下一层/地面接上来，交替左右）
                float side = (i % 2 == 0) ? -1f : 1f;
                float rampBaseY = i == 0 ? 0f : tierY[i - 1];
                var ramp = Box(ctx, "TowerRamp",
                    o + new Vector3(side * (tierHalf[i] - 2f), (rampBaseY + tierY[i]) / 2f - 0.25f,
                        tierZ[i] - 7.5f),
                    new Vector3(3.4f, 0.5f, 7.5f), new Color(0.48f, 0.44f, 0.52f));
                ramp.transform.rotation = Quaternion.Euler(-Mathf.Rad2Deg *
                    Mathf.Atan2(tierY[i] - rampBaseY, 7.5f), 0, 0);
                // 塔壁上的旧话石板（越高越淡）
                float fade = 0.65f - i * 0.18f;
                Decoration(ctx, "OldWordsSlab", o + new Vector3(-tierHalf[i] + 1f, tierY[i] + 1.6f, tierZ[i]),
                    new Vector3(0.2f, 1.6f, 4f), new Color(fade, fade * 0.6f, fade * 0.75f));
                Decoration(ctx, "OldWordsSlab", o + new Vector3(tierHalf[i] - 1f, tierY[i] + 1.6f, tierZ[i]),
                    new Vector3(0.2f, 1.6f, 4f), new Color(fade, fade * 0.6f, fade * 0.75f));
            }

            // 塔顶的新我之光
            var apex = new GameObject("TowerApexLight");
            apex.transform.position = o + new Vector3(0, tierY[2] + 5f, tierZ[2]);
            var al = apex.AddComponent<Light>();
            al.type = LightType.Point;
            al.range = 26;
            al.intensity = 1.6f;
            al.color = new Color(1f, 0.9f, 0.65f);

            // 广场石柱（旧我的影壁）
            for (int i = -2; i <= 2; i++)
            {
                if (i == 0) continue;
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "TowerPillar";
                pillar.transform.position = o + new Vector3(i * 9, 2.5f, -18);
                pillar.transform.localScale = new Vector3(1.3f, 2.5f, 1.3f);
                Paint(ctx, pillar, new Color(0.4f, 0.38f, 0.46f));
                Player.CameraOcclusionFade.RegisterOccluder(pillar.GetComponent<Renderer>());
            }

            AddCeilingLight(o + new Vector3(0, 8f, -10), new Color(0.8f, 0.78f, 0.95f), 38);

            // 传送门：回失败展览馆（南）/ 通往旧事回声馆（塔侧，终局开启即解锁）
            MakePortal(ctx, o + new Vector3(0f, 0, -32), PortalRole.Back);
            MakePortal(ctx, o + new Vector3(-20f, 0, 24), PortalRole.Forward);
        }

        // ================= 第 26 区：欠条长廊 / 未播出的广播室（第八章 8-1） =================

        /// <summary>
        /// 8-1 欠条长廊（方案 8.5）。三层路径：
        /// Reality（住宅区小商店与走廊）→ Adversity（走廊被逆境化、欠条残片浮现）
        /// → Inner World（欠条长廊与广播室）→ Reality。
        ///
        /// 【这一关的核心设定：门一直是开着的】
        /// 广播室的门从关卡开始的第一秒起就没有锁，尽头也没有守门人。
        /// 真正的门槛是玩家愿不愿意在 Exposure 高、SelfWorth 低、身上挂着钉的状态下
        /// 主动走进去（验收第 37 条）。所以这里**不摆任何箭头、任务标记或引导光带**——
        /// 门就在那儿，走不走由玩家决定。
        /// </summary>
        static void BuildDebtCorridor(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[25];
            Shame.DebtFragment.ResetAll();

            Color floorC = new Color(0.3f, 0.29f, 0.32f);
            Color wallC = new Color(0.26f, 0.24f, 0.28f);

            // ---- Reality：住宅区小商店（关卡起点，也是恢复点）----
            // 地板往北铺到 z=-29，与长廊地板（-30 起）叠上一米：
            // 两块地各自摆各自的，接缝处差一点就是一脚踩空。
            Box(ctx, "Shop_Floor", o + new Vector3(0, -0.25f, -41), new Vector3(24, 0.5f, 24),
                new Color(0.38f, 0.36f, 0.34f));
            // 四面墙**不能用 Ring**：Ring 是封闭的一圈，北墙会把通往长廊的路堵死。
            // 北墙拆成左右两段，中间留出 6 米宽的门洞。
            Color shopWall = new Color(0.34f, 0.32f, 0.3f);
            Box(ctx, "Shop_Wall", o + new Vector3(0, 1.8f, -53), new Vector3(24, 3.6f, 1), shopWall);
            Box(ctx, "Shop_Wall", o + new Vector3(-12, 1.8f, -41), new Vector3(1, 3.6f, 24), shopWall);
            Box(ctx, "Shop_Wall", o + new Vector3(12, 1.8f, -41), new Vector3(1, 3.6f, 24), shopWall);
            Box(ctx, "Shop_Wall", o + new Vector3(-7.5f, 1.8f, -29.5f), new Vector3(9, 3.6f, 1), shopWall);
            Box(ctx, "Shop_Wall", o + new Vector3(7.5f, 1.8f, -29.5f), new Vector3(9, 3.6f, 1), shopWall);
            Box(ctx, "Shop_Counter", o + new Vector3(-6, 0.55f, -38), new Vector3(4.6f, 1.1f, 1.6f),
                new Color(0.46f, 0.36f, 0.24f));
            for (int i = 0; i < 3; i++)
                Decoration(ctx, "Shop_Shelf", o + new Vector3(7, 1.1f + i * 0.9f, -44 + i * 0.2f),
                    new Vector3(6f, 0.16f, 1.2f), new Color(0.5f, 0.46f, 0.4f));
            AddCeilingLight(o + new Vector3(0, 3.6f, -42), new Color(0.95f, 0.9f, 0.8f), 22);

            // 欠条台：分期偿还入口。每还一期就要被问一次
            var desk = Box(ctx, "DebtDesk", o + new Vector3(-6, 1.25f, -38),
                new Vector3(2.2f, 0.2f, 1.2f), new Color(0.85f, 0.82f, 0.72f));
            desk.AddComponent<Shame.DebtDesk>();
            Plaque(o + new Vector3(-6, 2.3f, -38), "欠条台 · 分期偿还", new Color(0.92f, 0.86f, 0.6f));

            // 家中抽屉：隐瞒类交互物。当下真的有用，账单也真的会来
            var drawer = Box(ctx, "HomeDrawer", o + new Vector3(6, 0.6f, -38),
                new Vector3(2.4f, 1.2f, 1.4f), new Color(0.44f, 0.34f, 0.24f));
            drawer.AddComponent<Shame.ConcealmentDrawer>().coverStory = "先借一笔，回头再说";
            Plaque(o + new Vector3(6, 2.1f, -38), "抽屉", new Color(0.8f, 0.76f, 0.66f));

            // 恢复点：羞耻状态的回落处（不回退关卡进度）
            var spot = new GameObject("ShameRecoverySpot");
            spot.transform.position = o + new Vector3(0, 0.4f, -46);
            var spotCol = spot.AddComponent<BoxCollider>();
            spotCol.isTrigger = true;
            spotCol.size = new Vector3(6f, 3f, 6f);
            spot.AddComponent<Shame.ShameRecoverySpot>();
            Decoration(ctx, "RecoveryPad", o + new Vector3(0, 0.06f, -46),
                new Vector3(5f, 0.06f, 5f), new Color(0.5f, 0.6f, 0.5f));

            // ---- Adversity → Inner：长廊本体（基础 4 段，隐瞒会让它继续变长）----
            Box(ctx, "Corridor_Floor", o + new Vector3(0, -0.25f, -6),
                new Vector3(9, 0.5f, 48), floorC);
            Box(ctx, "Corridor_Wall", o + new Vector3(4.6f, 1.9f, -6),
                new Vector3(0.4f, 3.8f, 48), wallC);
            Box(ctx, "Corridor_Wall", o + new Vector3(-4.6f, 1.9f, -6),
                new Vector3(0.4f, 3.8f, 48), wallC);
            Decoration(ctx, "Corridor_Ceiling", o + new Vector3(0, 3.9f, -6),
                new Vector3(9.4f, 0.3f, 48), new Color(0.22f, 0.21f, 0.24f));
            for (int i = 0; i < 4; i++)
                AddCeilingLight(o + new Vector3(0, 3.4f, -24 + i * 12),
                    new Color(0.8f, 0.82f, 0.9f), 16);

            // 每周门：每扇代表一次追问，必须逐一通过，不可跳过
            for (int i = 0; i < 3; i++)
            {
                float z = -20f + i * 14f;
                var door = new GameObject("WeeklyDoor_base_" + i);
                door.transform.position = o + new Vector3(0, 0, z);
                Box(ctx, "WeeklyDoorFrame", o + new Vector3(1.5f, 1.5f, z),
                    new Vector3(0.3f, 3f, 0.3f), new Color(0.44f, 0.36f, 0.26f));
                Box(ctx, "WeeklyDoorFrame", o + new Vector3(-1.5f, 1.5f, z),
                    new Vector3(0.3f, 3f, 0.3f), new Color(0.44f, 0.36f, 0.26f));
                Decoration(ctx, "WeeklyDoorLintel", o + new Vector3(0, 3.1f, z),
                    new Vector3(3.4f, 0.25f, 0.3f), new Color(0.4f, 0.33f, 0.24f));
                var col = door.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(3.2f, 3f, 1.4f);
                col.center = Vector3.up * 1.5f;
                door.AddComponent<Shame.WeeklyDoor>().doorIndex = i + 1;
            }

            // 欠条残片：环境叙事物，捡齐三片可缩短悬案段
            var fragSpots = new[]
            {
                new Vector3(-3.2f, 0.5f, -26),
                new Vector3(3.4f, 0.5f, -10),
                new Vector3(-3.0f, 0.5f, 8),
            };
            foreach (var f in fragSpots)
            {
                var frag = Decoration(ctx, "DebtFragment", o + f,
                    new Vector3(0.6f, 0.02f, 0.42f), new Color(0.92f, 0.88f, 0.74f));
                var fc = frag.AddComponent<BoxCollider>();
                fc.isTrigger = true;
                fc.size = new Vector3(2.4f, 3f, 2.4f);
                frag.AddComponent<Shame.DebtFragment>();
            }

            // 公告位：现实层锚点之一（走廊尽头的公告栏）
            Box(ctx, "NoticeBoard", o + new Vector3(-4.2f, 1.9f, 16),
                new Vector3(0.25f, 2.4f, 3.4f), new Color(0.5f, 0.46f, 0.38f));
            Plaque(o + new Vector3(-3.6f, 3.4f, 16), "公告位", new Color(0.8f, 0.78f, 0.7f));

            // ---- 广播室：终点。门从第一秒起就是开着的 ----
            // 【坐标要和长廊接得上】基础长廊的地板到 z=+18 为止，所以广播室的地板
            // 必须**正好从 18 开始**（中心 25、进深 14 → 18~32）。
            // 长廊每延长一段（14 米），整间屋子往后挪 14——接缝始终对齐，
            // 不会在门口留一段没有地板的空档。
            var room = new GameObject("BroadcastRoom");
            room.transform.position = o + new Vector3(0, 0, 25);
            Piece(ctx, room, "Broadcast_Floor", o + new Vector3(0, -0.25f, 25),
                new Vector3(16, 0.5f, 14), new Color(0.32f, 0.3f, 0.34f), true);
            Piece(ctx, room, "Broadcast_Wall", o + new Vector3(-8, 2.1f, 25),
                new Vector3(0.4f, 4.2f, 14), wallC, true);
            Piece(ctx, room, "Broadcast_Wall", o + new Vector3(8, 2.1f, 25),
                new Vector3(0.4f, 4.2f, 14), wallC, true);
            Piece(ctx, room, "Broadcast_Wall", o + new Vector3(0, 2.1f, 32),
                new Vector3(16, 4.2f, 0.4f), wallC, true);
            Piece(ctx, room, "Broadcast_Ceiling", o + new Vector3(0, 4.3f, 25),
                new Vector3(16.4f, 0.3f, 14), new Color(0.2f, 0.19f, 0.22f), false);
            // 门框——**没有门板**。这扇门没有关过
            Piece(ctx, room, "Broadcast_DoorFrame", o + new Vector3(-2.2f, 1.6f, 18.2f),
                new Vector3(0.4f, 3.2f, 0.5f), new Color(0.5f, 0.42f, 0.3f), true);
            Piece(ctx, room, "Broadcast_DoorFrame", o + new Vector3(2.2f, 1.6f, 18.2f),
                new Vector3(0.4f, 3.2f, 0.5f), new Color(0.5f, 0.42f, 0.3f), true);
            Piece(ctx, room, "Broadcast_Lintel", o + new Vector3(0, 3.35f, 18.2f),
                new Vector3(4.8f, 0.3f, 0.5f), new Color(0.46f, 0.38f, 0.28f), false);
            Piece(ctx, room, "Broadcast_Desk", o + new Vector3(0, 0.6f, 28),
                new Vector3(4.2f, 1.2f, 1.8f), new Color(0.42f, 0.34f, 0.26f), true);
            Piece(ctx, room, "Broadcast_Mic", o + new Vector3(0, 1.5f, 28),
                new Vector3(0.22f, 0.6f, 0.22f), new Color(0.3f, 0.3f, 0.34f), false);

            // ON AIR 红灯：门上永不亮起的那一盏——「待审悬置」的可视化象征
            var lamp = Decoration(ctx, "OnAirLight", o + new Vector3(0, 3.9f, 18.2f),
                new Vector3(1.6f, 0.5f, 0.3f), new Color(0.35f, 0.06f, 0.06f));
            lamp.transform.SetParent(room.transform, true);
            lamp.AddComponent<Shame.OnAirLight>();
            var plaque = new GameObject("OnAirPlaque");
            plaque.transform.SetParent(room.transform, true);
            plaque.transform.position = o + new Vector3(0, 4.5f, 18.2f);
            WorldText.Attach(plaque, "ON AIR", 44, 0.05f, new Color(0.55f, 0.2f, 0.2f));
            plaque.AddComponent<FaceCamera>();

            // 进门触发自行陈述（玩家自己走进来才算——系统不推）
            var doorTrigger = new GameObject("BroadcastDoor");
            doorTrigger.transform.SetParent(room.transform, true);
            doorTrigger.transform.position = o + new Vector3(0, 1.5f, 19.2f);
            var dt = doorTrigger.AddComponent<BoxCollider>();
            dt.isTrigger = true;
            dt.size = new Vector3(4.2f, 3f, 1.6f);
            doorTrigger.AddComponent<Shame.BroadcastDoor>();
            AddCeilingLight(o + new Vector3(0, 4f, 26), new Color(0.85f, 0.85f, 0.95f), 20);

            // 长廊延长系统：起点在基础长廊的末端，往 +Z 生长，广播室整体后退
            var growth = Shame.CorridorGrowthSystem.Ensure();
            growth.Bind(ctx, o + new Vector3(0, 0, 18), Vector3.forward, room.transform);

            // 传送门用显式目标：本关不在 300 米网格上，按坐标反推的区号对不上
            MakePortal(ctx, o + new Vector3(-6f, 0, -50), 8, "旧事回声馆");
            MakePortal(ctx, o + new Vector3(6f, 0, -50), 26, "二十元回声教室");
        }

        // ================= 第 27 区：二十元回声教室（第八章 8-2） =================

        /// <summary>
        /// 8-2 回声教室（方案 8.6）：夜间、灯光半开的自习空间。
        ///
        /// 【三个目标交互物全部位于视线锥内，且不可能全程回避】（验收第 39 条）
        /// 归还在前排主锥中心，完成本职在交叉视线区，步行离场要穿过全场——
        /// 绕开无法通关。这条布局是本章防"回避成为最优解"的地基，
        /// 不是难度设计，是主题设计。
        /// </summary>
        static void BuildEchoClassroom(WorldContext ctx)
        {
            Vector3 o = ctx.zoneOrigins[26];

            Color floorC = new Color(0.35f, 0.33f, 0.3f);
            Color wallC = new Color(0.3f, 0.3f, 0.33f);

            Box(ctx, "Classroom_Floor", o + new Vector3(0, -0.25f, 0), new Vector3(30, 0.5f, 40), floorC);
            // 门外的小门厅：教室地板到 z=-20 为止，而门与传送门都在更外面。
            // 没有这块地，"走出去"这个终局动作的最后两步就是踩空。
            Box(ctx, "Classroom_Foyer", o + new Vector3(0, -0.25f, -24),
                new Vector3(18, 0.5f, 10), new Color(0.33f, 0.31f, 0.29f));
            // 三面整墙 + 南墙留门洞（x∈[-3,3]）：整关的终局动作就是从这个洞走出去，
            // 用 Ring 封一圈等于把通关条件封死。
            Box(ctx, "Classroom_Wall", o + new Vector3(0, 2.1f, 20), new Vector3(30, 4.2f, 1), wallC);
            Box(ctx, "Classroom_Wall", o + new Vector3(-15, 2.1f, 0), new Vector3(1, 4.2f, 40), wallC);
            Box(ctx, "Classroom_Wall", o + new Vector3(15, 2.1f, 0), new Vector3(1, 4.2f, 40), wallC);
            Box(ctx, "Classroom_Wall", o + new Vector3(-9, 2.1f, -20), new Vector3(12, 4.2f, 1), wallC);
            Box(ctx, "Classroom_Wall", o + new Vector3(9, 2.1f, -20), new Vector3(12, 4.2f, 1), wallC);
            Decoration(ctx, "Classroom_Ceiling", o + new Vector3(0, 4.3f, 0),
                new Vector3(30.4f, 0.3f, 40), new Color(0.2f, 0.2f, 0.23f));

            // 夜间半开灯：只开前半边。后排是暗的——低语正是从暗处来的
            AddCeilingLight(o + new Vector3(0, 3.9f, 10), new Color(0.9f, 0.9f, 0.85f), 24);
            AddCeilingLight(o + new Vector3(0, 3.9f, -2), new Color(0.85f, 0.86f, 0.82f), 20);

            // 课桌组：前排四列 × 后排三排
            for (int row = 0; row < 5; row++)
                for (int col = -2; col <= 2; col++)
                {
                    float z = 12f - row * 5.5f;
                    var deskGo = Box(ctx, "ClassDesk", o + new Vector3(col * 5f, 0.65f, z),
                        new Vector3(3.2f, 0.16f, 1.5f), new Color(0.5f, 0.42f, 0.3f));
                    Decoration(ctx, "ClassDeskLeg", o + new Vector3(col * 5f, 0.32f, z),
                        new Vector3(3.0f, 0.64f, 0.3f), new Color(0.34f, 0.3f, 0.26f));
                    Player.CameraOcclusionFade.RegisterOccluder(deskGo.GetComponent<Renderer>());
                }

            // 讲台与黑板（前排锚点）
            Box(ctx, "Podium", o + new Vector3(0, 0.6f, 17), new Vector3(4.4f, 1.2f, 1.6f),
                new Color(0.44f, 0.36f, 0.28f));
            Decoration(ctx, "Blackboard", o + new Vector3(0, 2.4f, 19.4f),
                new Vector3(12f, 2.4f, 0.2f), new Color(0.18f, 0.24f, 0.2f));

            // ---- 目标动作一｜归还：教室前排，主视线锥中心 ----
            var giveBack = Box(ctx, "ObjectiveReturn", o + new Vector3(0, 1.35f, 15),
                new Vector3(1.2f, 0.3f, 0.9f), new Color(0.9f, 0.85f, 0.5f));
            var giveBackObj = giveBack.AddComponent<Shame.ObjectiveStation>();
            giveBackObj.objectiveId = Shame.ShameLineController.ObjReturn;
            giveBackObj.holdSeconds = 3.4f;
            Plaque(o + new Vector3(0, 2.4f, 15), "归还（长按）", new Color(0.92f, 0.86f, 0.55f));

            // ---- 目标动作二｜完成本职：自习座位，交叉视线区 ----
            var seat = Box(ctx, "ObjectiveSeat", o + new Vector3(-5, 1.0f, 1),
                new Vector3(1.6f, 0.2f, 1.2f), new Color(0.72f, 0.78f, 0.85f));
            var seatObj = seat.AddComponent<Shame.ObjectiveStation>();
            seatObj.objectiveId = Shame.ShameLineController.ObjOwnWork;
            seatObj.holdSeconds = 5.2f;
            seatObj.underMentalAttack = true;
            Plaque(o + new Vector3(-5, 2.2f, 1), "完成本职（长按，期间会挨话）",
                new Color(0.8f, 0.86f, 0.92f));

            // ---- 搜查回响（可选支线）：别人的包。系统允许，也如实结账 ----
            var bag = Box(ctx, "OthersBag", o + new Vector3(8, 0.9f, 4),
                new Vector3(1.1f, 0.7f, 0.8f), new Color(0.4f, 0.36f, 0.42f));
            bag.AddComponent<Shame.SearchEcho>();

            // ---- 目标动作三｜步行离场：教室门，要穿过全场视线锥 ----
            var exit = new GameObject("ClassroomExit");
            exit.transform.position = o + new Vector3(0, 1.5f, -18);
            var ec = exit.AddComponent<BoxCollider>();
            ec.isTrigger = true;
            ec.size = new Vector3(5f, 3f, 2f);
            exit.AddComponent<Shame.ClassroomExit>();
            Box(ctx, "ExitFrame", o + new Vector3(-2.6f, 1.6f, -18),
                new Vector3(0.4f, 3.2f, 0.5f), new Color(0.5f, 0.42f, 0.3f));
            Box(ctx, "ExitFrame", o + new Vector3(2.6f, 1.6f, -18),
                new Vector3(0.4f, 3.2f, 0.5f), new Color(0.5f, 0.42f, 0.3f));
            Plaque(o + new Vector3(0, 3.6f, -18), "门 · 走出去（不要冲刺）",
                new Color(0.85f, 0.88f, 0.8f));

            // 恢复点：门外的走廊一角
            var spot = new GameObject("ShameRecoverySpot");
            spot.transform.position = o + new Vector3(-9, 0.4f, -16);
            var spotCol = spot.AddComponent<BoxCollider>();
            spotCol.isTrigger = true;
            spotCol.size = new Vector3(5f, 3f, 5f);
            spot.AddComponent<Shame.ShameRecoverySpot>();

            MakePortal(ctx, o + new Vector3(-6f, 0, -25), 25, "欠条长廊");
            MakePortal(ctx, o + new Vector3(6f, 0, -25), 0, "独居小屋（安全屋）");
        }

        /// <summary>把一块几何体建好并挂到指定父节点下（长廊/广播室这类要整体移动的组）。</summary>
        static void Piece(WorldContext ctx, GameObject parent, string name, Vector3 pos,
            Vector3 scale, Color color, bool solid)
        {
            GameObject go = solid ? Box(ctx, name, pos, scale, color)
                                  : Decoration(ctx, name, pos, scale, color);
            if (go != null && parent != null) go.transform.SetParent(parent.transform, true);
        }

        public static void AddCeilingLight(Vector3 pos, Color color, float range)
        {
            // 强度不再是写死的 1.0：range=40 的顶灯配 intensity=1，10 米外照度只有 1/100，
            // 摆了等于没摆——"拖延线之后的关卡一片黑"最直接的成因。统一走照明口径换算。
            SceneLighting.MakePoint("Court_Light", pos, color, range);
        }

        /// <summary>责任天平托盘（作为天平根的子物件，便于回正动画）。</summary>
        static Transform ScalePan(WorldContext ctx, Transform parent, Vector3 localPos)
        {
            var pan = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(pan.GetComponent<Collider>());
            pan.name = "ScalePan";
            pan.transform.SetParent(parent, false);
            pan.transform.localPosition = localPos;
            pan.transform.localScale = new Vector3(1.8f, 0.1f, 1.8f);
            Paint(ctx, pan, new Color(0.7f, 0.65f, 0.4f));
            return pan.transform;
        }

        // ================= 分区氛围·色彩脚本（NavMesh 烘焙后调用，零美术） =================

        /// <summary>一个区域的"色彩脚本"配置：色调滤镜/饱和/对比/暗角/色相偏移/雾色。</summary>
        struct ZoneMood
        {
            public Color filter;    // 色调滤镜（乘算，近白微偏色）
            public float sat;       // 饱和 -100..100
            public float con;       // 对比 -100..100
            public float vig;       // 暗角 0..1
            public float hue;       // 色相偏移 -180..180
            public Color fog;       // 该区专属雾色（供 DayNightCycle 混合）
        }

        static ZoneMood M(Color filter, float sat, float con, float vig, Color fog, float hue = 0f) =>
            new ZoneMood { filter = filter, sat = sat, con = con, vig = vig, hue = hue, fog = fog };

        // 24 区色彩脚本：每个场景按其心理主题给一套专属氛围（冷/暖/去饱和/压抑/神秘…）
        static readonly ZoneMood[] Moods =
        {
            /*0  独居小屋*/    M(new Color(1.00f,0.92f,0.78f), -12, 8,  0.34f, new Color(0.09f,0.10f,0.15f)),
            /*1  训练武馆*/    M(new Color(1.00f,0.97f,0.90f),   4, 6,  0.20f, new Color(0.55f,0.58f,0.62f)),
            /*2  噪声街区*/    M(new Color(0.92f,0.96f,1.05f),  10, 8,  0.22f, new Color(0.62f,0.68f,0.78f)),
            /*3  求职荒原*/    M(new Color(0.95f,0.96f,1.00f), -28, 6,  0.30f, new Color(0.66f,0.68f,0.72f)),
            /*4  城市广场*/    M(new Color(1.05f,0.90f,0.72f),  14, 10, 0.24f, new Color(0.30f,0.24f,0.28f)),
            /*5  责任法院*/    M(new Color(0.88f,0.95f,1.05f),  -6, 14, 0.30f, new Color(0.40f,0.46f,0.56f)),
            /*6  审判庭*/      M(new Color(0.94f,0.97f,1.02f),  -8, 18, 0.34f, new Color(0.36f,0.40f,0.50f)),
            /*7  拖延沼泽*/    M(new Color(0.85f,1.00f,0.85f), -18, 8,  0.36f, new Color(0.10f,0.16f,0.12f), -6),
            /*8  回声馆*/      M(new Color(0.90f,0.94f,1.05f), -20, 6,  0.36f, new Color(0.14f,0.16f,0.24f)),
            /*9  两元赌桌*/    M(new Color(1.05f,0.95f,0.70f),   6, 12, 0.32f, new Color(0.20f,0.17f,0.12f)),
            /*10 债务车影*/    M(new Color(0.86f,0.92f,1.08f),   4, 14, 0.30f, new Color(0.10f,0.13f,0.22f)),
            /*11 眼神走廊*/    M(new Color(0.90f,1.00f,0.98f),  -6, 12, 0.30f, new Color(0.30f,0.40f,0.42f), -4),
            /*12 挑衅路口*/    M(new Color(1.04f,0.95f,0.85f),  14, 10, 0.24f, new Color(0.34f,0.30f,0.30f)),
            /*13 目标遗忘房*/  M(new Color(0.92f,0.90f,1.02f), -18, 6,  0.34f, new Color(0.16f,0.15f,0.22f)),
            /*14 老实人消耗局*/M(new Color(0.93f,0.98f,0.93f), -20, 8,  0.30f, new Color(0.34f,0.40f,0.36f)),
            /*15 无限代付走廊*/M(new Color(0.88f,0.95f,1.05f), -10, 14, 0.32f, new Color(0.24f,0.30f,0.40f)),
            /*16 饥饿荒巷*/    M(new Color(0.88f,0.93f,1.06f),   2, 12, 0.34f, new Color(0.10f,0.12f,0.20f)),
            /*17 车库寒夜*/    M(new Color(0.86f,0.92f,1.08f), -26, 10, 0.34f, new Color(0.16f,0.20f,0.30f)),
            /*18 病房回廊*/    M(new Color(0.96f,0.99f,1.03f), -14, 6,  0.26f, new Color(0.70f,0.74f,0.80f)),
            /*19 图书馆*/      M(new Color(1.05f,0.94f,0.72f), -10, 8,  0.36f, new Color(0.18f,0.16f,0.12f)),
            /*20 追问大厅*/    M(new Color(0.95f,0.88f,1.08f),  -4, 12, 0.34f, new Color(0.20f,0.16f,0.28f), 6),
            /*21 意志断桥*/    M(new Color(0.82f,0.90f,1.12f),  -8, 16, 0.40f, new Color(0.06f,0.09f,0.18f)),
            /*22 失败展览馆*/  M(new Color(0.96f,0.97f,1.03f),  -6, 16, 0.32f, new Color(0.30f,0.32f,0.40f)),
            /*23 意志塔*/      M(new Color(1.04f,0.96f,0.80f),   6, 10, 0.28f, new Color(0.22f,0.22f,0.30f)),
            /*24 开放城区*/    M(new Color(0.99f,0.98f,0.99f),   2, 6,  0.18f, new Color(0.58f,0.63f,0.72f)),
        };

        /// <summary>该区专属雾色（DayNightCycle 按当前所在区取用；越界回退中性）。</summary>
        public static Color FogTintOf(int zone) =>
            zone >= 0 && zone < Moods.Length ? Moods[zone].fog : new Color(0.4f, 0.45f, 0.55f);

        /// <summary>
        /// 为每个区域生成一个"局部分级 Volume"——镜头进入该区即平滑切到该区色彩脚本。
        /// 纯后处理、按镜头位置自动混合，不改 RenderSettings，不依赖美术资源。
        /// NavMesh 烘焙之后再建（触发盒不参与寻路烘焙）。
        /// </summary>
        public static void SetupZoneMoods(WorldContext ctx)
        {
            for (int i = 0; i < ZoneCount && i < Moods.Length; i++)
            {
                var m = Moods[i];
                var go = new GameObject("ZoneMood_" + ZoneIdOf(i));
                go.transform.position = ctx.zoneOrigins[i];

                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(200, 90, 200);   // 覆盖整区（区间距 300，互不重叠）

                var vol = go.AddComponent<Volume>();
                vol.isGlobal = false;
                vol.blendDistance = 24f;                // 边缘平滑过渡
                vol.priority = 20f;                     // 高于全局分级(10)

                var p = ScriptableObject.CreateInstance<VolumeProfile>();
                vol.sharedProfile = p;

                var ca = p.Add<ColorAdjustments>(true);
                ca.colorFilter.Override(m.filter);
                ca.saturation.Override(m.sat);
                ca.contrast.Override(m.con);
                if (Mathf.Abs(m.hue) > 0.01f) ca.hueShift.Override(m.hue);

                var vg = p.Add<Vignette>(true);
                vg.intensity.Override(m.vig);
                vg.smoothness.Override(0.45f);
            }
        }

        // ================= 动态生命（NavMesh 烘焙后调用） =================

        public static void SpawnLife(WorldContext ctx)
        {
            var rng = new System.Random(11);
            foreach (var rawPos in ctx.pedestrianSpawns)
            {
                // 吸附到 NavMesh 地面，吸附失败则不生成（避免悬空行人）
                Vector3 pos = rawPos;
                if (UnityEngine.AI.NavMesh.SamplePosition(rawPos, out UnityEngine.AI.NavMeshHit navHit,
                        4f, UnityEngine.AI.NavMesh.AllAreas))
                    pos = navHit.position + Vector3.up * 1f;
                else continue;
                // 行人此前就是一个【胶囊基元】——"完全没有人样"是字面意义上的：
                // 那真的只是 CreatePrimitive(Capsule) 涂了个随机颜色。
                // 改用项目里已有的程序化人形骨骼（HumanoidRig，玩家无模型时的同一套
                // 兜底），十几个方块拼出头/躯干/四肢，手机上开销可忽略，
                // 但一眼就是个人；再挂 SimpleAnimator 让他们走路时摆臂迈腿。
                var ped = new GameObject("Pedestrian");
                ped.transform.position = pos;

                var visual = new GameObject("Visual").transform;
                visual.SetParent(ped.transform, false);
                var rig = Combat.HumanoidRig.Build(visual, PedestrianLook(rng), ctx.mat);

                // 用 HumanoidAnimator（驱动各关节的那个）而不是 SimpleAnimator
                //（后者只整体倾斜 visual，没有 rig 字段，对方块骨骼等于没动）
                var anim = ped.AddComponent<Combat.HumanoidAnimator>();
                anim.visual = visual;
                anim.rig = rig;

                // NavMeshAgent 自己不需要碰撞体，但行人要能被兵器判定扫到/挡住镜头，
                // 补一个与原胶囊等效的碰撞体
                var col = ped.AddComponent<CapsuleCollider>();
                col.height = 1.8f; col.radius = 0.32f; col.center = new Vector3(0, -0.1f, 0);

                var agent = ped.AddComponent<UnityEngine.AI.NavMeshAgent>();
                agent.radius = 0.32f;
                agent.height = 1.8f;
                // NavMeshAgent 会把 transform 原点**吸附到导航面**，出生时给的 +1m 抬升
                // 会被立刻抹掉。而 HumanoidRig 的骨架是以腰为原点、脚在 -1 左右，
                // 于是整个下半身埋进地里（实机截图：路人只露上半身）。
                // baseOffset 正是为此存在的：告诉 Agent「我的原点离脚底有多高」。
                agent.baseOffset = 1f;
                ped.AddComponent<PedestrianWanderer>().anim = anim;
            }

            var carRng = new System.Random(23);
            foreach (var route in ctx.carRoutes)
            {
                for (int i = 0; i < 2; i++)
                {
                    Color carCol = new Color(
                        0.3f + (float)carRng.NextDouble() * 0.6f,
                        0.3f + (float)carRng.NextDouble() * 0.6f,
                        0.35f + (float)carRng.NextDouble() * 0.6f);
                    Vector3 start = Vector3.Lerp(route.a, route.b, 0.15f + 0.5f * i);

                    // 车根：空物体承载碰撞与行驶（+z 为车头，CarMover 朝行进方向），
                    // 车身/车顶/四轮/车窗/灯为子物件，在本地坐标（根缩放=1）装配，避免非均匀缩放畸变
                    var root = new GameObject("Car");
                    root.transform.position = start;
                    var col = root.AddComponent<BoxCollider>();
                    col.center = new Vector3(0, 0.55f, 0);
                    col.size = new Vector3(1.9f, 1.1f, 4.2f);

                    BuildCarPart(ctx, root.transform, "Car", new Vector3(0, 0.5f, 0),
                        new Vector3(1.9f, 0.7f, 4.2f), carCol, false);                       // 车身
                    BuildCarPart(ctx, root.transform, "Car", new Vector3(0, 1.0f, -0.15f),
                        new Vector3(1.62f, 0.6f, 2.1f), carCol * 0.92f, true);                // 车顶座舱
                    // 前后风挡与侧窗（深色玻璃）
                    var glass = new Color(0.16f, 0.2f, 0.26f);
                    BuildCarPart(ctx, root.transform, "CarGlass", new Vector3(0, 1.02f, 0.95f),
                        new Vector3(1.45f, 0.5f, 0.1f), glass, true);
                    BuildCarPart(ctx, root.transform, "CarGlass", new Vector3(0, 1.02f, -1.28f),
                        new Vector3(1.45f, 0.5f, 0.1f), glass, true);
                    // 车灯：前白后红
                    BuildCarPart(ctx, root.transform, "CarLight", new Vector3(0.6f, 0.5f, 2.1f),
                        new Vector3(0.35f, 0.22f, 0.08f), new Color(1f, 0.97f, 0.85f), true);
                    BuildCarPart(ctx, root.transform, "CarLight", new Vector3(-0.6f, 0.5f, 2.1f),
                        new Vector3(0.35f, 0.22f, 0.08f), new Color(1f, 0.97f, 0.85f), true);
                    BuildCarPart(ctx, root.transform, "CarLight", new Vector3(0.6f, 0.5f, -2.1f),
                        new Vector3(0.35f, 0.22f, 0.08f), new Color(0.8f, 0.12f, 0.12f), true);
                    BuildCarPart(ctx, root.transform, "CarLight", new Vector3(-0.6f, 0.5f, -2.1f),
                        new Vector3(0.35f, 0.22f, 0.08f), new Color(0.8f, 0.12f, 0.12f), true);
                    // 四只车轮（圆盘，轴沿 x）
                    var rubber = new Color(0.1f, 0.1f, 0.11f);
                    float[] wx = { -0.95f, 0.95f }, wz = { 1.3f, -1.3f };
                    foreach (var xx in wx)
                        foreach (var zz in wz)
                        {
                            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                            Object.DestroyImmediate(wheel.GetComponent<Collider>());
                            wheel.name = "Wheel";
                            wheel.transform.SetParent(root.transform, false);
                            wheel.transform.localPosition = new Vector3(xx, 0.35f, zz);
                            wheel.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                            wheel.transform.localScale = new Vector3(0.72f, 0.12f, 0.72f);
                            Paint(ctx, wheel, rubber);
                        }

                    var mover = root.AddComponent<CarMover>();
                    mover.pointA = route.a;
                    mover.pointB = route.b;
                    mover.speed = 6f + (float)carRng.NextDouble() * 4f;
                }
            }
        }

        // ================= 构件辅助 =================

        static readonly Color WallColor = new Color(0.72f, 0.68f, 0.6f);

        public static GameObject Box(WorldContext ctx, string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            Paint(ctx, go, color);
            return go;
        }

        /// <summary>无碰撞装饰件（路面标线、地毯、窗户等），不干扰 NavMesh。</summary>
        public static GameObject Decoration(WorldContext ctx, string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = Box(ctx, name, pos, scale, color);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static void Ring(WorldContext ctx, Vector3 center, float half, float h, Color c) =>
            Ring(ctx, center, new Vector2(half, half), h, c);

        public static void Ring(WorldContext ctx, Vector3 center, Vector2 half, float h, Color c)
        {
            Box(ctx, "Wall", center + new Vector3(0, h / 2, half.y), new Vector3(half.x * 2, h, 1), c);
            Box(ctx, "Wall", center + new Vector3(0, h / 2, -half.y), new Vector3(half.x * 2, h, 1), c);
            Box(ctx, "Wall", center + new Vector3(half.x, h / 2, 0), new Vector3(1, h, half.y * 2), c);
            Box(ctx, "Wall", center + new Vector3(-half.x, h / 2, 0), new Vector3(1, h, half.y * 2), c);
        }

        public static void Building(WorldContext ctx, Vector3 basePos, float w, float h, float d, System.Random rng)
        {
            Color bodyColor = new Color(
                0.45f + (float)rng.NextDouble() * 0.25f,
                0.42f + (float)rng.NextDouble() * 0.22f,
                0.4f + (float)rng.NextDouble() * 0.25f);
            Box(ctx, "Building", basePos + new Vector3(0, h / 2, 0), new Vector3(w, h, d), bodyColor);

            float facing = basePos.z > 0 ? -1f : 1f;

            // 底层裙房（略外扩的基座）：打破"一根方柱"的呆板轮廓
            Color plinth = bodyColor * 0.82f; plinth.a = 1f;
            Box(ctx, "BuildingBase", basePos + new Vector3(0, 1.1f, 0),
                new Vector3(w + 0.8f, 2.2f, d + 0.8f), plinth);
            // 沿街入口门廊
            Decoration(ctx, "BuildingDoor", basePos + new Vector3(0, 1.2f, facing * (d / 2f + 0.5f)),
                new Vector3(2.2f, 2.4f, 0.2f), new Color(0.2f, 0.22f, 0.26f));

            // 顶部女儿墙压顶（外挑一圈）
            Color parapet = bodyColor * 0.7f; parapet.a = 1f;
            Box(ctx, "BuildingParapet", basePos + new Vector3(0, h + 0.25f, 0),
                new Vector3(w + 0.6f, 0.5f, d + 0.6f), parapet);

            // 屋顶设备：水箱 / 空调机组 / 通风管（消灭"平顶方盒"感）
            var rc = new Color(0.5f, 0.5f, 0.54f);
            var tank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tank.name = "RoofTank";
            tank.transform.position = basePos + new Vector3(-w * 0.22f, h + 1.1f, d * 0.15f);
            tank.transform.localScale = new Vector3(1.6f, 1.0f, 1.6f);
            Paint(ctx, tank, new Color(0.55f, 0.5f, 0.45f));
            Box(ctx, "RoofAC", basePos + new Vector3(w * 0.24f, h + 0.7f, -d * 0.18f),
                new Vector3(1.8f, 0.9f, 1.3f), rc);
            Box(ctx, "RoofAC", basePos + new Vector3(w * 0.05f, h + 0.6f, d * 0.28f),
                new Vector3(1.2f, 0.7f, 1.0f), rc);
            var vent = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            vent.name = "RoofVent";
            Object.DestroyImmediate(vent.GetComponent<Collider>());
            vent.transform.position = basePos + new Vector3(-w * 0.05f, h + 0.9f, -d * 0.02f);
            vent.transform.localScale = new Vector3(0.35f, 0.8f, 0.35f);
            Paint(ctx, vent, new Color(0.4f, 0.4f, 0.44f));

            // 正面窗户（带凹陷窗框，朝向街道一侧）
            Color frame = bodyColor * 0.55f; frame.a = 1f;
            int rows = Mathf.Clamp(Mathf.FloorToInt(h / 3f), 2, 5);
            for (int r = 0; r < rows; r++)
                for (int cIdx = 0; cIdx < 3; cIdx++)
                {
                    Vector3 wp = basePos + new Vector3(-w / 3f + cIdx * w / 3f, 2f + r * (h - 3f) / rows,
                        facing * (d / 2f + 0.05f));
                    Decoration(ctx, "WindowFrame", wp, new Vector3(1.7f, 1.4f, 0.12f), frame);
                    Decoration(ctx, "Window", wp + new Vector3(0, 0, facing * 0.04f),
                        new Vector3(1.4f, 1.1f, 0.1f), new Color(0.95f, 0.9f, 0.6f));
                }
        }

        /// <summary>
        /// 造一个"看得出是人"的 NPC 躯体：程序化人形骨骼 + 关节动画 + 碰撞体 + NavMeshAgent。
        ///
        /// 开放城区的市民、AI 现场生成场景里的 NPC 与经典关卡的路人共用这一套。
        /// 各建各的会立刻露馅：同一条街上一半是方块人、一半是纯色胶囊——
        /// 而胶囊正是上面 SpawnLife 注释里刚被判过死刑的那种"完全没有人样"。
        /// </summary>
        public static GameObject MakeHumanoidNpc(WorldContext ctx, string name,
            Vector3 groundPos, System.Random rng)
        {
            var go = new GameObject(name);
            go.transform.position = groundPos + Vector3.up * 1f;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            var rig = Combat.HumanoidRig.Build(visual, PedestrianLook(rng), ctx != null ? ctx.mat : null);

            var anim = go.AddComponent<Combat.HumanoidAnimator>();
            anim.visual = visual;
            anim.rig = rig;

            var col = go.AddComponent<CapsuleCollider>();
            col.height = 1.8f; col.radius = 0.32f; col.center = new Vector3(0, -0.1f, 0);

            var agent = go.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.radius = 0.32f;
            agent.height = 1.8f;
            agent.baseOffset = 1f;   // 骨架原点在腰、脚底在 -1，不补这一米就半身入地
            return go;
        }

        /// <summary>随机行人外观：肤色/衣裤/鞋各自小幅随机，路人之间看得出差别。</summary>
        public static Combat.HumanoidRig.Config PedestrianLook(System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            return new Combat.HumanoidRig.Config
            {
                skin = Color.Lerp(new Color(0.94f, 0.78f, 0.64f), new Color(0.55f, 0.40f, 0.30f), R()),
                top = new Color(0.25f + R() * 0.5f, 0.25f + R() * 0.5f, 0.3f + R() * 0.5f),
                bottom = new Color(0.18f + R() * 0.3f, 0.18f + R() * 0.3f, 0.22f + R() * 0.35f),
                shoes = new Color(0.14f + R() * 0.15f, 0.14f + R() * 0.15f, 0.16f + R() * 0.15f),
                hair = Color.Lerp(new Color(0.08f, 0.07f, 0.07f), new Color(0.35f, 0.24f, 0.14f), R()),
                eye = new Color(0.1f, 0.1f, 0.12f),
                hasHat = R() < 0.18f,
                hatColor = new Color(0.3f + R() * 0.4f, 0.3f + R() * 0.35f, 0.3f + R() * 0.35f),
                bulk = 0.86f + R() * 0.3f
            };
        }

        public static void Lamp(WorldContext ctx, Vector3 basePos)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "LampPole";
            pole.transform.position = basePos + new Vector3(0, 1.9f, 0);
            pole.transform.localScale = new Vector3(0.14f, 1.9f, 0.14f);
            Paint(ctx, pole, new Color(0.25f, 0.25f, 0.28f));

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "LampHead";
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.transform.position = basePos + new Vector3(0, 3.9f, 0);
            head.transform.localScale = new Vector3(0.5f, 0.3f, 0.5f);
            Paint(ctx, head, new Color(0.45f, 0.45f, 0.4f));

            // 路灯：range 13→20、强度按照明口径换算（≈3.4）。原来的 1.3/13m 在夜里
            // 只能照亮灯柱脚下两三米，走出去就是全黑——那不叫路灯，叫地灯。
            var l = SceneLighting.MakePoint("LampLight",
                basePos + new Vector3(0, 3.6f, 0), new Color(1f, 0.88f, 0.6f), 20f);
            l.enabled = false; // 由昼夜循环开关

            if (ctx.dayNight != null)
            {
                ctx.dayNight.lamps.Add(l);
                ctx.dayNight.lampHeads.Add(head.GetComponent<MeshRenderer>());
            }
        }

        /// <summary>车辆部件：本地坐标下的立方子件（decorative=true 去碰撞体）。</summary>
        static GameObject BuildCarPart(WorldContext ctx, Transform parent, string name,
            Vector3 localPos, Vector3 localScale, Color color, bool decorative)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (decorative) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            Paint(ctx, go, color);
            return go;
        }

        /// <summary>行道树：锥形树干 + 分叉枝 + 多球体积树冠（有机、非"棒棒糖"）。</summary>
        static void Tree(WorldContext ctx, Vector3 basePos)
        {
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "TreeTrunk";
            trunk.transform.position = basePos + new Vector3(0, 1.5f, 0);
            trunk.transform.localScale = new Vector3(0.34f, 1.5f, 0.34f);
            Paint(ctx, trunk, new Color(0.36f, 0.26f, 0.16f));

            // 两根分叉主枝（斜插入树冠）
            var branchColor = new Color(0.34f, 0.24f, 0.15f);
            int[] bs = { -1, 1 };
            foreach (var sgn in bs)
            {
                var br = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                br.name = "TreeBranch";
                Object.DestroyImmediate(br.GetComponent<Collider>());
                br.transform.position = basePos + new Vector3(sgn * 0.5f, 3.0f, 0);
                br.transform.rotation = Quaternion.Euler(0, 0, sgn * 32f);
                br.transform.localScale = new Vector3(0.16f, 0.9f, 0.16f);
                Paint(ctx, br, branchColor);
                Player.CameraOcclusionFade.RegisterOccluder(br.GetComponent<Renderer>());
            }

            // 多球树冠：不同大小/绿度/偏移堆出有机体积
            var crowns = new (Vector3 off, float s, Color c)[]
            {
                (new Vector3(0f, 3.5f, 0f),    2.7f, new Color(0.20f, 0.44f, 0.20f)),
                (new Vector3(0.9f, 4.1f, 0.4f),1.9f, new Color(0.26f, 0.52f, 0.24f)),
                (new Vector3(-0.8f,4.0f,-0.5f),1.7f, new Color(0.22f, 0.48f, 0.22f)),
                (new Vector3(0.2f, 4.7f, -0.2f),1.5f,new Color(0.30f, 0.56f, 0.28f)),
            };
            foreach (var cr in crowns)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "TreeCrown";
                Object.DestroyImmediate(s.GetComponent<Collider>());
                s.transform.position = basePos + cr.off;
                s.transform.localScale = Vector3.one * cr.s;
                Paint(ctx, s, cr.c);
                Player.CameraOcclusionFade.RegisterOccluder(s.GetComponent<Renderer>());
            }
            Player.CameraOcclusionFade.RegisterOccluder(trunk.GetComponent<Renderer>());
        }

        static void Bench(WorldContext ctx, Vector3 basePos, float yRot)
        {
            var seat = Box(ctx, "Bench", basePos + new Vector3(0, 0.45f, 0),
                new Vector3(2.2f, 0.12f, 0.6f), new Color(0.5f, 0.35f, 0.2f));
            seat.transform.rotation = Quaternion.Euler(0, yRot, 0);
            var back = Decoration(ctx, "BenchBack", basePos + new Vector3(0, 0.85f, -0.28f),
                new Vector3(2.2f, 0.5f, 0.08f), new Color(0.45f, 0.32f, 0.18f));
            back.transform.RotateAround(basePos, Vector3.up, yRot);
            for (int i = -1; i <= 1; i += 2)
            {
                var leg = Decoration(ctx, "BenchLeg", basePos + new Vector3(i * 0.9f, 0.22f, 0),
                    new Vector3(0.12f, 0.45f, 0.5f), new Color(0.2f, 0.2f, 0.22f));
                leg.transform.RotateAround(basePos, Vector3.up, yRot);
            }
        }

        static void TrashBin(WorldContext ctx, Vector3 basePos)
        {
            var bin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bin.name = "TrashBin";
            bin.transform.position = basePos + new Vector3(0, 0.45f, 0);
            bin.transform.localScale = new Vector3(0.5f, 0.45f, 0.5f);
            Paint(ctx, bin, new Color(0.2f, 0.4f, 0.28f));
        }

        /// <summary>斑马线：一组白色条纹。</summary>
        static void Crosswalk(WorldContext ctx, Vector3 center, bool alongZ)
        {
            for (int i = -3; i <= 3; i++)
            {
                Vector3 offset = alongZ ? new Vector3(0, 0, i * 1.2f) : new Vector3(i * 1.2f, 0, 0);
                Vector3 size = alongZ ? new Vector3(7.2f, 0.05f, 0.6f) : new Vector3(0.6f, 0.05f, 7.2f);
                Decoration(ctx, "Zebra", center + offset + new Vector3(0, 0.06f, 0), size,
                    new Color(0.92f, 0.92f, 0.92f));
            }
        }

        static void BusStop(WorldContext ctx, Vector3 basePos)
        {
            Box(ctx, "BusStopBack", basePos + new Vector3(0, 1.3f, 0.9f),
                new Vector3(4f, 2.4f, 0.12f), new Color(0.55f, 0.6f, 0.65f));
            Decoration(ctx, "BusStopRoof", basePos + new Vector3(0, 2.6f, 0.2f),
                new Vector3(4.4f, 0.1f, 1.8f), new Color(0.3f, 0.35f, 0.42f));
            Box(ctx, "BusStopSeat", basePos + new Vector3(0, 0.45f, 0.6f),
                new Vector3(3.2f, 0.12f, 0.5f), new Color(0.5f, 0.35f, 0.2f));
            Decoration(ctx, "BusSign", basePos + new Vector3(1.8f, 2f, 0.86f),
                new Vector3(0.8f, 0.8f, 0.06f), new Color(0.2f, 0.5f, 0.85f));
        }

        static void Mire(WorldContext ctx, Vector3 basePos, Vector3 size)
        {
            Decoration(ctx, "MireVisual", basePos + new Vector3(0, 0.03f, 0),
                new Vector3(size.x, 0.06f, size.z), new Color(0.12f, 0.08f, 0.18f));
            var zone = new GameObject("MireZone");
            zone.transform.position = basePos + new Vector3(0, 1, 0);
            var col = zone.AddComponent<BoxCollider>();
            col.size = size;
            zone.AddComponent<ProcrastinationMire>();
        }

        /// <summary>
        /// 建一扇门。
        ///
        /// 目标【不再由调用处写死】，而是由 Portal 在运行时按章节顺序解析（见 Portal.Resolve）：
        /// 每个关卡只有两扇门——回上一关（Back）与去下一关（Forward）。
        /// 以前是每个 Build 函数各自硬编码目标区号，于是训练武馆同时开着通往
        /// 噪声街区、两元赌桌、独居小屋的三扇门，其中大半既不是相邻关、也进不去，
        /// 显示出来纯属干扰。
        ///
        /// 门所属的区域由横坐标反推：各区原点是 (300·i, 0, 0)，而门相对区原点的偏移
        /// 最大不过 ±60 米，所以 round(x/300) 必然等于区号——不需要每个 Build 函数
        /// 再多传一个参数进来。
        /// </summary>
        static void MakePortal(WorldContext ctx, Vector3 basePos, PortalRole role)
            => MakePortal(ctx, basePos, role, -1, null);

        /// <summary>
        /// 建一扇【显式目标】的门：目标区号写死，不参与章节解析。
        ///
        /// 只给**不在章节序列里的区域**用：开放城区常开常在，AI 现场生成的临时站点
        /// 更是压根没有章节号——它们都算不出"上一关/下一关"。
        /// 经典 24 关一律走上面那个按章节解析的版本，免得又退回"每扇门硬编码目标"的老问题。
        /// </summary>
        public static void MakePortal(WorldContext ctx, Vector3 basePos, int targetZone,
            string label = null)
            => MakePortal(ctx, basePos, PortalRole.Forward, targetZone, label);

        static void MakePortal(WorldContext ctx, Vector3 basePos, PortalRole role,
            int explicitZone, string explicitLabel)
        {
            int homeZone = Mathf.Clamp(Mathf.RoundToInt(basePos.x / 300f), 0, ZoneCount - 1);
            var root = new GameObject("Portal_" + ZoneIdOf(homeZone) + "_" + role);
            root.transform.position = basePos;

            Box(ctx, "PortalPillar", basePos + new Vector3(-1.6f, 1.6f, 0), new Vector3(0.5f, 3.2f, 0.5f),
                new Color(0.3f, 0.5f, 0.7f));
            Box(ctx, "PortalPillar", basePos + new Vector3(1.6f, 1.6f, 0), new Vector3(0.5f, 3.2f, 0.5f),
                new Color(0.3f, 0.5f, 0.7f));
            Decoration(ctx, "PortalTop", basePos + new Vector3(0, 3.4f, 0), new Vector3(3.7f, 0.4f, 0.5f),
                new Color(0.3f, 0.5f, 0.7f));
            var glow = Decoration(ctx, "PortalGlow", basePos + new Vector3(0, 1.6f, 0),
                new Vector3(2.7f, 2.8f, 0.15f),
                role == PortalRole.Back ? new Color(0.85f, 0.8f, 0.55f) : new Color(0.55f, 0.85f, 1f));

            // 门顶标牌（文字由 Portal 按解析结果实时刷新）
            var signGo = new GameObject("PortalSign");
            signGo.transform.SetParent(root.transform, false);
            signGo.transform.position = basePos + new Vector3(0, 4.2f, 0);
            var tm = WorldText.Attach(signGo, "", 56, 0.07f, new Color(0.7f, 0.95f, 1f));
            signGo.AddComponent<FaceCamera>();

            // 【门前地台】——每扇门下面都保证有实地可站。
            // 传送改成「站定才走」之后暴露了一个一直存在的关卡问题：
            // 训练武馆的东西两扇门开在 x=±24，而武馆地板只到 ±25，触发体 3 米宽
            // 有半米悬在虚空上，玩家往门里再走一步就掉下去。与其逐个挪几十扇门的坐标，
            // 不如让 MakePortal 自己铺一块比触发体更大的地台：任何门口从此都踩得住。
            Box(ctx, "GroundPad_Portal", basePos + new Vector3(0, -0.25f, 0),
                new Vector3(5.2f, 0.5f, 4.4f), new Color(0.32f, 0.34f, 0.4f));

            var trigger = new GameObject("PortalTrigger");
            trigger.transform.SetParent(root.transform, false);
            trigger.transform.position = basePos + new Vector3(0, 1.5f, 0);
            var col = trigger.AddComponent<BoxCollider>();
            col.size = new Vector3(3f, 3f, 2.2f);
            var portal = trigger.AddComponent<Portal>();
            portal.homeZone = homeZone;
            portal.role = role;
            portal.explicitZone = explicitZone;
            portal.explicitLabel = explicitLabel;
            portal.sign = tm;
            portal.glow = glow;
        }

        /// <summary>世界空间铭牌（3D 文字，常朝镜头）：给展品/展台等标注可读文本。</summary>
        static void Plaque(Vector3 pos, string text, Color color)
        {
            var go = new GameObject("Plaque");
            go.transform.position = pos;
            WorldText.Attach(go, text, 44, 0.05f, color);
            go.AddComponent<FaceCamera>();
        }

        static readonly Dictionary<string, Material> MatCache = new Dictionary<string, Material>();

        public static void Paint(WorldContext ctx, GameObject go, Color c)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return;

            // 按物体名判定表面类别（墙/地/木/砖/金属…），并按尺寸算平铺次数——
            // 同 (颜色, 类别, 平铺) 共享一份材质：既上纹理又不炸材质数（移动端友好，
            // 保持 SRP Batcher 生效，不用 MaterialPropertyBlock）。
            SurfaceKind kind = KindOf(go.name);
            int tile = kind == SurfaceKind.None ? 0 : TileFor(go.transform.localScale);
            string key = ColorKey(c) + "|" + (int)kind + "|" + tile;

            if (!MatCache.TryGetValue(key, out var m) || m == null)
            {
                if (ctx.mat != null) m = new Material(ctx.mat);
                else
                {
                    var sh = Shader.Find("Universal Render Pipeline/Lit");
                    if (sh == null) sh = Shader.Find("Standard");
                    m = new Material(sh);
                }
                m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                // 轻度光滑度：让表面在天光/主光下有微弱高光与反射，摆脱"纯哑光塑料"观感
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.18f);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.18f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);

                // 程序化细节纹理（灰度）作为 _BaseMap 与颜色相乘：保留分区配色，叠加真实表面
                if (kind != SurfaceKind.None)
                {
                    var alb = ProceduralTextures.Albedo(kind);
                    if (alb != null)
                    {
                        var sc = new Vector2(tile, tile);
                        if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", alb); m.SetTextureScale("_BaseMap", sc); }
                        if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", alb); m.SetTextureScale("_MainTex", sc); }
                    }
                }
                MatCache[key] = m;
            }
            r.sharedMaterial = m;
        }

        /// <summary>颜色量化为稳定字符串键（0..255）。</summary>
        static string ColorKey(Color c) =>
            (int)(c.r * 255) + "," + (int)(c.g * 255) + "," + (int)(c.b * 255);

        /// <summary>按物体尺寸估算平铺次数（约每 3 世界单位一格），限幅避免过密/过疏。</summary>
        static int TileFor(Vector3 s)
        {
            float a = Mathf.Abs(s.x), b = Mathf.Abs(s.y), c = Mathf.Abs(s.z);
            float max = Mathf.Max(a, Mathf.Max(b, c));
            float min = Mathf.Min(a, Mathf.Min(b, c));
            float mid = a + b + c - max - min;             // 取两个较大边的均值定平铺
            return Mathf.Clamp(Mathf.RoundToInt((max + mid) * 0.5f / 3f), 1, 24);
        }

        /// <summary>按 GameObject 名称把表面归类到对应纹理（发光/玻璃/标牌等保持无纹理）。</summary>
        static SurfaceKind KindOf(string n)
        {
            if (string.IsNullOrEmpty(n)) return SurfaceKind.None;
            if (NameHas(n, "Glow", "Flame", "Light", "Warn", "Spark", "Window", "Sign",
                    "Zebra", "Bill", "Crown", "Canvas", "Frame", "Spot", "Portal", "Glass",
                    "Ped", "Ring")) return SurfaceKind.None;
            if (NameHas(n, "Bench", "Desk", "Shelf", "Door", "Plank", "SleepBox", "Bed",
                    "Tree", "Book", "Crate")) return SurfaceKind.Wood;
            if (NameHas(n, "Pillar", "Car", "Station", "Basin", "Bin", "Pole", "Nurse",
                    "Booth", "Machine", "Scale", "Rail", "Lamp", "Bus")) return SurfaceKind.Metal;
            if (NameHas(n, "Building")) return SurfaceKind.Brick;
            if (NameHas(n, "Road", "Ground", "Pave", "Asphalt", "Street")) return SurfaceKind.Ground;
            if (NameHas(n, "Carpet", "Mat", "Rug", "Mire")) return SurfaceKind.Fabric;
            if (NameHas(n, "Wall", "Divider", "Joint")) return SurfaceKind.Plaster;
            if (NameHas(n, "Floor", "Tier", "Arena", "Seg", "Head", "Plinth", "Base",
                    "Platform", "Pan", "Ramp")) return SurfaceKind.Concrete;
            return SurfaceKind.None;
        }

        static bool NameHas(string n, params string[] keys)
        {
            foreach (var k in keys) if (n.Contains(k)) return true;
            return false;
        }
    }

    /// <summary>3D 文字朝向镜头。</summary>
    public class FaceCamera : MonoBehaviour
    {
        void LateUpdate()
        {
            if (Camera.main != null)
                transform.rotation = Quaternion.LookRotation(
                    transform.position - Camera.main.transform.position);
        }
    }

    /// <summary>
    /// 失败展览馆·走近重构触发：靠近某件"失败"展品时，把"这就是你"的审判
    /// 重构成"发生过，但不是你的全部"。once=true 用作一次性入场提示。
    /// </summary>
    public class ExhibitReframeZone : MonoBehaviour
    {
        public string line;
        public bool once;
        public float cooldown = 12f;

        bool _done;
        float _readyAt;

        void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (once && _done) return;
            if (Time.time < _readyAt) return;
            var p = other.GetComponentInParent<Player.PlayerController>();
            if (p == null) return;
            if (once) _done = true; else _readyAt = Time.time + cooldown;
            if (!string.IsNullOrEmpty(line)) Core.GameEvents.RaiseSubtitle(line);
        }
    }
}
