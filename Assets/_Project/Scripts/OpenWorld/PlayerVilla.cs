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
            Lights(ctx, h);
            BlockAgents(h);

            // 室内舒适镜头：进屋自动收短吊杆、停掉跟随绕行（防晕，见 IndoorZone）
            IndoorZone.ResetAll();
            IndoorZone.Create(h + new Vector3(0, SlabY * 0.5f + 1f, 0),
                new Vector3(W - 1f, SlabY + FloorH, D - 1f), "Villa_Indoor");
            IndoorZone.Create(h + new Vector3(-W / 2f - 14f, 2f, -14f),
                new Vector3(16f, 4f, 11f), "Garage_Indoor");

            InteriorSpawn = h + new Vector3(0, 1.1f, 24f);        // 玄关正中
            DoorSpawn = h + new Vector3(0, 1.1f, D / 2f + 6f);    // 门廊外
        }

        // ================= 场地 / 外壳 =================

        static void Site(WorldContext ctx, Vector3 h)
        {
            ZoneBuilder.Box(ctx, "Villa_Lot", h + new Vector3(4f, -0.12f, 0f),
                new Vector3(150f, 0.24f, 74f), new Color(0.36f, 0.44f, 0.30f));
            // 一层地板：起居区木地板，湿区与厨房瓷砖
            ZoneBuilder.Decoration(ctx, "Floor_G", h + new Vector3(0, 0.03f, 0),
                new Vector3(W - 0.6f, 0.06f, D - 0.6f), FloorWood);
            ZoneBuilder.Decoration(ctx, "Floor_Tile_K", h + new Vector3(28f, 0.05f, 8f),
                new Vector3(27f, 0.06f, 15f), FloorTile);
            ZoneBuilder.Decoration(ctx, "Floor_Tile_B", h + new Vector3(-35f, 0.05f, -12f),
                new Vector3(13f, 0.06f, 11f), FloorTile);
            // 入户步道：一路铺到主干道边，走回家不用在草地上找方向
            ZoneBuilder.Decoration(ctx, "Path", h + new Vector3(0, 0.06f, D / 2f + 20f),
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
                    ZoneBuilder.Box(ctx, "Wall_N", h + new Vector3(0, y + FloorH / 2f, hd),
                        new Vector3(W, FloorH, WallT), Wall);
                ZoneBuilder.Box(ctx, "Wall_S", h + new Vector3(0, y + FloorH / 2f, -hd),
                    new Vector3(W, FloorH, WallT), Wall);
                ZoneBuilder.Box(ctx, "Wall_W", h + new Vector3(-hw, y + FloorH / 2f, 0),
                    new Vector3(WallT, FloorH, D), Wall);
                ZoneBuilder.Box(ctx, "Wall_E", h + new Vector3(hw, y + FloorH / 2f, 0),
                    new Vector3(WallT, FloorH, D), Wall);

                if (lv == 0)
                {
                    // 一层北墙：中间留大门（大门朝街——宅子在主干道以南，人从北边走过来）
                    ZoneBuilder.Box(ctx, "Wall_N_L", h + new Vector3(-(hw + 4.2f) / 2f, y + FloorH / 2f, hd),
                        new Vector3(hw - 4.2f, FloorH, WallT), Wall);
                    ZoneBuilder.Box(ctx, "Wall_N_R", h + new Vector3((hw + 4.2f) / 2f, y + FloorH / 2f, hd),
                        new Vector3(hw - 4.2f, FloorH, WallT), Wall);
                    ZoneBuilder.Box(ctx, "Wall_N_Head", h + new Vector3(0, y + FloorH - 0.55f, hd),
                        new Vector3(8.4f, 1.1f, WallT), Wall);
                    DoorLeaf(ctx, h + new Vector3(-2.1f, y, hd), 4.2f, true, -0.35f);
                    DoorLeaf(ctx, h + new Vector3(2.1f, y, hd), 4.2f, true, 0.35f);
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
            ZoneBuilder.Decoration(ctx, "Ceil_2F", h + new Vector3(0, SlabY + FloorH - 0.12f, 0),
                new Vector3(W - 0.6f, 0.16f, D - 0.6f), Ceil);
            ZoneBuilder.Box(ctx, "Roof", h + new Vector3(0, SlabY + FloorH + 0.2f, 0),
                new Vector3(W + 2.2f, 0.4f, D + 2.2f), WallWarm);
            ZoneBuilder.Decoration(ctx, "Eave", h + new Vector3(0, SlabY + FloorH + 0.55f, 0),
                new Vector3(W + 3.6f, 0.25f, D + 3.6f), Dark);
            // 女儿墙：屋顶不是一块飘着的板
            for (int s = -1; s <= 1; s += 2)
            {
                ZoneBuilder.Decoration(ctx, "Parapet", h + new Vector3(0, SlabY + FloorH + 0.9f, s * (D / 2f + 1.4f)),
                    new Vector3(W + 3.4f, 1.1f, 0.35f), WallWarm);
                ZoneBuilder.Decoration(ctx, "Parapet", h + new Vector3(s * (W / 2f + 1.4f), SlabY + FloorH + 0.9f, 0),
                    new Vector3(0.35f, 1.1f, D + 3.4f), WallWarm);
            }

            // 门廊
            for (int s = -1; s <= 1; s += 2)
                ZoneBuilder.Box(ctx, "Porch_Col", h + new Vector3(s * 5f, 2f, hd + 5f),
                    new Vector3(0.7f, 4f, 0.7f), Wall);
            ZoneBuilder.Decoration(ctx, "Porch_Roof", h + new Vector3(0, 4.2f, hd + 3.5f),
                new Vector3(13f, 0.4f, 8f), WallWarm);
            ZoneBuilder.Box(ctx, "Porch_Step", h + new Vector3(0, 0.1f, hd + 1.6f),
                new Vector3(13f, 0.2f, 3f), FloorTile);
            OpenWorldBuilder.HomeSign(h + new Vector3(0, 5f, hd + 1f), "我 的 住 处");
        }

        /// <summary>二层楼板：楼梯口开洞，其余整块。</summary>
        static void Slab(WorldContext ctx, Vector3 h)
        {
            // 楼板整块铺满，只在楼梯正上方留一个 x∈[17,27]、z∈[14,23] 的井口。
            // 洞开小一点很重要：二层若有一大片空洞，玩家走着走着就掉回一层——
            // 那既不像房子，也是最容易被当成 Bug 的一种体验。
            ZoneBuilder.Box(ctx, "Slab_A", h + new Vector3(0, SlabY - 0.15f, -8f),
                new Vector3(W - 0.6f, 0.3f, 44f), Ceil);
            ZoneBuilder.Box(ctx, "Slab_B", h + new Vector3(0, SlabY - 0.15f, 26.5f),
                new Vector3(W - 0.6f, 0.3f, 7f), Ceil);
            ZoneBuilder.Box(ctx, "Slab_C", h + new Vector3(-12.35f, SlabY - 0.15f, 18.5f),
                new Vector3(58.7f, 0.3f, 9f), Ceil);
            ZoneBuilder.Box(ctx, "Slab_D", h + new Vector3(34.35f, SlabY - 0.15f, 18.5f),
                new Vector3(14.7f, 0.3f, 9f), Ceil);

            // 楼梯井三面护栏（楼梯从南往北上来，北侧那边留出上下口）
            ZoneBuilder.Box(ctx, "StairRail", h + new Vector3(22f, SlabY + 0.6f, 13.9f),
                new Vector3(10.4f, 1.2f, 0.16f), Metal);
            for (int s = -1; s <= 1; s += 2)
                ZoneBuilder.Box(ctx, "StairRail", h + new Vector3(22f + s * 5.2f, SlabY + 0.6f, 18.5f),
                    new Vector3(0.16f, 1.2f, 9f), Metal);
        }

        static void Window(WorldContext ctx, Vector3 at, float width, bool alongZ)
        {
            Vector3 glass = alongZ ? new Vector3(0.1f, 2.4f, width) : new Vector3(width, 2.4f, 0.1f);
            Vector3 frame = alongZ ? new Vector3(0.16f, 2.7f, width + 0.5f) : new Vector3(width + 0.5f, 2.7f, 0.16f);
            ZoneBuilder.Decoration(ctx, "WindowFrame", at, frame, WoodLight);
            ZoneBuilder.Decoration(ctx, "Window", at, glass, Glass);
            // 中挺：一整块玻璃看着像洞，加一竖一横就像窗
            Vector3 mull = alongZ ? new Vector3(0.14f, 2.4f, 0.12f) : new Vector3(0.12f, 2.4f, 0.14f);
            ZoneBuilder.Decoration(ctx, "Mullion", at, mull, WoodLight);
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
                    ZoneBuilder.Box(ctx, "Wall", h + new Vector3((cursor + a) / 2f, y + FloorH / 2f, z),
                        new Vector3(a - cursor, FloorH, WallT), Wall);
                // 门楣 + 门套 + 门扇
                ZoneBuilder.Box(ctx, "Wall_Head", h + new Vector3(d, y + (FloorH + DoorH) / 2f, z),
                    new Vector3(DoorW, FloorH - DoorH, WallT), Wall);
                DoorFrame(ctx, h + new Vector3(d, y, z), false);
                DoorLeaf(ctx, h + new Vector3(d, y, z), DoorW, false, 0.42f);
                cursor = b;
            }
            if (x1 > cursor)
                ZoneBuilder.Box(ctx, "Wall", h + new Vector3((cursor + x1) / 2f, y + FloorH / 2f, z),
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
                    ZoneBuilder.Box(ctx, "Wall", h + new Vector3(x, y + FloorH / 2f, (cursor + a) / 2f),
                        new Vector3(WallT, FloorH, a - cursor), Wall);
                ZoneBuilder.Box(ctx, "Wall_Head", h + new Vector3(x, y + (FloorH + DoorH) / 2f, d),
                    new Vector3(WallT, FloorH - DoorH, DoorW), Wall);
                DoorFrame(ctx, h + new Vector3(x, y, d), true);
                DoorLeaf(ctx, h + new Vector3(x, y, d), DoorW, true, 0.42f);
                cursor = b;
            }
            if (z1 > cursor)
                ZoneBuilder.Box(ctx, "Wall", h + new Vector3(x, y + FloorH / 2f, (cursor + z1) / 2f),
                    new Vector3(WallT, FloorH, z1 - cursor), Wall);
            Skirting(ctx, h, y, x, z0, z1, true);
        }

        /// <summary>门套：门洞四边的一圈木线，"有没有它"就是毛坯与装修的差别。</summary>
        static void DoorFrame(WorldContext ctx, Vector3 at, bool alongZ)
        {
            Vector3 side = alongZ ? new Vector3(WallT + 0.14f, DoorH, 0.16f) : new Vector3(0.16f, DoorH, WallT + 0.14f);
            Vector3 top = alongZ ? new Vector3(WallT + 0.14f, 0.16f, DoorW + 0.3f) : new Vector3(DoorW + 0.3f, 0.16f, WallT + 0.14f);
            Vector3 offA = alongZ ? new Vector3(0, DoorH / 2f, -DoorW / 2f) : new Vector3(-DoorW / 2f, DoorH / 2f, 0);
            Vector3 offB = alongZ ? new Vector3(0, DoorH / 2f, DoorW / 2f) : new Vector3(DoorW / 2f, DoorH / 2f, 0);
            ZoneBuilder.Decoration(ctx, "DoorCase", at + offA, side, WoodLight);
            ZoneBuilder.Decoration(ctx, "DoorCase", at + offB, side, WoodLight);
            ZoneBuilder.Decoration(ctx, "DoorCase", at + new Vector3(0, DoorH, 0), top, WoodLight);
        }

        /// <summary>
        /// 门扇：半开着靠在门洞一侧。
        ///
        /// 不做成可开关的活动门是有意的——它会挡住玩家、需要碰撞回退、还要处理
        /// "关在门后"的死锁。半开的门在观感上已经完成了"这是一扇门"的表达，
        /// 而且永远不会把人卡住。
        /// </summary>
        static void DoorLeaf(WorldContext ctx, Vector3 at, float width, bool alongZ, float openSide)
        {
            // 门扇靠在门洞的一侧、并朝屋里转开约 70°——所以它的厚度方向与门洞垂直
            float side = Mathf.Sign(openSide) * (width / 2f - 0.5f);
            Vector3 pos = alongZ
                ? at + new Vector3(0.5f * Mathf.Sign(openSide), DoorH / 2f, side)
                : at + new Vector3(side, DoorH / 2f, 0.5f * Mathf.Sign(openSide));
            Vector3 size = alongZ
                ? new Vector3(1.0f, DoorH - 0.08f, 0.1f)
                : new Vector3(0.1f, DoorH - 0.08f, 1.0f);
            var leaf = ZoneBuilder.Decoration(ctx, "DoorLeaf", pos, size, WoodLight);
            // 把手
            Vector3 knob = alongZ ? new Vector3(0, 0, -0.42f * Mathf.Sign(openSide))
                                  : new Vector3(-0.42f * Mathf.Sign(openSide), 0, 0);
            ZoneBuilder.Decoration(ctx, "DoorKnob", pos + knob - new Vector3(0, 0.15f, 0),
                new Vector3(0.14f, 0.14f, 0.14f), Metal);
        }

        /// <summary>踢脚线：贴着墙脚的一条深色木线，房间立刻"装修过"。</summary>
        static void Skirting(WorldContext ctx, Vector3 h, float y, float fixedAxis, float a, float b, bool alongZ)
        {
            float len = b - a, mid = (a + b) / 2f;
            if (len <= 0.2f) return;
            Vector3 pos = alongZ ? new Vector3(fixedAxis, y + 0.09f, mid) : new Vector3(mid, y + 0.09f, fixedAxis);
            Vector3 size = alongZ ? new Vector3(WallT + 0.1f, 0.18f, len) : new Vector3(len, 0.18f, WallT + 0.1f);
            ZoneBuilder.Decoration(ctx, "Skirting", h + pos, size, Skirt);
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
            WallX(ctx, h, 0, 16f, 30f, 42f, 36f);         // 洗衣房
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

        /// <summary>一部真的能走上去的楼梯（每级 26cm，CharacterController 迈得上）。</summary>
        static void Stairs(WorldContext ctx, Vector3 h)
        {
            Vector3 baseAt = h + new Vector3(22f, 0, 16f);
            int steps = Mathf.CeilToInt(SlabY / 0.26f);
            float rise = SlabY / steps, run = 0.42f;
            for (int i = 0; i < steps; i++)
            {
                float y = rise * (i + 1);
                ZoneBuilder.Box(ctx, "Step", baseAt + new Vector3(0, y / 2f, i * run),
                    new Vector3(5f, y, run + 0.02f), FloorTile);
                if (i % 3 == 0)
                    ZoneBuilder.Decoration(ctx, "StepNose", baseAt + new Vector3(0, y + 0.02f, i * run - run / 2f),
                        new Vector3(5f, 0.04f, 0.1f), Wood);
            }
            // 扶手
            for (int s = -1; s <= 1; s += 2)
            {
                ZoneBuilder.Decoration(ctx, "Handrail",
                    baseAt + new Vector3(s * 2.5f, SlabY / 2f + 0.95f, steps * run / 2f),
                    new Vector3(0.12f, 0.12f, steps * run), Wood);
                for (int i = 0; i < steps; i += 4)
                    ZoneBuilder.Decoration(ctx, "Baluster",
                        baseAt + new Vector3(s * 2.5f, rise * (i + 1) + 0.5f, i * run),
                        new Vector3(0.08f, 1f, 0.08f), Metal);
            }
            OpenWorldBuilder.HomeSign(h + new Vector3(22f, 2.6f, 14.2f), "上 二 层");
        }

        // ================= 一层 =================

        static void Foyer(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(0, 0, 24f);
            ZoneBuilder.Decoration(ctx, "EntryRug", c + new Vector3(0, 0.08f, 2f),
                new Vector3(5f, 0.05f, 3f), new Color(0.40f, 0.26f, 0.24f));
            ZoneBuilder.Box(ctx, "ShoeCabinet", c + new Vector3(-6.4f, 0.55f, 0),
                new Vector3(0.7f, 1.1f, 5f), Wood);
            ZoneBuilder.Decoration(ctx, "EntryMirror", c + new Vector3(6.5f, 1.7f, 0),
                new Vector3(0.1f, 1.8f, 2.6f), new Color(0.80f, 0.88f, 0.94f));
            Plant(ctx, c + new Vector3(5.4f, 0, 3.4f));
            for (int s = -1; s <= 1; s += 2)
                ZoneBuilder.Decoration(ctx, "Sconce", c + new Vector3(s * 7.6f, 2.4f, -2f),
                    new Vector3(0.3f, 0.5f, 0.3f), new Color(0.98f, 0.92f, 0.76f));
        }

        static void LivingRoom(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-3f, 0, -6f);

            ZoneBuilder.Decoration(ctx, "Rug", c + new Vector3(0, 0.08f, -2f),
                new Vector3(14f, 0.05f, 9f), new Color(0.36f, 0.27f, 0.25f));
            Sofa(ctx, c + new Vector3(0, 0, -6.4f), 0f);
            Sofa(ctx, c + new Vector3(-7.4f, 0, -1.5f), 90f);
            Sofa(ctx, c + new Vector3(7.4f, 0, -1.5f), -90f);
            ZoneBuilder.Box(ctx, "CoffeeTable", c + new Vector3(0, 0.34f, -1.6f),
                new Vector3(4.4f, 0.68f, 2.2f), Wood);
            ZoneBuilder.Decoration(ctx, "TableBooks", c + new Vector3(0.8f, 0.74f, -1.6f),
                new Vector3(0.8f, 0.12f, 0.6f), new Color(0.70f, 0.30f, 0.25f));
            ZoneBuilder.Decoration(ctx, "Vase", c + new Vector3(-1.2f, 0.86f, -1.6f),
                new Vector3(0.35f, 0.36f, 0.35f), new Color(0.55f, 0.70f, 0.72f));

            // —— 一整面墙的目标看板 ——
            // 看板挂在客厅南墙、面朝北——大门在北侧，一进屋抬头就是它
            var frame = ZoneBuilder.Box(ctx, "GoalBoard_Frame", c + new Vector3(0, 2.3f, -11.6f),
                new Vector3(17f, 4.6f, 0.34f), Dark);
            ZoneBuilder.Decoration(ctx, "GoalBoard_Screen", c + new Vector3(0, 2.3f, -11.4f),
                new Vector3(16.2f, 4.0f, 0.1f), new Color(0.07f, 0.11f, 0.17f));
            var gb = frame.AddComponent<Combat.GoalBoard>();
            gb.interactRange = 9f;
            HomeFixture.Attach(frame, HomeFixtureKind.GoalBoard);
            GoalBoardDisplay.Attach(frame, c + new Vector3(0, 2.3f, -11.35f), 16.2f, 4.0f, true);
            for (int i = -7; i <= 7; i++)
                ZoneBuilder.Decoration(ctx, "BoardLed", c + new Vector3(i * 1.1f, 0.28f, -11.3f),
                    new Vector3(0.5f, 0.06f, 0.2f), new Color(0.35f, 0.75f, 1f));

            ZoneBuilder.Box(ctx, "Sideboard", c + new Vector3(-9.4f, 0.42f, 4.4f),
                new Vector3(3.4f, 0.84f, 1f), Wood);
            Plant(ctx, c + new Vector3(9.4f, 0, 4.4f));
            FloorLamp(ctx, c + new Vector3(-9.6f, 0, -6.4f));
            CeilingFan(ctx, c + new Vector3(0, FloorH - 0.55f, -2f));
            AirConditioner(ctx, c + new Vector3(-9f, 2.9f, 5.6f));
            Curtain(ctx, h + new Vector3(-24f, 0, -D / 2f + 0.4f), 8f, false);
        }

        static void Dining(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(22f, 0, -9f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.2f, 6.8f), "餐 厅");

            ZoneBuilder.Box(ctx, "DiningTable", c + new Vector3(0, 0.76f, 0),
                new Vector3(3.2f, 0.14f, 6.2f), Wood);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    ZoneBuilder.Box(ctx, "TableLeg", c + new Vector3(sx * 1.3f, 0.38f, sz * 2.6f),
                        new Vector3(0.2f, 0.76f, 0.2f), Wood);

            for (int i = -1; i <= 1; i++)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 seat = c + new Vector3(s * 2.3f, 0, i * 2.1f);
                    DiningChair(ctx, seat, s > 0 ? -90f : 90f);
                    Vector3 plate = c + new Vector3(s * 1.05f, 0.84f, i * 2.1f);
                    ZoneBuilder.Decoration(ctx, "Plate", plate, new Vector3(0.62f, 0.04f, 0.62f),
                        new Color(0.96f, 0.96f, 0.97f));
                    ZoneBuilder.Decoration(ctx, "Glass", plate + new Vector3(-s * 0.5f, 0.11f, 0.34f),
                        new Vector3(0.16f, 0.26f, 0.16f), new Color(0.82f, 0.9f, 0.95f));
                    ZoneBuilder.Decoration(ctx, "Cutlery", plate + new Vector3(s * 0.44f, 0, 0),
                        new Vector3(0.06f, 0.02f, 0.36f), Metal);
                }

            ZoneBuilder.Decoration(ctx, "FruitBowl", c + new Vector3(0, 0.88f, 0),
                new Vector3(0.8f, 0.18f, 0.8f), new Color(0.74f, 0.70f, 0.62f));
            ZoneBuilder.Decoration(ctx, "Fruit", c + new Vector3(0, 0.99f, 0),
                new Vector3(0.56f, 0.18f, 0.56f), new Color(0.85f, 0.42f, 0.25f));
            for (int i = -1; i <= 1; i++)
            {
                ZoneBuilder.Decoration(ctx, "PendantRod", c + new Vector3(0, FloorH - 0.5f, i * 2.1f),
                    new Vector3(0.06f, 1f, 0.06f), Dark);
                ZoneBuilder.Decoration(ctx, "Pendant", c + new Vector3(0, FloorH - 1.05f, i * 2.1f),
                    new Vector3(0.9f, 0.3f, 0.9f), new Color(0.98f, 0.90f, 0.72f));
            }
        }

        static void Kitchen(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(28f, 0, 8f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.2f, 7.2f), "厨 房");

            // U 形橱柜
            Counter(ctx, c + new Vector3(0, 0, 6.4f), new Vector3(24f, 0.92f, 0.9f));
            Counter(ctx, c + new Vector3(-11.5f, 0, 1.5f), new Vector3(0.9f, 0.92f, 10f));
            Counter(ctx, c + new Vector3(11.5f, 0, 1.5f), new Vector3(0.9f, 0.92f, 10f));
            // 吊柜
            ZoneBuilder.Box(ctx, "UpperCabinet", c + new Vector3(-4f, 2.2f, 6.5f),
                new Vector3(12f, 1.2f, 0.7f), WoodLight);

            ZoneBuilder.Decoration(ctx, "Stove", c + new Vector3(4f, 0.97f, 6.4f),
                new Vector3(2.2f, 0.06f, 0.8f), Dark);
            for (int i = 0; i < 4; i++)
                ZoneBuilder.Decoration(ctx, "Burner",
                    c + new Vector3(3.4f + (i % 2) * 1.2f, 1.01f, 6.15f + (i / 2) * 0.5f),
                    new Vector3(0.34f, 0.03f, 0.34f), new Color(0.35f, 0.14f, 0.12f));
            ZoneBuilder.Box(ctx, "RangeHood", c + new Vector3(4f, 2.5f, 6.5f),
                new Vector3(2.4f, 0.6f, 0.9f), Metal);
            ZoneBuilder.Decoration(ctx, "Sink", c + new Vector3(-3f, 0.95f, 6.4f),
                new Vector3(1.4f, 0.12f, 0.8f), new Color(0.80f, 0.82f, 0.84f));
            ZoneBuilder.Decoration(ctx, "SinkTap", c + new Vector3(-3f, 1.22f, 6.7f),
                new Vector3(0.08f, 0.44f, 0.08f), Metal);

            var fridge = ZoneBuilder.Box(ctx, "Fridge", c + new Vector3(10.6f, 1.2f, 5.6f),
                new Vector3(1.8f, 2.4f, 1.6f), new Color(0.86f, 0.88f, 0.90f));
            HomeFixture.Attach(fridge, HomeFixtureKind.Fridge);
            ZoneBuilder.Decoration(ctx, "FridgeSeam", c + new Vector3(10.6f, 1.2f, 4.78f),
                new Vector3(1.7f, 0.05f, 0.05f), Dark);

            // 中岛 + 吧椅（可以坐）
            ZoneBuilder.Box(ctx, "Island", c + new Vector3(0, 0.46f, 0f), new Vector3(6f, 0.92f, 2.2f), Wood);
            ZoneBuilder.Decoration(ctx, "IslandTop", c + new Vector3(0, 0.95f, 0f), new Vector3(6.3f, 0.08f, 2.5f), Dark);
            for (int i = -1; i <= 1; i++)
                BarStool(ctx, c + new Vector3(i * 1.9f, 0, -2.2f));

            ZoneBuilder.Decoration(ctx, "Microwave", c + new Vector3(-11.4f, 1.25f, 4f),
                new Vector3(0.9f, 0.55f, 1.1f), Dark);
            ZoneBuilder.Decoration(ctx, "Kettle", c + new Vector3(-11.4f, 1.1f, 0f),
                new Vector3(0.36f, 0.36f, 0.36f), Metal);
        }

        static void Counter(WorldContext ctx, Vector3 at, Vector3 size)
        {
            ZoneBuilder.Box(ctx, "Counter", at + new Vector3(0, size.y / 2f, 0), size,
                new Color(0.58f, 0.56f, 0.54f));
            ZoneBuilder.Decoration(ctx, "CounterTop", at + new Vector3(0, size.y + 0.04f, 0),
                new Vector3(size.x + 0.2f, 0.08f, size.z + 0.2f), Dark);
        }

        static void GuestBath(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-35f, 0, -12f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.2f, 5.4f), "卫 生 间");
            Bathroom(ctx, c, false);
        }

        static void Gym(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-31f, 0, 18f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.2f, 11f), "健 身 房");
            ZoneBuilder.Decoration(ctx, "GymMat", c + new Vector3(0, 0.08f, 0),
                new Vector3(20f, 0.06f, 22f), new Color(0.22f, 0.24f, 0.26f));

            // 跑步机（能真的跑起来，见 Treadmill）
            Treadmill.Build(ctx, c + new Vector3(-5f, 0, 6f));

            // 哑铃架
            ZoneBuilder.Box(ctx, "RackBase", c + new Vector3(6f, 0.4f, 9f), new Vector3(4.4f, 0.8f, 0.9f), Dark);
            for (int i = 0; i < 6; i++)
            {
                float x = c.x + 4.2f + i * 0.72f;
                ZoneBuilder.Decoration(ctx, "DumbbellBar", new Vector3(x, 0.88f, c.z + 9f),
                    new Vector3(0.12f, 0.12f, 0.5f), Metal);
                for (int s = -1; s <= 1; s += 2)
                    ZoneBuilder.Decoration(ctx, "DumbbellPlate", new Vector3(x, 0.88f, c.z + 9f + s * 0.22f),
                        new Vector3(0.36f, 0.36f, 0.12f), Dark);
            }

            // 卧推凳（可以坐）+ 杠铃架
            var bench = ZoneBuilder.Box(ctx, "Bench", c + new Vector3(4f, 0.5f, 2f),
                new Vector3(0.8f, 0.18f, 2.6f), new Color(0.30f, 0.16f, 0.16f));
            Sittable.Attach(bench, new Vector3(0, 0.62f, 0), Vector3.forward, false, "卧推凳");
            ZoneBuilder.Box(ctx, "BenchLeg", c + new Vector3(4f, 0.22f, 2f), new Vector3(0.6f, 0.44f, 2.2f), Dark);
            for (int s = -1; s <= 1; s += 2)
                ZoneBuilder.Box(ctx, "RackPost", c + new Vector3(4f + s * 0.9f, 0.75f, 3.3f),
                    new Vector3(0.14f, 1.5f, 0.14f), Metal);
            ZoneBuilder.Decoration(ctx, "Barbell", c + new Vector3(4f, 1.52f, 3.3f),
                new Vector3(2.8f, 0.1f, 0.1f), Metal);
            for (int s = -1; s <= 1; s += 2)
                ZoneBuilder.Decoration(ctx, "BarPlate", c + new Vector3(4f + s * 1.3f, 1.52f, 3.3f),
                    new Vector3(0.13f, 0.62f, 0.62f), Dark);

            // 单车 + 瑜伽垫 + 整面镜子
            ZoneBuilder.Box(ctx, "BikeBody", c + new Vector3(-5f, 0.5f, -2f), new Vector3(0.6f, 1f, 1.8f), Dark);
            ZoneBuilder.Decoration(ctx, "BikeSeat", c + new Vector3(-5f, 1.06f, -2.4f), new Vector3(0.42f, 0.14f, 0.7f), Metal);
            ZoneBuilder.Decoration(ctx, "BikeBar", c + new Vector3(-5f, 1.24f, -1.2f), new Vector3(0.86f, 0.1f, 0.1f), Metal);
            ZoneBuilder.Decoration(ctx, "BikeWheel", c + new Vector3(-5f, 0.36f, -1f), new Vector3(0.14f, 0.72f, 0.72f), Metal);
            ZoneBuilder.Decoration(ctx, "YogaMat", c + new Vector3(5f, 0.1f, -4f), new Vector3(1.3f, 0.05f, 2.8f),
                new Color(0.35f, 0.62f, 0.55f));
            ZoneBuilder.Decoration(ctx, "GymMirror", c + new Vector3(0, 1.8f, 11.3f),
                new Vector3(18f, 2.8f, 0.1f), new Color(0.78f, 0.86f, 0.92f));
            AirConditioner(ctx, c + new Vector3(-8f, 2.9f, -11.2f));
        }

        /// <summary>休息厅：沙发、书架、通往楼梯的过厅。</summary>
        static void Lounge(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-3f, 0, 18f);
            ZoneBuilder.Decoration(ctx, "Rug", c + new Vector3(0, 0.08f, 0),
                new Vector3(11f, 0.05f, 8f), new Color(0.30f, 0.32f, 0.36f));
            Sofa(ctx, c + new Vector3(0, 0, -3.8f), 0f);
            ZoneBuilder.Box(ctx, "LowShelf", c + new Vector3(-8f, 0.6f, 3f), new Vector3(1f, 1.2f, 7f), Wood);
            for (int i = 0; i < 10; i++)
                ZoneBuilder.Decoration(ctx, "Book", c + new Vector3(-8f, 1.36f, 0f + i * 0.62f),
                    new Vector3(0.7f, 0.34f, 0.22f), BookColor(i * 3));
            Plant(ctx, c + new Vector3(7.5f, 0, 4f));
            FloorLamp(ctx, c + new Vector3(-7f, 0, -5f));
            var chair = ZoneBuilder.Box(ctx, "ArmChair", c + new Vector3(6f, 0.45f, -2f),
                new Vector3(1.8f, 0.9f, 1.8f), new Color(0.42f, 0.30f, 0.32f));
            Sittable.Attach(chair, new Vector3(0, 0.55f, 0), -Vector3.forward, false, "扶手椅");
        }

        // ================= 二层 =================

        static void MasterBedroom(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-27f, SlabY, -15f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.2f, 13.6f), "主 卧");
            ZoneBuilder.Decoration(ctx, "BedRug", c + new Vector3(0, 0.08f, -2f),
                new Vector3(12f, 0.05f, 10f), new Color(0.34f, 0.28f, 0.30f));

            // 床：床架→床垫→床单→被子→枕头，可以躺上去休息
            ZoneBuilder.Box(ctx, "BedFrame", c + new Vector3(0, 0.24f, 0), new Vector3(5.2f, 0.48f, 6.8f), Wood);
            var mattress = ZoneBuilder.Box(ctx, "Mattress", c + new Vector3(0, 0.66f, 0),
                new Vector3(4.9f, 0.44f, 6.5f), new Color(0.93f, 0.92f, 0.90f));
            Sittable.Attach(mattress, new Vector3(0, 0.55f, 0), Vector3.forward, true, "床");
            ZoneBuilder.Decoration(ctx, "BedSheet", c + new Vector3(0, 0.9f, 0),
                new Vector3(5.1f, 0.05f, 6.7f), new Color(0.96f, 0.96f, 0.98f));
            ZoneBuilder.Decoration(ctx, "Quilt", c + new Vector3(0, 1.0f, -1.2f),
                new Vector3(5f, 0.24f, 4.2f), new Color(0.29f, 0.39f, 0.57f));
            ZoneBuilder.Decoration(ctx, "QuiltFold", c + new Vector3(0, 1.08f, 0.95f),
                new Vector3(5f, 0.16f, 0.55f), new Color(0.38f, 0.48f, 0.66f));
            for (int s = -1; s <= 1; s += 2)
                ZoneBuilder.Decoration(ctx, "Pillow", c + new Vector3(s * 1.15f, 1.1f, 2.6f),
                    new Vector3(1.9f, 0.3f, 1.0f), new Color(0.97f, 0.97f, 0.98f));
            ZoneBuilder.Box(ctx, "Headboard", c + new Vector3(0, 1.2f, 3.6f), new Vector3(5.4f, 1.8f, 0.28f), WallWarm);

            for (int s = -1; s <= 1; s += 2)
            {
                ZoneBuilder.Box(ctx, "Nightstand", c + new Vector3(s * 3.4f, 0.32f, 2.9f),
                    new Vector3(1.2f, 0.64f, 1.2f), Wood);
                ZoneBuilder.Decoration(ctx, "LampShade", c + new Vector3(s * 3.4f, 0.92f, 2.9f),
                    new Vector3(0.6f, 0.52f, 0.6f), new Color(0.98f, 0.92f, 0.75f));
            }

            var wardrobe = ZoneBuilder.Box(ctx, "Wardrobe", c + new Vector3(-8.5f, 1.25f, -1f),
                new Vector3(0.9f, 2.5f, 6f), Wood);
            HomeFixture.Attach(wardrobe, HomeFixtureKind.Wardrobe);
            for (int i = -1; i <= 1; i += 2)
                ZoneBuilder.Decoration(ctx, "WardrobeHandle", c + new Vector3(-8.0f, 1.25f, -1f + i * 0.8f),
                    new Vector3(0.06f, 0.5f, 0.06f), Metal);

            // 墙上的艺术画（可换成玩家自己的图片）
            UserPicture(ctx, c + new Vector3(0, 2.5f, 3.85f), new Vector3(4.2f, 2.6f, 0.08f),
                UserImageSlot.BedroomArtA, "艺 术 画");
            UserPicture(ctx, c + new Vector3(-8.9f, 2.4f, 5f), new Vector3(0.08f, 2.0f, 3.0f),
                UserImageSlot.BedroomArtB, "");

            CeilingFan(ctx, c + new Vector3(0, FloorH - 0.55f, -3f));
            AirConditioner(ctx, c + new Vector3(5f, 2.9f, 3.8f));
            Curtain(ctx, h + new Vector3(-24f, SlabY, -D / 2f + 0.4f), 8f, false);
        }

        static void MasterBath(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-33f, SlabY, 7f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.2f, 6.6f), "主 卫");
            Bathroom(ctx, c, true);
        }

        /// <summary>一套完整的卫生间：台盆 + 镜子 + 马桶 + 淋浴 +（主卫另加）浴缸。</summary>
        static void Bathroom(WorldContext ctx, Vector3 c, bool withTub)
        {
            ZoneBuilder.Decoration(ctx, "BathTile", c + new Vector3(0, 0.07f, 0),
                new Vector3(11f, 0.05f, 11f), FloorTile);

            ZoneBuilder.Box(ctx, "Vanity", c + new Vector3(0, 0.44f, 4.2f),
                new Vector3(4.2f, 0.88f, 1.0f), new Color(0.55f, 0.52f, 0.50f));
            ZoneBuilder.Decoration(ctx, "Basin", c + new Vector3(0, 0.92f, 4.2f),
                new Vector3(1.3f, 0.16f, 0.8f), new Color(0.95f, 0.95f, 0.96f));
            ZoneBuilder.Decoration(ctx, "Tap", c + new Vector3(0, 1.1f, 4.5f),
                new Vector3(0.1f, 0.34f, 0.1f), Metal);
            var mirror = ZoneBuilder.Box(ctx, "Mirror", c + new Vector3(0, 2.0f, 4.72f),
                new Vector3(3.4f, 1.6f, 0.08f), new Color(0.80f, 0.88f, 0.94f));
            HomeFixture.Attach(mirror, HomeFixtureKind.Mirror);
            ZoneBuilder.Decoration(ctx, "MirrorLight", c + new Vector3(0, 2.9f, 4.6f),
                new Vector3(3f, 0.1f, 0.22f), new Color(1f, 0.97f, 0.9f));

            ZoneBuilder.Box(ctx, "ToiletBase", c + new Vector3(-4.2f, 0.2f, 0.5f),
                new Vector3(0.85f, 0.4f, 1.3f), new Color(0.95f, 0.95f, 0.96f));
            ZoneBuilder.Box(ctx, "ToiletSeat", c + new Vector3(-4.2f, 0.45f, 0.35f),
                new Vector3(0.9f, 0.12f, 0.95f), new Color(0.97f, 0.97f, 0.98f));
            ZoneBuilder.Box(ctx, "ToiletTank", c + new Vector3(-4.2f, 0.7f, 1.15f),
                new Vector3(0.85f, 0.95f, 0.36f), new Color(0.95f, 0.95f, 0.96f));

            ZoneBuilder.Decoration(ctx, "ShowerTray", c + new Vector3(3.2f, 0.09f, -3f),
                new Vector3(3.4f, 0.12f, 3.4f), new Color(0.82f, 0.84f, 0.84f));
            ZoneBuilder.Decoration(ctx, "ShowerGlass", c + new Vector3(1.5f, 1.25f, -3f),
                new Vector3(0.08f, 2.5f, 3.4f), new Color(0.72f, 0.88f, 0.92f));
            ZoneBuilder.Decoration(ctx, "ShowerGlass2", c + new Vector3(3.2f, 1.25f, -1.3f),
                new Vector3(3.4f, 2.5f, 0.08f), new Color(0.72f, 0.88f, 0.92f));
            ZoneBuilder.Decoration(ctx, "ShowerHead", c + new Vector3(3.2f, 2.5f, -4.5f),
                new Vector3(0.44f, 0.1f, 0.44f), Metal);
            ZoneBuilder.Decoration(ctx, "ShowerPipe", c + new Vector3(3.2f, 1.9f, -4.75f),
                new Vector3(0.08f, 1.3f, 0.08f), Metal);
            ZoneBuilder.Decoration(ctx, "Towel", c + new Vector3(-4.6f, 1.6f, 3f),
                new Vector3(0.12f, 1.1f, 0.8f), new Color(0.86f, 0.62f, 0.42f));

            if (!withTub) return;
            ZoneBuilder.Box(ctx, "Bathtub", c + new Vector3(-2.8f, 0.34f, -3.4f),
                new Vector3(2.2f, 0.68f, 3.8f), new Color(0.95f, 0.95f, 0.96f));
            ZoneBuilder.Decoration(ctx, "TubWater", c + new Vector3(-2.8f, 0.64f, -3.4f),
                new Vector3(2.0f, 0.06f, 3.6f), new Color(0.55f, 0.80f, 0.88f));
        }

        static void Office(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(2f, SlabY, -19f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.2f, 10.6f), "办 公 室");
            ZoneBuilder.Decoration(ctx, "OfficeRug", c + new Vector3(0, 0.08f, 0),
                new Vector3(10f, 0.05f, 8f), new Color(0.28f, 0.30f, 0.34f));

            var desk = ZoneBuilder.Box(ctx, "OfficeDesk", c + new Vector3(0, 0.74f, 2.4f),
                new Vector3(6.4f, 0.14f, 2.6f), Wood);
            HomeFixture.Attach(desk, HomeFixtureKind.Desk);
            for (int s = -1; s <= 1; s += 2)
                ZoneBuilder.Box(ctx, "DeskLeg", c + new Vector3(s * 2.9f, 0.37f, 2.4f),
                    new Vector3(0.18f, 0.74f, 2.3f), Metal);
            ZoneBuilder.Box(ctx, "Drawer", c + new Vector3(2.1f, 0.33f, 2.4f),
                new Vector3(1.3f, 0.66f, 2.0f), Wood);

            OfficeChair(ctx, c + new Vector3(0, 0, 0.4f));

            var pc = ZoneBuilder.Box(ctx, "Monitor", c + new Vector3(-1.0f, 1.32f, 3.2f),
                new Vector3(2.6f, 1.1f, 0.1f), new Color(0.16f, 0.20f, 0.28f));
            HomeFixture.Attach(pc, HomeFixtureKind.Computer);
            ZoneBuilder.Decoration(ctx, "MonitorStand", c + new Vector3(-1.0f, 0.88f, 3.2f),
                new Vector3(0.32f, 0.3f, 0.32f), Dark);
            ZoneBuilder.Decoration(ctx, "Keyboard", c + new Vector3(-1.0f, 0.83f, 2.1f),
                new Vector3(1.8f, 0.05f, 0.6f), Dark);
            var phone = ZoneBuilder.Box(ctx, "Phone", c + new Vector3(1.4f, 0.84f, 1.9f),
                new Vector3(0.42f, 0.05f, 0.84f), new Color(0.85f, 0.9f, 1f));
            HomeFixture.Attach(phone, HomeFixtureKind.Phone);

            // 桌上带相框的照片（可换成玩家自己的图片）
            ZoneBuilder.Decoration(ctx, "FrameStand", c + new Vector3(2.4f, 0.86f, 3.0f),
                new Vector3(0.55f, 0.06f, 0.32f), Dark);
            UserPicture(ctx, c + new Vector3(2.4f, 1.2f, 3.1f), new Vector3(1.0f, 0.72f, 0.05f),
                UserImageSlot.DeskPhoto, "");
            ZoneBuilder.Decoration(ctx, "FrameEdge", c + new Vector3(2.4f, 1.2f, 3.14f),
                new Vector3(1.12f, 0.84f, 0.03f), new Color(0.55f, 0.42f, 0.24f));

            ZoneBuilder.Box(ctx, "FileCabinet", c + new Vector3(-6.5f, 0.9f, 2f),
                new Vector3(1f, 1.8f, 3.6f), Wood);
            Plant(ctx, c + new Vector3(6f, 0, -2.4f));
            AirConditioner(ctx, c + new Vector3(-4f, 2.9f, 3.6f));
        }

        static void Study(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(29f, SlabY, -19f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.2f, 10.6f), "书 房");

            for (int s = 0; s < 3; s++)
            {
                float x = c.x - 7f + s * 7f;
                ZoneBuilder.Box(ctx, "Bookcase", new Vector3(x, c.y + 1.4f, c.z + 9.6f),
                    new Vector3(6f, 2.8f, 0.55f), Wood);
                for (int shelf = 0; shelf < 5; shelf++)
                {
                    float y = c.y + 0.45f + shelf * 0.55f;
                    ZoneBuilder.Decoration(ctx, "Shelf", new Vector3(x, y, c.z + 9.5f),
                        new Vector3(5.8f, 0.06f, 0.5f), new Color(0.34f, 0.24f, 0.15f));
                    for (int b = 0; b < 18; b++)
                        ZoneBuilder.Decoration(ctx, "Book",
                            new Vector3(x - 2.7f + b * 0.3f, y + 0.2f, c.z + 9.5f),
                            new Vector3(0.21f, 0.34f + ((s + shelf + b) % 4) * 0.03f, 0.4f),
                            BookColor(s * 91 + shelf * 13 + b));
                }
            }

            var readChair = ZoneBuilder.Box(ctx, "ReadChair", c + new Vector3(-4f, 0.45f, 2f),
                new Vector3(1.9f, 0.9f, 1.9f), new Color(0.42f, 0.30f, 0.32f));
            Sittable.Attach(readChair, new Vector3(0, 0.55f, 0), Vector3.forward, false, "阅读椅");
            ZoneBuilder.Box(ctx, "SideTable", c + new Vector3(-1.8f, 0.32f, 2f),
                new Vector3(1.0f, 0.64f, 1.0f), Wood);
            ZoneBuilder.Decoration(ctx, "Cup", c + new Vector3(-1.8f, 0.72f, 2f),
                new Vector3(0.24f, 0.26f, 0.24f), new Color(0.92f, 0.92f, 0.9f));
            FloorLamp(ctx, c + new Vector3(-6f, 0, 2f));
            ZoneBuilder.Box(ctx, "StudyDesk", c + new Vector3(4f, 0.74f, 1f),
                new Vector3(4.4f, 0.14f, 2f), Wood);
            var studyChair = ZoneBuilder.Box(ctx, "StudyChair", c + new Vector3(4f, 0.45f, -0.9f),
                new Vector3(1.1f, 0.16f, 1.1f), Dark);
            Sittable.Attach(studyChair, new Vector3(0, 0.55f, 0), Vector3.forward, false, "书桌椅");
        }

        static void Balcony(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(14f, SlabY, 19f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.0f, -2.6f), "露 台");
            // 露台在二层北侧：地板由楼板承担，这里只做栏杆与陈设
            for (int s = -1; s <= 1; s += 2)
                ZoneBuilder.Decoration(ctx, "Railing", c + new Vector3(s * 13f, 0.6f, 4f),
                    new Vector3(0.12f, 1.2f, 20f), Metal);
            ZoneBuilder.Decoration(ctx, "Railing", c + new Vector3(0, 0.6f, 10f),
                new Vector3(26f, 1.2f, 0.12f), Metal);
            for (int i = -1; i <= 1; i += 2)
            {
                var lounger = ZoneBuilder.Box(ctx, "Lounger", c + new Vector3(i * 4f, 0.35f, 4f),
                    new Vector3(1.1f, 0.2f, 2.6f), new Color(0.90f, 0.88f, 0.82f));
                Sittable.Attach(lounger, new Vector3(0, 0.5f, 0), Vector3.forward, true, "躺椅");
            }
            ZoneBuilder.Box(ctx, "PatioTable", c + new Vector3(0, 0.36f, 4f),
                new Vector3(1.6f, 0.72f, 1.6f), WoodLight);
            Plant(ctx, c + new Vector3(-10f, 0, 8f));
            Plant(ctx, c + new Vector3(10f, 0, 8f));
        }

        // ================= 室外 =================

        static void Pool(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(W / 2f + 22f, 0, 6f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 2.8f, 13f), "泳 池");

            ZoneBuilder.Decoration(ctx, "PoolDeck", c + new Vector3(0, 0.05f, 0),
                new Vector3(30f, 0.1f, 26f), new Color(0.76f, 0.74f, 0.70f));
            ZoneBuilder.Decoration(ctx, "PoolWater", c + new Vector3(0, 0.08f, 0),
                new Vector3(18f, 0.06f, 11f), Water);
            ZoneBuilder.Decoration(ctx, "PoolLine", c + new Vector3(0, 0.1f, 0),
                new Vector3(17.4f, 0.02f, 0.18f), new Color(0.92f, 0.95f, 0.98f));
            for (int s = -1; s <= 1; s += 2)
            {
                ZoneBuilder.Box(ctx, "PoolCurb", c + new Vector3(0, 0.15f, s * 5.8f),
                    new Vector3(19f, 0.3f, 0.7f), FloorTile);
                ZoneBuilder.Box(ctx, "PoolCurb", c + new Vector3(s * 9.3f, 0.15f, 0),
                    new Vector3(0.7f, 0.3f, 12.3f), FloorTile);
                var lounger = ZoneBuilder.Box(ctx, "PoolLounger", c + new Vector3(s * 12.5f, 0.35f, 2f),
                    new Vector3(1.1f, 0.2f, 2.6f), new Color(0.90f, 0.88f, 0.82f));
                Sittable.Attach(lounger, new Vector3(0, 0.5f, 0), Vector3.forward, true, "泳池躺椅");
            }
            ZoneBuilder.Decoration(ctx, "PoolLadder", c + new Vector3(8f, 0.6f, 6.2f),
                new Vector3(0.1f, 1.2f, 0.7f), Metal);
            ZoneBuilder.Box(ctx, "UmbrellaPole", c + new Vector3(12.5f, 1.3f, -2f),
                new Vector3(0.14f, 2.6f, 0.14f), Metal);
            ZoneBuilder.Decoration(ctx, "Umbrella", c + new Vector3(12.5f, 2.7f, -2f),
                new Vector3(4f, 0.22f, 4f), new Color(0.85f, 0.40f, 0.32f));
            ZoneBuilder.AddCeilingLight(c + new Vector3(-5f, 0.5f, 0), new Color(0.45f, 0.85f, 1f), 16f);
            ZoneBuilder.AddCeilingLight(c + new Vector3(5f, 0.5f, 0), new Color(0.45f, 0.85f, 1f), 16f);
        }

        static void Garage(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-W / 2f - 14f, 0, -14f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 4.4f, -6.2f), "车 库");

            ZoneBuilder.Decoration(ctx, "GarageFloor", c + new Vector3(0, 0.06f, 0),
                new Vector3(16f, 0.08f, 11f), new Color(0.42f, 0.42f, 0.44f));
            ZoneBuilder.Box(ctx, "GarageWall", c + new Vector3(0, 1.9f, 5.5f), new Vector3(16f, 3.8f, 0.3f), WallWarm);
            for (int s = -1; s <= 1; s += 2)
                ZoneBuilder.Box(ctx, "GarageWall", c + new Vector3(s * 8f, 1.9f, 0), new Vector3(0.3f, 3.8f, 11f), WallWarm);
            ZoneBuilder.Box(ctx, "GarageRoof", c + new Vector3(0, 3.9f, 0), new Vector3(16.6f, 0.3f, 11.6f), WallWarm);
            ZoneBuilder.Decoration(ctx, "GarageDoor", c + new Vector3(0, 3.4f, -5.4f),
                new Vector3(15.4f, 0.9f, 0.2f), Metal);
            ZoneBuilder.Decoration(ctx, "Driveway", c + new Vector3(0, 0.06f, -13f),
                new Vector3(9f, 0.06f, 16f), new Color(0.48f, 0.48f, 0.50f));

            Car(ctx, c + new Vector3(-3.5f, 0, 0.5f), new Color(0.18f, 0.24f, 0.34f));
            Car(ctx, c + new Vector3(3.5f, 0, 0.5f), new Color(0.42f, 0.16f, 0.16f));

            ZoneBuilder.Box(ctx, "Workbench", c + new Vector3(6f, 0.45f, 4.2f), new Vector3(3f, 0.9f, 1.2f), Wood);
            for (int i = 0; i < 6; i++)
                ZoneBuilder.Decoration(ctx, "Tool", c + new Vector3(4.8f + i * 0.5f, 2.0f, 5.1f),
                    new Vector3(0.12f, 0.62f, 0.1f), i % 2 == 0 ? Metal : new Color(0.8f, 0.5f, 0.2f));
            ZoneBuilder.AddCeilingLight(c + new Vector3(0, 3.4f, 0), new Color(1f, 0.96f, 0.88f), 18f);
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
                ZoneBuilder.Decoration(ctx, "GardenLampPole", h + new Vector3(s * 8f, 1.4f, -D / 2f - 14f),
                    new Vector3(0.14f, 2.8f, 0.14f), Dark);
                ZoneBuilder.Decoration(ctx, "GardenLampHead", h + new Vector3(s * 8f, 2.9f, -D / 2f - 14f),
                    new Vector3(0.5f, 0.4f, 0.5f), new Color(1f, 0.94f, 0.78f));
                ZoneBuilder.AddCeilingLight(h + new Vector3(s * 8f, 2.9f, -D / 2f - 14f),
                    new Color(1f, 0.92f, 0.74f), 14f);
            }
            ZoneBuilder.Box(ctx, "Mailbox", h + new Vector3(9f, 0.8f, -D / 2f - 21f),
                new Vector3(0.4f, 1.6f, 0.4f), Dark);
            ZoneBuilder.Decoration(ctx, "MailboxTop", h + new Vector3(9f, 1.75f, -D / 2f - 21f),
                new Vector3(0.7f, 0.4f, 0.5f), new Color(0.55f, 0.20f, 0.18f));
            // 院墙 + 大门柱：宅基地到哪儿为止要看得出来
            for (int s = -1; s <= 1; s += 2)
            {
                ZoneBuilder.Box(ctx, "FenceWall", h + new Vector3(6f + s * 66f, 0.9f, 0),
                    new Vector3(0.5f, 1.8f, 78f), WallWarm);
                ZoneBuilder.Box(ctx, "FenceWall", h + new Vector3(6f, 0.9f, s * 39f),
                    new Vector3(132f, 1.8f, 0.5f), WallWarm);
            }
        }

        static void Tree(WorldContext ctx, Vector3 at)
        {
            ZoneBuilder.Box(ctx, "Trunk", at + new Vector3(0, 1.6f, 0), new Vector3(0.6f, 3.2f, 0.6f),
                new Color(0.36f, 0.26f, 0.18f));
            ZoneBuilder.Decoration(ctx, "Canopy", at + new Vector3(0, 4f, 0), new Vector3(4.6f, 3f, 4.6f),
                new Color(0.22f, 0.44f, 0.24f));
            ZoneBuilder.Decoration(ctx, "Canopy2", at + new Vector3(0.6f, 5.2f, -0.4f), new Vector3(3.2f, 2.2f, 3.2f),
                new Color(0.26f, 0.50f, 0.28f));
        }

        // ================= 宠物 =================

        static void Cat(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-12f, 0, -4f);
            ZoneBuilder.Decoration(ctx, "CatBed", c + new Vector3(0, 0.1f, 0),
                new Vector3(1.5f, 0.2f, 1.5f), new Color(0.62f, 0.48f, 0.40f));
            var bowl = ZoneBuilder.Box(ctx, "CatBowl", c + new Vector3(1.8f, 0.09f, 0),
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
            var seat = ZoneBuilder.Box(ctx, "Sofa", at + rot * new Vector3(0, 0.25f, 0),
                Abs(rot * seatSize), Cloth);
            Sittable.Attach(seat, new Vector3(0, 0.6f, 0), rot * Vector3.forward, false, "沙发");
            ZoneBuilder.Box(ctx, "SofaBack", at + rot * new Vector3(0, 0.78f, -0.85f),
                Abs(rot * new Vector3(5.2f, 1.0f, 0.5f)), Cloth);
            for (int s = -1; s <= 1; s += 2)
            {
                ZoneBuilder.Box(ctx, "SofaArm", at + rot * new Vector3(s * 2.5f, 0.6f, 0),
                    Abs(rot * new Vector3(0.45f, 0.7f, 2.0f)), Cloth);
                ZoneBuilder.Decoration(ctx, "Cushion", at + rot * new Vector3(s * 1.4f, 0.66f, -0.3f),
                    Abs(rot * new Vector3(0.8f, 0.24f, 0.8f)), new Color(0.72f, 0.66f, 0.55f));
            }
        }

        /// <summary>旋转之后的尺寸取绝对值——盒子没有朝向，只有边长。</summary>
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static void DiningChair(WorldContext ctx, Vector3 at, float yawDeg)
        {
            var rot = Quaternion.Euler(0, yawDeg, 0);
            var seat = ZoneBuilder.Box(ctx, "DiningChair", at + new Vector3(0, 0.48f, 0),
                Abs(rot * new Vector3(1.0f, 0.12f, 1.0f)), Wood);
            Sittable.Attach(seat, new Vector3(0, 0.55f, 0), rot * Vector3.forward, false, "餐椅");
            ZoneBuilder.Box(ctx, "ChairBack", at + rot * new Vector3(0, 0.95f, -0.45f),
                Abs(rot * new Vector3(1.0f, 1.0f, 0.12f)), Wood);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    ZoneBuilder.Decoration(ctx, "ChairLeg", at + new Vector3(sx * 0.4f, 0.24f, sz * 0.4f),
                        new Vector3(0.1f, 0.48f, 0.1f), Wood);
        }

        static void OfficeChair(WorldContext ctx, Vector3 at)
        {
            var seat = ZoneBuilder.Box(ctx, "ChairSeat", at + new Vector3(0, 0.52f, 0),
                new Vector3(1.2f, 0.18f, 1.2f), Dark);
            Sittable.Attach(seat, new Vector3(0, 0.58f, 0), Vector3.forward, false, "办公椅");
            ZoneBuilder.Box(ctx, "ChairBack", at + new Vector3(0, 1.1f, -0.55f),
                new Vector3(1.2f, 1.2f, 0.16f), Dark);
            ZoneBuilder.Decoration(ctx, "ChairPole", at + new Vector3(0, 0.26f, 0),
                new Vector3(0.16f, 0.52f, 0.16f), Metal);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f * Mathf.Deg2Rad;
                ZoneBuilder.Decoration(ctx, "ChairFoot",
                    at + new Vector3(Mathf.Cos(a) * 0.5f, 0.06f, Mathf.Sin(a) * 0.5f),
                    new Vector3(0.55f, 0.1f, 0.16f), Metal);
            }
        }

        static void BarStool(WorldContext ctx, Vector3 at)
        {
            var seat = ZoneBuilder.Box(ctx, "StoolSeat", at + new Vector3(0, 0.76f, 0),
                new Vector3(0.75f, 0.14f, 0.75f), Wood);
            Sittable.Attach(seat, new Vector3(0, 0.6f, 0), Vector3.forward, false, "吧椅");
            ZoneBuilder.Decoration(ctx, "StoolPole", at + new Vector3(0, 0.38f, 0),
                new Vector3(0.13f, 0.76f, 0.13f), Metal);
            ZoneBuilder.Decoration(ctx, "StoolFoot", at + new Vector3(0, 0.05f, 0),
                new Vector3(0.62f, 0.08f, 0.62f), Metal);
        }

        static void Plant(WorldContext ctx, Vector3 at)
        {
            ZoneBuilder.Box(ctx, "PlantPot", at + new Vector3(0, 0.3f, 0), new Vector3(0.8f, 0.6f, 0.8f),
                new Color(0.52f, 0.36f, 0.28f));
            ZoneBuilder.Decoration(ctx, "PlantLeaf", at + new Vector3(0, 1.1f, 0), new Vector3(1.2f, 1.3f, 1.2f),
                new Color(0.22f, 0.46f, 0.26f));
            ZoneBuilder.Decoration(ctx, "PlantLeaf", at + new Vector3(0.26f, 1.65f, -0.16f), new Vector3(0.8f, 0.9f, 0.8f),
                new Color(0.26f, 0.52f, 0.30f));
        }

        static void FloorLamp(WorldContext ctx, Vector3 at)
        {
            ZoneBuilder.Decoration(ctx, "LampBase", at + new Vector3(0, 0.05f, 0), new Vector3(0.55f, 0.1f, 0.55f), Dark);
            ZoneBuilder.Decoration(ctx, "LampPole", at + new Vector3(0, 0.9f, 0), new Vector3(0.08f, 1.8f, 0.08f), Metal);
            ZoneBuilder.Decoration(ctx, "LampShade", at + new Vector3(0, 1.95f, 0), new Vector3(0.75f, 0.55f, 0.75f),
                new Color(0.98f, 0.92f, 0.78f));
            ZoneBuilder.AddCeilingLight(at + new Vector3(0, 1.9f, 0), new Color(1f, 0.93f, 0.78f), 10f);
        }

        static void Curtain(WorldContext ctx, Vector3 at, float width, bool alongZ)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 off = alongZ ? new Vector3(0, 1.9f, s * (width / 2f - 0.6f))
                                     : new Vector3(s * (width / 2f - 0.6f), 1.9f, 0);
                Vector3 size = alongZ ? new Vector3(0.22f, 3.0f, 1.2f) : new Vector3(1.2f, 3.0f, 0.22f);
                ZoneBuilder.Decoration(ctx, "Curtain", at + off, size, new Color(0.62f, 0.55f, 0.48f));
            }
            Vector3 rod = alongZ ? new Vector3(0.08f, 0.08f, width + 0.6f) : new Vector3(width + 0.6f, 0.08f, 0.08f);
            ZoneBuilder.Decoration(ctx, "CurtainRod", at + new Vector3(0, 3.45f, 0), rod, Metal);
        }

        static void AirConditioner(WorldContext ctx, Vector3 at)
        {
            ZoneBuilder.Decoration(ctx, "AC", at, new Vector3(2.2f, 0.65f, 0.55f), new Color(0.96f, 0.96f, 0.97f));
            ZoneBuilder.Decoration(ctx, "AC_Vent", at + new Vector3(0, -0.24f, -0.18f),
                new Vector3(2.0f, 0.12f, 0.32f), new Color(0.55f, 0.78f, 0.92f));
            ZoneBuilder.Decoration(ctx, "AC_Led", at + new Vector3(0.8f, 0.12f, -0.28f),
                new Vector3(0.16f, 0.08f, 0.05f), new Color(0.3f, 0.95f, 0.5f));
        }

        static void CeilingFan(WorldContext ctx, Vector3 at)
        {
            ZoneBuilder.Decoration(ctx, "FanRod", at + new Vector3(0, 0.3f, 0), new Vector3(0.09f, 0.6f, 0.09f), Metal);
            var hub = ZoneBuilder.Decoration(ctx, "FanHub", at, new Vector3(0.45f, 0.2f, 0.45f), Metal);
            for (int i = 0; i < 4; i++)
            {
                var blade = ZoneBuilder.Decoration(ctx, "FanBlade", at, new Vector3(2.8f, 0.07f, 0.45f), Wood);
                blade.transform.SetParent(hub.transform, true);
                blade.transform.localRotation = Quaternion.Euler(0, i * 90f, 0);
            }
            hub.AddComponent<SpinY>().speed = 110f;
        }

        static void UserPicture(WorldContext ctx, Vector3 at, Vector3 size, UserImageSlot slot, string sign)
        {
            var go = ZoneBuilder.Decoration(ctx, "UserPicture_" + slot, at, size, new Color(0.62f, 0.60f, 0.66f));
            go.AddComponent<UserPictureFrame>().slot = slot;
            // 画框外圈：没有框的画看着像贴在墙上的一块色卡
            Vector3 frame = new Vector3(size.x > 0.2f ? size.x + 0.22f : size.x,
                size.y + 0.22f, size.z > 0.2f ? size.z + 0.22f : size.z);
            ZoneBuilder.Decoration(ctx, "PictureFrame", at + new Vector3(0, 0, 0.02f), frame,
                new Color(0.45f, 0.34f, 0.22f));
            if (!string.IsNullOrEmpty(sign))
                OpenWorldBuilder.HomeSign(at + new Vector3(0, size.y / 2f + 0.55f, 0), sign);
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
            ZoneBuilder.Box(ctx, "CarBody", at + new Vector3(0, 0.74f, 0), new Vector3(2.0f, 0.9f, 4.8f), body);
            ZoneBuilder.Box(ctx, "CarCabin", at + new Vector3(0, 1.42f, -0.2f), new Vector3(1.8f, 0.7f, 2.5f),
                new Color(body.r * 0.7f, body.g * 0.7f, body.b * 0.7f));
            for (int s = -1; s <= 1; s += 2)
            {
                ZoneBuilder.Decoration(ctx, "SideGlass", at + new Vector3(s * 0.92f, 1.42f, -0.2f),
                    new Vector3(0.06f, 0.52f, 2.3f), Glass);
                ZoneBuilder.Decoration(ctx, "Headlight", at + new Vector3(s * 0.66f, 0.82f, 2.42f),
                    new Vector3(0.5f, 0.26f, 0.08f), new Color(0.95f, 0.95f, 0.85f));
                ZoneBuilder.Decoration(ctx, "Taillight", at + new Vector3(s * 0.66f, 0.88f, -2.42f),
                    new Vector3(0.46f, 0.2f, 0.08f), new Color(0.75f, 0.15f, 0.12f));
            }
            ZoneBuilder.Decoration(ctx, "Windshield", at + new Vector3(0, 1.42f, 1.1f), new Vector3(1.7f, 0.62f, 0.1f), Glass);
            ZoneBuilder.Decoration(ctx, "RearGlass", at + new Vector3(0, 1.42f, -1.5f), new Vector3(1.7f, 0.62f, 0.1f), Glass);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    ZoneBuilder.Decoration(ctx, "Wheel", at + new Vector3(sx * 0.98f, 0.38f, sz * 1.6f),
                        new Vector3(0.26f, 0.76f, 0.76f), new Color(0.10f, 0.10f, 0.11f));
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
                ZoneBuilder.Decoration(ctx, "CeilLamp", at, new Vector3(1.6f, 0.16f, 1.6f), s.color);
                ZoneBuilder.Decoration(ctx, "LampRing", at - new Vector3(0, 0.1f, 0),
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
