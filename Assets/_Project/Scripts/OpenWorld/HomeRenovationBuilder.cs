using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Goals;
using AdversityRoad.Player;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// #327 之后的 Player Home 全面装修层。
    /// 不修改原 OpenWorldBuilder：运行时找到 Home_Floor 后，先清掉旧住宅内部物件，
    /// 再在同一坐标重建一座可步入、可交互、可持续生活的完整住宅。
    /// 这样可以把本轮改造限制在住宅，不碰城区其它系统。
    /// </summary>
    public static class HomeRenovationBuilder
    {
        static bool _started;
        static readonly Color Wall = new Color(0.78f, 0.75f, 0.69f);
        static readonly Color Floor = new Color(0.48f, 0.36f, 0.25f);
        static readonly Color Wood = new Color(0.38f, 0.25f, 0.16f);
        static readonly Color Metal = new Color(0.42f, 0.45f, 0.49f);
        static readonly Color Fabric = new Color(0.33f, 0.42f, 0.58f);
        static readonly Color White = new Color(0.93f, 0.93f, 0.91f);
        static readonly Color Glass = new Color(0.48f, 0.78f, 0.92f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_started) return;
            _started = true;
            var existing = GameObject.Find("HomeRenovationRuntime");
            if (existing != null) return;
            var go = new GameObject("HomeRenovationRuntime");
            go.AddComponent<Runner>();
        }

        sealed class Runner : MonoBehaviour
        {
            IEnumerator Start()
            {
                GameObject floor = null;
                for (int i = 0; i < 240 && floor == null; i++)
                {
                    floor = GameObject.Find("Home_Floor");
                    if (floor == null) yield return new WaitForSeconds(0.25f);
                }
                if (floor == null) yield break;

                Vector3 center = floor.transform.position;
                center.y = 0f;
                ClearOldHome(center);
                yield return null;
                Build(center);
            }
        }

        static void ClearOldHome(Vector3 c)
        {
            const float rx = 17f, rz = 15f;
            var all = UnityEngine.Object.FindObjectsOfType<Transform>();
            var roots = new HashSet<GameObject>();
            foreach (var t in all)
            {
                if (t == null) continue;
                Vector3 p = t.position;
                if (Mathf.Abs(p.x - c.x) > rx || Mathf.Abs(p.z - c.z) > rz || p.y < -0.25f || p.y > 5f) continue;
                if (t.GetComponentInParent<PlayerController>() != null) continue;
                if (t.GetComponentInParent<Camera>() != null) continue;
                string n = t.name;
                if (n == "City_Ground" || n == "Road" || n == "Sidewalk" || n == "Lane" || n == "Crosswalk" || n == "Path") continue;
                if (n == "HomeRenovationRuntime") continue;
                GameObject root = t.gameObject;
                if (root.transform.parent != null && root.transform.parent.name.StartsWith("HomeRenovation")) continue;
                roots.Add(root);
            }
            foreach (var go in roots) UnityEngine.Object.Destroy(go);
        }

        static void Build(Vector3 h)
        {
            var root = new GameObject("HomeRenovation_CompleteHome");
            root.transform.position = h;

            // 房屋主体：30 x 24，可自由穿行；南侧保留与原住宅完全相同的入口坐标。
            const float W = 30f, D = 24f, H = 3.4f;
            Box(root, "Floor", new Vector3(0, 0, 0), new Vector3(W, 0.25f, D), Floor);
            // 外墙分段，南侧中间开门。
            Box(root, "Wall_N", new Vector3(0, H / 2f, 12), new Vector3(W, H, 0.35f), Wall);
            Box(root, "Wall_W", new Vector3(-15, H / 2f, 0), new Vector3(0.35f, H, D), Wall);
            Box(root, "Wall_E", new Vector3(15, H / 2f, 0), new Vector3(0.35f, H, D), Wall);
            Box(root, "Wall_S_L", new Vector3(-10.5f, H / 2f, -12), new Vector3(9f, H, 0.35f), Wall);
            Box(root, "Wall_S_R", new Vector3(10.5f, H / 2f, -12), new Vector3(9f, H, 0.35f), Wall);

            // 房间分区：卧室、书房、办公室、卫生间、厨房、客餐厅。
            Box(root, "Partition_Bedroom", new Vector3(-6.2f, H / 2f, 5.3f), new Vector3(0.25f, H, 13.4f), Wall);
            Box(root, "Partition_Study", new Vector3(3.6f, H / 2f, 7.7f), new Vector3(0.25f, H, 8.6f), Wall);
            Box(root, "Partition_Bath", new Vector3(-10.7f, H / 2f, -6.4f), new Vector3(8.6f, H, 0.25f), Wall);
            Box(root, "Partition_Office", new Vector3(9.2f, H / 2f, 0.7f), new Vector3(0.25f, H, 8.2f), Wall);
            Box(root, "Partition_Kitchen", new Vector3(9.2f, H / 2f, -7.1f), new Vector3(0.25f, H, 5.7f), Wall);

            // 大目标看板：目标、计划、关卡路线动态绑定 GoalOS。
            BuildGoalBoard(root, new Vector3(0.5f, 1.8f, 11.72f));

            BuildBedroom(root, new Vector3(-10.8f, 0, 5.3f));
            BuildStudy(root, new Vector3(-1.5f, 0, 8.1f));
            BuildOffice(root, new Vector3(9.8f, 0, 4.8f));
            BuildBathroom(root, new Vector3(-10.8f, 0, -8.7f));
            BuildKitchenAndDining(root, new Vector3(9.7f, 0, -8.9f));
            BuildLiving(root, new Vector3(1.0f, 0, -2.0f));

            // 房屋外：独立健身房、泳池、车库，全部属于玩家住宅地块。
            BuildGym(root, h + new Vector3(24f, 0, 7f));
            BuildPool(root, h + new Vector3(21f, 0, -12f));
            BuildGarage(root, h + new Vector3(-24f, 0, -2f));
            BuildGardenAndFence(root);
            BuildCat(root, new Vector3(4f, 0, -4f));

            Light(root, new Vector3(0, 3f, 0), new Color(1f, 0.9f, 0.72f), 28f);
            Light(root, new Vector3(-9f, 3f, 6f), new Color(0.85f, 0.92f, 1f), 14f);
            Light(root, new Vector3(9f, 3f, 6f), new Color(0.9f, 0.95f, 1f), 14f);
            Light(root, new Vector3(9f, 3f, -8f), new Color(1f, 0.9f, 0.75f), 16f);
        }

        static void BuildGoalBoard(GameObject root, Vector3 p)
        {
            var board = Box(root, "GoalBoard_Large", p, new Vector3(8.2f, 4.4f, 0.28f), new Color(0.12f, 0.16f, 0.2f));
            var frame = Box(root, "GoalBoard_Frame", p + new Vector3(0, 0, -0.18f), new Vector3(8.5f, 4.7f, 0.12f), new Color(0.72f, 0.5f, 0.2f));
            frame.transform.SetSiblingIndex(0);
            board.AddComponent<GoalBoard>();
            HomeFixture.Attach(board, HomeFixtureKind.GoalBoard);

            var canvasGo = new GameObject("GoalBoard_DynamicCanvas");
            canvasGo.transform.SetParent(root.transform, false);
            canvasGo.transform.position = p + new Vector3(0, 0, -0.24f);
            canvasGo.transform.rotation = Quaternion.Euler(0, 180, 0);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            var textGo = new GameObject("GoalBoard_Text");
            textGo.transform.SetParent(canvasGo.transform, false);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 34;
            text.lineSpacing = 1.05f;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            text.rectTransform.sizeDelta = new Vector2(760, 410);
            text.rectTransform.localPosition = new Vector3(-3.8f, -2.05f, 0);
            textGo.AddComponent<HomeGoalBoardText>().text = text;
        }

        sealed class HomeGoalBoardText : MonoBehaviour
        {
            public Text text;
            float next;
            void Update()
            {
                if (Time.time < next) return;
                next = Time.time + 0.8f;
                var g = GoalOS.Active;
                if (g == null)
                {
                    text.text = "【我的人生目标】\n\n目标：尚未设置\n\n计划：先在目标系统中创建你的目标\n\n关卡路线：创建目标后，这里会自动显示里程碑 → 关键行动 → 逆境关卡 → 终局";
                    return;
                }
                var ms = g.CurrentMilestone();
                string plan = ms != null ? ms.title : "全部里程碑已完成 · 进入终局";
                var actions = new List<string>();
                foreach (var a in g.criticalActions)
                {
                    if (a.done) continue;
                    actions.Add(a.label);
                    if (actions.Count >= 2) break;
                }
                string actionLine = actions.Count == 0 ? "无待办关键行动" : string.Join(" / ", actions.ToArray());
                var route = new List<string>();
                foreach (var m in g.milestones)
                {
                    route.Add((m.done ? "✓ " : "○ ") + m.title);
                    if (route.Count >= 4) break;
                }
                string routeLine = route.Count == 0 ? "尚未生成关卡路线" : string.Join("  →  ", route.ToArray());
                text.text = "【我的目标 · JOURNEY BOARD】\n\n目标：" + g.title +
                            "\n\n计划：" + plan +
                            "\n下一行动：" + actionLine +
                            "\n\n关卡路线：\n" + routeLine +
                            "\n\n状态：" + (g.completed ? "目标已达成" : (g.journeyReady ? "旅程已就绪" : "旅程生成中"));
            }
        }

        static void BuildBedroom(GameObject root, Vector3 c)
        {
            // 床：床架 + 床垫 + 床单 + 被子 + 两个枕头。
            Box(root, "BedFrame", c + new Vector3(0, 0.35f, 1.0f), new Vector3(5.2f, 0.7f, 7f), Wood);
            Box(root, "Mattress", c + new Vector3(0, 0.85f, 1.0f), new Vector3(4.9f, 0.35f, 6.7f), White);
            Box(root, "BedSheet", c + new Vector3(0, 1.04f, 0.2f), new Vector3(4.75f, 0.08f, 4.9f), new Color(0.75f, 0.84f, 0.94f));
            Box(root, "Blanket", c + new Vector3(0, 1.13f, -1.15f), new Vector3(4.75f, 0.14f, 3.1f), new Color(0.42f, 0.55f, 0.74f));
            Box(root, "Pillow_L", c + new Vector3(-1.25f, 1.18f, 3.35f), new Vector3(2.1f, 0.28f, 1.05f), White);
            Box(root, "Pillow_R", c + new Vector3(1.25f, 1.18f, 3.35f), new Vector3(2.1f, 0.28f, 1.05f), White);
            Box(root, "Nightstand", c + new Vector3(-4.0f, 0.65f, 2.6f), new Vector3(1.2f, 1.3f, 1.4f), Wood);
            Light(root, c + new Vector3(-4f, 1.7f, 2.6f), new Color(1f, 0.75f, 0.45f), 5f);
            // 艺术画：三幅带画框的墙面艺术。
            Painting(root, c + new Vector3(-4.65f, 2.2f, 5.8f), new Vector3(2.4f, 1.6f, 0.12f), new Color(0.22f, 0.42f, 0.52f));
            Painting(root, c + new Vector3(0f, 2.15f, 5.82f), new Vector3(2.7f, 1.8f, 0.12f), new Color(0.72f, 0.42f, 0.28f));
            Painting(root, c + new Vector3(4.65f, 2.2f, 5.8f), new Vector3(2.4f, 1.6f, 0.12f), new Color(0.38f, 0.56f, 0.34f));
            HomeSign(c + new Vector3(0, 3.0f, 5.7f), "卧 室  ·  生 活 与 恢 复", root.transform);
        }

        static void BuildStudy(GameObject root, Vector3 c)
        {
            Box(root, "StudyDesk", c + new Vector3(0, 0.55f, -1.7f), new Vector3(4.8f, 1.1f, 1.7f), Wood);
            var desk = root.transform.Find("StudyDesk").gameObject;
            HomeFixture.Attach(desk, HomeFixtureKind.Desk);
            Box(root, "StudyChair", c + new Vector3(0, 0.5f, -3.2f), new Vector3(1.2f, 1f, 1.2f), Fabric);
            Bookcase(root, c + new Vector3(-2.9f, 1.6f, 2.4f));
            Bookcase(root, c + new Vector3(2.9f, 1.6f, 2.4f));
            HomeSign(c + new Vector3(0, 2.9f, 3.9f), "书 房  ·  学 习 与 思 考", root.transform);
        }

        static void Bookcase(GameObject root, Vector3 c)
        {
            Box(root, "Bookcase", c, new Vector3(4.6f, 3.2f, 0.65f), Wood);
            for (int shelf = 0; shelf < 3; shelf++)
            {
                float y = c.y - 1.15f + shelf * 1.05f;
                Box(root, "Shelf", c + new Vector3(0, y - c.y, -0.36f), new Vector3(4.2f, 0.08f, 0.12f), Wood);
                for (int i = 0; i < 8; i++)
                {
                    float x = c.x - 1.8f + i * 0.52f;
                    float hh = 0.55f + (i % 3) * 0.16f;
                    Box(root, "Book", new Vector3(x, y + 0.38f, c.z - 0.43f), new Vector3(0.34f, hh, 0.16f),
                        new Color(0.25f + (i % 4) * 0.1f, 0.32f + (i % 3) * 0.1f, 0.42f + (i % 2) * 0.12f));
                }
            }
        }

        static void BuildOffice(GameObject root, Vector3 c)
        {
            var desk = Box(root, "OfficeDesk", c + new Vector3(0, 0.6f, -1.0f), new Vector3(5.2f, 1.2f, 2f), Wood);
            HomeFixture.Attach(desk, HomeFixtureKind.Desk);
            Box(root, "OfficeChair", c + new Vector3(0, 0.65f, -2.8f), new Vector3(1.3f, 1.3f, 1.3f), Fabric);
            Box(root, "Monitor", c + new Vector3(-1.1f, 1.55f, -1.05f), new Vector3(1.8f, 1.1f, 0.15f), new Color(0.12f, 0.16f, 0.2f));
            Box(root, "Monitor2", c + new Vector3(1.1f, 1.55f, -1.05f), new Vector3(1.8f, 1.1f, 0.15f), new Color(0.12f, 0.16f, 0.2f));
            Box(root, "Keyboard", c + new Vector3(0, 1.23f, -0.5f), new Vector3(1.9f, 0.08f, 0.55f), Metal);
            Box(root, "OfficeLamp", c + new Vector3(2.0f, 1.55f, -0.9f), new Vector3(0.2f, 1.0f, 0.2f), Metal);
            Light(root, c + new Vector3(2.0f, 2.1f, -0.9f), new Color(0.8f, 0.9f, 1f), 6f);
            Painting(root, c + new Vector3(0, 2.25f, 3.85f), new Vector3(4.8f, 1.6f, 0.12f), new Color(0.24f, 0.34f, 0.44f));
            HomeSign(c + new Vector3(0, 2.95f, 3.75f), "办 公 室  ·  工 作 与 行 动", root.transform);
        }

        static void BuildBathroom(GameObject root, Vector3 c)
        {
            // 马桶
            Cylinder(root, "ToiletBase", c + new Vector3(1.8f, 0.45f, -0.2f), 0.9f, 0.9f, White);
            Cylinder(root, "ToiletBowl", c + new Vector3(1.8f, 0.92f, -0.2f), 0.72f, 0.45f, White);
            Box(root, "ToiletTank", c + new Vector3(1.8f, 1.15f, 1.0f), new Vector3(1.1f, 1.0f, 0.55f), White);
            // 洗手池 + 镜子
            Box(root, "BathroomSink", c + new Vector3(-2.1f, 0.65f, -1.5f), new Vector3(2.5f, 1.3f, 1.2f), White);
            var mirror = Box(root, "BathroomMirror", c + new Vector3(-2.1f, 2.15f, -2.1f), new Vector3(2.2f, 2.0f, 0.12f), Glass);
            HomeFixture.Attach(mirror, HomeFixtureKind.Mirror);
            // 淋浴区
            Box(root, "ShowerFloor", c + new Vector3(0, 0.1f, 3.1f), new Vector3(4.8f, 0.2f, 3.2f), new Color(0.62f, 0.67f, 0.7f));
            Box(root, "ShowerGlass", c + new Vector3(-2.3f, 1.5f, 3.1f), new Vector3(0.12f, 3.0f, 3.2f), new Color(0.55f, 0.8f, 0.9f));
            Cylinder(root, "ShowerHead", c + new Vector3(1.3f, 2.65f, 3.1f), 0.14f, 0.8f, Metal);
            HomeSign(c + new Vector3(0, 2.95f, -4.15f), "卫 生 间  ·  清 洁 与 恢 复", root.transform);
        }

        static void BuildKitchenAndDining(GameObject root, Vector3 c)
        {
            var fridge = Box(root, "KitchenFridge", c + new Vector3(4.0f, 1.25f, 1.7f), new Vector3(1.8f, 2.5f, 1.6f), new Color(0.84f, 0.87f, 0.89f));
            HomeFixture.Attach(fridge, HomeFixtureKind.Fridge);
            Box(root, "KitchenCounter", c + new Vector3(-0.2f, 0.55f, 2.0f), new Vector3(5.8f, 1.1f, 1.5f), new Color(0.6f, 0.57f, 0.53f));
            Box(root, "Stove", c + new Vector3(-1.8f, 1.15f, 1.98f), new Vector3(1.5f, 0.1f, 1.2f), Metal);
            for (int i = 0; i < 4; i++) Cylinder(root, "Burner", c + new Vector3(-2.25f + (i % 2) * 0.7f, 1.25f, 1.6f + (i / 2) * 0.65f), 0.18f, 0.08f, Color.black);
            Box(root, "KitchenSink", c + new Vector3(1.1f, 1.12f, 1.95f), new Vector3(1.2f, 0.12f, 1.0f), White);
            Box(root, "UpperCabinet", c + new Vector3(-0.3f, 2.3f, 2.0f), new Vector3(5.8f, 1.3f, 0.55f), Wood);

            // 六人餐桌：2+2+2 对称座位。
            Box(root, "DiningTable6", c + new Vector3(-1.5f, 0.65f, -3.0f), new Vector3(6.2f, 1.2f, 2.1f), Wood);
            Vector3[] seats = {
                new Vector3(-3.2f,0.5f,-3f), new Vector3(0.2f,0.5f,-3f),
                new Vector3(-3.2f,0.5f,-5.0f), new Vector3(0.2f,0.5f,-5.0f),
                new Vector3(-1.5f,0.5f,-1.5f), new Vector3(-1.5f,0.5f,-6.0f)
            };
            foreach (var s in seats) Box(root, "DiningChair", c + s, new Vector3(1.0f, 1.0f, 1.0f), Fabric);
            for (int i = 0; i < 6; i++) Cylinder(root, "Plate", c + new Vector3(-3.0f + (i % 3) * 1.5f, 1.28f, -2.9f - (i / 3) * 0.65f), 0.28f, 0.05f, White);
            HomeSign(c + new Vector3(-1.5f, 2.25f, -0.8f), "六 人 餐 厅  ·  家 庭 与 关 系", root.transform);
        }

        static void BuildLiving(GameObject root, Vector3 c)
        {
            Box(root, "LivingSofa", c + new Vector3(0, 0.55f, -2.2f), new Vector3(7.0f, 1.1f, 2.0f), Fabric);
            Box(root, "LivingTable", c + new Vector3(0, 0.4f, 0.5f), new Vector3(4.2f, 0.8f, 1.7f), Wood);
            Box(root, "TV", c + new Vector3(0, 1.7f, 5.4f), new Vector3(5.0f, 2.8f, 0.15f), new Color(0.08f, 0.1f, 0.13f));
            Painting(root, c + new Vector3(-6.3f, 2.2f, 1.2f), new Vector3(2.2f, 1.5f, 0.12f), new Color(0.4f, 0.32f, 0.2f));
        }

        static void BuildGym(GameObject root, Vector3 world)
        {
            var g = new GameObject("Home_Gym"); g.transform.SetParent(root.transform, true); g.transform.position = world;
            Box(g, "GymFloor", Vector3.zero, new Vector3(14, 0.2f, 10), new Color(0.17f, 0.18f, 0.2f));
            // 跑步机
            Box(g, "TreadmillBase", new Vector3(0, 0.55f, 0), new Vector3(5.2f, 0.35f, 2.0f), Metal);
            Box(g, "TreadmillBelt", new Vector3(0, 0.76f, 0), new Vector3(4.6f, 0.08f, 1.55f), new Color(0.05f, 0.06f, 0.07f));
            Cylinder(g, "TreadmillPostL", new Vector3(-2.1f, 1.5f, 0.5f), 0.1f, 1.6f, Metal);
            Cylinder(g, "TreadmillPostR", new Vector3(2.1f, 1.5f, 0.5f), 0.1f, 1.6f, Metal);
            Box(g, "TreadmillConsole", new Vector3(0, 2.2f, 0.55f), new Vector3(2.3f, 0.9f, 0.25f), new Color(0.12f, 0.18f, 0.22f));
            // 哑铃架 + 哑铃
            Box(g, "DumbbellRack", new Vector3(-4.7f, 0.9f, 2.8f), new Vector3(2.0f, 1.2f, 0.7f), Metal);
            for (int i = 0; i < 6; i++)
            {
                Cylinder(g, "Dumbbell", new Vector3(-5.2f + i * 0.18f, 1.65f, 2.8f), 0.12f, 0.75f, Metal);
            }
            // 杠铃/深蹲架
            Cylinder(g, "SquatPostL", new Vector3(3.9f, 1.8f, 2.7f), 0.15f, 3.4f, Metal);
            Cylinder(g, "SquatPostR", new Vector3(6.0f, 1.8f, 2.7f), 0.15f, 3.4f, Metal);
            Cylinder(g, "Barbell", new Vector3(4.95f, 3.2f, 2.7f), 0.12f, 2.6f, Metal);
            // 健身镜
            Box(g, "GymMirror", new Vector3(0, 2.2f, 4.7f), new Vector3(8.5f, 3.5f, 0.12f), Glass);
            HomeSign(world + new Vector3(0, 4.2f, -4.9f), "健 身 房  ·  身 体 与 行 动 力", root.transform);
        }

        static void BuildPool(GameObject root, Vector3 world)
        {
            var p = new GameObject("Home_Pool"); p.transform.SetParent(root.transform, true); p.transform.position = world;
            Box(p, "PoolDeck", Vector3.zero, new Vector3(18, 0.25f, 11), new Color(0.66f, 0.66f, 0.62f));
            Box(p, "PoolWater", new Vector3(0, 0.28f, 0), new Vector3(14, 0.18f, 7), new Color(0.12f, 0.58f, 0.78f));
            Box(p, "PoolEdge", new Vector3(0, 0.45f, -4.0f), new Vector3(15, 0.35f, 0.45f), White);
            Cylinder(p, "PoolLadder", new Vector3(5.3f, 0.9f, -3.0f), 0.08f, 1.4f, Metal);
            Cylinder(p, "PoolLadder2", new Vector3(6.0f, 0.9f, -3.0f), 0.08f, 1.4f, Metal);
            Box(p, "PoolChair", new Vector3(-7.0f, 0.55f, -4.0f), new Vector3(2.2f, 0.25f, 0.9f), White);
            Box(p, "PoolChairBack", new Vector3(-7.8f, 1.2f, -3.6f), new Vector3(0.25f, 1.2f, 0.9f), White);
            HomeSign(world + new Vector3(0, 2.0f, 5.3f), "私 人 游 泳 池  ·  休 息 与 恢 复", root.transform);
        }

        static void BuildGarage(GameObject root, Vector3 world)
        {
            var g = new GameObject("Home_Garage"); g.transform.SetParent(root.transform, true); g.transform.position = world;
            Box(g, "GarageFloor", Vector3.zero, new Vector3(12, 0.2f, 9), new Color(0.28f, 0.29f, 0.31f));
            Box(g, "GarageBack", new Vector3(0, 2.0f, 4.4f), new Vector3(12, 4f, 0.35f), Wall);
            Box(g, "GarageLeft", new Vector3(-5.8f, 2.0f, 0), new Vector3(0.35f, 4f, 9), Wall);
            Box(g, "GarageRight", new Vector3(5.8f, 2.0f, 0), new Vector3(0.35f, 4f, 9), Wall);
            Box(g, "GarageDoor", new Vector3(0, 2.0f, -4.4f), new Vector3(10.5f, 3.5f, 0.18f), new Color(0.32f, 0.35f, 0.39f));
            // 简化车辆
            Box(g, "CarBody", new Vector3(0, 0.85f, 0.2f), new Vector3(5.6f, 1.0f, 3.0f), new Color(0.18f, 0.3f, 0.55f));
            Box(g, "CarCabin", new Vector3(0.3f, 1.55f, 0.2f), new Vector3(3.0f, 1.0f, 2.5f), Glass);
            for (int i = 0; i < 4; i++)
            {
                float x = i < 2 ? -2.0f : 2.0f;
                float z = i % 2 == 0 ? -1.45f : 1.45f;
                Cylinder(g, "CarWheel", new Vector3(x, 0.55f, z), 0.62f, 0.32f, Color.black);
            }
            Box(g, "ToolCabinet", new Vector3(-4.5f, 1.2f, 2.9f), new Vector3(1.4f, 2.4f, 1.0f), Metal);
            HomeSign(world + new Vector3(0, 4.1f, -4.7f), "车 库  ·  车 辆 与 外 部 世 界", root.transform);
        }

        static void BuildGardenAndFence(GameObject root)
        {
            // 花坛、树和低围栏，把住宅、健身房、泳池、车库视觉上连成一个院落。
            for (int i = -1; i <= 1; i++)
            {
                Cylinder(root, "GardenTree", new Vector3(18 + i * 5, 1.7f, 10), 0.35f, 3.4f, new Color(0.32f, 0.2f, 0.12f));
                Sphere(root, "TreeCrown", new Vector3(18 + i * 5, 4.0f, 10), 1.8f, new Color(0.25f, 0.5f, 0.25f));
            }
            for (int i = -15; i <= 15; i += 3)
            {
                Box(root, "YardFence", new Vector3(i, 0.65f, -15), new Vector3(2.4f, 1.3f, 0.18f), Wood);
            }
        }

        static void BuildCat(GameObject root, Vector3 local)
        {
            var cat = new GameObject("Home_Pet_Cat"); cat.transform.SetParent(root.transform, false); cat.transform.localPosition = local;
            var comp = cat.AddComponent<HomeCatCompanion>();
            comp.homeRoot = root.transform;
            comp.origin = local;

            Sphere(cat, "CatBody", new Vector3(0, 0.65f, 0), 0.55f, new Color(0.72f, 0.48f, 0.28f));
            Sphere(cat, "CatHead", new Vector3(0, 1.18f, 0.42f), 0.43f, new Color(0.78f, 0.56f, 0.34f));
            ConeLike(cat, "EarL", new Vector3(-0.25f, 1.52f, 0.42f), 0.22f, 0.42f, new Color(0.72f, 0.48f, 0.28f));
            ConeLike(cat, "EarR", new Vector3(0.25f, 1.52f, 0.42f), 0.22f, 0.42f, new Color(0.72f, 0.48f, 0.28f));
            Sphere(cat, "EyeL", new Vector3(-0.15f, 1.26f, 0.78f), 0.055f, new Color(0.1f, 0.75f, 0.45f));
            Sphere(cat, "EyeR", new Vector3(0.15f, 1.26f, 0.78f), 0.055f, new Color(0.1f, 0.75f, 0.45f));
            var tail = Cylinder(cat, "CatTail", new Vector3(0.62f, 0.8f, -0.05f), 0.10f, 1.1f, new Color(0.72f, 0.48f, 0.28f));
            tail.transform.localRotation = Quaternion.Euler(0, 0, -35);
            Sphere(cat, "CatPawL", new Vector3(-0.28f, 0.3f, 0.3f), 0.18f, new Color(0.78f, 0.56f, 0.34f));
            Sphere(cat, "CatPawR", new Vector3(0.28f, 0.3f, 0.3f), 0.18f, new Color(0.78f, 0.56f, 0.34f));
            Cylinder(cat, "CatBowl", new Vector3(1.0f, 0.12f, 0.6f), 0.35f, 0.15f, Metal);
            HomeSign(local + new Vector3(0, 2.2f, 0), "小 猫", root.transform);
        }

        sealed class HomeCatCompanion : MonoBehaviour
        {
            public Transform homeRoot;
            public Vector3 origin;
            float seed;
            void Start() { seed = UnityEngine.Random.value * 100f; }
            void Update()
            {
                if (homeRoot == null) return;
                float t = Time.time * 0.45f + seed;
                Vector3 target = origin + new Vector3(Mathf.Sin(t) * 3.0f, 0, Mathf.Cos(t * 0.73f) * 2.2f);
                Vector3 worldTarget = homeRoot.TransformPoint(target);
                Vector3 pos = transform.position;
                Vector3 next = Vector3.MoveTowards(pos, worldTarget, Time.deltaTime * 0.65f);
                next.y = homeRoot.TransformPoint(origin).y;
                transform.position = next;
                if ((worldTarget - pos).sqrMagnitude > 0.08f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(worldTarget - pos), Time.deltaTime * 4f);
                transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 2.8f + seed) * 0.025f);
            }
        }

        static GameObject Box(GameObject parent, string name, Vector3 localPos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            Apply(go, color);
            return go;
        }

        static GameObject Sphere(GameObject parent, string name, Vector3 localPos, float radius, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name; go.transform.SetParent(parent.transform, false); go.transform.localPosition = localPos; go.transform.localScale = Vector3.one * radius * 2f; Apply(go, color); return go;
        }

        static GameObject Cylinder(GameObject parent, string name, Vector3 localPos, float radius, float height, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name; go.transform.SetParent(parent.transform, false); go.transform.localPosition = localPos; go.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f); Apply(go, color); return go;
        }

        static GameObject ConeLike(GameObject parent, string name, Vector3 localPos, float radius, float height, Color color)
        {
            // 使用缩放后的圆柱体近似猫耳，保持零外部模型依赖。
            var go = Cylinder(parent, name, localPos, radius, height, color);
            go.transform.localScale = new Vector3(radius * 1.5f, height / 2f, radius * 0.65f);
            go.transform.localRotation = Quaternion.Euler(0, 0, 0);
            return go;
        }

        static void Painting(GameObject parent, Vector3 p, Vector3 size, Color art)
        {
            Box(parent, "ArtFrame", p, size + new Vector3(0.25f, 0.25f, 0.08f), new Color(0.15f, 0.1f, 0.07f));
            Box(parent, "ArtCanvas", p + new Vector3(0, 0, -0.08f), size, art);
        }

        static void HomeSign(Vector3 p, string label, Transform parent = null)
        {
            var go = new GameObject("HomeSign_" + label.Replace(" ", "_"));
            if (parent != null) go.transform.SetParent(parent, true);
            go.transform.position = p;
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 20;
            var scaler = go.AddComponent<CanvasScaler>(); scaler.dynamicPixelsPerUnit = 10;
            var textGo = new GameObject("Text"); textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = 24; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.text = label;
            text.rectTransform.sizeDelta = new Vector2(500, 80); text.rectTransform.localScale = Vector3.one * 0.01f; textGo.transform.localPosition = new Vector3(-2.5f, -0.4f, 0);
        }

        static void Light(GameObject parent, Vector3 p, Color color, float range)
        {
            var go = new GameObject("HomeLight"); go.transform.SetParent(parent.transform, false); go.transform.localPosition = p;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = color; l.range = range; l.intensity = 1.3f;
        }

        static void Apply(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = color;
            r.sharedMaterial = mat;
        }
    }
}
