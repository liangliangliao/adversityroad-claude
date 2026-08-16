using UnityEngine;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// Player Home 升级版：一栋能住人的私人别墅。
    ///
    /// 【为什么把住处做大】
    /// 住处是这个游戏里唯一一处"没有敌人、没有倒计时"的地方——玩家从一场打不过的
    /// 关卡里退出来，回到的就是这里。原来的版本是一间 26×20 的开间：一张床、一张桌、
    /// 一面镜子，功能上够用，但它不是一个**想待着**的地方，而这恰恰是安全屋该有的作用。
    ///
    /// 现在按一栋真实住宅来排：客厅（一整面墙的目标看板）、卧室、办公室、书房、
    /// 卫生间、厨房、六人餐厅、健身房、室外泳池、车库。加上一只会走动、能喂的猫。
    /// 全部是运行时程序化搭建，和世界其它部分同一套构件——不引入任何美术资源。
    ///
    /// 墙上的画与桌上的相框支持玩家自己的图片：见 UserImageLibrary。
    /// </summary>
    public static class PlayerVilla
    {
        // 主体尺寸（单层大平层 + 室外泳池与车库）
        public const float W = 48f, D = 36f;
        const float WallH = 3.6f, WallT = 0.3f;

        static readonly Color Wall = new Color(0.86f, 0.83f, 0.78f);
        static readonly Color WallWarm = new Color(0.78f, 0.72f, 0.64f);
        static readonly Color FloorWood = new Color(0.46f, 0.33f, 0.22f);
        static readonly Color FloorTile = new Color(0.72f, 0.71f, 0.68f);
        static readonly Color Metal = new Color(0.62f, 0.64f, 0.68f);
        static readonly Color Dark = new Color(0.20f, 0.21f, 0.24f);
        static readonly Color Cloth = new Color(0.32f, 0.38f, 0.52f);
        static readonly Color Wood = new Color(0.40f, 0.28f, 0.18f);
        static readonly Color Water = new Color(0.22f, 0.58f, 0.78f);

        public static Vector3 InteriorSpawn { get; private set; }
        public static Vector3 DoorSpawn { get; private set; }

        public static void Build(WorldContext ctx, Vector3 h)
        {
            Ground(ctx, h);
            Shell(ctx, h);
            Partitions(ctx, h);

            LivingRoom(ctx, h);
            Bedroom(ctx, h);
            Office(ctx, h);
            Study(ctx, h);
            Bathroom(ctx, h);
            Kitchen(ctx, h);
            Dining(ctx, h);
            Gym(ctx, h);
            Pool(ctx, h);
            Garage(ctx, h);
            Cat(ctx, h);
            Lights(h);

            InteriorSpawn = h + new Vector3(0, 1.1f, -14.5f);   // 进门正前方，不和餐桌抢位置
            DoorSpawn = h + new Vector3(0, 1.1f, -D / 2f - 5f);
        }

        // ================= 外壳 =================

        static void Ground(WorldContext ctx, Vector3 h)
        {
            // 宅基地：房子 + 前院 + 泳池院 + 车库都站在同一块地上。
            // 尺寸卡在住宅区那块空地里（东边到便利店、北边到楼栋、南边到人行道为止）
            ZoneBuilder.Box(ctx, "Villa_Lot", h + new Vector3(10f, -0.12f, -1f),
                new Vector3(80f, 0.24f, 50f), new Color(0.38f, 0.44f, 0.32f));
            // 室内地板：起居区木地板，湿区瓷砖
            ZoneBuilder.Decoration(ctx, "Floor_Wood", h + new Vector3(0, 0.02f, 0),
                new Vector3(W - 0.6f, 0.06f, D - 0.6f), FloorWood);
            ZoneBuilder.Decoration(ctx, "Floor_Tile", h + new Vector3(-17f, 0.04f, -6f),
                new Vector3(13f, 0.06f, 22f), FloorTile);
            // 入户步道
            ZoneBuilder.Decoration(ctx, "Path", h + new Vector3(0, 0.06f, -D / 2f - 9f),
                new Vector3(6f, 0.06f, 18f), new Color(0.62f, 0.60f, 0.56f));
        }

        static void Shell(WorldContext ctx, Vector3 h)
        {
            float hw = W / 2f, hd = D / 2f;
            // 北 / 东 / 西 三面整墙
            ZoneBuilder.Box(ctx, "Wall_N", h + new Vector3(0, WallH / 2f, hd), new Vector3(W, WallH, WallT), Wall);
            ZoneBuilder.Box(ctx, "Wall_W", h + new Vector3(-hw, WallH / 2f, 0), new Vector3(WallT, WallH, D), Wall);
            ZoneBuilder.Box(ctx, "Wall_E", h + new Vector3(hw, WallH / 2f, 0), new Vector3(WallT, WallH, D), Wall);
            // 南墙留 6 米门洞
            ZoneBuilder.Box(ctx, "Wall_S", h + new Vector3(-15f, WallH / 2f, -hd), new Vector3(18f, WallH, WallT), Wall);
            ZoneBuilder.Box(ctx, "Wall_S", h + new Vector3(15f, WallH / 2f, -hd), new Vector3(18f, WallH, WallT), Wall);
            ZoneBuilder.Box(ctx, "Wall_S_Top", h + new Vector3(0, WallH - 0.4f, -hd), new Vector3(12f, 0.8f, WallT), Wall);

            // 屋顶（有顶才像房子；室内照明全靠吊灯）
            ZoneBuilder.Box(ctx, "Roof", h + new Vector3(0, WallH + 0.15f, 0), new Vector3(W + 1.4f, 0.3f, D + 1.4f), WallWarm);
            ZoneBuilder.Decoration(ctx, "Eave", h + new Vector3(0, WallH + 0.45f, 0), new Vector3(W + 2.2f, 0.2f, D + 2.2f), Dark);

            // 门廊：两根柱子 + 门牌
            ZoneBuilder.Box(ctx, "Porch_Col", h + new Vector3(-3.4f, 1.8f, -hd - 3f), new Vector3(0.5f, 3.6f, 0.5f), Wall);
            ZoneBuilder.Box(ctx, "Porch_Col", h + new Vector3(3.4f, 1.8f, -hd - 3f), new Vector3(0.5f, 3.6f, 0.5f), Wall);
            ZoneBuilder.Decoration(ctx, "Porch_Roof", h + new Vector3(0, 3.75f, -hd - 2f), new Vector3(9f, 0.3f, 5f), WallWarm);
            OpenWorldBuilder.HomeSign(h + new Vector3(0, 4.4f, -hd - 0.5f), "我 的 住 处");

            // 落地窗（北面朝院子）
            for (int i = -1; i <= 1; i++)
                ZoneBuilder.Decoration(ctx, "Window", h + new Vector3(i * 9f, 1.9f, hd - 0.2f),
                    new Vector3(6f, 2.6f, 0.12f), new Color(0.62f, 0.78f, 0.92f, 1f));
        }

        /// <summary>内隔墙：把这一层分成八个房间，每道墙都留门洞。</summary>
        static void Partitions(WorldContext ctx, Vector3 h)
        {
            // 东西向主走廊在 z = 0 一带；南北向两道主隔墙在 x = ±8
            WallWithDoor(ctx, h, new Vector3(-8f, 0, 5f), new Vector3(WallT, WallH, 26f), true);
            WallWithDoor(ctx, h, new Vector3(8f, 0, 5f), new Vector3(WallT, WallH, 26f), true);
            WallWithDoor(ctx, h, new Vector3(-14f, 0, 2f), new Vector3(20f, WallH, WallT), false);
            WallWithDoor(ctx, h, new Vector3(14f, 0, 2f), new Vector3(20f, WallH, WallT), false);
            WallWithDoor(ctx, h, new Vector3(-14f, 0, -6f), new Vector3(20f, WallH, WallT), false);
            WallWithDoor(ctx, h, new Vector3(14f, 0, -8f), new Vector3(20f, WallH, WallT), false);
            WallWithDoor(ctx, h, new Vector3(-4f, 0, -8f), new Vector3(10f, WallH, WallT), false);
        }

        /// <summary>一道中间留 2.6 米门洞的墙（沿长边切成两段 + 门楣）。</summary>
        static void WallWithDoor(WorldContext ctx, Vector3 h, Vector3 center, Vector3 size, bool alongZ)
        {
            const float door = 2.8f;
            float len = alongZ ? size.z : size.x;
            float seg = (len - door) / 2f;
            if (seg <= 0.2f) { ZoneBuilder.Box(ctx, "Wall", h + center + Vector3.up * (WallH / 2f), size, Wall); return; }

            float off = (seg + door) / 2f;
            if (alongZ)
            {
                ZoneBuilder.Box(ctx, "Wall", h + center + new Vector3(0, WallH / 2f, off), new Vector3(size.x, WallH, seg), Wall);
                ZoneBuilder.Box(ctx, "Wall", h + center + new Vector3(0, WallH / 2f, -off), new Vector3(size.x, WallH, seg), Wall);
                ZoneBuilder.Box(ctx, "Wall_Head", h + center + new Vector3(0, WallH - 0.35f, 0), new Vector3(size.x, 0.7f, door), Wall);
            }
            else
            {
                ZoneBuilder.Box(ctx, "Wall", h + center + new Vector3(off, WallH / 2f, 0), new Vector3(seg, WallH, size.z), Wall);
                ZoneBuilder.Box(ctx, "Wall", h + center + new Vector3(-off, WallH / 2f, 0), new Vector3(seg, WallH, size.z), Wall);
                ZoneBuilder.Box(ctx, "Wall_Head", h + center + new Vector3(0, WallH - 0.35f, 0), new Vector3(door, 0.7f, size.z), Wall);
            }
        }

        // ================= 客厅：一整面墙的目标看板 =================

        static void LivingRoom(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(0, 0, -6f);

            // 沙发组 + 茶几 + 地毯
            ZoneBuilder.Decoration(ctx, "Rug", c + new Vector3(0, 0.07f, -1f), new Vector3(9f, 0.05f, 6f),
                new Color(0.36f, 0.26f, 0.24f));
            Sofa(ctx, c + new Vector3(-4.6f, 0, -1f), true);
            Sofa(ctx, c + new Vector3(0, 0, -4.4f), false);
            ZoneBuilder.Box(ctx, "CoffeeTable", c + new Vector3(0, 0.32f, -1f), new Vector3(3.2f, 0.64f, 1.6f), Wood);
            ZoneBuilder.Decoration(ctx, "Books", c + new Vector3(0.6f, 0.7f, -1f), new Vector3(0.7f, 0.12f, 0.5f), new Color(0.7f, 0.3f, 0.25f));

            // —— 大型目标看板（这一版的主角）——
            // 玩家反馈"目标看板要放大并清楚显示目标、计划、关卡路线"：
            // 原来是墙上一块 4.4×2.2 的小板，现在做成一整面 12×4 的落地屏。
            var frame = ZoneBuilder.Box(ctx, "GoalBoard_Frame", c + new Vector3(0, 2.2f, 5.5f),
                new Vector3(13f, 4.4f, 0.3f), Dark);
            ZoneBuilder.Decoration(ctx, "GoalBoard_Screen", c + new Vector3(0, 2.2f, 5.3f),
                new Vector3(12.2f, 3.8f, 0.1f), new Color(0.08f, 0.12f, 0.18f));
            var gb = frame.AddComponent<Combat.GoalBoard>();
            gb.interactRange = 7f;    // 一整面墙，站在客厅任何位置都算"在看板前"
            HomeFixture.Attach(frame, HomeFixtureKind.GoalBoard);
            // 把目标、计划、关卡路线直接写在这面墙上（不用先打开面板）
            GoalBoardDisplay.Attach(frame, c + new Vector3(0, 2.2f, 5.3f), 12.2f, 3.8f);
            // 看板正下方的一排指示灯：远远看过去就知道那面墙是活的
            for (int i = -5; i <= 5; i++)
                ZoneBuilder.Decoration(ctx, "BoardLed", c + new Vector3(i * 1.1f, 0.25f, 5.2f),
                    new Vector3(0.5f, 0.06f, 0.2f), new Color(0.35f, 0.75f, 1f));

            // 电视墙对面的边柜 + 绿植 + 落地灯
            ZoneBuilder.Box(ctx, "Sideboard", c + new Vector3(-7f, 0.4f, 3f), new Vector3(2f, 0.8f, 1f), Wood);
            Plant(ctx, c + new Vector3(6.6f, 0, 3.4f));
            Plant(ctx, c + new Vector3(-6.8f, 0, -5f));
            FloorLamp(ctx, c + new Vector3(5.4f, 0, -4.6f));

            // 空调（挂机）与吊扇：家里该有的电器
            AirConditioner(ctx, c + new Vector3(-6.5f, 2.7f, 5.4f));
            CeilingFan(ctx, c + new Vector3(0, 3.2f, -2f));
        }

        static void Sofa(WorldContext ctx, Vector3 at, bool faceEast)
        {
            Vector3 seat = faceEast ? new Vector3(1.9f, 0.45f, 4.2f) : new Vector3(4.2f, 0.45f, 1.9f);
            Vector3 back = faceEast ? new Vector3(0.5f, 0.9f, 4.2f) : new Vector3(4.2f, 0.9f, 0.5f);
            Vector3 backOff = faceEast ? new Vector3(-0.7f, 0.75f, 0) : new Vector3(0, 0.75f, -0.7f);
            ZoneBuilder.Box(ctx, "Sofa", at + new Vector3(0, 0.23f, 0), seat, Cloth);
            ZoneBuilder.Box(ctx, "SofaBack", at + backOff, back, Cloth);
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 p = faceEast ? new Vector3(0, 0.62f, i * 1.2f) : new Vector3(i * 1.2f, 0.62f, 0);
                ZoneBuilder.Decoration(ctx, "Cushion", at + p, new Vector3(0.7f, 0.22f, 0.7f),
                    new Color(0.72f, 0.66f, 0.55f));
            }
        }

        // ================= 卧室：床品齐全 + 墙上艺术画 =================

        static void Bedroom(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-15f, 0, 10f);

            // 床：床架 → 床垫 → 床单 → 被子 → 两只枕头
            ZoneBuilder.Box(ctx, "BedFrame", c + new Vector3(0, 0.22f, 0), new Vector3(4.6f, 0.44f, 6.2f), Wood);
            ZoneBuilder.Box(ctx, "Mattress", c + new Vector3(0, 0.6f, 0), new Vector3(4.3f, 0.4f, 5.9f),
                new Color(0.93f, 0.92f, 0.90f));
            ZoneBuilder.Decoration(ctx, "BedSheet", c + new Vector3(0, 0.82f, 0), new Vector3(4.5f, 0.05f, 6.1f),
                new Color(0.95f, 0.95f, 0.97f));
            // 被子只盖下三分之二，露出床单——铺满会看不出这是"被子"
            ZoneBuilder.Decoration(ctx, "Quilt", c + new Vector3(0, 0.92f, -1.1f), new Vector3(4.4f, 0.22f, 3.9f),
                new Color(0.30f, 0.40f, 0.58f));
            ZoneBuilder.Decoration(ctx, "QuiltFold", c + new Vector3(0, 1.0f, 0.85f), new Vector3(4.4f, 0.16f, 0.5f),
                new Color(0.38f, 0.48f, 0.66f));
            ZoneBuilder.Decoration(ctx, "Pillow", c + new Vector3(-1.05f, 1.02f, 2.3f), new Vector3(1.8f, 0.28f, 0.9f),
                new Color(0.97f, 0.97f, 0.98f));
            ZoneBuilder.Decoration(ctx, "Pillow", c + new Vector3(1.05f, 1.02f, 2.3f), new Vector3(1.8f, 0.28f, 0.9f),
                new Color(0.97f, 0.97f, 0.98f));
            ZoneBuilder.Box(ctx, "Headboard", c + new Vector3(0, 1.1f, 3.2f), new Vector3(4.8f, 1.6f, 0.25f), WallWarm);

            // 床头柜 + 台灯
            for (int i = -1; i <= 1; i += 2)
            {
                ZoneBuilder.Box(ctx, "Nightstand", c + new Vector3(i * 3f, 0.3f, 2.6f), new Vector3(1.1f, 0.6f, 1.1f), Wood);
                ZoneBuilder.Decoration(ctx, "LampShade", c + new Vector3(i * 3f, 0.85f, 2.6f), new Vector3(0.55f, 0.5f, 0.55f),
                    new Color(0.98f, 0.92f, 0.75f));
            }

            var wardrobe = ZoneBuilder.Box(ctx, "Wardrobe", c + new Vector3(-6.4f, 1.2f, -1f), new Vector3(0.8f, 2.4f, 4.6f), Wood);
            HomeFixture.Attach(wardrobe, HomeFixtureKind.Wardrobe);

            // —— 墙上的艺术画（支持玩家自己的图片）——
            UserPicture(ctx, c + new Vector3(0, 2.3f, 5.35f), new Vector3(3.4f, 2.1f, 0.08f),
                UserImageSlot.BedroomArtA, "艺 术 画");
            UserPicture(ctx, c + new Vector3(-6.55f, 2.2f, 4f), new Vector3(0.08f, 1.6f, 2.4f),
                UserImageSlot.BedroomArtB, "");

            CeilingFan(ctx, c + new Vector3(0, 3.2f, -2.5f));
            AirConditioner(ctx, c + new Vector3(3.5f, 2.7f, 5.3f));
        }

        // ================= 办公室：办公桌椅 + 带相框的照片 =================

        static void Office(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(15f, 0, 10f);

            var desk = ZoneBuilder.Box(ctx, "OfficeDesk", c + new Vector3(0, 0.72f, 1.6f), new Vector3(5.2f, 0.12f, 2.2f), Wood);
            HomeFixture.Attach(desk, HomeFixtureKind.Desk);
            ZoneBuilder.Box(ctx, "DeskLeg", c + new Vector3(-2.4f, 0.36f, 1.6f), new Vector3(0.16f, 0.72f, 2f), Metal);
            ZoneBuilder.Box(ctx, "DeskLeg", c + new Vector3(2.4f, 0.36f, 1.6f), new Vector3(0.16f, 0.72f, 2f), Metal);
            ZoneBuilder.Box(ctx, "Drawer", c + new Vector3(1.7f, 0.32f, 1.6f), new Vector3(1.2f, 0.64f, 1.8f), Wood);

            OfficeChair(ctx, c + new Vector3(0, 0, -0.6f));

            var pc = ZoneBuilder.Box(ctx, "Monitor", c + new Vector3(-0.8f, 1.28f, 2.2f), new Vector3(2.2f, 1.0f, 0.1f),
                new Color(0.16f, 0.20f, 0.28f));
            HomeFixture.Attach(pc, HomeFixtureKind.Computer);
            ZoneBuilder.Decoration(ctx, "MonitorStand", c + new Vector3(-0.8f, 0.85f, 2.2f), new Vector3(0.3f, 0.3f, 0.3f), Dark);
            ZoneBuilder.Decoration(ctx, "Keyboard", c + new Vector3(-0.8f, 0.8f, 1.3f), new Vector3(1.6f, 0.05f, 0.5f), Dark);
            var phone = ZoneBuilder.Box(ctx, "Phone", c + new Vector3(1.2f, 0.81f, 1.1f), new Vector3(0.4f, 0.05f, 0.8f),
                new Color(0.85f, 0.9f, 1f));
            HomeFixture.Attach(phone, HomeFixtureKind.Phone);

            // —— 桌上带相框的照片（支持玩家自己的图片）——
            ZoneBuilder.Decoration(ctx, "FrameStand", c + new Vector3(2.0f, 0.83f, 2.0f), new Vector3(0.5f, 0.06f, 0.3f), Dark);
            UserPicture(ctx, c + new Vector3(2.0f, 1.16f, 2.1f), new Vector3(0.92f, 0.66f, 0.05f),
                UserImageSlot.DeskPhoto, "");
            ZoneBuilder.Decoration(ctx, "FrameEdge", c + new Vector3(2.0f, 1.16f, 2.14f), new Vector3(1.02f, 0.76f, 0.03f),
                new Color(0.55f, 0.42f, 0.24f));

            ZoneBuilder.Box(ctx, "Cabinet", c + new Vector3(6.2f, 0.9f, 2f), new Vector3(0.8f, 1.8f, 3.4f), Wood);
            Plant(ctx, c + new Vector3(5.8f, 0, -2.4f));
            AirConditioner(ctx, c + new Vector3(-3f, 2.7f, 5.3f));
        }

        static void OfficeChair(WorldContext ctx, Vector3 at)
        {
            ZoneBuilder.Box(ctx, "ChairSeat", at + new Vector3(0, 0.5f, 0), new Vector3(1.1f, 0.16f, 1.1f), Dark);
            ZoneBuilder.Box(ctx, "ChairBack", at + new Vector3(0, 1.05f, -0.5f), new Vector3(1.1f, 1.1f, 0.14f), Dark);
            ZoneBuilder.Decoration(ctx, "ChairPole", at + new Vector3(0, 0.25f, 0), new Vector3(0.14f, 0.5f, 0.14f), Metal);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f * Mathf.Deg2Rad;
                ZoneBuilder.Decoration(ctx, "ChairFoot", at + new Vector3(Mathf.Cos(a) * 0.45f, 0.06f, Mathf.Sin(a) * 0.45f),
                    new Vector3(0.5f, 0.1f, 0.14f), Metal);
            }
        }

        // ================= 书房：书柜与书 =================

        static void Study(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(15f, 0, -2f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.1f, 3.2f), "书 房");

            // 三组顶天立地的书柜，每层塞满不同颜色的书脊
            for (int s = 0; s < 3; s++)
            {
                float x = c.x - 5.4f + s * 5.4f;
                ZoneBuilder.Box(ctx, "Bookcase", new Vector3(x, 1.3f, c.z + 2.6f), new Vector3(4.6f, 2.6f, 0.5f), Wood);
                for (int shelf = 0; shelf < 5; shelf++)
                {
                    float y = 0.4f + shelf * 0.5f;
                    ZoneBuilder.Decoration(ctx, "Shelf", new Vector3(x, y, c.z + 2.5f), new Vector3(4.4f, 0.06f, 0.46f),
                        new Color(0.34f, 0.24f, 0.15f));
                    for (int b = 0; b < 14; b++)
                    {
                        float bx = x - 2.0f + b * 0.29f;
                        float hgt = 0.30f + ((s * 31 + shelf * 7 + b) % 5) * 0.03f;
                        var col = BookColor(s * 91 + shelf * 13 + b);
                        ZoneBuilder.Decoration(ctx, "Book", new Vector3(bx, y + hgt / 2f + 0.03f, c.z + 2.5f),
                            new Vector3(0.2f, hgt, 0.36f), col);
                    }
                }
            }

            // 读书角：单人椅 + 落地灯 + 小几
            ZoneBuilder.Box(ctx, "ReadChair", c + new Vector3(-3f, 0.45f, -2.6f), new Vector3(1.6f, 0.9f, 1.6f),
                new Color(0.42f, 0.30f, 0.32f));
            ZoneBuilder.Box(ctx, "SideTable", c + new Vector3(-1.2f, 0.3f, -2.6f), new Vector3(0.9f, 0.6f, 0.9f), Wood);
            ZoneBuilder.Decoration(ctx, "Cup", c + new Vector3(-1.2f, 0.68f, -2.6f), new Vector3(0.22f, 0.24f, 0.22f),
                new Color(0.92f, 0.92f, 0.9f));
            FloorLamp(ctx, c + new Vector3(-4.6f, 0, -2.6f));
        }

        static Color BookColor(int i)
        {
            switch (i % 7)
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

        // ================= 卫生间：镜子 / 马桶 / 浴室 =================

        static void Bathroom(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-17f, 0, -2f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.1f, 3.3f), "卫 生 间");

            // 洗手台 + 镜子
            ZoneBuilder.Box(ctx, "Vanity", c + new Vector3(0, 0.42f, 2.2f), new Vector3(3.6f, 0.84f, 0.9f),
                new Color(0.55f, 0.52f, 0.50f));
            ZoneBuilder.Decoration(ctx, "Basin", c + new Vector3(0, 0.88f, 2.2f), new Vector3(1.2f, 0.16f, 0.7f),
                new Color(0.95f, 0.95f, 0.96f));
            ZoneBuilder.Decoration(ctx, "Tap", c + new Vector3(0, 1.05f, 2.5f), new Vector3(0.1f, 0.32f, 0.1f), Metal);
            var mirror = ZoneBuilder.Box(ctx, "Mirror", c + new Vector3(0, 1.9f, 2.62f), new Vector3(3.0f, 1.5f, 0.08f),
                new Color(0.80f, 0.88f, 0.94f));
            HomeFixture.Attach(mirror, HomeFixtureKind.Mirror);
            ZoneBuilder.Decoration(ctx, "MirrorLight", c + new Vector3(0, 2.75f, 2.5f), new Vector3(2.6f, 0.1f, 0.2f),
                new Color(1f, 0.97f, 0.9f));

            // 马桶
            ZoneBuilder.Box(ctx, "ToiletBase", c + new Vector3(-3.6f, 0.2f, -1.2f), new Vector3(0.8f, 0.4f, 1.2f),
                new Color(0.95f, 0.95f, 0.96f));
            ZoneBuilder.Box(ctx, "ToiletSeat", c + new Vector3(-3.6f, 0.44f, -1.35f), new Vector3(0.86f, 0.12f, 0.9f),
                new Color(0.97f, 0.97f, 0.98f));
            ZoneBuilder.Box(ctx, "ToiletTank", c + new Vector3(-3.6f, 0.68f, -0.55f), new Vector3(0.8f, 0.9f, 0.35f),
                new Color(0.95f, 0.95f, 0.96f));

            // 淋浴间：玻璃隔断 + 花洒 + 地漏
            ZoneBuilder.Decoration(ctx, "ShowerGlass", c + new Vector3(2.0f, 1.2f, -2.4f), new Vector3(0.08f, 2.4f, 3.2f),
                new Color(0.72f, 0.88f, 0.92f));
            ZoneBuilder.Decoration(ctx, "ShowerGlass2", c + new Vector3(0.6f, 1.2f, -0.9f), new Vector3(2.8f, 2.4f, 0.08f),
                new Color(0.72f, 0.88f, 0.92f));
            ZoneBuilder.Decoration(ctx, "ShowerTray", c + new Vector3(0.6f, 0.08f, -2.4f), new Vector3(2.8f, 0.12f, 3.2f),
                new Color(0.82f, 0.84f, 0.84f));
            ZoneBuilder.Decoration(ctx, "ShowerHead", c + new Vector3(0.6f, 2.4f, -3.6f), new Vector3(0.4f, 0.1f, 0.4f), Metal);
            ZoneBuilder.Decoration(ctx, "ShowerPipe", c + new Vector3(0.6f, 1.8f, -3.85f), new Vector3(0.08f, 1.2f, 0.08f), Metal);

            // 浴缸
            ZoneBuilder.Box(ctx, "Bathtub", c + new Vector3(-2.4f, 0.32f, -4.2f), new Vector3(2.0f, 0.64f, 3.4f),
                new Color(0.95f, 0.95f, 0.96f));
            ZoneBuilder.Decoration(ctx, "TubWater", c + new Vector3(-2.4f, 0.6f, -4.2f), new Vector3(1.8f, 0.06f, 3.2f),
                new Color(0.55f, 0.80f, 0.88f));
            ZoneBuilder.Decoration(ctx, "Towel", c + new Vector3(2.9f, 1.5f, 0.6f), new Vector3(0.12f, 1.0f, 0.7f),
                new Color(0.86f, 0.62f, 0.42f));
        }

        // ================= 厨房 =================

        static void Kitchen(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-16f, 0, -12f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.1f, 3.2f), "厨 房");

            // L 形橱柜台面
            ZoneBuilder.Box(ctx, "Counter", c + new Vector3(0, 0.45f, 2.2f), new Vector3(9f, 0.9f, 0.9f),
                new Color(0.58f, 0.56f, 0.54f));
            ZoneBuilder.Decoration(ctx, "CounterTop", c + new Vector3(0, 0.92f, 2.2f), new Vector3(9.2f, 0.08f, 1.0f), Dark);
            ZoneBuilder.Box(ctx, "Counter", c + new Vector3(-4.1f, 0.45f, -0.6f), new Vector3(0.9f, 0.9f, 5f),
                new Color(0.58f, 0.56f, 0.54f));
            ZoneBuilder.Decoration(ctx, "CounterTop", c + new Vector3(-4.1f, 0.92f, -0.6f), new Vector3(1.0f, 0.08f, 5.2f), Dark);

            // 灶台 + 抽油烟机 + 水槽
            ZoneBuilder.Decoration(ctx, "Stove", c + new Vector3(1.8f, 0.97f, 2.2f), new Vector3(1.8f, 0.06f, 0.8f), Dark);
            for (int i = 0; i < 4; i++)
                ZoneBuilder.Decoration(ctx, "Burner", c + new Vector3(1.3f + (i % 2) * 1.0f, 1.01f, 1.98f + (i / 2) * 0.44f),
                    new Vector3(0.32f, 0.03f, 0.32f), new Color(0.35f, 0.14f, 0.12f));
            ZoneBuilder.Box(ctx, "RangeHood", c + new Vector3(1.8f, 2.3f, 2.5f), new Vector3(2.0f, 0.5f, 0.9f), Metal);
            ZoneBuilder.Decoration(ctx, "Sink", c + new Vector3(-1.8f, 0.95f, 2.2f), new Vector3(1.2f, 0.12f, 0.7f),
                new Color(0.80f, 0.82f, 0.84f));
            ZoneBuilder.Decoration(ctx, "SinkTap", c + new Vector3(-1.8f, 1.2f, 2.5f), new Vector3(0.08f, 0.4f, 0.08f), Metal);

            // 冰箱（补给）
            var fridge = ZoneBuilder.Box(ctx, "Fridge", c + new Vector3(4.4f, 1.15f, 2.0f), new Vector3(1.6f, 2.3f, 1.4f),
                new Color(0.86f, 0.88f, 0.90f));
            HomeFixture.Attach(fridge, HomeFixtureKind.Fridge);
            ZoneBuilder.Decoration(ctx, "FridgeSeam", c + new Vector3(4.4f, 1.15f, 1.28f), new Vector3(1.5f, 0.05f, 0.05f), Dark);

            // 中岛 + 吧椅
            ZoneBuilder.Box(ctx, "Island", c + new Vector3(1.2f, 0.45f, -2.6f), new Vector3(4.2f, 0.9f, 1.8f), Wood);
            ZoneBuilder.Decoration(ctx, "IslandTop", c + new Vector3(1.2f, 0.92f, -2.6f), new Vector3(4.4f, 0.08f, 2.0f), Dark);
            for (int i = -1; i <= 1; i++)
                BarStool(ctx, c + new Vector3(1.2f + i * 1.4f, 0, -4.0f));

            // 微波炉与几件小家电
            ZoneBuilder.Decoration(ctx, "Microwave", c + new Vector3(-4.1f, 1.2f, 0.6f), new Vector3(0.8f, 0.5f, 1.0f), Dark);
            ZoneBuilder.Decoration(ctx, "Kettle", c + new Vector3(-4.1f, 1.08f, -2.2f), new Vector3(0.35f, 0.35f, 0.35f), Metal);
        }

        static void BarStool(WorldContext ctx, Vector3 at)
        {
            ZoneBuilder.Box(ctx, "StoolSeat", at + new Vector3(0, 0.72f, 0), new Vector3(0.7f, 0.12f, 0.7f), Wood);
            ZoneBuilder.Decoration(ctx, "StoolPole", at + new Vector3(0, 0.36f, 0), new Vector3(0.12f, 0.72f, 0.12f), Metal);
            ZoneBuilder.Decoration(ctx, "StoolFoot", at + new Vector3(0, 0.04f, 0), new Vector3(0.6f, 0.08f, 0.6f), Metal);
        }

        // ================= 六人餐厅 =================

        static void Dining(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-4f, 0, -12.5f);   // 让开 x=0 那条进门通道
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.1f, 3.4f), "餐 厅");

            ZoneBuilder.Box(ctx, "DiningTable", c + new Vector3(0, 0.74f, 0), new Vector3(3.0f, 0.12f, 5.4f), Wood);
            ZoneBuilder.Box(ctx, "TableLeg", c + new Vector3(-1.2f, 0.37f, -2.2f), new Vector3(0.18f, 0.74f, 0.18f), Wood);
            ZoneBuilder.Box(ctx, "TableLeg", c + new Vector3(1.2f, 0.37f, -2.2f), new Vector3(0.18f, 0.74f, 0.18f), Wood);
            ZoneBuilder.Box(ctx, "TableLeg", c + new Vector3(-1.2f, 0.37f, 2.2f), new Vector3(0.18f, 0.74f, 0.18f), Wood);
            ZoneBuilder.Box(ctx, "TableLeg", c + new Vector3(1.2f, 0.37f, 2.2f), new Vector3(0.18f, 0.74f, 0.18f), Wood);

            // 六把椅子：两侧各三把，每个位置摆一套餐具
            for (int i = -1; i <= 1; i++)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 seat = c + new Vector3(s * 2.1f, 0, i * 1.8f);
                    ZoneBuilder.Box(ctx, "DiningChair", seat + new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.1f, 0.9f), Wood);
                    ZoneBuilder.Box(ctx, "ChairBack", seat + new Vector3(s * 0.4f, 0.9f, 0), new Vector3(0.12f, 0.9f, 0.9f), Wood);
                    ZoneBuilder.Decoration(ctx, "ChairLeg", seat + new Vector3(0, 0.22f, 0), new Vector3(0.7f, 0.45f, 0.7f), Wood);

                    Vector3 plate = c + new Vector3(s * 1.0f, 0.82f, i * 1.8f);
                    ZoneBuilder.Decoration(ctx, "Plate", plate, new Vector3(0.6f, 0.04f, 0.6f), new Color(0.96f, 0.96f, 0.97f));
                    ZoneBuilder.Decoration(ctx, "Glass", plate + new Vector3(s * -0.45f, 0.1f, 0.3f),
                        new Vector3(0.16f, 0.24f, 0.16f), new Color(0.82f, 0.9f, 0.95f));
                    ZoneBuilder.Decoration(ctx, "Cutlery", plate + new Vector3(s * 0.42f, 0, 0),
                        new Vector3(0.06f, 0.02f, 0.34f), Metal);
                }

            // 餐桌中央：一盘水果 + 烛台；头顶吊灯
            ZoneBuilder.Decoration(ctx, "FruitBowl", c + new Vector3(0, 0.86f, 0), new Vector3(0.7f, 0.16f, 0.7f),
                new Color(0.75f, 0.70f, 0.62f));
            ZoneBuilder.Decoration(ctx, "Fruit", c + new Vector3(0, 0.96f, 0), new Vector3(0.5f, 0.16f, 0.5f),
                new Color(0.85f, 0.42f, 0.25f));
            ZoneBuilder.Decoration(ctx, "PendantRod", c + new Vector3(0, 3.0f, 0), new Vector3(0.06f, 0.8f, 0.06f), Dark);
            ZoneBuilder.Decoration(ctx, "Pendant", c + new Vector3(0, 2.5f, 0), new Vector3(1.6f, 0.3f, 1.6f),
                new Color(0.98f, 0.90f, 0.72f));
        }

        // ================= 健身房 =================

        static void Gym(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(15f, 0, -12f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 3.1f, 3.4f), "健 身 房");
            ZoneBuilder.Decoration(ctx, "GymMat", c + new Vector3(0, 0.07f, 0), new Vector3(13f, 0.06f, 9f),
                new Color(0.22f, 0.24f, 0.26f));

            // 跑步机：底座 + 跑带 + 扶手 + 控制面板
            Vector3 tm = c + new Vector3(-4.2f, 0, 1.6f);
            ZoneBuilder.Box(ctx, "TreadBase", tm + new Vector3(0, 0.18f, 0), new Vector3(1.5f, 0.36f, 3.4f), Dark);
            ZoneBuilder.Decoration(ctx, "TreadBelt", tm + new Vector3(0, 0.38f, -0.2f), new Vector3(1.2f, 0.06f, 2.8f),
                new Color(0.12f, 0.12f, 0.13f));
            ZoneBuilder.Box(ctx, "TreadArm", tm + new Vector3(-0.62f, 0.8f, 1.4f), new Vector3(0.1f, 1.3f, 0.1f), Metal);
            ZoneBuilder.Box(ctx, "TreadArm", tm + new Vector3(0.62f, 0.8f, 1.4f), new Vector3(0.1f, 1.3f, 0.1f), Metal);
            ZoneBuilder.Decoration(ctx, "TreadPanel", tm + new Vector3(0, 1.35f, 1.5f), new Vector3(1.3f, 0.5f, 0.12f),
                new Color(0.25f, 0.55f, 0.75f));
            ZoneBuilder.Decoration(ctx, "TreadRail", tm + new Vector3(0, 1.3f, 1.05f), new Vector3(1.4f, 0.08f, 0.6f), Metal);

            // 哑铃架
            ZoneBuilder.Box(ctx, "RackBase", c + new Vector3(4.6f, 0.35f, 3.0f), new Vector3(3.6f, 0.7f, 0.8f), Dark);
            for (int i = 0; i < 5; i++)
            {
                float x = c.x + 3.2f + i * 0.7f;
                ZoneBuilder.Decoration(ctx, "DumbbellBar", new Vector3(x, 0.78f, c.z + 3.0f), new Vector3(0.12f, 0.12f, 0.5f), Metal);
                ZoneBuilder.Decoration(ctx, "DumbbellPlate", new Vector3(x, 0.78f, c.z + 2.78f), new Vector3(0.34f, 0.34f, 0.12f), Dark);
                ZoneBuilder.Decoration(ctx, "DumbbellPlate", new Vector3(x, 0.78f, c.z + 3.22f), new Vector3(0.34f, 0.34f, 0.12f), Dark);
            }

            // 卧推凳 + 杠铃
            ZoneBuilder.Box(ctx, "Bench", c + new Vector3(1.0f, 0.45f, -1.6f), new Vector3(0.7f, 0.16f, 2.4f),
                new Color(0.30f, 0.16f, 0.16f));
            ZoneBuilder.Box(ctx, "BenchLeg", c + new Vector3(1.0f, 0.2f, -1.6f), new Vector3(0.5f, 0.5f, 2.0f), Dark);
            ZoneBuilder.Box(ctx, "RackPost", c + new Vector3(0.2f, 0.7f, -0.4f), new Vector3(0.12f, 1.4f, 0.12f), Metal);
            ZoneBuilder.Box(ctx, "RackPost", c + new Vector3(1.8f, 0.7f, -0.4f), new Vector3(0.12f, 1.4f, 0.12f), Metal);
            ZoneBuilder.Decoration(ctx, "Barbell", c + new Vector3(1.0f, 1.42f, -0.4f), new Vector3(2.6f, 0.09f, 0.09f), Metal);
            ZoneBuilder.Decoration(ctx, "BarPlate", c + new Vector3(-0.2f, 1.42f, -0.4f), new Vector3(0.12f, 0.6f, 0.6f), Dark);
            ZoneBuilder.Decoration(ctx, "BarPlate", c + new Vector3(2.2f, 1.42f, -0.4f), new Vector3(0.12f, 0.6f, 0.6f), Dark);

            // 动感单车 + 瑜伽垫 + 落地镜（健身房必有的一面镜子）
            ZoneBuilder.Box(ctx, "BikeBody", c + new Vector3(-4.6f, 0.5f, -2.6f), new Vector3(0.6f, 1.0f, 1.8f), Dark);
            ZoneBuilder.Decoration(ctx, "BikeSeat", c + new Vector3(-4.6f, 1.05f, -3.0f), new Vector3(0.4f, 0.14f, 0.7f), Metal);
            ZoneBuilder.Decoration(ctx, "BikeBar", c + new Vector3(-4.6f, 1.2f, -1.9f), new Vector3(0.8f, 0.1f, 0.1f), Metal);
            ZoneBuilder.Decoration(ctx, "BikeWheel", c + new Vector3(-4.6f, 0.35f, -1.7f), new Vector3(0.14f, 0.7f, 0.7f), Metal);
            ZoneBuilder.Decoration(ctx, "YogaMat", c + new Vector3(4.4f, 0.09f, -2.6f), new Vector3(1.2f, 0.05f, 2.6f),
                new Color(0.35f, 0.62f, 0.55f));
            ZoneBuilder.Decoration(ctx, "GymMirror", c + new Vector3(0, 1.7f, 4.3f), new Vector3(10f, 2.6f, 0.1f),
                new Color(0.78f, 0.86f, 0.92f));

            AirConditioner(ctx, c + new Vector3(-5.5f, 2.7f, -4.2f));
        }

        // ================= 室外：泳池 =================

        static void Pool(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(W / 2f + 16f, 0, 2f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 2.6f, 9.5f), "泳 池");

            // 池台 + 下沉水面 + 池边压顶
            ZoneBuilder.Decoration(ctx, "PoolDeck", c + new Vector3(0, 0.05f, 0), new Vector3(20f, 0.1f, 18f),
                new Color(0.76f, 0.74f, 0.70f));
            ZoneBuilder.Decoration(ctx, "PoolWater", c + new Vector3(0, 0.08f, 0), new Vector3(12f, 0.06f, 8f), Water);
            ZoneBuilder.Decoration(ctx, "PoolLine", c + new Vector3(0, 0.1f, 0), new Vector3(11.6f, 0.02f, 0.16f),
                new Color(0.92f, 0.95f, 0.98f));
            // 池沿：四条压顶石，走上去有实体
            ZoneBuilder.Box(ctx, "PoolCurb", c + new Vector3(0, 0.14f, 4.3f), new Vector3(13f, 0.28f, 0.6f), FloorTile);
            ZoneBuilder.Box(ctx, "PoolCurb", c + new Vector3(0, 0.14f, -4.3f), new Vector3(13f, 0.28f, 0.6f), FloorTile);
            ZoneBuilder.Box(ctx, "PoolCurb", c + new Vector3(6.3f, 0.14f, 0), new Vector3(0.6f, 0.28f, 9.2f), FloorTile);
            ZoneBuilder.Box(ctx, "PoolCurb", c + new Vector3(-6.3f, 0.14f, 0), new Vector3(0.6f, 0.28f, 9.2f), FloorTile);

            // 泳池梯、躺椅、遮阳伞
            ZoneBuilder.Decoration(ctx, "PoolLadder", c + new Vector3(5.4f, 0.6f, 4.6f), new Vector3(0.08f, 1.2f, 0.6f), Metal);
            for (int i = -1; i <= 1; i += 2)
            {
                ZoneBuilder.Box(ctx, "Lounger", c + new Vector3(i * 8.4f, 0.35f, 1.4f), new Vector3(1.0f, 0.2f, 2.4f),
                    new Color(0.90f, 0.88f, 0.82f));
                ZoneBuilder.Decoration(ctx, "LoungerBack", c + new Vector3(i * 8.4f, 0.62f, 2.5f), new Vector3(1.0f, 0.6f, 0.14f),
                    new Color(0.90f, 0.88f, 0.82f));
            }
            ZoneBuilder.Box(ctx, "UmbrellaPole", c + new Vector3(8.4f, 1.2f, -1.2f), new Vector3(0.12f, 2.4f, 0.12f), Metal);
            ZoneBuilder.Decoration(ctx, "Umbrella", c + new Vector3(8.4f, 2.5f, -1.2f), new Vector3(3.4f, 0.2f, 3.4f),
                new Color(0.85f, 0.40f, 0.32f));

            // 水下灯：夜里泳池是整块宅子最好看的地方
            ZoneBuilder.AddCeilingLight(c + new Vector3(-3f, 0.5f, 0), new Color(0.45f, 0.85f, 1f), 14f);
            ZoneBuilder.AddCeilingLight(c + new Vector3(3f, 0.5f, 0), new Color(0.45f, 0.85f, 1f), 14f);
        }

        // ================= 室外：车库与车 =================

        static void Garage(WorldContext ctx, Vector3 h)
        {
            // 车库贴着东侧院墙，门朝南开出一条车道通向街面（不占人行道）
            Vector3 c = h + new Vector3(W / 2f + 12f, 0, -D / 2f + 4f);
            OpenWorldBuilder.HomeSign(c + new Vector3(0, 4.2f, -5.2f), "车 库");
            ZoneBuilder.Decoration(ctx, "Driveway", c + new Vector3(0, 0.06f, -10f),
                new Vector3(7f, 0.06f, 12f), new Color(0.48f, 0.48f, 0.50f));

            ZoneBuilder.Decoration(ctx, "GarageFloor", c + new Vector3(0, 0.06f, 0), new Vector3(12f, 0.08f, 9f),
                new Color(0.42f, 0.42f, 0.44f));
            ZoneBuilder.Box(ctx, "GarageWall", c + new Vector3(0, 1.8f, 4.5f), new Vector3(12f, 3.6f, 0.3f), WallWarm);
            ZoneBuilder.Box(ctx, "GarageWall", c + new Vector3(-6f, 1.8f, 0), new Vector3(0.3f, 3.6f, 9f), WallWarm);
            ZoneBuilder.Box(ctx, "GarageWall", c + new Vector3(6f, 1.8f, 0), new Vector3(0.3f, 3.6f, 9f), WallWarm);
            ZoneBuilder.Box(ctx, "GarageRoof", c + new Vector3(0, 3.7f, 0), new Vector3(12.6f, 0.3f, 9.6f), WallWarm);
            // 卷帘门：抬起停在门楣上（车开得出去）
            ZoneBuilder.Decoration(ctx, "GarageDoor", c + new Vector3(0, 3.25f, -4.4f), new Vector3(11.4f, 0.8f, 0.2f), Metal);

            Car(ctx, c + new Vector3(0, 0, 0.4f));

            // 工作台与工具墙
            ZoneBuilder.Box(ctx, "Workbench", c + new Vector3(4.6f, 0.45f, 3.4f), new Vector3(2.2f, 0.9f, 1.0f), Wood);
            for (int i = 0; i < 5; i++)
                ZoneBuilder.Decoration(ctx, "Tool", c + new Vector3(3.7f + i * 0.45f, 1.9f, 4.2f),
                    new Vector3(0.12f, 0.6f, 0.1f), i % 2 == 0 ? Metal : new Color(0.8f, 0.5f, 0.2f));
            ZoneBuilder.AddCeilingLight(c + new Vector3(0, 3.2f, 0), new Color(1f, 0.96f, 0.88f), 16f);
        }

        static void Car(WorldContext ctx, Vector3 at)
        {
            var body = new Color(0.18f, 0.24f, 0.34f);
            ZoneBuilder.Box(ctx, "CarBody", at + new Vector3(0, 0.72f, 0), new Vector3(2.0f, 0.86f, 4.6f), body);
            ZoneBuilder.Box(ctx, "CarCabin", at + new Vector3(0, 1.38f, -0.2f), new Vector3(1.8f, 0.66f, 2.4f),
                new Color(0.14f, 0.18f, 0.26f));
            ZoneBuilder.Decoration(ctx, "Windshield", at + new Vector3(0, 1.38f, 1.05f), new Vector3(1.7f, 0.6f, 0.1f),
                new Color(0.55f, 0.72f, 0.82f));
            ZoneBuilder.Decoration(ctx, "RearGlass", at + new Vector3(0, 1.38f, -1.45f), new Vector3(1.7f, 0.6f, 0.1f),
                new Color(0.55f, 0.72f, 0.82f));
            ZoneBuilder.Decoration(ctx, "SideGlass", at + new Vector3(0.92f, 1.38f, -0.2f), new Vector3(0.06f, 0.5f, 2.2f),
                new Color(0.55f, 0.72f, 0.82f));
            ZoneBuilder.Decoration(ctx, "SideGlass", at + new Vector3(-0.92f, 1.38f, -0.2f), new Vector3(0.06f, 0.5f, 2.2f),
                new Color(0.55f, 0.72f, 0.82f));
            ZoneBuilder.Decoration(ctx, "Headlight", at + new Vector3(0.65f, 0.8f, 2.32f), new Vector3(0.5f, 0.25f, 0.08f),
                new Color(0.95f, 0.95f, 0.85f));
            ZoneBuilder.Decoration(ctx, "Headlight", at + new Vector3(-0.65f, 0.8f, 2.32f), new Vector3(0.5f, 0.25f, 0.08f),
                new Color(0.95f, 0.95f, 0.85f));
            ZoneBuilder.Decoration(ctx, "Taillight", at + new Vector3(0.65f, 0.85f, -2.32f), new Vector3(0.45f, 0.2f, 0.08f),
                new Color(0.75f, 0.15f, 0.12f));
            ZoneBuilder.Decoration(ctx, "Taillight", at + new Vector3(-0.65f, 0.85f, -2.32f), new Vector3(0.45f, 0.2f, 0.08f),
                new Color(0.75f, 0.15f, 0.12f));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    ZoneBuilder.Decoration(ctx, "Wheel", at + new Vector3(sx * 0.95f, 0.36f, sz * 1.5f),
                        new Vector3(0.24f, 0.72f, 0.72f), new Color(0.10f, 0.10f, 0.11f));
        }

        // ================= 宠物猫 =================

        static void Cat(WorldContext ctx, Vector3 h)
        {
            Vector3 c = h + new Vector3(-3.5f, 0, -3f);

            // 猫窝与食盆（喂食就在这里按 E）
            ZoneBuilder.Decoration(ctx, "CatBed", c + new Vector3(0, 0.1f, 0), new Vector3(1.4f, 0.18f, 1.4f),
                new Color(0.62f, 0.48f, 0.40f));
            var bowl = ZoneBuilder.Box(ctx, "CatBowl", c + new Vector3(1.6f, 0.09f, 0), new Vector3(0.5f, 0.18f, 0.5f),
                new Color(0.85f, 0.72f, 0.35f));

            var cat = PetCat.Create(ctx, c + new Vector3(0, 0, 0), h);
            bowl.AddComponent<CatFeeder>().cat = cat;
        }

        // ================= 通用小件 =================

        static void Plant(WorldContext ctx, Vector3 at)
        {
            ZoneBuilder.Box(ctx, "PlantPot", at + new Vector3(0, 0.28f, 0), new Vector3(0.7f, 0.56f, 0.7f),
                new Color(0.52f, 0.36f, 0.28f));
            ZoneBuilder.Decoration(ctx, "PlantLeaf", at + new Vector3(0, 1.0f, 0), new Vector3(1.1f, 1.2f, 1.1f),
                new Color(0.22f, 0.46f, 0.26f));
            ZoneBuilder.Decoration(ctx, "PlantLeaf", at + new Vector3(0.25f, 1.5f, -0.15f), new Vector3(0.7f, 0.8f, 0.7f),
                new Color(0.26f, 0.52f, 0.30f));
        }

        static void FloorLamp(WorldContext ctx, Vector3 at)
        {
            ZoneBuilder.Decoration(ctx, "LampBase", at + new Vector3(0, 0.05f, 0), new Vector3(0.5f, 0.1f, 0.5f), Dark);
            ZoneBuilder.Decoration(ctx, "LampPole", at + new Vector3(0, 0.85f, 0), new Vector3(0.08f, 1.7f, 0.08f), Metal);
            ZoneBuilder.Decoration(ctx, "LampShade", at + new Vector3(0, 1.85f, 0), new Vector3(0.7f, 0.5f, 0.7f),
                new Color(0.98f, 0.92f, 0.78f));
            ZoneBuilder.AddCeilingLight(at + new Vector3(0, 1.8f, 0), new Color(1f, 0.93f, 0.78f), 9f);
        }

        /// <summary>挂式空调：出风口带一条蓝色导风板，一眼认得出不是柜子。</summary>
        static void AirConditioner(WorldContext ctx, Vector3 at)
        {
            ZoneBuilder.Decoration(ctx, "AC", at, new Vector3(2.0f, 0.6f, 0.5f), new Color(0.96f, 0.96f, 0.97f));
            ZoneBuilder.Decoration(ctx, "AC_Vent", at + new Vector3(0, -0.22f, -0.16f), new Vector3(1.8f, 0.12f, 0.3f),
                new Color(0.55f, 0.78f, 0.92f));
            ZoneBuilder.Decoration(ctx, "AC_Led", at + new Vector3(0.7f, 0.1f, -0.26f), new Vector3(0.16f, 0.08f, 0.05f),
                new Color(0.3f, 0.95f, 0.5f));
        }

        /// <summary>吊扇：三片扇叶挂在 SpinY 上，慢慢转——静止的扇叶看着像装饰板。</summary>
        static void CeilingFan(WorldContext ctx, Vector3 at)
        {
            ZoneBuilder.Decoration(ctx, "FanRod", at + new Vector3(0, 0.25f, 0), new Vector3(0.08f, 0.5f, 0.08f), Metal);
            var hub = ZoneBuilder.Decoration(ctx, "FanHub", at, new Vector3(0.4f, 0.18f, 0.4f), Metal);
            for (int i = 0; i < 3; i++)
            {
                var blade = ZoneBuilder.Decoration(ctx, "FanBlade", at, new Vector3(2.4f, 0.06f, 0.4f), Wood);
                blade.transform.SetParent(hub.transform, true);
                blade.transform.localRotation = Quaternion.Euler(0, i * 120f, 0);
            }
            hub.AddComponent<SpinY>().speed = 90f;
        }

        /// <summary>
        /// 一幅可以换成玩家自己图片的画/照片。
        /// 先用占位色显示；UserImageLibrary 找到对应图片时会替换贴图。
        /// </summary>
        static void UserPicture(WorldContext ctx, Vector3 at, Vector3 size, UserImageSlot slot, string sign)
        {
            var go = ZoneBuilder.Decoration(ctx, "UserPicture_" + slot, at, size, new Color(0.62f, 0.60f, 0.66f));
            go.AddComponent<UserPictureFrame>().slot = slot;
            if (!string.IsNullOrEmpty(sign))
                OpenWorldBuilder.HomeSign(at + new Vector3(0, size.y / 2f + 0.5f, 0), sign);
        }

        static void Lights(Vector3 h)
        {
            ZoneBuilder.AddCeilingLight(h + new Vector3(0, 3.2f, -6f), new Color(1f, 0.92f, 0.76f), 24f);   // 客厅
            ZoneBuilder.AddCeilingLight(h + new Vector3(-15f, 3.2f, 10f), new Color(1f, 0.88f, 0.70f), 18f); // 卧室
            ZoneBuilder.AddCeilingLight(h + new Vector3(15f, 3.2f, 10f), new Color(0.94f, 0.96f, 1f), 18f);  // 办公室
            ZoneBuilder.AddCeilingLight(h + new Vector3(15f, 3.2f, -2f), new Color(1f, 0.94f, 0.82f), 16f);  // 书房
            ZoneBuilder.AddCeilingLight(h + new Vector3(-17f, 3.2f, -2f), new Color(0.92f, 0.96f, 1f), 14f); // 卫生间
            ZoneBuilder.AddCeilingLight(h + new Vector3(-16f, 3.2f, -12f), new Color(1f, 0.95f, 0.84f), 18f);// 厨房
            ZoneBuilder.AddCeilingLight(h + new Vector3(-4f, 3.2f, -12.5f), new Color(1f, 0.90f, 0.74f), 16f); // 餐厅
            ZoneBuilder.AddCeilingLight(h + new Vector3(15f, 3.2f, -12f), new Color(0.94f, 0.97f, 1f), 20f); // 健身房
        }
    }

    /// <summary>绕 Y 轴匀速转（吊扇）。</summary>
    public class SpinY : MonoBehaviour
    {
        public float speed = 60f;
        void Update() => transform.Rotate(0f, speed * Time.deltaTime, 0f, Space.Self);
    }
}
