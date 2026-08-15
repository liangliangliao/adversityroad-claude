using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// V2.0 开放世界垂直切片（方案 6 章 / 21.2）：
    /// **一片连续可自由移动的城市区域** —— 玩家住宅 + 街道 + 公交站 + 商店 +
    /// 办公区 + 小巷，从家门口一路走到任务区，全程不经过关卡菜单。
    ///
    /// 它不是"把原关卡摆在一张大地图上"，也不追求面积最大：
    /// 密度由目标节点与逆境事件驱动，不以面积为 KPI（方案 24 风险表）。
    /// 六大区域各自登记 EncounterSlot / RecoverySpot / 裂隙锚点，
    /// AI 章节只能落进这些已导航验证的位置。
    /// </summary>
    public static class OpenWorldBuilder
    {
        /// <summary>城区在 ZoneBuilder 里的区域索引（BuildAll 时赋值）。</summary>
        public static int CityZoneIndex = -1;

        static readonly Color Asphalt = new Color(0.19f, 0.19f, 0.21f);
        static readonly Color Sidewalk = new Color(0.55f, 0.55f, 0.57f);
        static readonly Color Ground = new Color(0.36f, 0.38f, 0.36f);
        static readonly Color WallC = new Color(0.72f, 0.68f, 0.6f);

        public static Vector3 HomeDoorSpawn { get; private set; }
        public static Vector3 HomeInteriorSpawn { get; private set; }

        public static void Build(WorldContext ctx, int zoneIndex)
        {
            CityZoneIndex = zoneIndex;
            Vector3 o = ctx.zoneOrigins[zoneIndex];
            DistrictCatalog.Clear();

            // ---------- 地面：一整片连续地板（不分区、无接缝、可自由行走） ----------
            ZoneBuilder.Box(ctx, "City_Ground", o + new Vector3(0, -0.25f, 0),
                new Vector3(260, 0.5f, 150), Ground);

            BuildMainRoad(ctx, o);
            BuildResidential(ctx, o);
            BuildCommercial(ctx, o);
            BuildOffice(ctx, o);
            BuildTransit(ctx, o);
            BuildService(ctx, o);
            BuildEdge(ctx, o);

            // 城区外圈：低矮护栏（挡住边界但不遮视野）
            ZoneBuilder.Ring(ctx, o, new Vector2(128, 73), 2.4f, new Color(0.32f, 0.33f, 0.36f));

            // 回到 V1 关卡选择世界的门（旧内容一个不丢：随时可以回去打经典章节）
            ZoneBuilder.MakePortal(ctx, o + new Vector3(-118, 0, -60), 0, "独居小屋");
        }

        // ================= 主干道（交通区脊柱） =================

        static void BuildMainRoad(WorldContext ctx, Vector3 o)
        {
            ZoneBuilder.Decoration(ctx, "Road", o + new Vector3(0, 0.02f, 0),
                new Vector3(250, 0.04f, 12), Asphalt);
            for (int x = -120; x <= 120; x += 8)
                ZoneBuilder.Decoration(ctx, "Lane", o + new Vector3(x, 0.05f, 0),
                    new Vector3(3f, 0.04f, 0.3f), Color.white);

            ZoneBuilder.Decoration(ctx, "Sidewalk", o + new Vector3(0, 0.05f, 9.5f),
                new Vector3(250, 0.1f, 7), Sidewalk);
            ZoneBuilder.Decoration(ctx, "Sidewalk", o + new Vector3(0, 0.05f, -9.5f),
                new Vector3(250, 0.1f, 7), Sidewalk);

            // 十字路口（交通区核心）
            ZoneBuilder.Decoration(ctx, "Road", o + new Vector3(20, 0.03f, 0),
                new Vector3(12, 0.04f, 120), Asphalt);
            ZoneBuilder.Decoration(ctx, "Crosswalk", o + new Vector3(12, 0.06f, 0),
                new Vector3(0.6f, 0.04f, 11), Color.white);
            ZoneBuilder.Decoration(ctx, "Crosswalk", o + new Vector3(28, 0.06f, 0),
                new Vector3(0.6f, 0.04f, 11), Color.white);

            for (int x = -110; x <= 110; x += 22)
            {
                ZoneBuilder.Lamp(ctx, o + new Vector3(x, 0, 7.2f));
                ZoneBuilder.Lamp(ctx, o + new Vector3(x + 11, 0, -7.2f));
            }

            // 车流：主干道双向 + 支路
            ctx.carRoutes.Add(new CarRoute
            {
                a = o + new Vector3(-120, 0.6f, 3),
                b = o + new Vector3(120, 0.6f, 3)
            });
            ctx.carRoutes.Add(new CarRoute
            {
                a = o + new Vector3(120, 0.6f, -3),
                b = o + new Vector3(-120, 0.6f, -3)
            });
            ctx.carRoutes.Add(new CarRoute
            {
                a = o + new Vector3(23, 0.6f, -58),
                b = o + new Vector3(23, 0.6f, 58)
            });

            // 行人：整条街都有生活，不是"只有敌人"（验收标准 20）
            for (int x = -110; x <= 110; x += 14)
            {
                ctx.pedestrianSpawns.Add(o + new Vector3(x, 1f, 9.5f));
                ctx.pedestrianSpawns.Add(o + new Vector3(x + 7, 1f, -9.5f));
            }
        }

        // ================= 住宅区（含 Player Home 实体化） =================

        static void BuildResidential(WorldContext ctx, Vector3 o)
        {
            Vector3 c = o + new Vector3(-88, 0, 34);
            var d = DistrictCatalog.Register("residential", "住宅区", c);

            var rng = new System.Random(9001);
            // 小区楼栋
            for (int i = 0; i < 4; i++)
                ZoneBuilder.Building(ctx, o + new Vector3(-118 + i * 20, 0, 58), 14, 16 + i * 3, 12, rng);
            // 便利店
            Shop(ctx, o + new Vector3(-58, 0, 20), "便利店", new Color(0.35f, 0.65f, 0.5f));
            // 小区绿地与长椅（恢复点）
            ZoneBuilder.Decoration(ctx, "Lawn", o + new Vector3(-72, 0.06f, 40),
                new Vector3(24, 0.06f, 16), new Color(0.28f, 0.42f, 0.26f));
            Bench(ctx, o + new Vector3(-72, 0, 34));
            d.recoverySpots.Add(o + new Vector3(-72, 1.1f, 34));
            d.recoverySpots.Add(o + new Vector3(-58, 1.1f, 16));

            BuildPlayerHome(ctx, o + new Vector3(-96, 0, 26));

            d.encounterSlots.Add(o + new Vector3(-78, 1.1f, 16));
            d.encounterSlots.Add(o + new Vector3(-104, 1.1f, 44));
            d.encounterSlots.Add(o + new Vector3(-62, 1.1f, 50));
            d.riftAnchor = o + new Vector3(-84, 0, 18);

            for (int i = 0; i < 4; i++)
                ctx.pedestrianSpawns.Add(o + new Vector3(-110 + i * 16, 1f, 46));
        }

        /// <summary>
        /// Player Home（方案 6.3 / 17.3）：V1.0 安全屋的功能一个不少，
        /// 但升级为开放世界里可以走进去的真实住宅——玄关、客厅、卧室、厨房、卫生间，
        /// 书桌、电脑、手机、冰箱、床、衣柜、窗户和墙上的目标板。
        /// 菜单功能尽量由房间物件承载，而不是弹一堆按钮。
        /// </summary>
        static void BuildPlayerHome(WorldContext ctx, Vector3 h)
        {
            const float W = 26f, D = 20f;
            ZoneBuilder.Box(ctx, "Home_Floor", h + new Vector3(0, -0.05f, 0),
                new Vector3(W, 0.2f, D), new Color(0.5f, 0.42f, 0.34f));

            // 外墙（南墙留门洞，正对街道）
            ZoneBuilder.Box(ctx, "Wall", h + new Vector3(0, 1.6f, D / 2), new Vector3(W, 3.2f, 0.4f), WallC);
            ZoneBuilder.Box(ctx, "Wall", h + new Vector3(-W / 2, 1.6f, 0), new Vector3(0.4f, 3.2f, D), WallC);
            ZoneBuilder.Box(ctx, "Wall", h + new Vector3(W / 2, 1.6f, 0), new Vector3(0.4f, 3.2f, D), WallC);
            ZoneBuilder.Box(ctx, "Wall", h + new Vector3(-8, 1.6f, -D / 2), new Vector3(10, 3.2f, 0.4f), WallC);
            ZoneBuilder.Box(ctx, "Wall", h + new Vector3(8, 1.6f, -D / 2), new Vector3(10, 3.2f, 0.4f), WallC);
            ZoneBuilder.Box(ctx, "Wall", h + new Vector3(0, 2.9f, -D / 2), new Vector3(6, 0.6f, 0.4f), WallC);

            // 内隔墙：玄关 / 客厅 / 卧室 / 厨房 / 卫生间
            ZoneBuilder.Box(ctx, "Wall", h + new Vector3(-4.5f, 1.6f, -4), new Vector3(0.3f, 3.2f, 12), WallC);
            ZoneBuilder.Box(ctx, "Wall", h + new Vector3(6, 1.6f, 2), new Vector3(0.3f, 3.2f, 16), WallC);
            ZoneBuilder.Box(ctx, "Wall", h + new Vector3(-9, 1.6f, 2), new Vector3(9, 3.2f, 0.3f), WallC);

            HomeSign(h + new Vector3(0, 3.6f, -D / 2 - 0.4f), "我 的 住 处");

            // —— 玄关（进门第一格）——
            ZoneBuilder.Decoration(ctx, "Rug", h + new Vector3(0, 0.08f, -8),
                new Vector3(4, 0.04f, 2.4f), new Color(0.42f, 0.24f, 0.22f));

            // —— 客厅：沙发 / 茶几 / 电视 ——
            ZoneBuilder.Box(ctx, "Sofa", h + new Vector3(1.5f, 0.4f, -3.5f),
                new Vector3(5, 0.8f, 1.8f), new Color(0.34f, 0.36f, 0.44f));
            ZoneBuilder.Box(ctx, "Table", h + new Vector3(1.5f, 0.3f, -0.5f),
                new Vector3(3, 0.6f, 1.4f), new Color(0.42f, 0.3f, 0.2f));
            ZoneBuilder.Decoration(ctx, "Screen", h + new Vector3(1.5f, 1.4f, 5.6f),
                new Vector3(3.4f, 1.9f, 0.15f), new Color(0.16f, 0.18f, 0.24f));

            // —— 卧室（西北）：床 / 衣柜 / 窗 ——
            ZoneBuilder.Box(ctx, "Bed", h + new Vector3(-9, 0.35f, 6.5f),
                new Vector3(4, 0.7f, 5.4f), new Color(0.35f, 0.42f, 0.6f));
            ZoneBuilder.Box(ctx, "Pillow", h + new Vector3(-9, 0.8f, 8.6f),
                new Vector3(2.6f, 0.3f, 1.1f), new Color(0.9f, 0.9f, 0.92f));
            var wardrobe = ZoneBuilder.Box(ctx, "Wardrobe", h + new Vector3(-12.2f, 1.2f, 1.2f),
                new Vector3(0.8f, 2.4f, 4f), new Color(0.46f, 0.34f, 0.24f));
            HomeFixture.Attach(wardrobe, HomeFixtureKind.Wardrobe);
            ZoneBuilder.Decoration(ctx, "Window", h + new Vector3(-12.9f, 1.9f, 7.5f),
                new Vector3(0.1f, 1.6f, 3.2f), new Color(0.65f, 0.8f, 1f));

            // —— 书桌角（东北）：书桌 / 电脑 / 手机 / 椅子 ——
            var desk = ZoneBuilder.Box(ctx, "Desk", h + new Vector3(9.5f, 0.5f, 6.5f),
                new Vector3(4.4f, 1f, 1.8f), new Color(0.42f, 0.3f, 0.2f));
            HomeFixture.Attach(desk, HomeFixtureKind.Desk);
            var pc = ZoneBuilder.Box(ctx, "Computer", h + new Vector3(9.5f, 1.5f, 7.1f),
                new Vector3(1.8f, 1f, 0.15f), new Color(0.55f, 0.75f, 1f));
            HomeFixture.Attach(pc, HomeFixtureKind.Computer);
            var phone = ZoneBuilder.Box(ctx, "Phone", h + new Vector3(8f, 1.06f, 6.2f),
                new Vector3(0.4f, 0.06f, 0.8f), new Color(0.85f, 0.9f, 1f));
            HomeFixture.Attach(phone, HomeFixtureKind.Phone);
            ZoneBuilder.Box(ctx, "Chair", h + new Vector3(9.5f, 0.35f, 4.6f),
                new Vector3(1.1f, 0.7f, 1.1f), new Color(0.25f, 0.25f, 0.28f));

            // —— 厨房（东南）：冰箱 / 灶台 ——
            var fridge = ZoneBuilder.Box(ctx, "Fridge", h + new Vector3(11.5f, 1.1f, -5f),
                new Vector3(1.6f, 2.2f, 1.4f), new Color(0.86f, 0.88f, 0.9f));
            HomeFixture.Attach(fridge, HomeFixtureKind.Fridge);
            ZoneBuilder.Box(ctx, "Counter", h + new Vector3(9.5f, 0.5f, -8f),
                new Vector3(5, 1f, 1.4f), new Color(0.6f, 0.58f, 0.55f));

            // —— 卫生间（西南）：镜子 ——
            ZoneBuilder.Box(ctx, "Wall", h + new Vector3(-9, 1.6f, -4.2f), new Vector3(9, 3.2f, 0.3f), WallC);
            var mirror = ZoneBuilder.Box(ctx, "Mirror", h + new Vector3(-12.4f, 1.6f, -6.5f),
                new Vector3(0.15f, 1.6f, 1.8f), new Color(0.78f, 0.86f, 0.92f));
            HomeFixture.Attach(mirror, HomeFixtureKind.Mirror);
            ZoneBuilder.Box(ctx, "Sink", h + new Vector3(-11.6f, 0.45f, -6.5f),
                new Vector3(1.2f, 0.9f, 1.4f), new Color(0.9f, 0.9f, 0.92f));

            // —— 墙上的目标板（Goal Board 的物理化身）——
            var board = ZoneBuilder.Box(ctx, "GoalBoard", h + new Vector3(3.5f, 1.7f, 9.7f),
                new Vector3(4.4f, 2.2f, 0.25f), new Color(0.95f, 0.8f, 0.25f));
            board.AddComponent<GoalBoard>();
            HomeFixture.Attach(board, HomeFixtureKind.GoalBoard);
            HomeSign(h + new Vector3(3.5f, 3.15f, 9.5f), "目 标 板");

            // 室内灯
            ZoneBuilder.AddCeilingLight(h + new Vector3(0, 2.9f, 0), new Color(1f, 0.9f, 0.72f), 20f);
            ZoneBuilder.AddCeilingLight(h + new Vector3(9, 2.9f, 6), new Color(0.9f, 0.94f, 1f), 12f);

            HomeInteriorSpawn = h + new Vector3(0, 1.1f, -6);
            HomeDoorSpawn = h + new Vector3(0, 1.1f, -D / 2 - 4);

            // 门口台阶 → 直接连到住宅区人行道，走出去就是开放世界（不经过菜单）
            ZoneBuilder.Decoration(ctx, "Path", h + new Vector3(0, 0.06f, -D / 2 - 8),
                new Vector3(6, 0.06f, 16), Sidewalk);
        }

        // ================= 商业区 =================

        static void BuildCommercial(WorldContext ctx, Vector3 o)
        {
            Vector3 c = o + new Vector3(-30, 0, 36);
            var d = DistrictCatalog.Register("commercial", "商业区", c);

            var rng = new System.Random(9002);
            ZoneBuilder.Building(ctx, o + new Vector3(-46, 0, 52), 22, 20, 18, rng);   // 商场主体
            HomeSign(o + new Vector3(-46, 12f, 42.5f), "商 场");
            Shop(ctx, o + new Vector3(-18, 0, 24), "店铺", new Color(0.6f, 0.45f, 0.3f));
            Shop(ctx, o + new Vector3(-6, 0, 24), "店铺", new Color(0.45f, 0.4f, 0.6f));

            // 广场（人群压力发生地）
            ZoneBuilder.Decoration(ctx, "Plaza", o + new Vector3(-28, 0.06f, 34),
                new Vector3(30, 0.06f, 20), new Color(0.6f, 0.58f, 0.56f));
            for (int i = 0; i < 6; i++)
                ctx.pedestrianSpawns.Add(o + new Vector3(-40 + i * 5, 1f, 34));
            Bench(ctx, o + new Vector3(-28, 0, 26));
            d.recoverySpots.Add(o + new Vector3(-28, 1.1f, 26));

            d.encounterSlots.Add(o + new Vector3(-28, 1.1f, 34));
            d.encounterSlots.Add(o + new Vector3(-14, 1.1f, 46));
            d.encounterSlots.Add(o + new Vector3(-44, 1.1f, 30));
            d.riftAnchor = o + new Vector3(-34, 0, 42);
        }

        // ================= 工作区 =================

        static void BuildOffice(WorldContext ctx, Vector3 o)
        {
            Vector3 c = o + new Vector3(46, 0, 38);
            var d = DistrictCatalog.Register("office", "工作区", c);

            var rng = new System.Random(9003);
            ZoneBuilder.Building(ctx, o + new Vector3(40, 0, 56), 20, 30, 16, rng);
            ZoneBuilder.Building(ctx, o + new Vector3(66, 0, 54), 16, 24, 14, rng);
            HomeSign(o + new Vector3(40, 16f, 47.5f), "写 字 楼");

            // 一层大堂（可走入的室内：会议室 + 招聘区）
            Vector3 lobby = o + new Vector3(46, 0, 30);
            ZoneBuilder.Box(ctx, "Office_Floor", lobby + new Vector3(0, -0.05f, 0),
                new Vector3(28, 0.2f, 16), new Color(0.62f, 0.62f, 0.64f));
            ZoneBuilder.Box(ctx, "Wall", lobby + new Vector3(0, 1.6f, 8), new Vector3(28, 3.2f, 0.4f), WallC);
            ZoneBuilder.Box(ctx, "Wall", lobby + new Vector3(-14, 1.6f, 0), new Vector3(0.4f, 3.2f, 16), WallC);
            ZoneBuilder.Box(ctx, "Wall", lobby + new Vector3(14, 1.6f, 0), new Vector3(0.4f, 3.2f, 16), WallC);
            ZoneBuilder.Box(ctx, "Wall", lobby + new Vector3(-9, 1.6f, -8), new Vector3(10, 3.2f, 0.4f), WallC);
            ZoneBuilder.Box(ctx, "Wall", lobby + new Vector3(9, 1.6f, -8), new Vector3(10, 3.2f, 0.4f), WallC);
            // 会议桌 + 招聘台
            ZoneBuilder.Box(ctx, "Table", lobby + new Vector3(-7, 0.5f, 3),
                new Vector3(7, 1f, 2.6f), new Color(0.4f, 0.32f, 0.26f));
            ZoneBuilder.Box(ctx, "Counter", lobby + new Vector3(8, 0.55f, 4),
                new Vector3(6, 1.1f, 1.6f), new Color(0.5f, 0.5f, 0.56f));
            HomeSign(lobby + new Vector3(8, 2.4f, 4.9f), "招 聘 区");
            ZoneBuilder.AddCeilingLight(lobby + new Vector3(0, 2.9f, 0), new Color(0.92f, 0.95f, 1f), 22f);

            d.encounterSlots.Add(lobby + new Vector3(0, 1.1f, 0));
            d.encounterSlots.Add(o + new Vector3(52, 1.1f, 46));
            d.encounterSlots.Add(o + new Vector3(34, 1.1f, 20));
            d.recoverySpots.Add(o + new Vector3(58, 1.1f, 22));
            d.riftAnchor = lobby + new Vector3(-10, 0, -4);

            for (int i = 0; i < 4; i++)
                ctx.pedestrianSpawns.Add(o + new Vector3(34 + i * 8, 1f, 18));
        }

        // ================= 交通区 =================

        static void BuildTransit(WorldContext ctx, Vector3 o)
        {
            Vector3 c = o + new Vector3(20, 0, 0);
            var d = DistrictCatalog.Register("transit", "交通区", c);

            // 公交站（家门口走两步就能坐车——快速移动入口）
            BusStop(ctx, o + new Vector3(-40, 0, 10.5f));
            BusStop(ctx, o + new Vector3(56, 0, -10.5f));

            // 地铁入口
            ZoneBuilder.Box(ctx, "SubwayEntrance", o + new Vector3(20, 1.2f, 16),
                new Vector3(6, 2.4f, 4), new Color(0.3f, 0.34f, 0.42f));
            HomeSign(o + new Vector3(20, 3.2f, 13.8f), "地 铁 站");

            // 停车场
            ZoneBuilder.Decoration(ctx, "Parking", o + new Vector3(92, 0.06f, -32),
                new Vector3(46, 0.06f, 34), new Color(0.3f, 0.3f, 0.32f));
            for (int i = 0; i < 6; i++)
                ZoneBuilder.Decoration(ctx, "ParkLine", o + new Vector3(74 + i * 7, 0.08f, -32),
                    new Vector3(0.25f, 0.04f, 12), Color.white);
            for (int i = 0; i < 4; i++)
                ZoneBuilder.Box(ctx, "Pillar", o + new Vector3(76 + i * 12, 2f, -46),
                    new Vector3(1.2f, 4f, 1.2f), new Color(0.42f, 0.42f, 0.45f));

            d.encounterSlots.Add(o + new Vector3(20, 1.1f, 0));
            d.encounterSlots.Add(o + new Vector3(92, 1.1f, -32));
            d.encounterSlots.Add(o + new Vector3(-40, 1.1f, 14));
            d.recoverySpots.Add(o + new Vector3(-40, 1.1f, 12));
            d.riftAnchor = o + new Vector3(92, 0, -42);
        }

        // ================= 生活服务区 =================

        static void BuildService(WorldContext ctx, Vector3 o)
        {
            Vector3 c = o + new Vector3(-26, 0, -38);
            var d = DistrictCatalog.Register("service", "生活服务区", c);

            var rng = new System.Random(9004);
            ZoneBuilder.Building(ctx, o + new Vector3(-52, 0, -46), 20, 14, 16, rng);
            HomeSign(o + new Vector3(-52, 9f, -37.5f), "医 院");
            Shop(ctx, o + new Vector3(-18, 0, -24), "餐馆", new Color(0.7f, 0.45f, 0.3f));
            Shop(ctx, o + new Vector3(-4, 0, -24), "银行", new Color(0.4f, 0.5f, 0.65f));

            // 公园（恢复/支持点）
            ZoneBuilder.Decoration(ctx, "Lawn", o + new Vector3(-30, 0.06f, -50),
                new Vector3(32, 0.06f, 22), new Color(0.26f, 0.42f, 0.24f));
            Bench(ctx, o + new Vector3(-30, 0, -42));
            Bench(ctx, o + new Vector3(-38, 0, -56));
            d.recoverySpots.Add(o + new Vector3(-30, 1.1f, -42));
            d.recoverySpots.Add(o + new Vector3(-38, 1.1f, -56));
            d.recoverySpots.Add(o + new Vector3(-18, 1.1f, -20));

            d.encounterSlots.Add(o + new Vector3(-26, 1.1f, -30));
            d.encounterSlots.Add(o + new Vector3(-48, 1.1f, -56));
            d.encounterSlots.Add(o + new Vector3(-8, 1.1f, -40));
            d.riftAnchor = o + new Vector3(-44, 0, -34);

            for (int i = 0; i < 4; i++)
                ctx.pedestrianSpawns.Add(o + new Vector3(-40 + i * 12, 1f, -28));
        }

        // ================= 边缘区 =================

        static void BuildEdge(WorldContext ctx, Vector3 o)
        {
            Vector3 c = o + new Vector3(88, 0, 34);
            var d = DistrictCatalog.Register("edge", "边缘区", c);

            // 小巷：两排高墙夹出的窄道（高风险遭遇/宿敌出没）
            ZoneBuilder.Box(ctx, "Wall", o + new Vector3(88, 2.5f, 44), new Vector3(40, 5, 0.6f),
                new Color(0.34f, 0.32f, 0.3f));
            ZoneBuilder.Box(ctx, "Wall", o + new Vector3(88, 2.5f, 26), new Vector3(40, 5, 0.6f),
                new Color(0.34f, 0.32f, 0.3f));
            ZoneBuilder.Decoration(ctx, "AlleyGround", o + new Vector3(88, 0.06f, 35),
                new Vector3(40, 0.06f, 18), new Color(0.24f, 0.24f, 0.25f));
            for (int i = 0; i < 4; i++)
                ZoneBuilder.Box(ctx, "TrashBin", o + new Vector3(74 + i * 9, 0.6f, 41),
                    new Vector3(1.2f, 1.2f, 1.2f), new Color(0.24f, 0.3f, 0.26f));

            // 废弃建筑与车库入口
            ZoneBuilder.Box(ctx, "AbandonedBuilding", o + new Vector3(112, 5f, 36),
                new Vector3(16, 10, 16), new Color(0.32f, 0.3f, 0.3f));
            ZoneBuilder.Box(ctx, "GarageMouth", o + new Vector3(64, 1.6f, 35),
                new Vector3(2, 3.2f, 8), new Color(0.2f, 0.2f, 0.22f));

            ZoneBuilder.Lamp(ctx, o + new Vector3(80, 0, 44.8f));
            ZoneBuilder.Lamp(ctx, o + new Vector3(98, 0, 25.2f));

            d.encounterSlots.Add(o + new Vector3(88, 1.1f, 35));
            d.encounterSlots.Add(o + new Vector3(108, 1.1f, 28));
            d.encounterSlots.Add(o + new Vector3(70, 1.1f, 40));
            d.recoverySpots.Add(o + new Vector3(80, 1.1f, 44));
            d.riftAnchor = o + new Vector3(104, 0, 40);
        }

        // ================= 世界生活（NavMesh 烘焙后调用） =================

        /// <summary>
        /// 有日程的市民（方案 6.6）：居住 → 出行 → 工作/活动 → 返回。
        /// 城市必须先是"有人生活的地方"，然后才是"有敌人的地方"（验收标准 20）。
        /// </summary>
        public static void SpawnCityNpcs(WorldContext ctx)
        {
            if (CityZoneIndex < 0 || !DistrictCatalog.Ready) return;
            Vector3 o = ctx.zoneOrigins[CityZoneIndex];
            var res = DistrictCatalog.Get("residential");
            var off = DistrictCatalog.Get("office");
            var com = DistrictCatalog.Get("commercial");
            var svc = DistrictCatalog.Get("service");
            if (res == null || off == null || com == null || svc == null) return;

            var rng = new System.Random(4242);
            // 绝大多数是普通市民；挑战/伪装型只有两个，且都会先亮明身份
            NpcKind[] kinds =
            {
                NpcKind.Ordinary, NpcKind.Ordinary, NpcKind.Ordinary, NpcKind.Ordinary,
                NpcKind.Ordinary, NpcKind.Ordinary, NpcKind.Neutral, NpcKind.Ally,
                NpcKind.Quest, NpcKind.Ordinary, NpcKind.Challenge, NpcKind.Disguised
            };

            for (int i = 0; i < kinds.Length; i++)
            {
                Vector3 home = res.center + new Vector3(
                    (float)(rng.NextDouble() * 30 - 15), 0, (float)(rng.NextDouble() * 20 - 10));
                if (!UnityEngine.AI.NavMesh.SamplePosition(home, out var hit, 6f,
                        UnityEngine.AI.NavMesh.AllAreas)) continue;

                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "Citizen_" + CityNpc.KindLabel(kinds[i]);
                go.transform.position = hit.position + Vector3.up * 1f;
                go.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
                ZoneBuilder.Paint(ctx, go, new Color(
                    0.4f + (float)rng.NextDouble() * 0.45f,
                    0.42f + (float)rng.NextDouble() * 0.4f,
                    0.45f + (float)rng.NextDouble() * 0.4f));
                go.AddComponent<UnityEngine.AI.NavMeshAgent>();

                Vector3 work = (i % 2 == 0 ? off.center : com.center) +
                    new Vector3((float)(rng.NextDouble() * 16 - 8), 0, (float)(rng.NextDouble() * 12 - 6));
                Vector3 leisure = svc.center +
                    new Vector3((float)(rng.NextDouble() * 20 - 10), 0, (float)(rng.NextDouble() * 14 - 7));
                CityNpc.Attach(go, kinds[i], hit.position, work, leisure);

                // 非普通 NPC 头顶挂一个可读标签：敌人识别必须清晰、公平
                if (kinds[i] != NpcKind.Ordinary)
                    HomeSign(go.transform.position + new Vector3(0, 2.2f, 0),
                        CityNpc.KindLabel(kinds[i]));
            }

            // 街边的固定摊位与人群感（不动的生活痕迹）
            for (int i = 0; i < 3; i++)
                ZoneBuilder.Decoration(ctx, "Stall", o + new Vector3(-24 + i * 12, 1.1f, 12.5f),
                    new Vector3(3.2f, 2.2f, 2.2f), new Color(0.6f, 0.5f, 0.35f));
        }

        // ================= 小构件 =================

        static void Shop(WorldContext ctx, Vector3 p, string label, Color tint)
        {
            ZoneBuilder.Box(ctx, "ShopBody", p + new Vector3(0, 2.2f, 0), new Vector3(10, 4.4f, 8), tint);
            ZoneBuilder.Decoration(ctx, "ShopGlass", p + new Vector3(0, 1.6f, -4.1f),
                new Vector3(7.4f, 2.4f, 0.12f), new Color(0.7f, 0.85f, 0.95f, 1f));
            ZoneBuilder.Decoration(ctx, "ShopSign", p + new Vector3(0, 3.6f, -4.15f),
                new Vector3(6.4f, 0.9f, 0.15f), tint * 1.4f);
            HomeSign(p + new Vector3(0, 3.6f, -4.35f), label);
        }

        static void Bench(WorldContext ctx, Vector3 p)
        {
            ZoneBuilder.Box(ctx, "Bench", p + new Vector3(0, 0.45f, 0),
                new Vector3(3.2f, 0.25f, 1f), new Color(0.5f, 0.36f, 0.24f));
            ZoneBuilder.Decoration(ctx, "BenchBack", p + new Vector3(0, 0.9f, 0.45f),
                new Vector3(3.2f, 0.9f, 0.15f), new Color(0.5f, 0.36f, 0.24f));
        }

        static void BusStop(WorldContext ctx, Vector3 p)
        {
            ZoneBuilder.Box(ctx, "BusStopPole", p + new Vector3(-2.4f, 1.5f, 0),
                new Vector3(0.2f, 3f, 0.2f), new Color(0.3f, 0.32f, 0.36f));
            ZoneBuilder.Decoration(ctx, "BusStopRoof", p + new Vector3(0, 2.9f, 0),
                new Vector3(6, 0.2f, 2.4f), new Color(0.4f, 0.45f, 0.5f));
            Bench(ctx, p + new Vector3(0, 0, 0.6f));
            HomeSign(p + new Vector3(0, 3.4f, 0), "公 交 站");
        }

        /// <summary>世界内文字标牌（面向镜头，零美术资源）。</summary>
        public static void HomeSign(Vector3 pos, string text)
        {
            var go = new GameObject("Sign_" + text);
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = 52;
            tm.characterSize = 0.08f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.95f, 0.92f, 0.8f);
            var mr = go.GetComponent<MeshRenderer>();
            if (tm.font != null) mr.material = tm.font.material;
            go.AddComponent<FaceCamera>();
        }
    }
}
