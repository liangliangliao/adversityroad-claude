using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Combat;
using AdversityRoad.Goals;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 玩家住宅 V3 3D 装修模型。
    /// 只由 OpenWorldBuilder.BuildPlayerHome 显式调用，不使用 RuntimeInitializeOnLoadMethod。
    /// 目标：把住宅从简易安全屋升级为可步入的完整生活基地。
    /// </summary>
    public static class PlayerHome3DModel
    {
        static readonly Color Wall = new Color(0.78f, 0.76f, 0.71f);
        static readonly Color Floor = new Color(0.42f, 0.29f, 0.19f);
        static readonly Color Wood = new Color(0.30f, 0.20f, 0.13f);
        static readonly Color Wood2 = new Color(0.48f, 0.32f, 0.20f);
        static readonly Color Fabric = new Color(0.35f, 0.43f, 0.56f);
        static readonly Color White = new Color(0.93f, 0.93f, 0.90f);
        static readonly Color Metal = new Color(0.55f, 0.58f, 0.62f);
        static readonly Color Glass = new Color(0.32f, 0.68f, 0.82f);
        static readonly Color Green = new Color(0.25f, 0.48f, 0.30f);
        static readonly Color Pool = new Color(0.10f, 0.55f, 0.78f);

        public static void Build(WorldContext ctx, Vector3 h)
        {
            var root = new GameObject("PlayerHome_3D_Complete");
            root.transform.position = h;

            const float W = 30f;
            const float D = 22f;
            const float H = 3.4f;

            ZoneBuilder.Box(ctx, "Home_Floor", h + new Vector3(0, -0.05f, 0), new Vector3(W, 0.2f, D), Floor);

            // 外墙：南侧中央保留 6m 入户门洞。
            WallBox(ctx, h + new Vector3(0, H / 2f, D / 2f), new Vector3(W, H, 0.35f), Wall);
            WallBox(ctx, h + new Vector3(-W / 2f, H / 2f, 0), new Vector3(0.35f, H, D), Wall);
            WallBox(ctx, h + new Vector3(W / 2f, H / 2f, 0), new Vector3(0.35f, H, D), Wall);
            WallBox(ctx, h + new Vector3(-10.5f, H / 2f, -D / 2f), new Vector3(9f, H, 0.35f), Wall);
            WallBox(ctx, h + new Vector3(10.5f, H / 2f, -D / 2f), new Vector3(9f, H, 0.35f), Wall);
            WallBox(ctx, h + new Vector3(0, 3.05f, -D / 2f), new Vector3(6f, 0.7f, 0.35f), Wall);

            // 开放式大客餐厅 + 独立卧室/书房/办公室/卫生间/厨房。
            WallBox(ctx, h + new Vector3(-6.0f, H / 2f, 4.8f), new Vector3(0.25f, H, 12.4f), Wall);
            WallBox(ctx, h + new Vector3(3.8f, H / 2f, 7.5f), new Vector3(0.25f, H, 7.0f), Wall);
            WallBox(ctx, h + new Vector3(9.0f, H / 2f, 1.5f), new Vector3(0.25f, H, 8.0f), Wall);
            WallBox(ctx, h + new Vector3(-10.5f, H / 2f, -5.7f), new Vector3(9f, H, 0.25f), Wall);
            WallBox(ctx, h + new Vector3(9.0f, H / 2f, -7.3f), new Vector3(0.25f, H, 4.8f), Wall);

            BuildLivingAndDining(ctx, root, h);
            BuildBedroom(ctx, root, h);
            BuildStudy(ctx, root, h);
            BuildOffice(ctx, root, h);
            BuildBathroom(ctx, root, h);
            BuildKitchen(ctx, root, h);
            BuildGym(ctx, root, h);
            BuildPool(ctx, root, h);
            BuildGarage(ctx, root, h);
            BuildCat(ctx, root, h);
            BuildGarden(ctx, root, h);
            BuildGoalBoard(ctx, root, h);

            ZoneBuilder.AddCeilingLight(h + new Vector3(0, 3.0f, -1), new Color(1f, 0.90f, 0.72f), 25f);
            ZoneBuilder.AddCeilingLight(h + new Vector3(-10, 3.0f, 6), new Color(0.88f, 0.93f, 1f), 12f);
            ZoneBuilder.AddCeilingLight(h + new Vector3(10, 3.0f, 5), new Color(0.88f, 0.93f, 1f), 12f);
            ZoneBuilder.AddCeilingLight(h + new Vector3(10, 3.0f, -8), new Color(1f, 0.88f, 0.70f), 14f);

            // 入口道路保持与原开放城区连通。
            ZoneBuilder.Decoration(ctx, "Path", h + new Vector3(0, 0.06f, -18), new Vector3(6, 0.06f, 14), new Color(0.55f, 0.55f, 0.57f));
            HomeSign(h + new Vector3(0, 3.9f, -11.4f), "我 的 住 处  ·  LIFE BASE");

            OpenWorldBuilder.HomeInteriorSpawn = h + new Vector3(0, 1.1f, -7);
            OpenWorldBuilder.HomeDoorSpawn = h + new Vector3(0, 1.1f, -15);
        }

        static void BuildLivingAndDining(WorldContext ctx, GameObject root, Vector3 h)
        {
            // 客厅：沙发、茶几、电视墙、艺术画。
            Box(ctx, root, "Living_Sofa", h + new Vector3(-0.5f, 0.55f, -2.0f), new Vector3(5.8f, 1.1f, 1.8f), new Color(0.38f, 0.40f, 0.46f));
            Box(ctx, root, "Living_Table", h + new Vector3(-0.5f, 0.32f, 0.6f), new Vector3(3.4f, 0.6f, 1.4f), Wood2);
            Box(ctx, root, "TV_Wall", h + new Vector3(-0.5f, 1.55f, 9.0f), new Vector3(5.2f, 2.7f, 0.16f), new Color(0.08f, 0.10f, 0.13f));
            Painting(ctx, root, h + new Vector3(5.4f, 2.15f, 10.72f), new Vector3(2.8f, 1.6f, 0.10f), new Color(0.20f, 0.43f, 0.52f));

            // 六人餐厅：明确 6 个座位 + 餐具。
            Box(ctx, root, "Dining_Table", h + new Vector3(5.1f, 0.55f, -1.4f), new Vector3(5.8f, 1.1f, 2.4f), Wood2);
            Vector3[] seats =
            {
                new Vector3(2.1f,0.35f,-1.4f), new Vector3(8.1f,0.35f,-1.4f),
                new Vector3(3.1f,0.35f,-3.2f), new Vector3(7.1f,0.35f,-3.2f),
                new Vector3(3.1f,0.35f,0.4f), new Vector3(7.1f,0.35f,0.4f)
            };
            for (int i = 0; i < seats.Length; i++) Box(ctx, root, "Dining_Chair_" + (i + 1), h + seats[i], new Vector3(1.0f,0.7f,1.0f), Fabric);
            for (int i = 0; i < 6; i++) Box(ctx, root, "PlaceSetting_" + (i + 1), h + new Vector3(3.1f + (i % 2) * 4.0f, 1.14f, -2.5f + (i / 2) * 1.0f), new Vector3(0.38f,0.04f,0.38f), White);
        }

        static void BuildBedroom(WorldContext ctx, GameObject root, Vector3 h)
        {
            Vector3 c = h + new Vector3(-10.8f, 0, 5.4f);
            // 床：床架 + 床垫 + 床单 + 被子 + 双枕头。
            Box(ctx, root, "Bed_Frame", c + new Vector3(0,0.35f,0.4f), new Vector3(5.0f,0.7f,6.6f), Wood);
            Box(ctx, root, "Bed_Mattress", c + new Vector3(0,0.82f,0.4f), new Vector3(4.7f,0.28f,6.25f), White);
            Box(ctx, root, "Bed_Sheet", c + new Vector3(0,1.00f,-0.1f), new Vector3(4.55f,0.08f,4.7f), new Color(0.78f,0.84f,0.92f));
            Box(ctx, root, "Bed_Blanket", c + new Vector3(0,1.10f,-1.25f), new Vector3(4.55f,0.14f,3.15f), Fabric);
            Box(ctx, root, "Pillow_Left", c + new Vector3(-1.2f,1.15f,3.05f), new Vector3(2.0f,0.26f,1.0f), White);
            Box(ctx, root, "Pillow_Right", c + new Vector3(1.2f,1.15f,3.05f), new Vector3(2.0f,0.26f,1.0f), White);
            Box(ctx, root, "Bedside_Left", c + new Vector3(-3.8f,0.55f,2.3f), new Vector3(1.2f,1.1f,1.2f), Wood2);
            Box(ctx, root, "Bedside_Right", c + new Vector3(3.8f,0.55f,2.3f), new Vector3(1.2f,1.1f,1.2f), Wood2);
            Painting(ctx, root, c + new Vector3(-4.2f,2.2f,5.2f), new Vector3(2.2f,1.5f,0.10f), new Color(0.22f,0.42f,0.54f));
            Painting(ctx, root, c + new Vector3(0f,2.25f,5.2f), new Vector3(2.4f,1.7f,0.10f), new Color(0.68f,0.38f,0.28f));
            Painting(ctx, root, c + new Vector3(4.2f,2.2f,5.2f), new Vector3(2.2f,1.5f,0.10f), new Color(0.35f,0.55f,0.34f));
            HomeSign(c + new Vector3(0,3.7f,5.0f), "卧 室  ·  REST");
        }

        static void BuildStudy(WorldContext ctx, GameObject root, Vector3 h)
        {
            Vector3 c = h + new Vector3(-1.6f,0,8.1f);
            Box(ctx, root, "Study_Desk", c + new Vector3(0,0.55f,-1.4f), new Vector3(4.8f,1.1f,1.6f), Wood2);
            Box(ctx, root, "Study_Chair", c + new Vector3(0,0.4f,-3.0f), new Vector3(1.2f,0.8f,1.2f), Fabric);
            for (int side = -1; side <= 1; side += 2)
            {
                Box(ctx, root, "Bookcase_" + side, c + new Vector3(side * 2.8f,1.5f,2.0f), new Vector3(0.7f,3.0f,4.6f), Wood);
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 5; col++)
                        Box(ctx, root, "Book", c + new Vector3(side * 2.55f + side * col * 0.08f, 0.45f + row * 0.58f, 0.2f + col * 0.72f), new Vector3(0.32f,0.42f,0.55f), new Color(0.20f + row*0.08f,0.30f + col*0.04f,0.38f));
            }
            HomeSign(c + new Vector3(0,3.6f,4.1f), "书 房  ·  LEARN");
        }

        static void BuildOffice(WorldContext ctx, GameObject root, Vector3 h)
        {
            Vector3 c = h + new Vector3(11.2f,0,5.1f);
            var desk = Box(ctx, root, "Office_Desk", c + new Vector3(0,0.55f,-1.0f), new Vector3(5.0f,1.1f,1.8f), Wood2);
            HomeFixture.Attach(desk, HomeFixtureKind.Desk);
            Box(ctx, root, "Office_Chair", c + new Vector3(0,0.45f,-2.7f), new Vector3(1.4f,0.9f,1.3f), Fabric);
            Box(ctx, root, "Monitor_Left", c + new Vector3(-1.2f,1.55f,-0.2f), new Vector3(1.9f,1.2f,0.12f), new Color(0.08f,0.18f,0.25f));
            Box(ctx, root, "Monitor_Right", c + new Vector3(1.2f,1.55f,-0.2f), new Vector3(1.9f,1.2f,0.12f), new Color(0.08f,0.18f,0.25f));
            Box(ctx, root, "Keyboard", c + new Vector3(0,1.12f,-0.8f), new Vector3(1.9f,0.08f,0.55f), Metal);
            Box(ctx, root, "Office_Lamp", c + new Vector3(2.0f,1.8f,-0.9f), new Vector3(0.18f,1.0f,0.18f), Metal);
            Painting(ctx, root, c + new Vector3(0,2.2f,2.9f), new Vector3(3.2f,1.8f,0.10f), new Color(0.55f,0.35f,0.25f));
            HomeSign(c + new Vector3(0,3.5f,3.0f), "办 公 室  ·  WORK");
        }

        static void BuildBathroom(WorldContext ctx, GameObject root, Vector3 h)
        {
            Vector3 c = h + new Vector3(-10.8f,0,-7.8f);
            Box(ctx, root, "Bathroom_Floor", c, new Vector3(8.5f,0.12f,5.0f), new Color(0.62f,0.65f,0.67f));
            Box(ctx, root, "Bathroom_Sink", c + new Vector3(-2.5f,0.5f,0), new Vector3(1.6f,1.0f,1.2f), White);
            var mirror = Box(ctx, root, "Bathroom_Mirror", c + new Vector3(-2.5f,2.0f,1.0f), new Vector3(0.15f,1.6f,1.8f), new Color(0.65f,0.82f,0.90f));
            HomeFixture.Attach(mirror, HomeFixtureKind.Mirror);
            Box(ctx, root, "Bathroom_Toilet", c + new Vector3(0.2f,0.45f,0), new Vector3(1.5f,0.9f,1.9f), White);
            Box(ctx, root, "Bathroom_Shower", c + new Vector3(2.5f,1.2f,0), new Vector3(2.0f,2.4f,2.2f), new Color(0.72f,0.78f,0.80f));
            Box(ctx, root, "Shower_Glass", c + new Vector3(1.5f,1.4f,0), new Vector3(0.08f,2.8f,2.4f), Glass);
            HomeSign(c + new Vector3(0,3.2f,1.9f), "洗 手 间  ·  BATH");
        }

        static void BuildKitchen(WorldContext ctx, GameObject root, Vector3 h)
        {
            Vector3 c = h + new Vector3(10.8f,0,-8.0f);
            Box(ctx, root, "Kitchen_Counter_Back", c + new Vector3(0,0.6f,2.0f), new Vector3(7.0f,1.2f,1.2f), Wood2);
            Box(ctx, root, "Kitchen_Island", c + new Vector3(0,0.65f,-0.4f), new Vector3(5.0f,1.3f,1.8f), Wood2);
            Box(ctx, root, "Kitchen_Stove", c + new Vector3(-1.9f,1.28f,1.45f), new Vector3(1.8f,0.12f,0.9f), Metal);
            Box(ctx, root, "Kitchen_Sink", c + new Vector3(1.7f,1.2f,1.45f), new Vector3(1.4f,0.16f,0.9f), White);
            var fridge = Box(ctx, root, "Kitchen_Fridge", c + new Vector3(3.4f,1.2f,2.0f), new Vector3(1.6f,2.4f,1.5f), Metal);
            HomeFixture.Attach(fridge, HomeFixtureKind.Fridge);
            Box(ctx, root, "Kitchen_Oven", c + new Vector3(-3.0f,1.1f,2.0f), new Vector3(1.4f,2.2f,1.4f), Metal);
            Box(ctx, root, "Kitchen_Cabinet_1", c + new Vector3(-1.2f,2.1f,2.0f), new Vector3(1.4f,1.2f,1.2f), Wood);
            Box(ctx, root, "Kitchen_Cabinet_2", c + new Vector3(0.3f,2.1f,2.0f), new Vector3(1.4f,1.2f,1.2f), Wood);
            HomeSign(c + new Vector3(0,3.3f,2.6f), "厨 房  ·  HOME COOK");
        }

        static void BuildGym(WorldContext ctx, GameObject root, Vector3 h)
        {
            Vector3 c = h + new Vector3(8.0f,0,13.0f);
            Box(ctx, root, "Gym_Floor", c, new Vector3(12f,0.12f,6f), new Color(0.18f,0.20f,0.22f));
            Box(ctx, root, "Gym_BackWall", c + new Vector3(0,1.6f,3), new Vector3(12f,3.2f,0.25f), Wall);
            // 跑步机
            Box(ctx, root, "Treadmill_Base", c + new Vector3(-3.2f,0.55f,0), new Vector3(3.2f,0.45f,1.5f), Metal);
            Box(ctx, root, "Treadmill_Belt", c + new Vector3(-3.2f,0.83f,0), new Vector3(2.7f,0.08f,1.2f), new Color(0.05f,0.05f,0.06f));
            Box(ctx, root, "Treadmill_Console", c + new Vector3(-3.2f,1.7f,0.5f), new Vector3(1.1f,0.8f,0.35f), Metal);
            Box(ctx, root, "Treadmill_Handle", c + new Vector3(-3.2f,1.35f,1.0f), new Vector3(0.18f,1.0f,0.18f), Metal);
            // 哑铃架 + 哑铃
            Box(ctx, root, "Dumbbell_Rack", c + new Vector3(0.2f,0.7f,1.7f), new Vector3(4.2f,1.0f,0.8f), Metal);
            for (int i = 0; i < 6; i++)
            {
                Box(ctx, root, "Dumbbell_" + i, c + new Vector3(-1.4f + i * 0.55f,1.35f,1.7f), new Vector3(0.35f,0.55f,0.8f), Metal);
            }
            // 深蹲架与杠铃
            Box(ctx, root, "Squat_Rack_Left", c + new Vector3(3.8f,1.5f,-1.5f), new Vector3(0.35f,3.0f,0.35f), Metal);
            Box(ctx, root, "Squat_Rack_Right", c + new Vector3(5.6f,1.5f,-1.5f), new Vector3(0.35f,3.0f,0.35f), Metal);
            Box(ctx, root, "Barbell", c + new Vector3(4.7f,2.6f,-1.5f), new Vector3(2.5f,0.18f,0.18f), Metal);
            Painting(ctx, root, c + new Vector3(0,2.3f,2.82f), new Vector3(4.5f,1.7f,0.08f), new Color(0.20f,0.22f,0.25f));
            HomeSign(c + new Vector3(0,3.4f,2.85f), "健 身 房  ·  TRAIN");
        }

        static void BuildPool(WorldContext ctx, GameObject root, Vector3 h)
        {
            Vector3 c = h + new Vector3(6.5f,0,18.2f);
            Box(ctx, root, "Pool_Deck", c, new Vector3(15f,0.10f,8f), new Color(0.62f,0.56f,0.48f));
            Box(ctx, root, "Pool_Water", c + new Vector3(0,0.12f,0), new Vector3(11.5f,0.18f,5.2f), Pool);
            Box(ctx, root, "Pool_Edge", c + new Vector3(-6.0f,0.25f,0), new Vector3(0.35f,0.4f,6.0f), White);
            Box(ctx, root, "Pool_Ladder", c + new Vector3(5.2f,0.7f,0), new Vector3(0.25f,1.4f,1.8f), Metal);
            Box(ctx, root, "Pool_Lounger_1", c + new Vector3(-5.0f,0.35f,4.0f), new Vector3(2.5f,0.35f,1.0f), White);
            Box(ctx, root, "Pool_Lounger_2", c + new Vector3(2.0f,0.35f,4.0f), new Vector3(2.5f,0.35f,1.0f), White);
            HomeSign(c + new Vector3(0,2.5f,4.0f), "泳 池  ·  RECOVER");
        }

        static void BuildGarage(WorldContext ctx, GameObject root, Vector3 h)
        {
            Vector3 c = h + new Vector3(-11.5f,0,-13.8f);
            Box(ctx, root, "Garage_Floor", c, new Vector3(10f,0.12f,7f), new Color(0.34f,0.35f,0.37f));
            Box(ctx, root, "Garage_Back", c + new Vector3(0,1.7f,3.4f), new Vector3(10f,3.4f,0.25f), Wall);
            Box(ctx, root, "Garage_Left", c + new Vector3(-5,1.7f,0), new Vector3(0.25f,3.4f,7f), Wall);
            Box(ctx, root, "Garage_Right", c + new Vector3(5,1.7f,0), new Vector3(0.25f,3.4f,7f), Wall);
            Box(ctx, root, "Garage_Door", c + new Vector3(0,1.6f,-3.35f), new Vector3(8.8f,3.0f,0.15f), Metal);
            // 低模汽车：车身、车顶、四个车轮。
            Box(ctx, root, "Car_Body", c + new Vector3(0,0.65f,0), new Vector3(5.4f,0.8f,2.2f), new Color(0.12f,0.14f,0.17f));
            Box(ctx, root, "Car_Cabin", c + new Vector3(0.3f,1.25f,0), new Vector3(2.8f,0.8f,1.8f), new Color(0.18f,0.25f,0.30f));
            for (int x = -2; x <= 2; x += 4)
                for (int z = -1; z <= 1; z += 2)
                    Box(ctx, root, "Car_Wheel", c + new Vector3(x * 0.8f,0.35f,z * 0.8f), new Vector3(0.65f,0.65f,0.38f), new Color(0.04f,0.04f,0.05f));
            Box(ctx, root, "Tool_Cabinet", c + new Vector3(-4.0f,1.1f,2.0f), new Vector3(1.3f,2.2f,1.2f), Wood);
            HomeSign(c + new Vector3(0,3.4f,3.2f), "车 库  ·  GARAGE");
        }

        static void BuildCat(WorldContext ctx, GameObject root, Vector3 h)
        {
            Vector3 c = h + new Vector3(2.5f,0,-4.0f);
            Color cat = new Color(0.62f,0.43f,0.30f);
            Box(ctx, root, "Cat_Bed", c + new Vector3(0,0.18f,0), new Vector3(1.8f,0.35f,1.5f), new Color(0.48f,0.34f,0.42f));
            Box(ctx, root, "Cat_Body", c + new Vector3(0,0.75f,0), new Vector3(0.9f,0.65f,1.25f), cat);
            Box(ctx, root, "Cat_Head", c + new Vector3(0,1.35f,0.55f), new Vector3(0.75f,0.75f,0.75f), cat);
            Box(ctx, root, "Cat_Ear_L", c + new Vector3(-0.25f,1.85f,0.55f), new Vector3(0.18f,0.35f,0.18f), cat);
            Box(ctx, root, "Cat_Ear_R", c + new Vector3(0.25f,1.85f,0.55f), new Vector3(0.18f,0.35f,0.18f), cat);
            Box(ctx, root, "Cat_Tail", c + new Vector3(0.55f,0.85f,-0.75f), new Vector3(0.25f,0.25f,1.0f), cat);
            Box(ctx, root, "Cat_Bowl", c + new Vector3(1.2f,0.12f,0.4f), new Vector3(0.55f,0.18f,0.55f), Metal);
            HomeSign(c + new Vector3(0,2.3f,0), "🐱 小 猫 的 家");
        }

        static void BuildGarden(WorldContext ctx, GameObject root, Vector3 h)
        {
            Box(ctx, root, "Garden_Lawn", h + new Vector3(-1,0.02f,17.0f), new Vector3(28f,0.04f,8f), Green);
            for (int i = -12; i <= 12; i += 4)
            {
                Box(ctx, root, "Fence_Post", h + new Vector3(i,0.8f,21.2f), new Vector3(0.2f,1.6f,0.2f), Wood);
                Box(ctx, root, "Fence_Rail", h + new Vector3(i + 2,0.65f,21.2f), new Vector3(4f,0.18f,0.18f), Wood);
            }
        }

        static void BuildGoalBoard(WorldContext ctx, GameObject root, Vector3 h)
        {
            Vector3 p = h + new Vector3(1.5f,2.15f,10.72f);
            var board = Box(ctx, root, "GoalBoard_LARGE", p, new Vector3(8.5f,4.6f,0.28f), new Color(0.08f,0.11f,0.15f));
            HomeFixture.Attach(board, HomeFixtureKind.GoalBoard);
            board.AddComponent<GoalBoard>();
            // 金属边框与三块信息栏。
            Box(ctx, root, "GoalBoard_Frame_Top", p + new Vector3(0,2.38f,0), new Vector3(8.9f,0.18f,0.34f), new Color(0.75f,0.52f,0.18f));
            Box(ctx, root, "GoalBoard_Frame_Bottom", p + new Vector3(0,-2.38f,0), new Vector3(8.9f,0.18f,0.34f), new Color(0.75f,0.52f,0.18f));
            AddBoardText(root, p + new Vector3(0,1.45f,-0.20f), "MY GOAL  ·  我的目标", 0.34f, TextAnchor.MiddleCenter);
            AddBoardText(root, p + new Vector3(0,0.55f,-0.20f), "PLAN  ·  当前计划", 0.26f, TextAnchor.MiddleCenter);
            AddBoardText(root, p + new Vector3(0,-0.35f,-0.20f), "NEXT ACTION  ·  下一行动", 0.23f, TextAnchor.MiddleCenter);
            AddBoardText(root, p + new Vector3(0,-1.25f,-0.20f), "LEVEL ROUTE  ·  关卡路线", 0.23f, TextAnchor.MiddleCenter);
            var display = root.AddComponent<HomeGoalBoardDisplay>();
            display.target = p;
        }

        static void AddBoardText(GameObject root, Vector3 p, string s, float size, TextAnchor anchor)
        {
            var go = new GameObject("GoalBoard_Label");
            go.transform.SetParent(root.transform, true);
            go.transform.position = p;
            var tm = go.AddComponent<TextMesh>();
            tm.text = s;
            tm.fontSize = Mathf.RoundToInt(size * 100f);
            tm.characterSize = size;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
        }

        static GameObject Box(WorldContext ctx, GameObject root, string name, Vector3 pos, Vector3 size, Color color)
        {
            var go = ZoneBuilder.Box(ctx, name, pos, size, color);
            go.transform.SetParent(root.transform, true);
            return go;
        }

        static void WallBox(WorldContext ctx, Vector3 pos, Vector3 size, Color color)
        {
            Box(ctx, null, "Home_Wall", pos, size, color);
        }

        static void Painting(WorldContext ctx, GameObject root, Vector3 pos, Vector3 size, Color color)
        {
            Box(ctx, root, "Wall_Art_Frame", pos, size + new Vector3(0.16f,0.16f,0.04f), Wood);
            Box(ctx, root, "Wall_Art", pos + new Vector3(0,0,-0.04f), size, color);
        }

        static void HomeSign(Vector3 pos, string text)
        {
            var go = new GameObject("Home_Sign_" + text);
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 52;
            tm.characterSize = 0.08f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
        }
    }

    /// <summary>动态目标看板：不替代 GoalBoard，只负责把 GoalOS 的目标/计划/路线物理化到住宅。</summary>
    sealed class HomeGoalBoardDisplay : MonoBehaviour
    {
        public Vector3 target;
        TextMesh _text;
        float _next;

        void Start()
        {
            var go = new GameObject("GoalBoard_DynamicStatus");
            go.transform.SetParent(transform, true);
            go.transform.position = target + new Vector3(0,0,-0.24f);
            _text = go.AddComponent<TextMesh>();
            _text.fontSize = 34;
            _text.characterSize = 0.075f;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.color = new Color(0.88f,0.94f,1f);
        }

        void Update()
        {
            if (_text == null || Time.time < _next) return;
            _next = Time.time + 1f;
            var g = GoalOS.Active;
            if (g == null)
            {
                _text.text = "目标：尚未设置\n计划：进入目标系统创建计划\n下一行动：设置今天最重要的一步\n关卡路线：目标 → 里程碑 → 逆境关卡";
                return;
            }
            var ms = g.CurrentMilestone();
            string plan = ms != null ? ms.title : "里程碑已完成";
            var actions = new List<string>();
            foreach (var a in g.criticalActions)
            {
                if (!a.done) actions.Add(a.label);
                if (actions.Count >= 2) break;
            }
            var route = new List<string>();
            foreach (var m in g.milestones)
            {
                route.Add((m.done ? "✓" : "○") + m.title);
                if (route.Count >= 4) break;
            }
            _text.text = "目标：" + g.title + "\n计划：" + plan + "\n下一行动：" + (actions.Count > 0 ? string.Join(" / ", actions.ToArray()) : "无待办") + "\n路线：" + (route.Count > 0 ? string.Join(" → ", route.ToArray()) : "尚未生成") ;
        }
    }
}
