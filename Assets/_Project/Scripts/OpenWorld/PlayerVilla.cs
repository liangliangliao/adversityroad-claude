using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 我的住处：一栋两层的私人别墅。
    ///
    /// 【这一版重做了什么】
    /// 上一版是 48×36 的单层平面，问题玩家都指出来了：房间之间没真正隔开、
    /// 没有门、没有屋顶、照明不足，走一圈还会晕。这一版按真实住宅的做法重排：
    ///
    /// · 占地 84×60（约 5000 ㎡，是上一版的三倍多），**两层**，中间有一部能走的楼梯；
    /// · 每个房间都由完整的墙围出来，墙上开的每一个洞都装了门框与门扇，
    ///   而不是"留个缺口当门"；
    /// · 一层与二层各有楼板与天花，屋顶封顶，每个房间单独吊灯 + 灯具本体；
    /// · 踢脚线、门套、窗框、窗帘、地毯这些"看着才像房子"的东西一并补上；
    /// · 椅子/沙发/床可以真的坐下、躺下休息，跑步机能真的跑起来；
    /// · 整栋楼标记为寻路禁区：市民与敌人一律进不来，这里只属于玩家。
    ///
    /// 仍然是运行时程序化搭建，不引入任何美术资源。
    /// </summary>
    public static class PlayerVilla
    {
        // ---- 主体尺寸 ----
        public const float W = 84f, D = 60f;      // 一层外墙尺寸
        public const float FloorH = 3.9f;         // 层高（净高 ≈3.6）
        public const float SlabY = 4.1f;          // 二层楼板顶面
        const float WallT = 0.28f;
        const float DoorW = 3.0f, DoorH = 2.6f;

        // ---- 配色 ----
        static readonly Color Wall = new Color(0.88f, 0.86f, 0.82f);
        static readonly Color WallWarm = new Color(0.76f, 0.70f, 0.62f);
        static readonly Color Skirt = new Color(0.55f, 0.50f, 0.45f);
        static readonly Color FloorWood = new Color(0.47f, 0.34f, 0.23f);
        static readonly Color FloorTile = new Color(0.74f, 0.73f, 0.70f);
        static readonly Color Ceil = new Color(0.94f, 0.93f, 0.91f);
        static readonly Color Metal = new Color(0.62f, 0.64f, 0.68f);
        static readonly Color Dark = new Color(0.20f, 0.21f, 0.24f);
        static readonly Color Cloth = new Color(0.33f, 0.39f, 0.52f);
        static readonly Color Wood = new Color(0.40f, 0.28f, 0.18f);
        static readonly Color WoodLight = new Color(0.58f, 0.44f, 0.28f);
        static readonly Color Glass = new Color(0.60f, 0.78f, 0.90f);
        static readonly Color Water = new Color(0.22f, 0.58f, 0.78f);

        public static Vector3 InteriorSpawn { get; private set; }
        public static Vector3 DoorSpawn { get; private set; }

        /// <summary>整栋宅子（含院子）的世界包围盒——用来把 NPC 挡在外面。</summary>
        public static Bounds Lot { get; private set; }

        public static void Build(WorldContext ctx, Vector3 h)
        {
            // 宅基地：房子 + 前院 + 泳池院 + 车库，卡在主干道以南那块新地里
            Lot = new Bounds(h + new Vector3(4f, 4f, 0f), new Vector3(150f, 20f, 74f));

            VillaKit.Begin(ctx, h);
            Site(ctx, h);
            Shell(ctx, h);
            GroundFloorWalls(ctx, h);
            UpperFloorWalls(ctx, h);
            Stairs(ctx, h);

            Foyer(ctx, h);
            LivingRoom(ctx, h);
            Dining(ctx, h);
            Kitchen(ctx, h);
            GuestBath(ctx, h);
            Gym(ctx, h);
            Lounge(ctx, h);

            MasterBedroom(ctx, h);
            MasterBath(ctx, h);
            Office(ctx, h);
            Study(ctx, h);
            Balcony(ctx, h);

            Pool(ctx, h);
            Garage(ctx, h);
            Garden(ctx, h);
            Cat(ctx, h);
            Clutter(ctx, h);
            Lights(ctx, h);
            BlockAgents(h);

            // 室内舒适镜头：进屋自动收短吊杆、停掉跟随绕行（防晕，见 IndoorZone）
            IndoorZone.ResetAll();
            IndoorZone.Create(h + new Vector3(0, SlabY * 0.5f + 1f, 0),
                new Vector3(W - 1f, SlabY + FloorH, D - 1f), "Villa_Indoor");
            IndoorZone.Create(h + new Vector3(-W / 2f - 14f, 2f, 8f),
                new Vector3(17f, 4f, 12f), "Garage_Indoor");

            // 开局站在客厅、正对目标看板——上一版落在玄关门洞里，
            // 玩家看到的是"站在自家大门口"，第一眼既不是家也不是目标
            InteriorSpawn = h + new Vector3(-3f, 1.1f, -2f);
            DoorSpawn = h + new Vector3(0, 1.1f, D / 2f + 6f);    // 门廊外
        }

        // ================= 场地 / 外壳 =================

        static void Site(WorldContext ctx, Vector3 h)
        {
            VillaKit.Box("Villa_Lot", h + new Vector3(4f, -0.12f, 0f),
                new Vector3(150f, 0.24f, 74f), new Color(0.36f, 0.44f, 0.30f));
            // 一层地板：起居区木地板，湿区与厨房瓷砖
            VillaKit.Deco("Floor_G", h + new Vector3(0, 0.03f, 0),
                new Vector3(W - 0.6f, 0.06f, D - 0.6f), FloorWood);
            VillaKit.Deco("Floor_Tile_K", h + new Vector3(28f, 0.05f, 8f),
                new Vector3(27f, 0.06f, 15f), FloorTile);
            VillaKit.Deco("Floor_Tile_B", h + new Vector3(-35f, 0.05f, -12f),
                new Vector3(13f, 0.06f, 11f), FloorTile);
            // 入户步道：一路铺到主干道边，走回家不用在草地上找方向
            VillaKit.Deco("Path", h + new Vector3(0, 0.06f, D / 2f + 20f),
                new Vector3(8f, 0.06f, 40f), new Color(0.63f, 0.61f, 0.57f));
        }

        static void Shell(WorldContext ctx, Vector3 h)
        {
            float hw = W / 2f, hd = D / 2f;

            for (int lv = 0; lv < 2; lv++)
            {
                float y = lv * SlabY;
                // 外墙：南 / 东 / 西 三面整墙；北面一层留大门（见下）
                if (lv == 1)
                    VillaKit.Box("Wall_N", h + new Vector3(0, y + FloorH / 2f, hd),
                        new Vector3(W, FloorH, WallT), Wall);
                VillaKit.Box("Wall_S", h + new Vector3(0, y + FloorH / 2f, -hd),
                    new Vector3(W, FloorH, WallT), Wall);
                VillaKit.Box("Wall_W", h + new Vector3(-hw, y + FloorH / 2f, 0),
                    new Vector3(WallT, FloorH, D), Wall);
                VillaKit.Box("Wall_E", h + new Vector3(hw, y + FloorH / 2f, 0),
                    new Vector3(WallT, FloorH, D), Wall);

                if (lv == 0)
                {
                    // 一层北墙：中间留大门（大门朝街——宅子在主干道以南，人从北边走过来）
                    VillaKit.Box("Wall_N_L", h + new Vector3(-(hw + 4.2f) / 2f, y + FloorH / 2f, hd),
                        new Vector3(hw - 4.2f, FloorH, WallT), Wall);
                    VillaKit.Box("Wall_N_R", h + new Vector3((hw + 4.2f) / 2f, y + FloorH / 2f, hd),
                        new Vector3(hw - 4.2f, FloorH, WallT), Wall);
                    VillaKit.Box("Wall_N_Head", h + new Vector3(0, y + FloorH - 0.55f, hd),
                        new Vector3(8.4f, 1.1f, WallT), Wall);
                    VillaKit.Doorway(h + new Vector3(0, y, hd), 8.4f, 3.0f, true, WoodLight, Wood);
                    VillaKit.WallSign(h + new Vector3(0, 3.6f, hd + 0.35f), "入 口", Vector3.forward, 3.2f);
                }

                // 窗：南北各三扇、东西各两扇（带窗框，室内看得见外面）
                for (int i = -1; i <= 1; i++)
                {
                    Window(ctx, h + new Vector3(i * 24f, y + 1.9f, -hd), 8f, false);
                    if (lv == 1 || i != 0) Window(ctx, h + new Vector3(i * 24f, y + 1.9f, hd), 8f, false);
                }
                for (int i = -1; i <= 1; i += 2)
                {
                    Window(ctx, h + new Vector3(-hw, y + 1.9f, i * 16f), 8f, true);
                    Window(ctx, h + new Vector3(hw, y + 1.9f, i * 16f), 8f, true);
                }
            }

            // 二层楼板（一层的天花）：中间在楼梯口留一个洞
            Slab(ctx, h);
            // 二层天花 + 屋顶
            VillaKit.Deco("Ceil_2F", h + new Vector3(0, SlabY + FloorH - 0.12f, 0),
                new Vector3(W - 0.6f, 0.16f, D - 0.6f), Ceil);
            VillaKit.Box("Roof", h + new Vector3(0, SlabY + FloorH + 0.2f, 0),
                new Vector3(W + 2.2f, 0.4f, D + 2.2f), WallWarm);
            // 两坡顶：两块斜板搭在女儿墙里侧。轴对齐的平屋顶正是"像方块"的一大来源
            for (int s2 = -1; s2 <= 1; s2 += 2)
                VillaKit.Deco("RoofSlope", h + new Vector3(0, SlabY + FloorH + 2.0f, s2 * (D / 4f)),
                    new Vector3(W + 2.6f, 0.35f, D / 2f + 2.6f), new Color(0.42f, 0.30f, 0.26f),
                    new Vector3(s2 * 14f, 0, 0));
            VillaKit.Deco("RoofRidge", h + new Vector3(0, SlabY + FloorH + 3.6f, 0),
                new Vector3(W + 3f, 0.4f, 1.2f), new Color(0.34f, 0.24f, 0.21f));
            // 烟囱：屋顶上有个东西，整栋房子的轮廓才不是一个盒子
            VillaKit.Box("Chimney", h + new Vector3(-W / 4f, SlabY + FloorH + 3.2f, D / 5f),
                new Vector3(2.2f, 3.4f, 2.2f), new Color(0.52f, 0.40f, 0.34f));
            VillaKit.Deco("ChimneyCap", h + new Vector3(-W / 4f, SlabY + FloorH + 5.0f, D / 5f),
                new Vector3(2.8f, 0.3f, 2.8f), Dark);
            VillaKit.Deco("Eave", h + new Vector3(0, SlabY + FloorH + 0.55f, 0),
                new Vector3(W + 3.6f, 0.25f, D + 3.6f), Dark);
            // 女儿墙：屋顶不是一块飘着的板
            for (int s = -1; s <= 1; s += 2)
            {
                VillaKit.Deco("Parapet", h + new Vector3(0, SlabY + FloorH + 0.9f, s * (D / 2f + 1.4f)),
                    new Vector3(W + 3.4f, 1.1f, 0.35f), WallWarm);
                VillaKit.Deco("Parapet", h + new Vector3(s * (W / 2f + 1.4f), SlabY + FloorH + 0.9f, 0),
                    new Vector3(0.35f, 1.1f, D + 3.4f), WallWarm);
            }

            // 门廊
            for (int s = -1; s <= 1; s += 2)
                VillaKit.Box("Porch_Col", h + new Vector3(s * 5f, 2f, hd + 5f),
                    new Vector3(0.7f, 4f, 0.7f), Wall);
            VillaKit.Deco("Porch_Roof", h + new Vector3(0, 4.2f, hd + 3.5f),
                new Vector3(13f, 0.4f, 8f), WallWarm);
            VillaKit.Box("Porch_Step", h + new Vector3(0, 0.1f, hd + 1.6f),
                new Vector3(13f, 0.2f, 3f), FloorTile);
            VillaKit.WallSign(h + new Vector3(0, 4.9f, hd + 0.5f), "我 的 住 处", Vector3.forward, 6f);
        }

        /// <summary>二层楼板：楼梯口开洞，其余整块。</summary>
        static void Slab(WorldContext ctx, Vector3 h)
        {
            // 楼板整块铺满，只在楼梯正上方留一个 x∈[17,27]、z∈[14,23] 的井口。
            // 洞开小一点很重要：二层若有一大片空洞，玩家走着走着就掉回一层——
            // 那既不像房子，也是最容易被当成 Bug 的一种体验。
            VillaKit.Box("Slab_A", h + new Vector3(0, SlabY - 0.15f, -8f),
                new Vector3(W - 0.6f, 0.3f, 44f), Ceil);
            VillaKit.Box("Slab_B", h + new Vector3(0, SlabY - 0.15f, 26.5f),
                new Vector3(W - 0.6f, 0.3f, 7f), Ceil);
            VillaKit.Box("Slab_C", h + new Vector3(-12.35f, SlabY - 0.15f, 18.5f),
                new Vector3(58.7f, 0.3f, 9f), Ceil);
            VillaKit.Box("Slab_D", h + new Vector3(34.35f, SlabY - 0.15f, 18.5f),
                new Vector3(14.7f, 0.3f, 9f), Ceil);

            // 楼梯井护栏：楼梯从北往南上、出口在 z=14，所以**南侧不能拦**，
            // 拦在那里等于把上楼的人堵在最后一级台阶上。北侧与东西两侧各一道。
            VillaKit.Box("StairRail", h + new Vector3(22f, SlabY + 0.6f, 23.1f),
                new Vector3(10.4f, 1.2f, 0.16f), Metal);
            for (int s = -1; s <= 1; s += 2)
                VillaKit.Box("StairRail", h + new Vector3(22f + s * 5.1f, SlabY + 0.6f, 18.5f),
                    new Vector3(0.16f, 1.2f, 9f), Metal);
        }

        static void Window(WorldContext ctx, Vector3 at, float width, bool alongZ)
        {
            Vector3 glass = alongZ ? new Vector3(0.1f, 2.4f, width) : new Vector3(width, 2.4f, 0.1f);
            Vector3 frame = alongZ ? new Vector3(0.16f, 2.7f, width + 0.5f) : new Vector3(width + 0.5f, 2.7f, 0.16f);
            VillaKit.Deco("WindowFrame", at, frame, WoodLight);
            VillaKit.Deco("Window", at, glass, Glass);
            // 中挺：一整块玻璃看着像洞，加一竖一横就像窗
            Vector3 mull = alongZ ? new Vector3(0.14f, 2.4f, 0.12f) : new Vector3(0.12f, 2.4f, 0.14f);
            VillaKit.Deco("Mullion", at, mull, WoodLight);
        }

        // ================= 墙与门（真的有门扇） =================

        /// <summary>沿 X 方向的一道内墙，doors 里给门中心的 x 坐标。</summary>
        static void WallX(WorldContext ctx, Vector3 h, float y, float z, float x0, float x1, params float[] doors)
        {
            var cuts = new List<float>(doors);
            cuts.Sort();
            float cursor = x0;
            foreach (var d in cuts)
            {
                float a = d - DoorW / 2f, b = d + DoorW / 2f;
                if (a > cursor)
                    VillaKit.Box("Wall", h + new Vector3((cursor + a) / 2f, y + FloorH / 2f, z),
                        new Vector3(a - cursor, FloorH, WallT), Wall);
                // 门楣 + 门套 + 门扇
                VillaKit.Box("Wall_Head", h + new Vector3(d, y + (FloorH + DoorH) / 2f, z),
                    new Vector3(DoorW, FloorH - DoorH, WallT), Wall);
                VillaKit.Doorway(h + new Vector3(d, y, z), DoorW, DoorH, false, WoodLight, Wood);
                cursor = b;
            }
            if (x1 > cursor)
                VillaKit.Box("Wall", h + new Vector3((cursor + x1) / 2f, y + FloorH / 2f, z),
                    new Vector3(x1 - cursor, FloorH, WallT), Wall);
            Skirting(ctx, h, y, z, x0, x1, false);
        }

        /// <summary>沿 Z 方向的一道内墙，doors 里给门中心的 z 坐标。</summary>
        static void WallZ(WorldContext ctx, Vector3 h, float y, float x, float z0, float z1, params float[] doors)
        {
            var cuts = new List<float>(doors);
            cuts.Sort();
            float cursor = z0;
            foreach (var d in cuts)
            {
                float a = d - DoorW / 2f, b = d + DoorW / 2f;
                if (a > cursor)
                    VillaKit.Box("Wall", h + new Vector3(x, y + FloorH / 2f, (cursor + a) / 2f),
                        new Vector3(WallT, FloorH, a - cursor), Wall);
                VillaKit.Box("Wall_Head", h + new Vector3(x, y + (FloorH + DoorH) / 2f, d),
                    new Vector3(WallT, FloorH - DoorH, DoorW), Wall);
                VillaKit.Doorway(h + new Vector3(x, y, d), DoorW, DoorH, true, WoodLight, Wood);
                cursor = b;
            }
            if (z1 > cursor)
                VillaKit.Box("Wall", h + new Vector3(x, y + FloorH / 2f, (cursor + z1) / 2f),
                    new Vector3(WallT, FloorH, z1 - cursor), Wall);
            Skirting(ctx, h, y, x, z0, z1, true);
        }

        /// <summary>踢脚线：贴着墙脚的一条深色木线，房间立刻"装修过"。</summary>
        static void Skirting(WorldContext ctx, Vector3 h, float y, float fixedAxis, float a, float b, bool alongZ)
        {
            float len = b - a, mid = (a + b) / 2f;
            if (len <= 0.2f) return;
            Vector3 pos = alongZ ? new Vector3(fixedAxis, y + 0.09f, mid) : new Vector3(mid, y + 0.09f, fixedAxis);
            Vector3 size = alongZ ? new Vector3(WallT + 0.1f, 0.18f, len) : new Vector3(len, 0.18f, WallT + 0.1f);
            VillaKit.Deco("Skirting", h + pos, size, Skirt);
        }

        static void GroundFloorWalls(WorldContext ctx, Vector3 h)
        {
            // 玄关（大门在北侧，进门先是一个门厅）
            WallZ(ctx, h, 0, -8f, 18f, 30f);
            WallZ(ctx, h, 0, 8f, 18f, 30f);
            WallX(ctx, h, 0, 18f, -8f, 8f, 0f);           // 玄关 → 屋内

            // 客厅 / 餐厅 / 客卫 / 健身房 / 休息厅
            WallZ(ctx, h, 0, 14f, -30f, 6f, -9f);         // 客厅 ↔ 餐厅
            WallX(ctx, h, 0, 0f, 14f, 42f, 28f);          // 餐厅 ↔ 厨房
            WallZ(ctx, h, 0, -28f, -30f, -6f, -12f);      // 客卫
            WallX(ctx, h, 0, -6f, -42f, -28f);            // 客卫北墙
            WallX(ctx, h, 0, 6f, -42f, -20f, -32f);       // 健身房南墙
            WallZ(ctx, h, 0, -20f, 6f, 30f, 18f);         // 健身房 ↔ 休息厅
            WallX(ctx, h, 0, 6f, -20f, 14f, -4f);         // 客厅 ↔ 休息厅
            WallZ(ctx, h, 0, 14f, 6f, 30f, 22f);          // 休息厅 ↔ 楼梯厅
            // 【厨房与楼梯厅之间必须有墙】上一版 x∈[14,42]、z∈[0,30] 是一整个空间，
            // 楼梯就直接立在厨房里——玩家的原话是"上二层，从厨房上去？"。
            // 这道墙把厨房收在 z<14，楼梯厅独立成间，两者之间留一道门。
            WallX(ctx, h, 0, 14f, 14f, 42f, 19f);         // 厨房 ↔ 楼梯厅
            WallZ(ctx, h, 0, 30f, 14f, 30f, 26f);         // 楼梯厅 ↔ 洗衣房（东侧，留门）
        }

        static void UpperFloorWalls(WorldContext ctx, Vector3 h)
        {
            float y = SlabY;
            WallZ(ctx, h, y, -12f, -30f, 14f, -20f);      // 主卧 ↔ 办公室
            WallX(ctx, h, y, 0f, -42f, -12f, -30f);       // 主卧 ↔ 主卫/衣帽
            WallZ(ctx, h, y, -24f, 0f, 14f, 7f);          // 主卫 ↔ 衣帽间
            WallX(ctx, h, y, -8f, -12f, 42f, 4f, 28f);    // 办公室/书房 ↔ 走廊
            WallZ(ctx, h, y, 16f, -30f, -8f, -19f);       // 办公室 ↔ 书房
            WallX(ctx, h, y, 8f, -12f, 14f, 0f);          // 走廊 ↔ 露台（留一道观景门）
            WallX(ctx, h, y, 14f, -42f, -12f);            // 主卫北墙
        }

        /// <summary>
        /// 一部真的能走上去的楼梯。
        ///
        /// 【上一版为什么"要跳过隔板"】台阶按 26cm 一级算，接近角色控制器的
        /// 迈步上限（0.3），加上楼梯立在厨房当中、上去之后两侧都是楼板洞——
        /// 走不上去、上去了也没地方落脚。
        ///
        /// 现在：楼梯在自己的楼梯厅里，**从北往南上**（正对进厅的门），
        /// 22 级、每级 18.6cm，长度正好铺满楼梯井到二层楼板边缘——
        /// 最后一级踏面与二层地面齐平，往前一步就是二层走廊。
        /// </summary>
        static void Stairs(WorldContext ctx, Vector3 h)
        {
            const float startZ = 23f, endZ = 14f;     // 与楼板井口（z 14~23）严格对齐
            const int steps = 22;
            float run = (startZ - endZ) / steps, rise = SlabY / steps;
            Vector3 axis = h + new Vector3(22f, 0, 0);

            for (int i = 0; i < steps; i++)
            {
                float y = rise * (i + 1);
                float z = startZ - (i + 0.5f) * run;
                VillaKit.Box("Step", axis + new Vector3(0, y / 2f, z),
                    new Vector3(5.2f, y, run + 0.02f), FloorTile);
                // 踏面前沿的木质包边：一级级看得清，不是一堆灰台阶
                VillaKit.Deco("StepNose", axis + new Vector3(0, y + 0.02f, z + run / 2f),
                    new Vector3(5.2f, 0.05f, 0.12f), Wood);
            }

            // 两侧扶手：立柱按级排，扶手是一根斜着的圆柱
            for (int s2 = -1; s2 <= 1; s2 += 2)
            {
                for (int i = 0; i < steps; i += 3)
                {
                    float y = rise * (i + 1);
                    float z = startZ - (i + 0.5f) * run;
                    VillaKit.Metal(VillaKit.Cyl("Baluster", axis + new Vector3(s2 * 2.5f, y, z),
                        0.035f, 0.95f, Metal), Metal);
                }
                float midY = SlabY / 2f + 0.95f;
                float len = Mathf.Sqrt(Mathf.Pow(startZ - endZ, 2f) + SlabY * SlabY);
                float tilt = Mathf.Atan2(SlabY, startZ - endZ) * Mathf.Rad2Deg;
                VillaKit.Cyl("Handrail", axis + new Vector3(s2 * 2.5f, 0, (startZ + endZ) / 2f), 0.06f, 0.1f, Wood);
                VillaKit.CylAxis("Handrail", axis + new Vector3(s2 * 2.5f, midY, (startZ + endZ) / 2f),
                    0.06f, len, Wood, new Vector3(90f - tilt, 0, 0));
            }

            // 楼梯厅里的指示：牌子钉在进厅那道墙上，而不是飘在空中
            VillaKit.WallSign(h + new Vector3(14.4f, 2.5f, 26f), "上 二 层", Vector3.right, 2.6f);
            ZoneBuilder.AddCeilingLight(h + new Vector3(22f, 3.4f, 20f), new Color(1f, 0.95f, 0.86f), 20f);
        }

        // ================= 一层 =================

        static void Foyer(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(0, 0, 24f);
            VillaKit.Deco("EntryRug", c + new Vector3(0, 0.08f, 2f),
                new Vector3(5f, 0.05f, 3f), new Color(0.40f, 0.26f, 0.24f));
            VillaKit.Box("ShoeCabinet", c + new Vector3(-6.4f, 0.55f, 0),
                new Vector3(0.7f, 1.1f, 5f), Wood);
            VillaKit.Deco("EntryMirror", c + new Vector3(6.5f, 1.7f, 0),
                new Vector3(0.1f, 1.8f, 2.6f), new Color(0.80f, 0.88f, 0.94f));
            Plant(ctx, c + new Vector3(5.4f, 0, 3.4f));
            for (int s = -1; s <= 1; s += 2)
                VillaKit.Deco("Sconce", c + new Vector3(s * 7.6f, 2.4f, -2f),
                    new Vector3(0.3f, 0.5f, 0.3f), new Color(0.98f, 0.92f, 0.76f));
        }

        static void LivingRoom(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-3f, 0, -6f);

            VillaKit.Deco("Rug", c + new Vector3(0, 0.08f, -2f),
                new Vector3(14f, 0.05f, 9f), new Color(0.36f, 0.27f, 0.25f));
            Sofa(ctx, c + new Vector3(0, 0, -6.4f), 0f);
            Sofa(ctx, c + new Vector3(-7.4f, 0, -1.5f), 90f);
            Sofa(ctx, c + new Vector3(7.4f, 0, -1.5f), -90f);
            VillaKit.Box("CoffeeTable", c + new Vector3(0, 0.34f, -1.6f),
                new Vector3(4.4f, 0.68f, 2.2f), Wood);
            VillaKit.Deco("TableBooks", c + new Vector3(0.8f, 0.74f, -1.6f),
                new Vector3(0.8f, 0.12f, 0.6f), new Color(0.70f, 0.30f, 0.25f));
            VillaKit.Deco("Vase", c + new Vector3(-1.2f, 0.86f, -1.6f),
                new Vector3(0.35f, 0.36f, 0.35f), new Color(0.55f, 0.70f, 0.72f));

            // —— 一整面墙的目标看板 ——
            // 看板挂在客厅南墙、面朝北——大门在北侧，一进屋抬头就是它
            var frame = VillaKit.Box("GoalBoard_Frame", c + new Vector3(0, 2.3f, -11.6f),
                new Vector3(17f, 4.6f, 0.34f), Dark);
            VillaKit.Deco("GoalBoard_Screen", c + new Vector3(0, 2.3f, -11.4f),
                new Vector3(16.2f, 4.0f, 0.1f), new Color(0.07f, 0.11f, 0.17f));
            var gb = frame.AddComponent<Combat.GoalBoard>();
            gb.interactRange = 9f;
            HomeFixture.Attach(frame, HomeFixtureKind.GoalBoard);
            GoalBoardDisplay.Attach(frame, c + new Vector3(0, 2.3f, -11.35f), 16.2f, 4.0f, true);
            for (int i = -7; i <= 7; i++)
                VillaKit.Deco("BoardLed", c + new Vector3(i * 1.1f, 0.28f, -11.3f),
                    new Vector3(0.5f, 0.06f, 0.2f), new Color(0.35f, 0.75f, 1f));

            // 看板旁边的兵器架：出门取剑、回家放下（见 WeaponRack）
            WeaponRack.Build(ctx, c + new Vector3(-9.8f, 0, -10.4f), Vector3.forward);

            VillaKit.Box("Sideboard", c + new Vector3(-9.4f, 0.42f, 4.4f),
                new Vector3(3.4f, 0.84f, 1f), Wood);
            Plant(ctx, c + new Vector3(9.4f, 0, 4.4f));
            FloorLamp(ctx, c + new Vector3(-9.6f, 0, -6.4f));
            CeilingFan(ctx, c + new Vector3(0, FloorH - 0.55f, -2f));
            // 客厅：挂在西侧隔墙（x=-8 那道）上，出风朝东
            AirConditioner(ctx, h + new Vector3(-19.8f, 2.9f, -6f), Vector3.right);
            Curtain(ctx, h + new Vector3(-24f, 0, -D / 2f + 0.4f), 8f, false);
        }

        static void Dining(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(22f, 0, -9f);
            VillaKit.WallSign(c + new Vector3(-8f, 2.5f, 0), "餐 厅", Vector3.right, 2.2f);

            VillaKit.Box("DiningTable", c + new Vector3(0, 0.76f, 0),
                new Vector3(3.2f, 0.14f, 6.2f), Wood);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    VillaKit.Cyl("TableLeg", c + new Vector3(sx * 1.3f, 0f, sz * 2.6f), 0.09f, 0.76f, Wood, true);
            // 桌沿一圈线脚：平板和"有做工的桌子"差别就在这一圈
            VillaKit.Deco("TableEdge", c + new Vector3(0, 0.69f, 0), new Vector3(3.34f, 0.1f, 6.34f), Wood * 0.85f);

            for (int i = -1; i <= 1; i++)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 seat = c + new Vector3(s * 2.3f, 0, i * 2.1f);
                    DiningChair(ctx, seat, s > 0 ? -90f : 90f);
                    Vector3 plate = c + new Vector3(s * 1.05f, 0.84f, i * 2.1f);
                    VillaKit.Deco("Plate", plate, new Vector3(0.62f, 0.04f, 0.62f),
                        new Color(0.96f, 0.96f, 0.97f));
                    VillaKit.Deco("Glass", plate + new Vector3(-s * 0.5f, 0.11f, 0.34f),
                        new Vector3(0.16f, 0.26f, 0.16f), new Color(0.82f, 0.9f, 0.95f));
                    VillaKit.Deco("Cutlery", plate + new Vector3(s * 0.44f, 0, 0),
                        new Vector3(0.06f, 0.02f, 0.36f), Metal);
                }

            VillaKit.Deco("FruitBowl", c + new Vector3(0, 0.88f, 0),
                new Vector3(0.8f, 0.18f, 0.8f), new Color(0.74f, 0.70f, 0.62f));
            VillaKit.Deco("Fruit", c + new Vector3(0, 0.99f, 0),
                new Vector3(0.56f, 0.18f, 0.56f), new Color(0.85f, 0.42f, 0.25f));
            for (int i = -1; i <= 1; i++)
            {
                VillaKit.Deco("PendantRod", c + new Vector3(0, FloorH - 0.5f, i * 2.1f),
                    new Vector3(0.06f, 1f, 0.06f), Dark);
                VillaKit.Deco("Pendant", c + new Vector3(0, FloorH - 1.05f, i * 2.1f),
                    new Vector3(0.9f, 0.3f, 0.9f), new Color(0.98f, 0.90f, 0.72f));
            }
        }

        static void Kitchen(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(28f, 0, 7f);
            VillaKit.WallSign(c + new Vector3(0, 2.5f, -6.9f), "厨 房", Vector3.forward, 2.2f);

            // U 形橱柜
            Counter(ctx, c + new Vector3(0, 0, 6.4f), new Vector3(24f, 0.92f, 0.9f));
            Counter(ctx, c + new Vector3(-11.5f, 0, 1.5f), new Vector3(0.9f, 0.92f, 10f));
            Counter(ctx, c + new Vector3(11.5f, 0, 1.5f), new Vector3(0.9f, 0.92f, 10f));
            // 吊柜
            VillaKit.Box("UpperCabinet", c + new Vector3(-4f, 2.2f, 6.5f),
                new Vector3(12f, 1.2f, 0.7f), WoodLight);

            VillaKit.Deco("Stove", c + new Vector3(4f, 0.97f, 6.4f),
                new Vector3(2.2f, 0.06f, 0.8f), Dark);
            for (int i = 0; i < 4; i++)
                VillaKit.Deco("Burner",
                    c + new Vector3(3.4f + (i % 2) * 1.2f, 1.01f, 6.15f + (i / 2) * 0.5f),
                    new Vector3(0.34f, 0.03f, 0.34f), new Color(0.35f, 0.14f, 0.12f));
            VillaKit.Box("RangeHood", c + new Vector3(4f, 2.5f, 6.5f),
                new Vector3(2.4f, 0.6f, 0.9f), Metal);
            VillaKit.Deco("Sink", c + new Vector3(-3f, 0.95f, 6.4f),
                new Vector3(1.4f, 0.12f, 0.8f), new Color(0.80f, 0.82f, 0.84f));
            VillaKit.Deco("SinkTap", c + new Vector3(-3f, 1.22f, 6.7f),
                new Vector3(0.08f, 0.44f, 0.08f), Metal);

            var fridge = VillaKit.Box("Fridge", c + new Vector3(10.6f, 1.2f, 5.6f),
                new Vector3(1.8f, 2.4f, 1.6f), new Color(0.86f, 0.88f, 0.90f));
            HomeFixture.Attach(fridge, HomeFixtureKind.Fridge);
            VillaKit.Deco("FridgeSeam", c + new Vector3(10.6f, 1.2f, 4.78f),
                new Vector3(1.7f, 0.05f, 0.05f), Dark);

            // 中岛 + 吧椅（可以坐）
            VillaKit.Box("Island", c + new Vector3(0, 0.46f, 0f), new Vector3(6f, 0.92f, 2.2f), Wood);
            VillaKit.Deco("IslandTop", c + new Vector3(0, 0.95f, 0f), new Vector3(6.3f, 0.08f, 2.5f), Dark);
            for (int i = -1; i <= 1; i++)
                BarStool(ctx, c + new Vector3(i * 1.9f, 0, -2.2f));

            VillaKit.Deco("Microwave", c + new Vector3(-11.4f, 1.25f, 4f),
                new Vector3(0.9f, 0.55f, 1.1f), Dark);
            VillaKit.Deco("Kettle", c + new Vector3(-11.4f, 1.1f, 0f),
                new Vector3(0.36f, 0.36f, 0.36f), Metal);
        }

        static void Counter(WorldContext ctx, Vector3 at, Vector3 size)
        {
            VillaKit.Box("Counter", at + new Vector3(0, size.y / 2f, 0), size,
                new Color(0.58f, 0.56f, 0.54f));
            VillaKit.Deco("CounterTop", at + new Vector3(0, size.y + 0.04f, 0),
                new Vector3(size.x + 0.2f, 0.08f, size.z + 0.2f), Dark);
        }

        static void GuestBath(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-35f, 0, -12f);
            VillaKit.WallSign(c + new Vector3(6.9f, 2.5f, 0), "卫 生 间", Vector3.left, 2.6f);
            Bathroom(ctx, c, false);
        }

        static void Gym(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-31f, 0, 18f);
            VillaKit.WallSign(c + new Vector3(0, 2.5f, -11.9f), "健 身 房", Vector3.forward, 2.6f);
            VillaKit.Deco("GymMat", c + new Vector3(0, 0.08f, 0),
                new Vector3(20f, 0.06f, 22f), new Color(0.22f, 0.24f, 0.26f));

            // 跑步机（能真的跑起来，见 Treadmill）
            Treadmill.Build(ctx, c + new Vector3(-5f, 0, 6f));

            // 哑铃架
            VillaKit.Box("RackBase", c + new Vector3(6f, 0.4f, 9f), new Vector3(4.4f, 0.8f, 0.9f), Dark);
            for (int i = 0; i < 6; i++)
            {
                float x = c.x + 4.2f + i * 0.72f;
                VillaKit.Deco("DumbbellBar", new Vector3(x, 0.88f, c.z + 9f),
                    new Vector3(0.12f, 0.12f, 0.5f), Metal);
                for (int s = -1; s <= 1; s += 2)
                    VillaKit.Deco("DumbbellPlate", new Vector3(x, 0.88f, c.z + 9f + s * 0.22f),
                        new Vector3(0.36f, 0.36f, 0.12f), Dark);
            }

            // 卧推凳（可以坐）+ 杠铃架
            var bench = VillaKit.Box("Bench", c + new Vector3(4f, 0.5f, 2f),
                new Vector3(0.8f, 0.18f, 2.6f), new Color(0.30f, 0.16f, 0.16f));
            Sittable.Attach(bench, Vector3.back, "卧推凳", true);   // 躺下时头朝杠铃架那一头
            VillaKit.Box("BenchLeg", c + new Vector3(4f, 0.22f, 2f), new Vector3(0.6f, 0.44f, 2.2f), Dark);
            for (int s = -1; s <= 1; s += 2)
                VillaKit.Box("RackPost", c + new Vector3(4f + s * 0.9f, 0.75f, 3.3f),
                    new Vector3(0.14f, 1.5f, 0.14f), Metal);
            VillaKit.Deco("Barbell", c + new Vector3(4f, 1.52f, 3.3f),
                new Vector3(2.8f, 0.1f, 0.1f), Metal);
            for (int s = -1; s <= 1; s += 2)
                VillaKit.Deco("BarPlate", c + new Vector3(4f + s * 1.3f, 1.52f, 3.3f),
                    new Vector3(0.13f, 0.62f, 0.62f), Dark);

            // 单车 + 瑜伽垫 + 整面镜子
            VillaKit.Box("BikeBody", c + new Vector3(-5f, 0.5f, -2f), new Vector3(0.6f, 1f, 1.8f), Dark);
            VillaKit.Deco("BikeSeat", c + new Vector3(-5f, 1.06f, -2.4f), new Vector3(0.42f, 0.14f, 0.7f), Metal);
            VillaKit.Deco("BikeBar", c + new Vector3(-5f, 1.24f, -1.2f), new Vector3(0.86f, 0.1f, 0.1f), Metal);
            VillaKit.Deco("BikeWheel", c + new Vector3(-5f, 0.36f, -1f), new Vector3(0.14f, 0.72f, 0.72f), Metal);
            // 瑜伽垫：躺在地上的那个"健身动作"——躺下拉伸/仰卧，起来时是撑地起身
            var mat = VillaKit.Deco("YogaMat", c + new Vector3(5f, 0.1f, -4f),
                new Vector3(1.3f, 0.05f, 2.8f), new Color(0.35f, 0.62f, 0.55f));
            Sittable.Attach(mat, Vector3.back, "瑜伽垫", true, true);
            VillaKit.Deco("GymMirror", c + new Vector3(0, 1.8f, 11.3f),
                new Vector3(18f, 2.8f, 0.1f), new Color(0.78f, 0.86f, 0.92f));
            // 健身房：挂在西外墙上，出风朝东
            AirConditioner(ctx, h + new Vector3(-41.6f, 2.9f, 18f), Vector3.right);
        }

        /// <summary>休息厅：沙发、书架、通往楼梯的过厅。</summary>
        static void Lounge(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-3f, 0, 18f);
            VillaKit.Deco("Rug", c + new Vector3(0, 0.08f, 0),
                new Vector3(11f, 0.05f, 8f), new Color(0.30f, 0.32f, 0.36f));
            Sofa(ctx, c + new Vector3(0, 0, -3.8f), 0f);
            VillaKit.Box("LowShelf", c + new Vector3(-8f, 0.6f, 3f), new Vector3(1f, 1.2f, 7f), Wood);
            for (int i = 0; i < 10; i++)
                VillaKit.Deco("Book", c + new Vector3(-8f, 1.36f, 0f + i * 0.62f),
                    new Vector3(0.7f, 0.34f, 0.22f), BookColor(i * 3));
            Plant(ctx, c + new Vector3(7.5f, 0, 4f));
            FloorLamp(ctx, c + new Vector3(-7f, 0, -5f));
            var chair = VillaKit.Box("ArmChair", c + new Vector3(6f, 0.45f, -2f),
                new Vector3(1.8f, 0.9f, 1.8f), new Color(0.42f, 0.30f, 0.32f));
            Sittable.Attach(chair, -Vector3.forward, "扶手椅");
        }

        // ================= 二层 =================

        static void MasterBedroom(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-27f, SlabY, -15f);
            VillaKit.WallSign(c + new Vector3(0, 2.5f, -14.85f), "主 卧", Vector3.forward, 2.2f);
            VillaKit.Deco("BedRug", c + new Vector3(0, 0.08f, -2f),
                new Vector3(12f, 0.05f, 10f), new Color(0.34f, 0.28f, 0.30f));

            // 床：床架→床垫→床单→被子→枕头，可以躺上去休息
            VillaKit.Box("BedFrame", c + new Vector3(0, 0.24f, 0), new Vector3(5.2f, 0.48f, 6.8f), Wood);
            var mattress = VillaKit.Box("Mattress", c + new Vector3(0, 0.66f, 0),
                new Vector3(4.9f, 0.44f, 6.5f), new Color(0.93f, 0.92f, 0.90f));
            Sittable.Attach(mattress, Vector3.back, "床", true, false, default,
                0.30f);   // 被子顶面比床垫高 0.24 米：不抬起来就躺进被子里了
            VillaKit.Deco("BedSheet", c + new Vector3(0, 0.9f, 0),
                new Vector3(5.1f, 0.05f, 6.7f), new Color(0.96f, 0.96f, 0.98f));
            VillaKit.Deco("Quilt", c + new Vector3(0, 1.0f, -1.2f),
                new Vector3(5f, 0.24f, 4.2f), new Color(0.29f, 0.39f, 0.57f));
            VillaKit.Deco("QuiltFold", c + new Vector3(0, 1.08f, 0.95f),
                new Vector3(5f, 0.16f, 0.55f), new Color(0.38f, 0.48f, 0.66f));
            for (int s = -1; s <= 1; s += 2)
                VillaKit.Deco("Pillow", c + new Vector3(s * 1.15f, 1.1f, 2.6f),
                    new Vector3(1.9f, 0.3f, 1.0f), new Color(0.97f, 0.97f, 0.98f));
            VillaKit.Box("Headboard", c + new Vector3(0, 1.2f, 3.6f), new Vector3(5.4f, 1.8f, 0.28f), WallWarm);

            for (int s = -1; s <= 1; s += 2)
            {
                VillaKit.Box("Nightstand", c + new Vector3(s * 3.4f, 0.32f, 2.9f),
                    new Vector3(1.2f, 0.64f, 1.2f), Wood);
                VillaKit.Deco("LampShade", c + new Vector3(s * 3.4f, 0.92f, 2.9f),
                    new Vector3(0.6f, 0.52f, 0.6f), new Color(0.98f, 0.92f, 0.75f));
            }

            var wardrobe = VillaKit.Box("Wardrobe", c + new Vector3(-8.5f, 1.25f, -1f),
                new Vector3(0.9f, 2.5f, 6f), Wood);
            HomeFixture.Attach(wardrobe, HomeFixtureKind.Wardrobe);
            for (int i = -1; i <= 1; i += 2)
                VillaKit.Deco("WardrobeHandle", c + new Vector3(-8.0f, 1.25f, -1f + i * 0.8f),
                    new Vector3(0.06f, 0.5f, 0.06f), Metal);

            // 墙上的艺术画（可换成玩家自己的图片）
            UserPicture(ctx, c + new Vector3(0, 2.5f, 3.85f), new Vector3(4.2f, 2.6f, 0.08f),
                UserImageSlot.BedroomArtA, "艺 术 画");
            UserPicture(ctx, c + new Vector3(-8.9f, 2.4f, 5f), new Vector3(0.08f, 2.0f, 3.0f),
                UserImageSlot.BedroomArtB, "");

            CeilingFan(ctx, c + new Vector3(0, FloorH - 0.55f, -3f));
            // 主卧：挂在南外墙上，出风朝北
            AirConditioner(ctx, h + new Vector3(-27f, SlabY + 2.9f, -29.6f), Vector3.forward);
            Curtain(ctx, h + new Vector3(-24f, SlabY, -D / 2f + 0.4f), 8f, false);
        }

        static void MasterBath(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-33f, SlabY, 7f);
            VillaKit.WallSign(c + new Vector3(8.85f, 2.5f, 0), "主 卫", Vector3.left, 2.2f);
            Bathroom(ctx, c, true);
        }

        /// <summary>一套完整的卫生间：台盆 + 镜子 + 马桶 + 淋浴 +（主卫另加）浴缸。</summary>
        static void Bathroom(WorldContext ctx, Vector3 c, bool withTub)
        {
            VillaKit.Deco("BathTile", c + new Vector3(0, 0.07f, 0),
                new Vector3(11f, 0.05f, 11f), FloorTile);

            VillaKit.Box("Vanity", c + new Vector3(0, 0.44f, 4.2f),
                new Vector3(4.2f, 0.88f, 1.0f), new Color(0.55f, 0.52f, 0.50f));
            VillaKit.Deco("VanityTop", c + new Vector3(0, 0.9f, 4.2f),
                new Vector3(4.4f, 0.08f, 1.1f), new Color(0.30f, 0.30f, 0.32f));
            // 台盆：圆盆而不是一块白板
            VillaKit.Cyl("Basin", c + new Vector3(0, 0.86f, 4.2f), 0.42f, 0.2f, new Color(0.95f, 0.95f, 0.96f));
            VillaKit.Cyl("BasinInner", c + new Vector3(0, 0.9f, 4.2f), 0.34f, 0.14f, new Color(0.88f, 0.89f, 0.90f));
            // 水龙头：立管 + 弯头出水口，按 E 能开
            var tapBody = VillaKit.Cyl("Tap", c + new Vector3(0, 0.96f, 4.55f), 0.05f, 0.34f, Metal, true);
            VillaKit.Metal(tapBody, Metal);
            VillaKit.Metal(VillaKit.CylAxis("TapSpout", c + new Vector3(0, 1.3f, 4.4f), 0.045f, 0.34f,
                Metal, new Vector3(90f, 0, 0)), Metal);
            for (int s2 = -1; s2 <= 1; s2 += 2)
                VillaKit.Metal(VillaKit.Cyl("TapHandle", c + new Vector3(s2 * 0.22f, 0.98f, 4.55f),
                    0.035f, 0.12f, Metal), Metal);
            WaterOutlet.Attach(tapBody, c + new Vector3(0, 1.22f, 4.32f), 0.06f, 0.34f, "水 龙 头");
            var mirror = VillaKit.Box("Mirror", c + new Vector3(0, 2.0f, 4.72f),
                new Vector3(3.4f, 1.6f, 0.08f), new Color(0.80f, 0.88f, 0.94f));
            HomeFixture.Attach(mirror, HomeFixtureKind.Mirror);
            VillaKit.Deco("MirrorLight", c + new Vector3(0, 2.9f, 4.6f),
                new Vector3(3f, 0.1f, 0.22f), new Color(1f, 0.97f, 0.9f));

            VillaKit.Box("ToiletBase", c + new Vector3(-4.2f, 0.2f, 0.5f),
                new Vector3(0.85f, 0.4f, 1.3f), new Color(0.95f, 0.95f, 0.96f));
            VillaKit.Box("ToiletSeat", c + new Vector3(-4.2f, 0.45f, 0.35f),
                new Vector3(0.9f, 0.12f, 0.95f), new Color(0.97f, 0.97f, 0.98f));
            VillaKit.Box("ToiletTank", c + new Vector3(-4.2f, 0.7f, 1.15f),
                new Vector3(0.85f, 0.95f, 0.36f), new Color(0.95f, 0.95f, 0.96f));

            VillaKit.Deco("ShowerTray", c + new Vector3(3.2f, 0.09f, -3f),
                new Vector3(3.4f, 0.12f, 3.4f), new Color(0.82f, 0.84f, 0.84f));
            VillaKit.Deco("ShowerGlass", c + new Vector3(1.5f, 1.25f, -3f),
                new Vector3(0.08f, 2.5f, 3.4f), new Color(0.72f, 0.88f, 0.92f));
            VillaKit.Deco("ShowerGlass2", c + new Vector3(3.2f, 1.25f, -1.3f),
                new Vector3(3.4f, 2.5f, 0.08f), new Color(0.72f, 0.88f, 0.92f));
            var head = VillaKit.Cyl("ShowerHead", c + new Vector3(3.2f, 2.44f, -4.4f), 0.24f, 0.1f, Metal, true);
            VillaKit.Metal(head, Metal);
            VillaKit.Metal(VillaKit.Cyl("ShowerRiser", c + new Vector3(3.2f, 0.9f, -4.72f), 0.045f, 1.6f, Metal), Metal);
            VillaKit.Metal(VillaKit.CylAxis("ShowerArm", c + new Vector3(3.2f, 2.5f, -4.56f), 0.04f, 0.4f,
                Metal, new Vector3(90f, 0, 0)), Metal);
            WaterOutlet.Attach(head, c + new Vector3(3.2f, 2.38f, -4.4f), 0.34f, 2.3f, "花 洒");
            VillaKit.Deco("Towel", c + new Vector3(-4.6f, 1.6f, 3f),
                new Vector3(0.12f, 1.1f, 0.8f), new Color(0.86f, 0.62f, 0.42f));
            VillaKit.Metal(VillaKit.CylAxis("TowelBar", c + new Vector3(-4.66f, 2.2f, 3f), 0.03f, 1.2f,
                Metal, new Vector3(90f, 0, 0)), Metal);
            VillaKit.Cyl("Bin", c + new Vector3(-3.2f, 0, 3.4f), 0.22f, 0.55f, new Color(0.32f, 0.34f, 0.36f), true);
            VillaKit.Cyl("Drain", c + new Vector3(3.2f, 0.12f, -3f), 0.12f, 0.03f, Metal);
            VillaKit.Cyl("SoapBottle", c + new Vector3(0.9f, 0.94f, 4.3f), 0.07f, 0.24f, new Color(0.85f, 0.86f, 0.90f));
            VillaKit.Sph("SoapCap", c + new Vector3(0.9f, 1.2f, 4.3f), 0.12f, new Color(0.45f, 0.62f, 0.72f));

            if (!withTub) return;
            VillaKit.Box("Bathtub", c + new Vector3(-2.8f, 0.34f, -3.4f),
                new Vector3(2.2f, 0.68f, 3.8f), new Color(0.95f, 0.95f, 0.96f));
            VillaKit.Deco("TubWater", c + new Vector3(-2.8f, 0.64f, -3.4f),
                new Vector3(2.0f, 0.06f, 3.6f), new Color(0.55f, 0.80f, 0.88f));
        }

        static void Office(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(2f, SlabY, -19f);
            VillaKit.WallSign(c + new Vector3(0, 2.5f, 11.4f), "办 公 室", Vector3.back, 2.6f);
            VillaKit.Deco("OfficeRug", c + new Vector3(0, 0.08f, 0),
                new Vector3(10f, 0.05f, 8f), new Color(0.28f, 0.30f, 0.34f));

            var desk = VillaKit.Box("OfficeDesk", c + new Vector3(0, 0.74f, 2.4f),
                new Vector3(6.4f, 0.14f, 2.6f), Wood);
            HomeFixture.Attach(desk, HomeFixtureKind.Desk);
            for (int s = -1; s <= 1; s += 2)
                VillaKit.Box("DeskLeg", c + new Vector3(s * 2.9f, 0.37f, 2.4f),
                    new Vector3(0.18f, 0.74f, 2.3f), Metal);
            VillaKit.Box("Drawer", c + new Vector3(2.1f, 0.33f, 2.4f),
                new Vector3(1.3f, 0.66f, 2.0f), Wood);

            OfficeChair(ctx, c + new Vector3(0, 0, 0.4f));

            var pc = VillaKit.Box("Monitor", c + new Vector3(-1.0f, 1.32f, 3.2f),
                new Vector3(2.6f, 1.1f, 0.1f), new Color(0.16f, 0.20f, 0.28f));
            HomeFixture.Attach(pc, HomeFixtureKind.Computer);
            VillaKit.Deco("MonitorStand", c + new Vector3(-1.0f, 0.88f, 3.2f),
                new Vector3(0.32f, 0.3f, 0.32f), Dark);
            VillaKit.Deco("Keyboard", c + new Vector3(-1.0f, 0.83f, 2.1f),
                new Vector3(1.8f, 0.05f, 0.6f), Dark);
            var phone = VillaKit.Box("Phone", c + new Vector3(1.4f, 0.84f, 1.9f),
                new Vector3(0.42f, 0.05f, 0.84f), new Color(0.85f, 0.9f, 1f));
            HomeFixture.Attach(phone, HomeFixtureKind.Phone);

            // 桌上带相框的照片（可换成玩家自己的图片）
            VillaKit.Deco("FrameStand", c + new Vector3(2.4f, 0.86f, 3.0f),
                new Vector3(0.55f, 0.06f, 0.32f), Dark);
            UserPicture(ctx, c + new Vector3(2.4f, 1.2f, 3.1f), new Vector3(1.0f, 0.72f, 0.05f),
                UserImageSlot.DeskPhoto, "");
            VillaKit.Deco("FrameEdge", c + new Vector3(2.4f, 1.2f, 3.14f),
                new Vector3(1.12f, 0.84f, 0.03f), new Color(0.55f, 0.42f, 0.24f));

            VillaKit.Box("FileCabinet", c + new Vector3(-6.5f, 0.9f, 2f),
                new Vector3(1f, 1.8f, 3.6f), Wood);
            Plant(ctx, c + new Vector3(6f, 0, -2.4f));
            // 办公室：挂在南外墙上，出风朝北
            AirConditioner(ctx, h + new Vector3(2f, SlabY + 2.9f, -29.6f), Vector3.forward);
        }

        static void Study(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(29f, SlabY, -19f);
            VillaKit.WallSign(c + new Vector3(0, 2.5f, 11.4f), "书 房", Vector3.back, 2.2f);

            for (int s = 0; s < 3; s++)
            {
                float x = c.x - 7f + s * 7f;
                VillaKit.Box("Bookcase", new Vector3(x, c.y + 1.4f, c.z + 9.6f),
                    new Vector3(6f, 2.8f, 0.55f), Wood);
                for (int shelf = 0; shelf < 5; shelf++)
                {
                    float y = c.y + 0.45f + shelf * 0.55f;
                    VillaKit.Deco("Shelf", new Vector3(x, y, c.z + 9.5f),
                        new Vector3(5.8f, 0.06f, 0.5f), new Color(0.34f, 0.24f, 0.15f));
                    for (int b = 0; b < 18; b++)
                        VillaKit.Deco("Book",
                            new Vector3(x - 2.7f + b * 0.3f, y + 0.2f, c.z + 9.5f),
                            new Vector3(0.21f, 0.34f + ((s + shelf + b) % 4) * 0.03f, 0.4f),
                            BookColor(s * 91 + shelf * 13 + b));
                }
            }

            var readChair = VillaKit.Box("ReadChair", c + new Vector3(-4f, 0.45f, 2f),
                new Vector3(1.9f, 0.9f, 1.9f), new Color(0.42f, 0.30f, 0.32f));
            Sittable.Attach(readChair, Vector3.forward, "阅读椅");
            VillaKit.Box("SideTable", c + new Vector3(-1.8f, 0.32f, 2f),
                new Vector3(1.0f, 0.64f, 1.0f), Wood);
            VillaKit.Deco("Cup", c + new Vector3(-1.8f, 0.72f, 2f),
                new Vector3(0.24f, 0.26f, 0.24f), new Color(0.92f, 0.92f, 0.9f));
            FloorLamp(ctx, c + new Vector3(-6f, 0, 2f));
            VillaKit.Box("StudyDesk", c + new Vector3(4f, 0.74f, 1f),
                new Vector3(4.4f, 0.14f, 2f), Wood);
            var studyChair = VillaKit.Box("StudyChair", c + new Vector3(4f, 0.45f, -0.9f),
                new Vector3(1.1f, 0.16f, 1.1f), Dark);
            Sittable.Attach(studyChair, Vector3.forward, "书桌椅");
        }

        static void Balcony(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(14f, SlabY, 19f);
            VillaKit.WallSign(c + new Vector3(0, 2.5f, -10.85f), "露 台", Vector3.forward, 2.2f);
            // 露台在二层北侧：地板由楼板承担，这里只做栏杆与陈设
            for (int s = -1; s <= 1; s += 2)
                VillaKit.Deco("Railing", c + new Vector3(s * 13f, 0.6f, 4f),
                    new Vector3(0.12f, 1.2f, 20f), Metal);
            VillaKit.Deco("Railing", c + new Vector3(0, 0.6f, 10f),
                new Vector3(26f, 1.2f, 0.12f), Metal);
            for (int i = -1; i <= 1; i += 2)
            {
                var lounger = VillaKit.Box("Lounger", c + new Vector3(i * 4f, 0.35f, 4f),
                    new Vector3(1.1f, 0.2f, 2.6f), new Color(0.90f, 0.88f, 0.82f));
                Sittable.Attach(lounger, Vector3.back, "躺椅", true);
            }
            VillaKit.Box("PatioTable", c + new Vector3(0, 0.36f, 4f),
                new Vector3(1.6f, 0.72f, 1.6f), WoodLight);
            Plant(ctx, c + new Vector3(-10f, 0, 8f));
            Plant(ctx, c + new Vector3(10f, 0, 8f));
        }

        // ================= 室外 =================

        static void Pool(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(W / 2f + 22f, 0, 6f);
            VillaKit.WallSign(c + new Vector3(0, 2.4f, 13f), "泳 池", Vector3.back, 2.2f);

            VillaKit.Deco("PoolDeck", c + new Vector3(0, 0.05f, 0),
                new Vector3(30f, 0.1f, 26f), new Color(0.76f, 0.74f, 0.70f));
            VillaKit.Deco("PoolWater", c + new Vector3(0, 0.08f, 0),
                new Vector3(18f, 0.06f, 11f), Water);
            VillaKit.Deco("PoolLine", c + new Vector3(0, 0.1f, 0),
                new Vector3(17.4f, 0.02f, 0.18f), new Color(0.92f, 0.95f, 0.98f));
            for (int s = -1; s <= 1; s += 2)
            {
                VillaKit.Box("PoolCurb", c + new Vector3(0, 0.15f, s * 5.8f),
                    new Vector3(19f, 0.3f, 0.7f), FloorTile);
                VillaKit.Box("PoolCurb", c + new Vector3(s * 9.3f, 0.15f, 0),
                    new Vector3(0.7f, 0.3f, 12.3f), FloorTile);
                var lounger = VillaKit.Box("PoolLounger", c + new Vector3(s * 12.5f, 0.35f, 2f),
                    new Vector3(1.1f, 0.2f, 2.6f), new Color(0.90f, 0.88f, 0.82f));
                Sittable.Attach(lounger, Vector3.back, "泳池躺椅", true);
            }
            VillaKit.Deco("PoolLadder", c + new Vector3(8f, 0.6f, 6.2f),
                new Vector3(0.1f, 1.2f, 0.7f), Metal);
            VillaKit.Box("UmbrellaPole", c + new Vector3(12.5f, 1.3f, -2f),
                new Vector3(0.14f, 2.6f, 0.14f), Metal);
            VillaKit.Deco("Umbrella", c + new Vector3(12.5f, 2.7f, -2f),
                new Vector3(4f, 0.22f, 4f), new Color(0.85f, 0.40f, 0.32f));
            ZoneBuilder.AddCeilingLight(c + new Vector3(-5f, 0.5f, 0), new Color(0.45f, 0.85f, 1f), 16f);
            ZoneBuilder.AddCeilingLight(c + new Vector3(5f, 0.5f, 0), new Color(0.45f, 0.85f, 1f), 16f);
        }

        static void Garage(WorldContext ctx, Vector3 h)
        {
            // 车库贴在宅子西侧，**卷帘门朝北对着街**——上一版门朝南、还没有车道，
            // 玩家绕到房子背面也看不到它，自然会问"车库在哪里"。
            Vector3 c = h + new Vector3(-W / 2f - 14f, 0, 8f);

            VillaKit.Deco("GarageFloor", c + new Vector3(0, 0.06f, 0),
                new Vector3(17f, 0.08f, 12f), new Color(0.42f, 0.42f, 0.44f));
            VillaKit.Box("GarageWall", c + new Vector3(0, 1.9f, -6f), new Vector3(17f, 3.8f, 0.3f), WallWarm);
            for (int s2 = -1; s2 <= 1; s2 += 2)
                VillaKit.Box("GarageWall", c + new Vector3(s2 * 8.5f, 1.9f, 0), new Vector3(0.3f, 3.8f, 12f), WallWarm);
            VillaKit.Box("GarageRoof", c + new Vector3(0, 3.95f, 0), new Vector3(17.8f, 0.35f, 12.8f), WallWarm);

            // 卷帘门：抬起停在门楣上，门洞两侧有门垛——远远就看得出这是车库
            VillaKit.Deco("GarageShutter", c + new Vector3(0, 3.45f, 6f),
                new Vector3(14f, 0.85f, 0.25f), Metal);
            for (int i = 0; i < 6; i++)
                VillaKit.Deco("ShutterSlat", c + new Vector3(0, 3.15f + i * 0.14f, 6f),
                    new Vector3(14f, 0.1f, 0.3f), Metal * (0.9f - i * 0.03f));
            for (int s2 = -1; s2 <= 1; s2 += 2)
                VillaKit.Box("GaragePier", c + new Vector3(s2 * 7.6f, 1.9f, 6f),
                    new Vector3(1.6f, 3.8f, 0.5f), WallWarm);
            VillaKit.WallSign(c + new Vector3(0, 4.4f, 6.3f), "车 库", Vector3.forward, 3.6f);

            // 车道：从车库门口一直铺到主干道，中间画上引导线
            VillaKit.Deco("Driveway", c + new Vector3(0, 0.06f, 26f),
                new Vector3(10f, 0.06f, 40f), new Color(0.48f, 0.48f, 0.50f));
            for (int i = 0; i < 9; i++)
                VillaKit.Deco("DriveLine", c + new Vector3(0, 0.09f, 9f + i * 4.2f),
                    new Vector3(0.3f, 0.02f, 2.2f), new Color(0.86f, 0.84f, 0.72f));

            Car(ctx, c + new Vector3(-4f, 0, 0f), new Color(0.18f, 0.24f, 0.34f));
            Car(ctx, c + new Vector3(4f, 0, 0f), new Color(0.42f, 0.16f, 0.16f));

            VillaKit.Box("Workbench", c + new Vector3(6.6f, 0.45f, -4.2f), new Vector3(3.4f, 0.9f, 1.2f), Wood);
            for (int i = 0; i < 6; i++)
                VillaKit.Deco("Tool", c + new Vector3(5.2f + i * 0.55f, 2.0f, -5.4f),
                    new Vector3(0.12f, 0.62f, 0.1f), i % 2 == 0 ? Metal : new Color(0.8f, 0.5f, 0.2f));
            // 轮胎、油桶、纸箱：车库空着最假
            for (int i = 0; i < 3; i++)
                VillaKit.CylAxis("Tyre", c + new Vector3(-7f, 0.42f + i * 0.34f, -4.6f),
                    0.42f, 0.3f, new Color(0.12f, 0.12f, 0.13f), new Vector3(90f, 0, 0), true);
            VillaKit.Cyl("OilDrum", c + new Vector3(7.4f, 0, 2.4f), 0.42f, 1.1f,
                new Color(0.30f, 0.45f, 0.28f), true);
            VillaKit.Box("Carton", c + new Vector3(-7.2f, 0.4f, 2.6f), new Vector3(1.2f, 0.8f, 1.2f),
                new Color(0.62f, 0.48f, 0.32f));
            ZoneBuilder.AddCeilingLight(c + new Vector3(0, 3.4f, 0), new Color(1f, 0.96f, 0.88f), 20f);
        }

        static void Garden(WorldContext ctx, Vector3 h)
        {
            // 前院：草坪、树、路灯、信箱——让宅子有"外面"
            for (int i = -2; i <= 2; i++)
            {
                Tree(ctx, h + new Vector3(i * 16f, 0, -D / 2f - 20f));
                if (i != 0) Tree(ctx, h + new Vector3(i * 20f, 0, D / 2f + 12f));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                VillaKit.Metal(VillaKit.Cyl("GardenLampPole", h + new Vector3(s * 8f, 0, -D / 2f - 14f),
                    0.07f, 2.8f, Dark), Dark);
                VillaKit.Emit(VillaKit.Sph("GardenLampHead", h + new Vector3(s * 8f, 3.0f, -D / 2f - 14f),
                    0.55f, new Color(1f, 0.94f, 0.78f)), new Color(1f, 0.93f, 0.75f), 2.4f);
                ZoneBuilder.AddCeilingLight(h + new Vector3(s * 8f, 2.9f, -D / 2f - 14f),
                    new Color(1f, 0.92f, 0.74f), 14f);
            }
            VillaKit.Box("Mailbox", h + new Vector3(9f, 0.8f, -D / 2f - 21f),
                new Vector3(0.4f, 1.6f, 0.4f), Dark);
            VillaKit.Deco("MailboxTop", h + new Vector3(9f, 1.75f, -D / 2f - 21f),
                new Vector3(0.7f, 0.4f, 0.5f), new Color(0.55f, 0.20f, 0.18f));
            // ---- 院墙与大门 ----
            //
            // 【上一版只能翻墙进来】北面是一整道 132 米长、没有任何开口的实墙，
            // 而房子的入户门正朝北——玩家从街上过来，除了翻墙没有别的走法。
            // 现在北面正对入户步道的位置留出 10 米宽的大门：两根门柱、门楣、
            // 两扇推开的铁艺门扇、门柱灯，以及门前一段车行道。
            const float GateHalf = 5f;
            float lotHX = 75f, lotHZ = 37f;          // 与 Site() 里的宅基地一致
            float cx = 4f;                            // 宅基地中心相对 h 的偏移
            for (int s = -1; s <= 1; s += 2)
            {
                // 东西两道侧墙
                VillaKit.Box("FenceWall", h + new Vector3(cx + s * lotHX, 1.1f, 0),
                    new Vector3(0.6f, 2.2f, lotHZ * 2f), WallWarm);
                // 南面整道 + 北面留门的两段
                VillaKit.Box("FenceWall", h + new Vector3(cx, 1.1f, -lotHZ),
                    new Vector3(lotHX * 2f, 2.2f, 0.6f), WallWarm);
            }
            // 北面：门洞左右两段墙
            VillaKit.Box("FenceWall_N_L",
                h + new Vector3((cx - lotHX - GateHalf) / 2f, 1.1f, lotHZ),
                new Vector3(Mathf.Abs(cx - lotHX + GateHalf), 2.2f, 0.6f), WallWarm);
            VillaKit.Box("FenceWall_N_R",
                h + new Vector3((cx + lotHX + GateHalf) / 2f, 1.1f, lotHZ),
                new Vector3(Mathf.Abs(cx + lotHX - GateHalf), 2.2f, 0.6f), WallWarm);

            // 门柱 + 门楣 + 柱灯
            for (int s = -1; s <= 1; s += 2)
            {
                VillaKit.Box("GatePier", h + new Vector3(s * GateHalf, 1.7f, lotHZ),
                    new Vector3(1.4f, 3.4f, 1.4f), WallWarm);
                VillaKit.Deco("GatePierCap", h + new Vector3(s * GateHalf, 3.5f, lotHZ),
                    new Vector3(1.8f, 0.25f, 1.8f), Dark);
                VillaKit.Emit(VillaKit.Sph("GateLamp", h + new Vector3(s * GateHalf, 3.85f, lotHZ),
                    0.5f, new Color(1f, 0.94f, 0.76f)), new Color(1f, 0.92f, 0.72f), 2.4f);
                ZoneBuilder.AddCeilingLight(h + new Vector3(s * GateHalf, 3.9f, lotHZ),
                    new Color(1f, 0.92f, 0.74f), 16f);
            }
            VillaKit.Deco("GateLintel", h + new Vector3(0, 3.6f, lotHZ),
                new Vector3(GateHalf * 2f + 1.6f, 0.4f, 0.5f), WallWarm);
            VillaKit.WallSign(h + new Vector3(0, 4.3f, lotHZ + 0.4f), "我 的 住 处", Vector3.forward, 5f);

            // 两扇朝院里推开的铁艺门扇（竖栅栏，不是一块板）
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 8; i++)
                {
                    float t = i / 7f;
                    Vector3 at = h + new Vector3(s * (GateHalf - 0.2f - t * 3.4f), 0.95f,
                        lotHZ - 0.4f - t * 1.4f);
                    VillaKit.Metal(VillaKit.Cyl("GateBar", at - new Vector3(0, 0.95f, 0),
                        0.045f, 1.9f, Metal), Metal);
                }
            for (int s = -1; s <= 1; s += 2)
                VillaKit.Metal(VillaKit.CylAxis("GateRail",
                    h + new Vector3(s * (GateHalf - 1.9f), 1.85f, lotHZ - 1.1f),
                    0.05f, 3.9f, Metal, new Vector3(0, s * 22f, 90f)), Metal);

            // 门前车行道：从大门一直铺到主干道，走回家不用在草地上找方向
            VillaKit.Deco("GateDrive", h + new Vector3(0, 0.06f, lotHZ + 12f),
                new Vector3(10f, 0.06f, 24f), new Color(0.55f, 0.54f, 0.52f));
        }

        static void Tree(WorldContext ctx, Vector3 at)
        {
            VillaKit.Cyl("Trunk", at, 0.28f, 3.4f, new Color(0.36f, 0.26f, 0.18f), true);
            VillaKit.Sph("Canopy", at + new Vector3(0, 4.2f, 0), 4.8f, new Color(0.22f, 0.44f, 0.24f));
            VillaKit.Sph("Canopy", at + new Vector3(0.9f, 5.3f, -0.5f), 3.2f, new Color(0.27f, 0.51f, 0.29f));
            VillaKit.Sph("Canopy", at + new Vector3(-0.8f, 4.9f, 0.7f), 2.8f, new Color(0.19f, 0.39f, 0.22f));
        }


        /// <summary>
        /// 把房间填满的那一层小东西。
        ///
        /// 玩家说"整个房间靠着空荡荡的"。家具摆得再对，一间十几米见方的屋子
        /// 只有三五件大件仍然是空的——真实的房间里到处是小物件：
        /// 杯子、书、遥控器、拖鞋、抱枕、相框、纸箱、挂钩、开关面板。
        /// 这里按房间补这一层，全部是小尺寸构件，成本极低，观感差别极大。
        /// </summary>
        static void Clutter(WorldContext ctx, Vector3 h)
        {
            var paper = new Color(0.90f, 0.88f, 0.82f);
            var mug = new Color(0.86f, 0.90f, 0.92f);

            // 客厅：遥控器、杂志、抱枕、拖鞋、地灯开关
            VillaKit.Deco("Remote", h + new Vector3(-2.2f, 0.72f, -7.4f), new Vector3(0.12f, 0.05f, 0.4f), Dark);
            VillaKit.Deco("Magazine", h + new Vector3(-4f, 0.71f, -7.8f), new Vector3(0.5f, 0.04f, 0.36f),
                new Color(0.72f, 0.32f, 0.28f));
            VillaKit.Cyl("Mug", h + new Vector3(-1.2f, 0.7f, -8.2f), 0.08f, 0.14f, mug);
            for (int i = 0; i < 2; i++)
                VillaKit.Deco("Slipper", h + new Vector3(-6.6f + i * 0.4f, 0.06f, -12.5f),
                    new Vector3(0.26f, 0.1f, 0.7f), new Color(0.45f, 0.35f, 0.42f));

            // 厨房：碗碟、砧板、调料瓶、果篮、抹布
            for (int i = 0; i < 4; i++)
                VillaKit.Cyl("Bowl", h + new Vector3(21f + i * 0.5f, 0.98f, 14f), 0.16f, 0.09f, mug);
            VillaKit.Deco("Board", h + new Vector3(24.5f, 0.99f, 14.2f), new Vector3(0.7f, 0.05f, 0.45f), WoodLight);
            for (int i = 0; i < 5; i++)
                VillaKit.Cyl("Spice", h + new Vector3(30f + i * 0.34f, 0.98f, 14.2f), 0.055f, 0.2f,
                    i % 2 == 0 ? new Color(0.75f, 0.45f, 0.2f) : new Color(0.35f, 0.55f, 0.3f));
            VillaKit.Deco("Cloth", h + new Vector3(26f, 0.98f, 14f), new Vector3(0.4f, 0.03f, 0.3f),
                new Color(0.6f, 0.7f, 0.75f));

            // 餐厅：餐巾、酒瓶、烛台
            VillaKit.Cyl("Bottle", h + new Vector3(23.4f, 0.84f, -9f), 0.07f, 0.36f,
                new Color(0.20f, 0.35f, 0.22f));
            VillaKit.Sph("BottleNeck", h + new Vector3(23.4f, 1.22f, -9f), 0.1f, new Color(0.20f, 0.35f, 0.22f));
            for (int s2 = -1; s2 <= 1; s2 += 2)
            {
                VillaKit.Metal(VillaKit.Cyl("Candlestick", h + new Vector3(20.6f, 0.83f, -9f + s2 * 1.2f),
                    0.06f, 0.22f, Metal), Metal);
                VillaKit.Emit(VillaKit.Cyl("Candle", h + new Vector3(20.6f, 1.05f, -9f + s2 * 1.2f),
                    0.03f, 0.16f, new Color(1f, 0.9f, 0.6f)), new Color(1f, 0.8f, 0.4f), 1.4f);
            }

            // 书房/办公室：纸堆、笔筒、台灯、废纸篓
            VillaKit.Deco("Papers", h + new Vector3(3.4f, SlabY + 0.82f, -16.6f),
                new Vector3(0.5f, 0.06f, 0.66f), paper);
            VillaKit.Cyl("PenCup", h + new Vector3(4.2f, SlabY + 0.81f, -16.2f), 0.07f, 0.16f, Dark);
            for (int i = 0; i < 3; i++)
                VillaKit.CylAxis("Pen", h + new Vector3(4.2f, SlabY + 1.0f, -16.2f), 0.012f, 0.2f,
                    new Color(0.8f, 0.3f, 0.25f), new Vector3(12f * i, 30f * i, 8f));
            VillaKit.Cyl("Bin", h + new Vector3(-2.4f, SlabY, -16f), 0.2f, 0.5f,
                new Color(0.3f, 0.32f, 0.34f), true);
            VillaKit.Cyl("DeskLampBase", h + new Vector3(-1.6f, SlabY + 0.81f, -16.6f), 0.12f, 0.05f, Dark);
            VillaKit.Metal(VillaKit.CylAxis("DeskLampArm", h + new Vector3(-1.6f, SlabY + 1.05f, -16.4f),
                0.02f, 0.5f, Metal, new Vector3(30f, 0, 0)), Metal);
            VillaKit.Emit(VillaKit.Cyl("DeskLampHead", h + new Vector3(-1.6f, SlabY + 1.25f, -16.1f),
                0.12f, 0.14f, new Color(1f, 0.95f, 0.8f)), new Color(1f, 0.94f, 0.78f), 1.6f);

            // 主卧：床尾凳、拖鞋、书、闹钟
            VillaKit.Box("BedBench", h + new Vector3(-27f, SlabY + 0.25f, -19.2f),
                new Vector3(4.2f, 0.5f, 1.1f), new Color(0.40f, 0.34f, 0.38f));
            VillaKit.Deco("Book", h + new Vector3(-23.6f, SlabY + 0.68f, -12.1f),
                new Vector3(0.36f, 0.08f, 0.5f), new Color(0.35f, 0.45f, 0.62f));
            VillaKit.Deco("Clock", h + new Vector3(-30.4f, SlabY + 0.7f, -12.1f),
                new Vector3(0.24f, 0.2f, 0.1f), Dark);

            // 健身房：水壶、毛巾、药球
            VillaKit.Cyl("Bottle", h + new Vector3(-27f, 0.9f, 20f), 0.06f, 0.28f,
                new Color(0.3f, 0.6f, 0.8f));
            VillaKit.Sph("MedBall", h + new Vector3(-25.6f, 0.32f, 20.6f), 0.62f,
                new Color(0.55f, 0.20f, 0.18f));
            VillaKit.Sph("MedBall", h + new Vector3(-24.8f, 0.28f, 21.4f), 0.5f,
                new Color(0.25f, 0.35f, 0.45f));
            VillaKit.Deco("GymTowel", h + new Vector3(-27f, 0.62f, 19.2f),
                new Vector3(0.5f, 0.08f, 0.34f), new Color(0.9f, 0.9f, 0.86f));

            // 玄关：钥匙盘、伞桶、挂钩
            VillaKit.Cyl("KeyTray", h + new Vector3(-6.4f, 1.14f, 24f), 0.16f, 0.05f, WoodLight);
            VillaKit.Cyl("UmbrellaStand", h + new Vector3(6.4f, 0, 26f), 0.22f, 0.6f,
                new Color(0.35f, 0.38f, 0.42f), true);
            for (int i = 0; i < 2; i++)
                VillaKit.CylAxis("Umbrella", h + new Vector3(6.4f, 0.7f, 26f), 0.03f, 0.9f,
                    new Color(0.3f, 0.35f, 0.5f), new Vector3(8f * (i + 1), 60f * i, 5f));
            for (int i = -1; i <= 1; i++)
                VillaKit.Metal(VillaKit.CylAxis("Hook", h + new Vector3(i * 0.5f, 1.9f, 29.7f),
                    0.025f, 0.18f, Metal, new Vector3(90f, 0, 0)), Metal);
        }

        // ================= 宠物 =================

        static void Cat(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-12f, 0, -4f);
            VillaKit.Deco("CatBed", c + new Vector3(0, 0.1f, 0),
                new Vector3(1.5f, 0.2f, 1.5f), new Color(0.62f, 0.48f, 0.40f));
            var bowl = VillaKit.Box("CatBowl", c + new Vector3(1.8f, 0.09f, 0),
                new Vector3(0.5f, 0.18f, 0.5f), new Color(0.85f, 0.72f, 0.35f));
            var cat = PetCat.Create(ctx, c, h);
            cat.wanderRadius = 14f;
            bowl.AddComponent<CatFeeder>().cat = cat;
        }

        // ================= 通用小件 =================

        static void Sofa(WorldContext ctx, Vector3 at, float yawDeg)
        {
            var rot = Quaternion.Euler(0, yawDeg, 0);
            Vector3 seatSize = new Vector3(5.2f, 0.5f, 2.0f);
            var seat = VillaKit.Box("Sofa", at + rot * new Vector3(0, 0.25f, 0),
                Abs(rot * seatSize), Cloth);
            Sittable.Attach(seat, rot * Vector3.forward, "沙发", true, false, rot * Vector3.right);
            VillaKit.Box("SofaBack", at + rot * new Vector3(0, 0.78f, -0.85f),
                Abs(rot * new Vector3(5.2f, 1.0f, 0.5f)), Cloth);
            for (int s = -1; s <= 1; s += 2)
            {
                VillaKit.Box("SofaArm", at + rot * new Vector3(s * 2.5f, 0.6f, 0),
                    Abs(rot * new Vector3(0.45f, 0.7f, 2.0f)), Cloth);
                VillaKit.Deco("Cushion", at + rot * new Vector3(s * 1.4f, 0.66f, -0.3f),
                    Abs(rot * new Vector3(0.8f, 0.24f, 0.8f)), new Color(0.72f, 0.66f, 0.55f));
            }
        }

        /// <summary>旋转之后的尺寸取绝对值——盒子没有朝向，只有边长。</summary>
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static void DiningChair(WorldContext ctx, Vector3 at, float yawDeg)
        {
            var rot = Quaternion.Euler(0, yawDeg, 0);
            var seat = VillaKit.Box("DiningChair", at + new Vector3(0, 0.48f, 0),
                Abs(rot * new Vector3(1.0f, 0.12f, 1.0f)), Wood);
            Sittable.Attach(seat, rot * Vector3.forward, "餐椅");
            VillaKit.Deco("ChairBack", at + rot * new Vector3(0, 0.95f, -0.45f),
                new Vector3(1.0f, 1.0f, 0.12f), Wood, new Vector3(-8f, yawDeg, 0));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    VillaKit.Cyl("ChairLeg", at + new Vector3(sx * 0.4f, 0f, sz * 0.4f), 0.045f, 0.48f, Wood);
            VillaKit.Deco("ChairCushion", at + new Vector3(0, 0.56f, 0),
                Abs(rot * new Vector3(0.92f, 0.12f, 0.92f)), Cloth);
        }

        static void OfficeChair(WorldContext ctx, Vector3 at)
        {
            var seat = VillaKit.Box("ChairSeat", at + new Vector3(0, 0.52f, 0),
                new Vector3(1.2f, 0.18f, 1.2f), Dark);
            Sittable.Attach(seat, Vector3.forward, "办公椅");
            VillaKit.Box("ChairBack", at + new Vector3(0, 1.1f, -0.55f),
                new Vector3(1.2f, 1.2f, 0.16f), Dark);
            VillaKit.Metal(VillaKit.Cyl("ChairPole", at, 0.07f, 0.52f, Metal), Metal);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f * Mathf.Deg2Rad;
                VillaKit.Metal(VillaKit.CylAxis("ChairFoot",
                    at + new Vector3(Mathf.Cos(a) * 0.28f, 0.06f, Mathf.Sin(a) * 0.28f),
                    0.035f, 0.56f, Metal, new Vector3(90f, -i * 72f, 0)), Metal);
                VillaKit.Sph("ChairCaster",
                    at + new Vector3(Mathf.Cos(a) * 0.54f, 0.05f, Mathf.Sin(a) * 0.54f), 0.1f, Dark);
            }
        }

        static void BarStool(WorldContext ctx, Vector3 at)
        {
            var seat = VillaKit.Cyl("StoolSeat", at + new Vector3(0, 0.72f, 0), 0.36f, 0.14f, Wood, true);
            Sittable.Attach(seat, Vector3.forward, "吧椅");
            VillaKit.Metal(VillaKit.Cyl("StoolPole", at, 0.06f, 0.72f, Metal), Metal);
            VillaKit.Metal(VillaKit.Cyl("StoolFoot", at, 0.3f, 0.05f, Metal), Metal);
            VillaKit.Metal(VillaKit.CylAxis("StoolRing", at + new Vector3(0, 0.28f, 0),
                0.28f, 0.05f, Metal, Vector3.zero), Metal);
        }

        static void Plant(WorldContext ctx, Vector3 at)
        {
            VillaKit.Cyl("PlantPot", at, 0.36f, 0.55f, new Color(0.52f, 0.36f, 0.28f), true);
            VillaKit.Cyl("PlantPotLip", at + new Vector3(0, 0.5f, 0), 0.4f, 0.08f, new Color(0.46f, 0.31f, 0.24f));
            VillaKit.Sph("PlantLeaf", at + new Vector3(0, 1.15f, 0), 1.25f, new Color(0.22f, 0.46f, 0.26f));
            VillaKit.Sph("PlantLeaf", at + new Vector3(0.3f, 1.62f, -0.18f), 0.8f, new Color(0.28f, 0.54f, 0.31f));
            VillaKit.Sph("PlantLeaf", at + new Vector3(-0.28f, 1.5f, 0.22f), 0.66f, new Color(0.20f, 0.42f, 0.24f));
        }

        static void FloorLamp(WorldContext ctx, Vector3 at)
        {
            VillaKit.Metal(VillaKit.Cyl("LampBase", at, 0.26f, 0.06f, Dark), Dark);
            VillaKit.Metal(VillaKit.Cyl("LampPole", at, 0.035f, 1.85f, Metal), Metal);
            VillaKit.Emit(VillaKit.Cyl("LampShade", at + new Vector3(0, 1.72f, 0), 0.36f, 0.42f,
                new Color(0.98f, 0.92f, 0.78f)), new Color(1f, 0.94f, 0.78f), 1.3f);
            ZoneBuilder.AddCeilingLight(at + new Vector3(0, 1.9f, 0), new Color(1f, 0.93f, 0.78f), 10f);
        }

        static void Curtain(WorldContext ctx, Vector3 at, float width, bool alongZ)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 off = alongZ ? new Vector3(0, 1.9f, s * (width / 2f - 0.6f))
                                     : new Vector3(s * (width / 2f - 0.6f), 1.9f, 0);
                Vector3 size = alongZ ? new Vector3(0.22f, 3.0f, 1.2f) : new Vector3(1.2f, 3.0f, 0.22f);
                // 实体：窗帘是 3 米高的一块布，镜头退到它背后就只剩一片布，
                // 而没有碰撞体的话镜头根本不知道自己被挡了（同门扇）
                VillaKit.Box("Curtain", at + off, size, new Color(0.62f, 0.55f, 0.48f));
            }
            VillaKit.Metal(VillaKit.CylAxis("CurtainRod", at + new Vector3(0, 3.45f, 0), 0.04f,
                width + 0.6f, Metal, alongZ ? new Vector3(90f, 0, 0) : new Vector3(0, 0, 90f)), Metal);
        }

        /// <summary>
        /// 挂式空调：**贴在墙上**，出风口朝房间。
        ///
        /// 上一版是"在房间某个高度放一个白盒子"，于是它悬在半空中。
        /// 现在按墙面位置 + 朝向摆：机身紧贴墙、导风板朝屋里、指示灯亮着。
        /// </summary>
        static void AirConditioner(WorldContext ctx, Vector3 wallAt, Vector3 faceDir)
        {
            faceDir = faceDir.normalized;
            float yaw = Quaternion.LookRotation(faceDir).eulerAngles.y;
            Vector3 at = wallAt + faceDir * 0.3f;    // 机身厚度的一半，紧贴墙面
            VillaKit.Deco("AC", at, new Vector3(2.3f, 0.68f, 0.5f), new Color(0.96f, 0.96f, 0.97f),
                new Vector3(0, yaw, 0));
            VillaKit.Deco("AC_Vent", at + faceDir * 0.2f - new Vector3(0, 0.26f, 0),
                new Vector3(2.0f, 0.14f, 0.3f), new Color(0.55f, 0.78f, 0.92f), new Vector3(-18f, yaw, 0));
            VillaKit.Emit(VillaKit.Deco("AC_Led", at + faceDir * 0.26f + new Vector3(0.85f, 0.12f, 0),
                new Vector3(0.14f, 0.07f, 0.05f), new Color(0.3f, 0.95f, 0.5f), new Vector3(0, yaw, 0)),
                new Color(0.3f, 0.95f, 0.5f), 2f);
            VillaKit.Deco("AC_Bracket", wallAt + faceDir * 0.06f, new Vector3(2.4f, 0.1f, 0.12f),
                new Color(0.72f, 0.72f, 0.74f), new Vector3(0, yaw, 0));
        }

        static void CeilingFan(WorldContext ctx, Vector3 at)
        {
            VillaKit.Deco("FanRod", at + new Vector3(0, 0.3f, 0), new Vector3(0.09f, 0.6f, 0.09f), Metal);
            var hub = VillaKit.Deco("FanHub", at, new Vector3(0.45f, 0.2f, 0.45f), Metal);
            for (int i = 0; i < 4; i++)
            {
                var blade = VillaKit.Deco("FanBlade", at, new Vector3(2.8f, 0.07f, 0.45f), Wood);
                blade.transform.SetParent(hub.transform, true);
                blade.transform.localRotation = Quaternion.Euler(0, i * 90f, 0);
            }
            hub.AddComponent<SpinY>().speed = 110f;
        }

        static void UserPicture(WorldContext ctx, Vector3 at, Vector3 size, UserImageSlot slot, string sign)
        {
            var go = VillaKit.Deco("UserPicture_" + slot, at, size, new Color(0.62f, 0.60f, 0.66f));
            go.AddComponent<UserPictureFrame>().slot = slot;
            // 画框外圈：没有框的画看着像贴在墙上的一块色卡
            Vector3 frame = new Vector3(size.x > 0.2f ? size.x + 0.22f : size.x,
                size.y + 0.22f, size.z > 0.2f ? size.z + 0.22f : size.z);
            VillaKit.Deco("PictureFrame", at + new Vector3(0, 0, 0.02f), frame,
                new Color(0.45f, 0.34f, 0.22f));
            // 画框自己就说明了它是什么，不再往空中挂一行字
        }

        static Color BookColor(int i)
        {
            switch (Mathf.Abs(i) % 7)
            {
                case 0: return new Color(0.62f, 0.22f, 0.20f);
                case 1: return new Color(0.20f, 0.35f, 0.52f);
                case 2: return new Color(0.24f, 0.45f, 0.30f);
                case 3: return new Color(0.72f, 0.62f, 0.28f);
                case 4: return new Color(0.42f, 0.28f, 0.48f);
                case 5: return new Color(0.78f, 0.74f, 0.66f);
                default: return new Color(0.30f, 0.30f, 0.34f);
            }
        }

        static void Car(WorldContext ctx, Vector3 at, Color body)
        {
            VillaKit.Box("CarBody", at + new Vector3(0, 0.74f, 0), new Vector3(2.0f, 0.9f, 4.8f), body);
            VillaKit.Box("CarCabin", at + new Vector3(0, 1.42f, -0.2f), new Vector3(1.8f, 0.7f, 2.5f),
                new Color(body.r * 0.7f, body.g * 0.7f, body.b * 0.7f));
            for (int s = -1; s <= 1; s += 2)
            {
                VillaKit.Deco("SideGlass", at + new Vector3(s * 0.92f, 1.42f, -0.2f),
                    new Vector3(0.06f, 0.52f, 2.3f), Glass);
                VillaKit.Deco("Headlight", at + new Vector3(s * 0.66f, 0.82f, 2.42f),
                    new Vector3(0.5f, 0.26f, 0.08f), new Color(0.95f, 0.95f, 0.85f));
                VillaKit.Deco("Taillight", at + new Vector3(s * 0.66f, 0.88f, -2.42f),
                    new Vector3(0.46f, 0.2f, 0.08f), new Color(0.75f, 0.15f, 0.12f));
            }
            VillaKit.Deco("Windshield", at + new Vector3(0, 1.42f, 1.1f), new Vector3(1.7f, 0.62f, 0.1f), Glass);
            VillaKit.Deco("RearGlass", at + new Vector3(0, 1.42f, -1.5f), new Vector3(1.7f, 0.62f, 0.1f), Glass);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    VillaKit.CylAxis("Wheel", at + new Vector3(sx * 0.98f, 0.38f, sz * 1.6f),
                        0.38f, 0.26f, new Color(0.10f, 0.10f, 0.11f), new Vector3(0, 0, 90f), true);
                    VillaKit.Metal(VillaKit.CylAxis("Hub", at + new Vector3(sx * 1.06f, 0.38f, sz * 1.6f),
                        0.2f, 0.06f, new Color(0.72f, 0.74f, 0.78f), new Vector3(0, 0, 90f)),
                        new Color(0.72f, 0.74f, 0.78f));
                }
            for (int sx = -1; sx <= 1; sx += 2)
                VillaKit.Deco("Mirror", at + new Vector3(sx * 1.12f, 1.42f, 1.0f),
                    new Vector3(0.3f, 0.16f, 0.14f), body * 0.8f);
            VillaKit.Deco("Bumper", at + new Vector3(0, 0.5f, 2.45f), new Vector3(2.0f, 0.28f, 0.16f), Dark);
            VillaKit.Deco("Bumper", at + new Vector3(0, 0.5f, -2.45f), new Vector3(2.0f, 0.28f, 0.16f), Dark);
        }

        /// <summary>每个房间一盏主灯（灯具 + 光源）——房间没有灯就是黑的。</summary>
        static void Lights(WorldContext ctx, Vector3 h)
        {
            var spots = new (Vector3 at, Color color, float range)[]
            {
                (new Vector3(0, 0, -24f), new Color(1f, 0.93f, 0.78f), 22f),      // 玄关
                (new Vector3(-3f, 0, -6f), new Color(1f, 0.92f, 0.76f), 34f),     // 客厅
                (new Vector3(22f, 0, -9f), new Color(1f, 0.90f, 0.74f), 26f),     // 餐厅
                (new Vector3(28f, 0, 8f), new Color(1f, 0.95f, 0.84f), 28f),      // 厨房
                (new Vector3(-35f, 0, -12f), new Color(0.92f, 0.96f, 1f), 20f),   // 客卫
                (new Vector3(-31f, 0, 18f), new Color(0.94f, 0.97f, 1f), 32f),    // 健身房
                (new Vector3(-3f, 0, 18f), new Color(1f, 0.93f, 0.80f), 28f),     // 休息厅
                (new Vector3(22f, 0, 20f), new Color(1f, 0.95f, 0.86f), 22f),     // 楼梯厅
                (new Vector3(-27f, SlabY, -15f), new Color(1f, 0.88f, 0.70f), 30f), // 主卧
                (new Vector3(-33f, SlabY, 7f), new Color(0.92f, 0.96f, 1f), 22f),   // 主卫
                (new Vector3(2f, SlabY, -19f), new Color(0.94f, 0.96f, 1f), 28f),   // 办公室
                (new Vector3(29f, SlabY, -19f), new Color(1f, 0.94f, 0.82f), 28f),  // 书房
                (new Vector3(14f, SlabY, 0f), new Color(1f, 0.94f, 0.84f), 26f),    // 二层走廊
            };

            foreach (var s in spots)
            {
                float ceilY = (s.at.y > 1f ? SlabY : 0f) + FloorH - 0.35f;
                Vector3 at = h + new Vector3(s.at.x, ceilY, s.at.z);
                VillaKit.Deco("CeilLamp", at, new Vector3(1.6f, 0.16f, 1.6f), s.color);
                VillaKit.Deco("LampRing", at - new Vector3(0, 0.1f, 0),
                    new Vector3(1.9f, 0.08f, 1.9f), Metal);
                ZoneBuilder.AddCeilingLight(at - new Vector3(0, 0.2f, 0), s.color, s.range);
            }
        }

        /// <summary>
        /// 把整栋宅子标成寻路禁区：市民、敌人一律进不来。
        ///
        /// 玩家的要求是"住所不能有其他 NPC 出入，只能允许玩家出入"。
        /// 玩家是 CharacterController，不走导航面，所以照常进出；
        /// 而所有 NPC 与敌人都是 NavMeshAgent，导航面上没有路，它们就到不了这里。
        /// 这比"生成时避开"可靠得多——那只管出生的那一刻，管不住之后的游荡。
        /// </summary>
        static void BlockAgents(Vector3 h)
        {
            var go = new GameObject("Villa_NoAgents");
            go.transform.position = Lot.center;
            var vol = go.AddComponent<NavMeshModifierVolume>();
            vol.size = new Vector3(Lot.size.x, 24f, Lot.size.z);
            vol.center = Vector3.zero;
            vol.area = 1;   // Not Walkable（默认对所有 Agent 类型生效）
        }
    }

    /// <summary>绕 Y 轴匀速转（吊扇）。</summary>
    public class SpinY : MonoBehaviour
    {
        public float speed = 60f;
        void Update() => transform.Rotate(0f, speed * Time.deltaTime, 0f, Space.Self);
    }
}
