using System.Collections.Generic;
using UnityEngine;
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

        static readonly string[] ZoneIds = { "home", "dojo", "street", "job", "plaza" };
        static readonly string[] ZoneNames = { "独居小屋", "训练武馆", "噪声街区", "求职荒原", "城市广场" };

        public static string ZoneIdOf(int index) =>
            index >= 0 && index < ZoneIds.Length ? ZoneIds[index] : "home";

        public static string ZoneNameOf(int index) =>
            index >= 0 && index < ZoneNames.Length ? ZoneNames[index] : "";

        public static void BuildAll(WorldContext ctx)
        {
            ctx.zoneOrigins = new[]
            {
                new Vector3(0, 0, 0),
                new Vector3(300, 0, 0),
                new Vector3(600, 0, 0),
                new Vector3(900, 0, 0),
                new Vector3(1200, 0, 0)
            };
            ctx.playerSpawns = new[]
            {
                ctx.zoneOrigins[0] + new Vector3(0, 1.1f, -5),
                ctx.zoneOrigins[1] + new Vector3(-18, 1.1f, 0),
                ctx.zoneOrigins[2] + new Vector3(-40, 1.1f, 8),
                ctx.zoneOrigins[3] + new Vector3(-38, 1.1f, 0),
                ctx.zoneOrigins[4] + new Vector3(-48, 1.1f, 0)
            };
            ctx.enemySpawns = new[]
            {
                ctx.zoneOrigins[0] + new Vector3(4, 1.1f, 5),
                ctx.zoneOrigins[1] + new Vector3(8, 1.1f, 8),
                ctx.zoneOrigins[2] + new Vector3(15, 1.1f, 9),
                ctx.zoneOrigins[3] + new Vector3(10, 1.1f, 5),
                ctx.zoneOrigins[4] + new Vector3(0, 1.1f, 8)
            };

            BuildHome(ctx);
            BuildDojo(ctx);
            BuildStreet(ctx);
            BuildJobSquare(ctx);
            BuildPlaza(ctx);
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
            MakePortal(ctx, o + new Vector3(0, 0, -10.5f), 1, ctx.playerSpawns[1]);
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

            MakePortal(ctx, o + new Vector3(-24f, 0, 0), 0, ctx.playerSpawns[0]);
            MakePortal(ctx, o + new Vector3(24f, 0, 0), 2, ctx.playerSpawns[2]);
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

            // 车辆路线（双向两车道）
            ctx.carRoutes.Add(new CarRoute { a = o + new Vector3(-46, 0.55f, -2), b = o + new Vector3(46, 0.55f, -2) });
            ctx.carRoutes.Add(new CarRoute { a = o + new Vector3(46, 0.55f, 2), b = o + new Vector3(-46, 0.55f, 2) });

            // 行人出生点（人行道上）
            for (int i = 0; i < 3; i++)
            {
                ctx.pedestrianSpawns.Add(o + new Vector3(-30 + i * 22, 1f, 9));
                ctx.pedestrianSpawns.Add(o + new Vector3(-20 + i * 22, 1f, -9));
            }

            MakePortal(ctx, o + new Vector3(-50f, 0, 8), 1, ctx.playerSpawns[1] + new Vector3(2, 0, 0));
            MakePortal(ctx, o + new Vector3(50f, 0, 8), 3, ctx.playerSpawns[3]);
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

            MakePortal(ctx, o + new Vector3(-40f, 0, 0), 2, ctx.playerSpawns[2] + new Vector3(2, 0, 0));
            MakePortal(ctx, o + new Vector3(40f, 0, 0), 4, ctx.playerSpawns[4]);
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

            // 喷泉
            var basin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            basin.name = "Fountain";
            basin.transform.position = o + new Vector3(0, 0.3f, -10);
            basin.transform.localScale = new Vector3(6, 0.3f, 6);
            Paint(ctx, basin, new Color(0.4f, 0.55f, 0.65f));
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

            MakePortal(ctx, o + new Vector3(-53f, 0, 0), 3, ctx.playerSpawns[3] + new Vector3(2, 0, 0));
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
                var ped = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                ped.name = "Pedestrian";
                ped.transform.position = pos;
                ped.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
                Paint(ctx, ped, new Color(
                    0.4f + (float)rng.NextDouble() * 0.5f,
                    0.4f + (float)rng.NextDouble() * 0.5f,
                    0.4f + (float)rng.NextDouble() * 0.5f));
                ped.AddComponent<UnityEngine.AI.NavMeshAgent>();
                ped.AddComponent<PedestrianWanderer>();
            }

            var carRng = new System.Random(23);
            foreach (var route in ctx.carRoutes)
            {
                for (int i = 0; i < 2; i++)
                {
                    var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    body.name = "Car";
                    Vector3 start = Vector3.Lerp(route.a, route.b, 0.15f + 0.5f * i);
                    body.transform.position = start;
                    body.transform.localScale = new Vector3(1.9f, 1f, 4.2f);
                    Paint(ctx, body, new Color(
                        0.3f + (float)carRng.NextDouble() * 0.6f,
                        0.3f + (float)carRng.NextDouble() * 0.6f,
                        0.35f + (float)carRng.NextDouble() * 0.6f));
                    var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(cabin.GetComponent<Collider>());
                    cabin.transform.SetParent(body.transform, false);
                    cabin.transform.localPosition = new Vector3(0, 0.7f, -0.1f);
                    cabin.transform.localScale = new Vector3(0.85f, 0.55f, 0.5f);
                    Paint(ctx, cabin, new Color(0.6f, 0.75f, 0.85f));
                    var mover = body.AddComponent<CarMover>();
                    mover.pointA = route.a;
                    mover.pointB = route.b;
                    mover.speed = 6f + (float)carRng.NextDouble() * 4f;
                }
            }
        }

        // ================= 构件辅助 =================

        static readonly Color WallColor = new Color(0.72f, 0.68f, 0.6f);

        static GameObject Box(WorldContext ctx, string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            Paint(ctx, go, color);
            return go;
        }

        /// <summary>无碰撞装饰件（路面标线、地毯、窗户等），不干扰 NavMesh。</summary>
        static GameObject Decoration(WorldContext ctx, string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = Box(ctx, name, pos, scale, color);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static void Ring(WorldContext ctx, Vector3 center, float half, float h, Color c) =>
            Ring(ctx, center, new Vector2(half, half), h, c);

        static void Ring(WorldContext ctx, Vector3 center, Vector2 half, float h, Color c)
        {
            Box(ctx, "Wall", center + new Vector3(0, h / 2, half.y), new Vector3(half.x * 2, h, 1), c);
            Box(ctx, "Wall", center + new Vector3(0, h / 2, -half.y), new Vector3(half.x * 2, h, 1), c);
            Box(ctx, "Wall", center + new Vector3(half.x, h / 2, 0), new Vector3(1, h, half.y * 2), c);
            Box(ctx, "Wall", center + new Vector3(-half.x, h / 2, 0), new Vector3(1, h, half.y * 2), c);
        }

        static void Building(WorldContext ctx, Vector3 basePos, float w, float h, float d, System.Random rng)
        {
            Color bodyColor = new Color(
                0.45f + (float)rng.NextDouble() * 0.25f,
                0.42f + (float)rng.NextDouble() * 0.22f,
                0.4f + (float)rng.NextDouble() * 0.25f);
            Box(ctx, "Building", basePos + new Vector3(0, h / 2, 0), new Vector3(w, h, d), bodyColor);

            // 正面窗户（朝向街道一侧，取 z 更接近区域中心的一面）
            float facing = basePos.z > 0 ? -1f : 1f;
            int rows = Mathf.Clamp(Mathf.FloorToInt(h / 3f), 2, 5);
            for (int r = 0; r < rows; r++)
                for (int cIdx = 0; cIdx < 3; cIdx++)
                    Decoration(ctx, "Window",
                        basePos + new Vector3(-w / 3f + cIdx * w / 3f, 2f + r * (h - 3f) / rows,
                            facing * (d / 2f + 0.06f)),
                        new Vector3(1.4f, 1.1f, 0.1f),
                        new Color(0.95f, 0.9f, 0.6f));
        }

        static void Lamp(WorldContext ctx, Vector3 basePos)
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

            var lightGo = new GameObject("LampLight");
            lightGo.transform.position = basePos + new Vector3(0, 3.6f, 0);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 13;
            l.intensity = 1.3f;
            l.color = new Color(1f, 0.85f, 0.55f);
            l.enabled = false; // 由昼夜循环开关

            if (ctx.dayNight != null)
            {
                ctx.dayNight.lamps.Add(l);
                ctx.dayNight.lampHeads.Add(head.GetComponent<MeshRenderer>());
            }
        }

        /// <summary>行道树：树干+双层树冠。</summary>
        static void Tree(WorldContext ctx, Vector3 basePos)
        {
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "TreeTrunk";
            trunk.transform.position = basePos + new Vector3(0, 1.4f, 0);
            trunk.transform.localScale = new Vector3(0.3f, 1.4f, 0.3f);
            Paint(ctx, trunk, new Color(0.4f, 0.28f, 0.16f));

            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "TreeCrown";
            Object.DestroyImmediate(crown.GetComponent<Collider>());
            crown.transform.position = basePos + new Vector3(0, 3.4f, 0);
            crown.transform.localScale = Vector3.one * 2.6f;
            Paint(ctx, crown, new Color(0.2f, 0.45f, 0.2f));

            var crown2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown2.name = "TreeCrown2";
            Object.DestroyImmediate(crown2.GetComponent<Collider>());
            crown2.transform.position = basePos + new Vector3(0.5f, 4.2f, 0.3f);
            crown2.transform.localScale = Vector3.one * 1.8f;
            Paint(ctx, crown2, new Color(0.26f, 0.52f, 0.24f));
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

        static void MakePortal(WorldContext ctx, Vector3 basePos, int targetZone, Vector3 targetPos)
        {
            var root = new GameObject("Portal_" + ZoneIdOf(targetZone));
            root.transform.position = basePos;

            Box(ctx, "PortalPillar", basePos + new Vector3(-1.6f, 1.6f, 0), new Vector3(0.5f, 3.2f, 0.5f),
                new Color(0.3f, 0.5f, 0.7f));
            Box(ctx, "PortalPillar", basePos + new Vector3(1.6f, 1.6f, 0), new Vector3(0.5f, 3.2f, 0.5f),
                new Color(0.3f, 0.5f, 0.7f));
            Decoration(ctx, "PortalTop", basePos + new Vector3(0, 3.4f, 0), new Vector3(3.7f, 0.4f, 0.5f),
                new Color(0.3f, 0.5f, 0.7f));
            Decoration(ctx, "PortalGlow", basePos + new Vector3(0, 1.6f, 0), new Vector3(2.7f, 2.8f, 0.15f),
                new Color(0.55f, 0.85f, 1f));

            // 门顶标牌
            var signGo = new GameObject("PortalSign");
            signGo.transform.SetParent(root.transform, false);
            signGo.transform.position = basePos + new Vector3(0, 4.2f, 0);
            var tm = signGo.AddComponent<TextMesh>();
            tm.text = "→ " + ZoneNameOf(targetZone);
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = 56;
            tm.characterSize = 0.07f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.7f, 0.95f, 1f);
            var mr = signGo.GetComponent<MeshRenderer>();
            if (tm.font != null) mr.material = tm.font.material;
            signGo.AddComponent<FaceCamera>();

            var trigger = new GameObject("PortalTrigger");
            trigger.transform.SetParent(root.transform, false);
            trigger.transform.position = basePos + new Vector3(0, 1.5f, 0);
            var col = trigger.AddComponent<BoxCollider>();
            col.size = new Vector3(3f, 3f, 2.2f);
            var portal = trigger.AddComponent<Portal>();
            portal.targetZoneIndex = targetZone;
            portal.targetPosition = targetPos;
            portal.targetName = ZoneNameOf(targetZone);
        }

        static readonly Dictionary<Color, Material> MatCache = new Dictionary<Color, Material>();

        static void Paint(WorldContext ctx, GameObject go, Color c)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return;
            if (!MatCache.TryGetValue(c, out var m) || m == null)
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
                MatCache[c] = m; // 同色共享材质：大量建筑/窗户下控制移动端开销
            }
            r.sharedMaterial = m;
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
}
