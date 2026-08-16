using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using AdversityRoad.Goals;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>一次生成出来的场景实例（运行时对象，可整体卸载）。</summary>
    public class SiteInstance
    {
        public string chapterId;
        public string siteId;
        public int zoneIndex = -1;
        /// <summary>它占用的坐标槽位（卸载时归还，避免场景越建越远丢精度）。</summary>
        public int slot = -1;
        public GameObject root;
        public Vector3 origin;
        public Vector3 playerSpawn;
        public Vector3 exitPoint;
        public readonly List<Vector3> enemySpawns = new List<Vector3>();
        public readonly List<Vector3> propAnchors = new List<Vector3>();
        public SiteBlueprint blueprint;
    }

    /// <summary>
    /// Site Builder：把 AI 的场景蓝图**在运行时建成一个真的能走进去的地方**。
    ///
    /// 这是 V2.0 与"只有 24 个固定场景"的分界线：
    /// 玩家输入的目标不同 → AI 描述的地方不同 → 这里生成的建筑、房间、道具、
    /// 灯光、NPC、敌人与规则就都不同。场景不预先存在，它是为这个目标临时长出来的。
    ///
    /// 三条硬约束（方案 5.4 / 19.5 / 19.6）：
    /// 1. AI 只给语义（类型/布局/房间/道具/角色类型/氛围），坐标一律由引擎决定；
    /// 2. 相同 assemblySeed 必须生成完全相同的场景——Bug 才可复现，验收才有意义；
    /// 3. 生成完立刻在本场景范围内烘焙 NavMesh，绝不放出未经导航验证的几何。
    /// </summary>
    public static class SiteBuilder
    {
        /// <summary>动态场景的世界坐标区：远离 V1 的 25 个静态区域，互不干扰。</summary>
        const float SiteOriginX = 20000f;
        const float SiteSpacing = 600f;

        static int _nextSlot;
        // 槽位回收：场景不断生成又卸载时，坐标不能一路往外飘——
        // 浮点精度会在十万单位外肉眼可见地抖起来。
        static readonly Stack<int> _freeSlots = new Stack<int>();
        static readonly Dictionary<string, SiteInstance> _live = new Dictionary<string, SiteInstance>();

        static readonly Color Floor = new Color(0.46f, 0.45f, 0.44f);
        static readonly Color Wall = new Color(0.7f, 0.68f, 0.63f);
        static readonly Color Trim = new Color(0.32f, 0.33f, 0.36f);

        public static IEnumerable<SiteInstance> Live => _live.Values;

        public static SiteInstance Find(string chapterId) =>
            chapterId != null && _live.TryGetValue(chapterId, out var s) ? s : null;

        // ================= 生成 =================

        /// <summary>
        /// 按蓝图生成场景（分帧进行）。相同 seed → 相同结果。
        /// 生成后注册为一个**动态区域**，传送/存档/雾色/剧情锁都能像原生区域一样对待它。
        ///
        /// 之所以要分帧：一处场景是上百个构件加一次导航烘焙，挤在同一帧里建完，
        /// 玩家会在走近入口的那一刻明显卡一下。所以按"外壳→布局→氛围→烘焙→放人"
        /// 分成若干步，每步之间让出一帧——建造过程本身不该被玩家感觉到。
        /// </summary>
        public static IEnumerator BuildRoutine(GoalChapterData chapter, WorldContext ctx,
            System.Action<SiteInstance> onDone)
        {
            if (chapter == null || chapter.site == null || chapter.site.IsEmpty)
            { onDone?.Invoke(null); yield break; }
            if (_live.TryGetValue(chapter.chapterId, out var existing) && existing.root != null)
            { onDone?.Invoke(existing); yield break; }

            var bp = chapter.site;
            var kind = SiteKitCatalog.Kind(bp.siteKind);
            var rng = new System.Random(chapter.assemblySeed);

            int slot = _freeSlots.Count > 0 ? _freeSlots.Pop() : _nextSlot++;
            var inst = new SiteInstance
            {
                chapterId = chapter.chapterId,
                siteId = "site_" + chapter.chapterId,
                slot = slot,
                origin = new Vector3(SiteOriginX + slot * SiteSpacing, 0f, 0f),
                blueprint = bp
            };

            inst.root = new GameObject("Site_" + bp.siteName);
            inst.root.transform.position = inst.origin;

            float scale = bp.sizeHint == "large" ? 1.35f : bp.sizeHint == "small" ? 0.75f : 1f;
            // 户外场景再放大一截：同样的 60×46 放在街上就是个院子，
            // 玩家要的"开阔地带"得真的开阔才成立
            if (!kind.indoor) scale *= 1.6f;
            float w = 60f * scale, d = 46f * scale;

            // ---- 周边街区：先把"这地方在城市里"建出来 ----
            // 只建一个盒子的话，玩家推门进去看到的是悬在黑里的一间房——
            // 那不是"模拟真实世界的场景"，那是一个测试关。先铺地、再起楼、再点灯。
            BuildSurroundings(inst, kind, rng, w, d);
            yield return null;

            // ---- 地面与外壳 ----
            Box(inst, "Site_Floor", new Vector3(0, -0.25f, 0), new Vector3(w + 8f, 0.5f, d + 8f), Floor);
            if (kind.indoor)
            {
                Shell(inst, w, d, 4.2f);
                Ceiling(inst, w, d, 4.2f);
            }
            else
            {
                // 户外不砌墙。四面矮墙会把"开阔地带"变成一个院子——
                // 边界改由外圈的临街楼与护栏段落承担，视线始终是通的。
                OpenEdge(inst, w, d);
            }
            yield return null;

            // ---- 布局 ----
            switch (bp.layout)
            {
                case "corridor": yield return BuildCorridor(inst, bp, rng, w, d); break;
                case "maze": yield return BuildMaze(inst, bp, rng, w, d); break;
                case "hall": yield return BuildHall(inst, bp, rng, w, d); break;
                case "openblock": yield return BuildOpenBlock(inst, bp, rng, w, d); break;
                case "courtyard": yield return BuildCourtyard(inst, bp, rng, w, d); break;
                default: yield return BuildRooms(inst, bp, rng, w, d); break;
            }

            // ---- 场景陈设：让它一眼看得出是"哪种地方" ----
            Furnish(inst, kind, rng, w, d);
            yield return null;

            // ---- 灯光与氛围 ----
            ApplyAmbience(inst, bp, kind, w, d);
            yield return null;

            // ---- 出入口 ----
            inst.playerSpawn = inst.origin + new Vector3(0, 1.1f, -d / 2 + 4f);
            inst.exitPoint = inst.origin + new Vector3(0, 0, -d / 2 + 1.5f);
            BuildEntranceMarker(inst, bp);

            // ---- 注册为动态区域（可传送、有名字、有雾色） ----
            inst.zoneIndex = ZoneBuilder.RegisterDynamicZone(inst.siteId, bp.siteName, inst.playerSpawn);

            // ---- 本场景独立烘焙导航（不碰主世界的 NavMesh） ----
            BakeNav(inst);
            yield return null;

            // ---- 人与敌人（导航好了才放） ----
            SpawnNpcs(inst, bp, rng, ctx);

            _live[chapter.chapterId] = inst;

            // 建完点一遍数：构件/灯/落点脚下有没有地。
            // 这处场景在 x=20000 之外，出问题时截图只有一片黑，光看画面判断不了
            // 是"没建出来"还是"建出来了但没光"——所以把可验证的数字打进日志。
            int parts = inst.root.GetComponentsInChildren<Renderer>(true).Length;
            int lamps = inst.root.GetComponentsInChildren<Light>(true).Length;
            Physics.SyncTransforms();
            bool grounded = Physics.Raycast(inst.playerSpawn + Vector3.up * 2f, Vector3.down,
                out RaycastHit gh, 30f, ~0, QueryTriggerInteraction.Ignore);
            Core.CloudDialogueService.AddLog("场景已生成：" + bp.siteName + "（" + kind.name + "·" +
                SiteKitCatalog.LayoutName(bp.layout) + "）构件 " + parts + " · 灯 " + lamps +
                " · 房间 " + bp.rooms.Count + " · NPC " + bp.npcs.Count +
                " · 落点" + (grounded ? "脚下有地(" + gh.collider.name + ")" : "⚠ 脚下悬空") +
                " · seed " + chapter.assemblySeed + " → 动态区域 #" + inst.zoneIndex);

            // 落点台子**无条件**补一块：一块 10×10 的板子几乎不要钱，
            // 而"玩家一进来就往下掉"是最劝退的一种失败，不值得为了省它去赌。
            // 顶面必须和主地板齐平（落点 y=1.1，板厚 0.6 → 中心 -0.3 时顶面正好 0）。
            // 差 5 公分就是一道台阶，走过去会被绊、动画也会抖。
            Box(inst, "GroundPad_Spawn",
                inst.root.transform.InverseTransformPoint(inst.playerSpawn) + new Vector3(0, -1.4f, 0),
                new Vector3(10f, 0.6f, 10f), Floor);

            onDone?.Invoke(inst);
        }

        /// <summary>卸载一个生成出来的场景（章节通关或玩家离开后回收）。</summary>
        public static void Unload(string chapterId)
        {
            if (chapterId == null || !_live.TryGetValue(chapterId, out var inst)) return;
            if (inst.root != null) Object.Destroy(inst.root);
            if (inst.slot >= 0) _freeSlots.Push(inst.slot);
            _live.Remove(chapterId);
        }

        public static void UnloadAll()
        {
            foreach (var kv in _live)
                if (kv.Value.root != null) Object.Destroy(kv.Value.root);
            _live.Clear();
            _freeSlots.Clear();
            _nextSlot = 0;
        }

        // ================= 布局生成器 =================

        /// <summary>房间群：中央走廊两侧开房间——办公层、公寓、病房区最常见的形状。</summary>
        static IEnumerator BuildRooms(SiteInstance inst, SiteBlueprint bp, System.Random rng, float w, float d)
        {
            int count = Mathf.Max(2, Mathf.Min(bp.rooms.Count, 6));
            float corridorW = 6f;
            float roomD = (d - corridorW) / 2f - 2f;
            float roomW = (w - 4f) / Mathf.Ceil(count / 2f);

            for (int i = 0; i < count; i++)
            {
                bool north = i % 2 == 0;
                int col = i / 2;
                float cx = -w / 2f + 2f + roomW * (col + 0.5f);
                float cz = north ? (corridorW / 2f + roomD / 2f) : -(corridorW / 2f + roomD / 2f);
                var room = bp.rooms[i % bp.rooms.Count];
                BuildRoomBox(inst, room, new Vector3(cx, 0, cz), new Vector2(roomW - 1.5f, roomD),
                    north, rng);
                yield return null;   // 一帧一间房：整片场景不再卡在同一帧里建完
            }

            // 走廊地面标线
            Deco(inst, "CorridorLine", new Vector3(0, 0.06f, 0), new Vector3(w - 4f, 0.04f, 0.3f),
                new Color(0.8f, 0.78f, 0.7f));
            inst.enemySpawns.Add(inst.origin + new Vector3(w * 0.25f, 1.1f, 0));
            inst.enemySpawns.Add(inst.origin + new Vector3(-w * 0.2f, 1.1f, 0));
        }

        /// <summary>长廊：一条走不完的通道，两侧是门——无限代付走廊那一类的通用形状。</summary>
        static IEnumerator BuildCorridor(SiteInstance inst, SiteBlueprint bp, System.Random rng, float w, float d)
        {
            float hallW = 9f;
            Box(inst, "Wall", new Vector3(0, 2.1f, hallW / 2f), new Vector3(w, 4.2f, 0.5f), Wall);
            Box(inst, "Wall", new Vector3(0, 2.1f, -hallW / 2f), new Vector3(w, 4.2f, 0.5f), Wall);

            int doors = Mathf.Max(4, Mathf.Min(bp.rooms.Count * 2, 10));
            for (int i = 0; i < doors; i++)
            {
                float x = -w / 2f + 4f + (w - 8f) * i / Mathf.Max(1, doors - 1);
                bool north = i % 2 == 0;
                float z = north ? hallW / 2f - 0.4f : -hallW / 2f + 0.4f;
                Deco(inst, "DoorFrame", new Vector3(x, 1.4f, z), new Vector3(1.8f, 2.8f, 0.18f), Trim);
                var room = bp.rooms.Count > 0 ? bp.rooms[i % bp.rooms.Count] : null;
                Sign(inst, new Vector3(x, 3.1f, z), room != null ? room.name : "门");
                if (room != null && room.props.Count > 0)
                    BuildProp(inst, room.props[i % room.props.Count],
                        new Vector3(x, 0, north ? hallW / 2f - 2.2f : -hallW / 2f + 2.2f), rng);
                if (i % 3 == 2) yield return null;
            }
            for (int i = 0; i < 3; i++)
                inst.enemySpawns.Add(inst.origin + new Vector3(-w / 4f + i * w / 4f, 1.1f, 0));
        }

        /// <summary>迷宫：分叉与死路——「需求迷宫」「概念迷环」这类障碍的通用形状。</summary>
        static IEnumerator BuildMaze(SiteInstance inst, SiteBlueprint bp, System.Random rng, float w, float d)
        {
            int cols = 7, rows = 5;
            float cw = w / cols, ch = d / rows;
            for (int x = 0; x < cols; x++)
                for (int z = 0; z < rows; z++)
                {
                    if ((x + z) % 2 != 0) continue;
                    if (rng.Next(100) < 30) continue;      // 留出通路
                    float px = -w / 2f + cw * (x + 0.5f);
                    float pz = -d / 2f + ch * (z + 0.5f);
                    bool horizontal = rng.Next(2) == 0;
                    Box(inst, "Wall", new Vector3(px, 1.6f, pz),
                        horizontal ? new Vector3(cw * 0.9f, 3.2f, 0.5f)
                                   : new Vector3(0.5f, 3.2f, ch * 0.9f), Wall);
                    if (z == 0) yield return null;
                }

            int propIdx = 0;
            foreach (var room in bp.rooms)
                foreach (var p in room.props)
                {
                    float px = -w / 2f + 4f + (float)rng.NextDouble() * (w - 8f);
                    float pz = -d / 2f + 4f + (float)rng.NextDouble() * (d - 8f);
                    BuildProp(inst, p, new Vector3(px, 0, pz), rng);
                    if (propIdx++ > 12) break;
                    if (propIdx % 4 == 0) yield return null;
                }
            inst.enemySpawns.Add(inst.origin + new Vector3(0, 1.1f, d / 4f));
            inst.enemySpawns.Add(inst.origin + new Vector3(w / 4f, 1.1f, -d / 4f));
        }

        /// <summary>大厅：一个开阔空间 + 立柱 + 中央焦点——Boss 战与集会场景。</summary>
        static IEnumerator BuildHall(SiteInstance inst, SiteBlueprint bp, System.Random rng, float w, float d)
        {
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && z == 0) continue;
                    BuildProp(inst, "pillar", new Vector3(x * w * 0.28f, 0, z * d * 0.28f), rng);
                    yield return null;
                }

            if (bp.rooms.Count > 0)
            {
                var focus = bp.rooms[0];
                Deco(inst, "Dais", new Vector3(0, 0.15f, d * 0.22f), new Vector3(12f, 0.3f, 8f),
                    new Color(0.38f, 0.36f, 0.4f));
                Sign(inst, new Vector3(0, 3.4f, d * 0.22f), focus.name);
                foreach (var p in focus.props)
                    BuildProp(inst, p, new Vector3((float)rng.NextDouble() * 8f - 4f, 0, d * 0.22f), rng);
            }
            for (int i = 1; i < bp.rooms.Count && i < 4; i++)
            {
                float ang = i * Mathf.PI * 2f / 4f;
                Vector3 at = new Vector3(Mathf.Cos(ang) * w * 0.3f, 0, Mathf.Sin(ang) * d * 0.3f);
                foreach (var p in bp.rooms[i].props) BuildProp(inst, p, at, rng);
                Sign(inst, at + Vector3.up * 3f, bp.rooms[i].name);
                yield return null;
            }
            inst.enemySpawns.Add(inst.origin + new Vector3(0, 1.1f, d * 0.18f));
            inst.enemySpawns.Add(inst.origin + new Vector3(-8f, 1.1f, 0));
            inst.enemySpawns.Add(inst.origin + new Vector3(8f, 1.1f, 0));
        }

        /// <summary>开放街区：户外——路面、人行道、两侧建筑、路灯。</summary>
        static IEnumerator BuildOpenBlock(SiteInstance inst, SiteBlueprint bp, System.Random rng, float w, float d)
        {
            Deco(inst, "Road", new Vector3(0, 0.03f, 0), new Vector3(w, 0.04f, 10f),
                new Color(0.19f, 0.19f, 0.21f));
            for (float x = -w / 2f + 4f; x < w / 2f; x += 7f)
                Deco(inst, "Lane", new Vector3(x, 0.06f, 0), new Vector3(2.6f, 0.04f, 0.3f), Color.white);
            Deco(inst, "Sidewalk", new Vector3(0, 0.06f, 8f), new Vector3(w, 0.08f, 6f),
                new Color(0.55f, 0.55f, 0.57f));
            Deco(inst, "Sidewalk", new Vector3(0, 0.06f, -8f), new Vector3(w, 0.08f, 6f),
                new Color(0.55f, 0.55f, 0.57f));

            int n = Mathf.Max(3, Mathf.Min(bp.rooms.Count, 6));
            for (int i = 0; i < n; i++)
            {
                float x = -w / 2f + 6f + (w - 12f) * i / Mathf.Max(1, n - 1);
                bool north = i % 2 == 0;
                float z = north ? d / 2f - 8f : -d / 2f + 8f;
                float h = 8f + (float)rng.NextDouble() * 10f;
                Box(inst, "Building", new Vector3(x, h / 2f, z), new Vector3(11f, h, 10f),
                    new Color(0.45f + (float)rng.NextDouble() * 0.2f, 0.44f, 0.46f));
                Deco(inst, "ShopGlass", new Vector3(x, 1.7f, z + (north ? -5.1f : 5.1f)),
                    new Vector3(7.5f, 2.6f, 0.12f), new Color(0.65f, 0.8f, 0.95f));
                var room = bp.rooms.Count > 0 ? bp.rooms[i % bp.rooms.Count] : null;
                if (room != null)
                {
                    Sign(inst, new Vector3(x, 4.2f, z + (north ? -5.3f : 5.3f)), room.name);
                    foreach (var p in room.props)
                        BuildProp(inst, p, new Vector3(x + (float)rng.NextDouble() * 4f - 2f, 0,
                            north ? z - 7f : z + 7f), rng);
                }
                Lamp(inst, new Vector3(x, 0, north ? 8f : -8f));
                yield return null;
            }
            inst.enemySpawns.Add(inst.origin + new Vector3(w * 0.2f, 1.1f, 0));
            inst.enemySpawns.Add(inst.origin + new Vector3(-w * 0.25f, 1.1f, 6f));
        }

        /// <summary>围合院落：四面建筑围出一个中庭——最适合"被围观"「被评价」的空间。</summary>
        static IEnumerator BuildCourtyard(SiteInstance inst, SiteBlueprint bp, System.Random rng, float w, float d)
        {
            float inner = 0.55f;
            Box(inst, "Building", new Vector3(0, 5f, d * inner / 2f + 6f), new Vector3(w, 10f, 10f), Wall);
            Box(inst, "Building", new Vector3(0, 5f, -(d * inner / 2f + 6f)), new Vector3(w, 10f, 10f), Wall);
            Box(inst, "Building", new Vector3(w * inner / 2f + 6f, 5f, 0), new Vector3(10f, 10f, d), Wall);
            Box(inst, "Building", new Vector3(-(w * inner / 2f + 6f), 5f, 0), new Vector3(10f, 10f, d), Wall);

            Deco(inst, "Yard", new Vector3(0, 0.05f, 0), new Vector3(w * inner, 0.06f, d * inner),
                new Color(0.42f, 0.44f, 0.4f));

            int i = 0;
            foreach (var room in bp.rooms)
            {
                float ang = i * Mathf.PI * 2f / Mathf.Max(1, bp.rooms.Count);
                Vector3 at = new Vector3(Mathf.Cos(ang) * w * 0.2f, 0, Mathf.Sin(ang) * d * 0.2f);
                Sign(inst, at + Vector3.up * 2.6f, room.name);
                foreach (var p in room.props) BuildProp(inst, p, at, rng);
                i++;
                yield return null;
            }
            inst.enemySpawns.Add(inst.origin + new Vector3(0, 1.1f, 0));
            inst.enemySpawns.Add(inst.origin + new Vector3(w * 0.15f, 1.1f, d * 0.12f));
        }

        /// <summary>一个带墙与门洞的房间 + 它自己的道具。</summary>
        static void BuildRoomBox(SiteInstance inst, SiteRoom room, Vector3 center, Vector2 size,
            bool doorSouth, System.Random rng)
        {
            float hx = size.x / 2f, hz = size.y / 2f;
            const float h = 3.4f;
            // 三面实墙 + 一面留门洞
            Box(inst, "Wall", center + new Vector3(hx, h / 2f, 0), new Vector3(0.35f, h, size.y), Wall);
            Box(inst, "Wall", center + new Vector3(-hx, h / 2f, 0), new Vector3(0.35f, h, size.y), Wall);
            float far = doorSouth ? hz : -hz;
            Box(inst, "Wall", center + new Vector3(0, h / 2f, far), new Vector3(size.x, h, 0.35f), Wall);

            float near = doorSouth ? -hz : hz;
            float side = (size.x - 2.4f) / 2f;
            Box(inst, "Wall", center + new Vector3(-(size.x - side) / 2f, h / 2f, near),
                new Vector3(side, h, 0.35f), Wall);
            Box(inst, "Wall", center + new Vector3((size.x - side) / 2f, h / 2f, near),
                new Vector3(side, h, 0.35f), Wall);
            Deco(inst, "DoorHead", center + new Vector3(0, h - 0.35f, near),
                new Vector3(2.4f, 0.7f, 0.35f), Wall);

            Sign(inst, center + new Vector3(0, h + 0.5f, near), room.name);

            // 道具沿墙摆，中间留出走位空间
            int n = 0;
            foreach (var p in room.props)
            {
                float px = -hx + 1.6f + (size.x - 3.2f) * (n % 3) / 2f;
                float pz = (doorSouth ? hz - 1.8f : -hz + 1.8f) * (n < 3 ? 1f : 0.35f);
                BuildProp(inst, p, center + new Vector3(px, 0, pz), rng);
                if (++n >= 6) break;
            }
            inst.propAnchors.Add(inst.origin + center);
        }

        // ================= 道具库 =================

        /// <summary>已批准道具 → 程序化构件。库外 id 直接忽略（Validator 已先清洗过一遍）。</summary>
        static void BuildProp(SiteInstance inst, string prop, Vector3 at, System.Random rng)
        {
            switch (prop)
            {
                case "desk":
                    Box(inst, "Desk", at + new Vector3(0, 0.5f, 0), new Vector3(2.2f, 1f, 1.1f), new Color(0.42f, 0.3f, 0.2f));
                    Deco(inst, "Monitor", at + new Vector3(0, 1.35f, 0.2f), new Vector3(1f, 0.6f, 0.1f), new Color(0.2f, 0.3f, 0.42f));
                    break;
                case "chair":
                    Box(inst, "Chair", at + new Vector3(0, 0.35f, 0), new Vector3(0.8f, 0.7f, 0.8f), new Color(0.25f, 0.25f, 0.28f));
                    break;
                case "table":
                    Box(inst, "Table", at + new Vector3(0, 0.45f, 0), new Vector3(3.2f, 0.9f, 1.6f), new Color(0.4f, 0.32f, 0.26f));
                    break;
                case "shelf":
                    Box(inst, "Shelf", at + new Vector3(0, 1.2f, 0), new Vector3(2.4f, 2.4f, 0.6f), new Color(0.45f, 0.33f, 0.22f));
                    break;
                case "cabinet":
                    Box(inst, "Cabinet", at + new Vector3(0, 1f, 0), new Vector3(1.2f, 2f, 0.7f), new Color(0.4f, 0.4f, 0.44f));
                    break;
                case "locker":
                    Box(inst, "Locker", at + new Vector3(0, 1.1f, 0), new Vector3(1.6f, 2.2f, 0.6f), new Color(0.36f, 0.42f, 0.46f));
                    break;
                case "server_rack":
                    Box(inst, "ServerRack", at + new Vector3(0, 1.2f, 0), new Vector3(1f, 2.4f, 1f), new Color(0.16f, 0.17f, 0.2f));
                    Deco(inst, "RackLight", at + new Vector3(0, 1.8f, 0.55f), new Vector3(0.7f, 0.9f, 0.06f), new Color(0.3f, 0.9f, 0.5f));
                    break;
                case "monitor":
                    Deco(inst, "Monitor", at + new Vector3(0, 1.4f, 0), new Vector3(1.2f, 0.7f, 0.1f), new Color(0.25f, 0.4f, 0.55f));
                    break;
                case "whiteboard":
                    Deco(inst, "Whiteboard", at + new Vector3(0, 1.7f, 0), new Vector3(3f, 1.8f, 0.12f), new Color(0.92f, 0.92f, 0.9f));
                    break;
                case "printer":
                    Box(inst, "Printer", at + new Vector3(0, 0.5f, 0), new Vector3(1.1f, 1f, 0.9f), new Color(0.55f, 0.56f, 0.58f));
                    break;
                case "counter":
                    Box(inst, "Counter", at + new Vector3(0, 0.55f, 0), new Vector3(4f, 1.1f, 1.2f), new Color(0.5f, 0.5f, 0.55f));
                    break;
                case "sofa":
                    Box(inst, "Sofa", at + new Vector3(0, 0.4f, 0), new Vector3(3.2f, 0.8f, 1.4f), new Color(0.34f, 0.36f, 0.44f));
                    break;
                case "bed":
                    Box(inst, "Bed", at + new Vector3(0, 0.35f, 0), new Vector3(2f, 0.7f, 3.6f), new Color(0.72f, 0.74f, 0.78f));
                    break;
                case "curtain":
                    Deco(inst, "Curtain", at + new Vector3(0, 1.6f, 0), new Vector3(2.6f, 3.2f, 0.1f), new Color(0.7f, 0.78f, 0.8f));
                    break;
                case "crate":
                    Box(inst, "Crate", at + new Vector3(0, 0.6f, 0), new Vector3(1.2f, 1.2f, 1.2f), new Color(0.5f, 0.38f, 0.24f));
                    break;
                case "barrier":
                    Box(inst, "Barrier", at + new Vector3(0, 0.6f, 0), new Vector3(2.6f, 1.2f, 0.3f), new Color(0.85f, 0.6f, 0.2f));
                    break;
                case "trashbin":
                    Box(inst, "TrashBin", at + new Vector3(0, 0.55f, 0), new Vector3(0.9f, 1.1f, 0.9f), new Color(0.24f, 0.3f, 0.26f));
                    break;
                case "plant":
                    Box(inst, "PlantPot", at + new Vector3(0, 0.3f, 0), new Vector3(0.7f, 0.6f, 0.7f), new Color(0.45f, 0.35f, 0.3f));
                    Deco(inst, "Leaves", at + new Vector3(0, 1.1f, 0), new Vector3(1.2f, 1.2f, 1.2f), new Color(0.22f, 0.45f, 0.25f));
                    break;
                case "pillar":
                    Box(inst, "Pillar", at + new Vector3(0, 2.1f, 0), new Vector3(1.3f, 4.2f, 1.3f), new Color(0.5f, 0.5f, 0.52f));
                    break;
                case "sign":
                    Deco(inst, "SignBoard", at + new Vector3(0, 2.2f, 0), new Vector3(2.2f, 0.8f, 0.12f), new Color(0.3f, 0.45f, 0.6f));
                    break;
                case "bench":
                    Box(inst, "Bench", at + new Vector3(0, 0.45f, 0), new Vector3(3f, 0.25f, 1f), new Color(0.5f, 0.36f, 0.24f));
                    break;
                case "vending":
                    Box(inst, "Vending", at + new Vector3(0, 1.05f, 0), new Vector3(1.4f, 2.1f, 0.9f), new Color(0.3f, 0.5f, 0.65f));
                    break;
                case "cart":
                    Box(inst, "Cart", at + new Vector3(0, 0.5f, 0), new Vector3(1.4f, 1f, 2f), new Color(0.55f, 0.55f, 0.6f));
                    break;
                case "pipe":
                    Deco(inst, "Pipe", at + new Vector3(0, 3.6f, 0), new Vector3(0.4f, 0.4f, 12f), new Color(0.42f, 0.42f, 0.46f));
                    break;
                case "fence":
                    Box(inst, "Fence", at + new Vector3(0, 1f, 0), new Vector3(6f, 2f, 0.2f), new Color(0.4f, 0.42f, 0.45f));
                    break;
                case "billboard":
                    Box(inst, "BillboardPole", at + new Vector3(0, 2f, 0), new Vector3(0.3f, 4f, 0.3f), Trim);
                    Deco(inst, "Billboard", at + new Vector3(0, 4.6f, 0), new Vector3(5f, 2.4f, 0.2f), new Color(0.8f, 0.7f, 0.4f));
                    break;
                case "stall":
                    Box(inst, "Stall", at + new Vector3(0, 1f, 0), new Vector3(3f, 2f, 2f), new Color(0.6f, 0.5f, 0.35f));
                    break;
                case "car":
                    Box(inst, "Car", at + new Vector3(0, 0.6f, 0), new Vector3(1.9f, 1.2f, 4.2f), new Color(0.4f, 0.42f, 0.5f));
                    break;
                case "lamp":
                    Lamp(inst, at);
                    break;
                case "door_frame":
                    Deco(inst, "DoorFrame", at + new Vector3(0, 1.4f, 0), new Vector3(1.8f, 2.8f, 0.18f), Trim);
                    break;
                case "stairs":
                    for (int i = 0; i < 6; i++)
                        Box(inst, "Step", at + new Vector3(0, 0.2f + i * 0.35f, i * 0.6f),
                            new Vector3(3f, 0.35f, 0.6f), new Color(0.48f, 0.48f, 0.5f));
                    break;
                case "papers":
                    Deco(inst, "Papers", at + new Vector3(0, 0.05f, 0), new Vector3(2.4f, 0.05f, 2.4f),
                        new Color(0.88f, 0.86f, 0.8f));
                    break;
            }
        }

        // ================= 氛围 / 出入口 / 人 =================

        static void ApplyAmbience(SiteInstance inst, SiteBlueprint bp,
            SiteKitCatalog.SiteKindInfo kind, float w, float d)
        {
            Color c;
            float intensity;
            switch (bp.ambience)
            {
                case "day": c = new Color(1f, 0.97f, 0.9f); intensity = 1.5f; break;
                case "dusk": c = new Color(1f, 0.75f, 0.55f); intensity = 1.1f; break;
                case "night": c = new Color(0.55f, 0.65f, 0.9f); intensity = 0.65f; break;
                case "rain": c = new Color(0.7f, 0.78f, 0.9f); intensity = 0.85f; break;
                case "indoor_warm": c = new Color(1f, 0.88f, 0.68f); intensity = 1.2f; break;
                case "flicker": c = new Color(0.85f, 0.9f, 1f); intensity = 0.9f; break;
                case "fog": c = new Color(0.75f, 0.78f, 0.82f); intensity = 0.8f; break;
                default: c = new Color(0.85f, 0.92f, 1f); intensity = 1.0f; break;   // indoor_cold
            }

            // 灯按网格铺满，而不是四个角各放一盏。
            //
            // 室内场景有天花板，主光（太阳）一点都照不进来——可见度**全部**由这些点光承担。
            // 原来一间 60×46 的屋子只放四盏 range=26 的灯，中段和四周直接是黑的，
            // 玩家走进去看到的就是"一片黑乎乎，什么都没有"。
            // URP 每个物体最多取 4 盏附加光，所以铺得密不会更贵，只会让每块地板
            // 都能挑到离它最近的那几盏。
            float step = kind.indoor ? 15f : 24f;
            int nx = Mathf.Max(2, Mathf.CeilToInt(w / step));
            int nz = Mathf.Max(2, Mathf.CeilToInt(d / step));
            int idx = 0;
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    float fx = (ix + 0.5f) / nx - 0.5f;
                    float fz = (iz + 0.5f) / nz - 0.5f;
                    var go = new GameObject("SiteLight" + idx++);
                    go.transform.SetParent(inst.root.transform, false);
                    go.transform.localPosition = new Vector3(
                        fx * w * 0.95f, kind.indoor ? 3.7f : 7f, fz * d * 0.95f);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.range = kind.indoor ? step * 1.9f : step * 2.4f;
                    l.intensity = intensity;
                    l.color = c;
                    if (bp.ambience == "flicker") go.AddComponent<SiteFlicker>();

                    // 看得见的灯具：光源本身不可见时，天花板会显得"莫名其妙地亮"
                    if (kind.indoor)
                        Deco(inst, "CeilPanel", go.transform.localPosition + new Vector3(0, 0.45f, 0),
                            new Vector3(2.4f, 0.12f, 1.2f), c);
                }

            // 补一盏斜向平行光：纯点光下墙面明暗过渡很硬，物体读不出体积。
            // 只作用于这处场景的观感，不参与主世界昼夜。
            var fillGo = new GameObject("SiteFill");
            fillGo.transform.SetParent(inst.root.transform, false);
            fillGo.transform.localPosition = new Vector3(0, 12f, 0);
            fillGo.transform.localRotation = Quaternion.Euler(52f, 34f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = kind.indoor ? 0.35f : 0.9f;
            fill.color = c;
            fill.shadows = LightShadows.None;
        }

        static void BuildEntranceMarker(SiteInstance inst, SiteBlueprint bp)
        {
            float localZ = inst.exitPoint.z - inst.origin.z;
            Sign(inst, new Vector3(0, 4.2f, localZ), "◀ " + bp.siteName + " · 出口");
            Deco(inst, "ExitPad", new Vector3(0, 0.07f, localZ),
                new Vector3(5f, 0.06f, 3f), new Color(0.4f, 0.8f, 0.6f));
        }

        /// <summary>只烘焙本场景（Children 收集）：不动主世界导航，卸载时一起消失。</summary>
        static void BakeNav(SiteInstance inst)
        {
            var surface = inst.root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        static void SpawnNpcs(SiteInstance inst, SiteBlueprint bp, System.Random rng, WorldContext ctx)
        {
            foreach (var npc in bp.npcs)
            {
                int n = Mathf.Clamp(npc.count, 1, 6);
                for (int i = 0; i < n; i++)
                {
                    Vector3 want = inst.origin + new Vector3(
                        (float)rng.NextDouble() * 40f - 20f, 1f, (float)rng.NextDouble() * 30f - 15f);
                    if (!NavMesh.SamplePosition(want, out var hit, 8f, NavMesh.AllAreas)) continue;

                    // 和城区市民、经典关卡路人同一套人形骨骼——AI 生成的场景不能一走进去
                    // 就掉档成胶囊人，那等于告诉玩家"这块地方是临时糊的"
                    var go = ZoneBuilder.MakeHumanoidNpc(ctx,
                        "Npc_" + SiteKitCatalog.NpcRoleName(npc.roleType), hit.position, rng);
                    go.transform.SetParent(inst.root.transform, true);

                    Vector3 station = hit.position;
                    Vector3 roam = inst.origin + new Vector3(
                        (float)rng.NextDouble() * 30f - 15f, 0, (float)rng.NextDouble() * 20f - 10f);
                    CityNpc.Attach(go, NpcKind.Ordinary, station,
                        npc.behavior == "station" ? station : roam,
                        npc.behavior == "patrol" ? roam : station);

                    if (i == 0 && !string.IsNullOrEmpty(npc.line))
                        SiteAmbientLine.Attach(go, SiteKitCatalog.NpcRoleName(npc.roleType), npc.line);
                }
            }
        }

        // ================= 构件辅助（全部挂在 site root 下，可整体卸载） =================

        static GameObject Box(SiteInstance inst, string name, Vector3 local, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(inst.root.transform, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            PaintLocal(go, color);
            return go;
        }

        static GameObject Deco(SiteInstance inst, string name, Vector3 local, Vector3 size, Color color)
        {
            var go = Box(inst, name, local, size, color);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// <summary>
        /// 按场景类型铺一套**认得出来**的陈设。
        ///
        /// 【为什么必须有这一层】布局生成器只负责"空间形状"——长廊就是两面墙加几个门框，
        /// 大厅就是一个空盒子。玩家走进"地铁站·长廊"，看到的是两堵墙和几块飘着的标牌，
        /// 于是"场景里什么都没有，除了一个出口标记"。房间道具清单也救不了：
        /// 一间房最多摆六件，而一个地铁站需要的是闸机排、站台、长椅阵列、柱子和灯箱——
        /// 那是**场所的固有构成**，不该指望 AI 逐件列出来。
        ///
        /// 所以这里按 siteKind 给每一类地方配一套固有陈设。AI 决定"这是地铁站"，
        /// 引擎负责"地铁站长什么样"——分工和坐标那条约束是一致的。
        /// </summary>
        static void Furnish(SiteInstance inst, SiteKitCatalog.SiteKindInfo kind,
            System.Random rng, float w, float d)
        {
            float hw = w / 2f, hd = d / 2f;
            switch (kind.id)
            {
                case "subway":
                    // 站台 + 轨道沟 + 闸机排 + 长椅 + 柱列 + 灯箱
                    Box(inst, "Platform", new Vector3(0, 0.35f, hd * 0.55f),
                        new Vector3(w * 0.86f, 0.7f, d * 0.3f), new Color(0.5f, 0.49f, 0.47f));
                    Deco(inst, "TrackPit", new Vector3(0, 0.04f, -hd * 0.5f),
                        new Vector3(w * 0.86f, 0.08f, d * 0.22f), new Color(0.13f, 0.13f, 0.15f));
                    for (int i = -3; i <= 3; i++)
                        Deco(inst, "Rail", new Vector3(i * w * 0.12f, 0.1f, -hd * 0.5f),
                            new Vector3(0.5f, 0.12f, d * 0.2f), new Color(0.4f, 0.4f, 0.44f));
                    for (int i = 0; i < 5; i++)
                        Box(inst, "Gate", new Vector3(-w * 0.32f + i * w * 0.16f, 0.6f, 0),
                            new Vector3(0.7f, 1.2f, 2.4f), new Color(0.55f, 0.57f, 0.6f));
                    for (int i = 0; i < 4; i++)
                        BuildProp(inst, "bench", new Vector3(-w * 0.3f + i * w * 0.2f, 0, hd * 0.5f), rng);
                    for (int i = 0; i < 6; i++)
                        Box(inst, "Pillar", new Vector3(-w * 0.36f + i * w * 0.145f, 2.1f, hd * 0.2f),
                            new Vector3(1f, 4.2f, 1f), new Color(0.58f, 0.57f, 0.55f));
                    for (int i = 0; i < 4; i++)
                        Deco(inst, "AdBox", new Vector3(-w * 0.28f + i * w * 0.19f, 2.2f, hd - 1.2f),
                            new Vector3(3.4f, 2f, 0.15f), new Color(0.95f, 0.88f, 0.6f));
                    break;

                case "office_floor":
                case "meeting_room":
                    for (int x = 0; x < 4; x++)
                        for (int z = 0; z < 3; z++)
                        {
                            var at = new Vector3(-hw * 0.6f + x * w * 0.28f, 0, -hd * 0.5f + z * d * 0.34f);
                            BuildProp(inst, "desk", at, rng);
                            BuildProp(inst, "chair", at + new Vector3(0, 0, -1.4f), rng);
                            Deco(inst, "Divider", at + new Vector3(0, 0.9f, 1.1f),
                                new Vector3(2.4f, 1.4f, 0.12f), new Color(0.55f, 0.56f, 0.6f));
                        }
                    BuildProp(inst, "printer", new Vector3(hw * 0.7f, 0, 0), rng);
                    BuildProp(inst, "whiteboard", new Vector3(0, 0, hd - 1.4f), rng);
                    break;

                case "recruit_hall":
                case "waiting_area":
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 6; c++)
                            BuildProp(inst, "chair",
                                new Vector3(-hw * 0.6f + c * w * 0.2f, 0, -hd * 0.4f + r * 3.2f), rng);
                    for (int i = 0; i < 3; i++)
                        BuildProp(inst, "counter", new Vector3(-w * 0.25f + i * w * 0.25f, 0, hd * 0.7f), rng);
                    for (int i = 0; i < 8; i++)
                        BuildProp(inst, "barrier",
                            new Vector3(-hw * 0.55f + i * w * 0.14f, 0, hd * 0.35f), rng);
                    Deco(inst, "CallScreen", new Vector3(0, 3.2f, hd - 1f),
                        new Vector3(5f, 1.6f, 0.15f), new Color(0.2f, 0.55f, 0.75f));
                    break;

                case "server_room":
                    for (int x = 0; x < 4; x++)
                        for (int z = 0; z < 4; z++)
                            BuildProp(inst, "server_rack",
                                new Vector3(-hw * 0.55f + x * w * 0.28f, 0, -hd * 0.5f + z * d * 0.28f), rng);
                    break;

                case "archive":
                case "library_room":
                    for (int x = 0; x < 5; x++)
                        for (int z = 0; z < 3; z++)
                            BuildProp(inst, "shelf",
                                new Vector3(-hw * 0.6f + x * w * 0.24f, 0, -hd * 0.4f + z * d * 0.3f), rng);
                    BuildProp(inst, "table", new Vector3(0, 0, hd * 0.7f), rng);
                    break;

                case "classroom":
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 5; c++)
                            BuildProp(inst, "desk",
                                new Vector3(-hw * 0.55f + c * w * 0.22f, 0, -hd * 0.4f + r * 3.6f), rng);
                    BuildProp(inst, "whiteboard", new Vector3(0, 0, hd - 1.4f), rng);
                    BuildProp(inst, "table", new Vector3(0, 0, hd * 0.6f), rng);
                    break;

                case "apartment":
                case "studio":
                    BuildProp(inst, "bed", new Vector3(-hw * 0.6f, 0, hd * 0.5f), rng);
                    BuildProp(inst, "desk", new Vector3(hw * 0.55f, 0, hd * 0.5f), rng);
                    BuildProp(inst, "sofa", new Vector3(0, 0, -hd * 0.4f), rng);
                    BuildProp(inst, "shelf", new Vector3(hw * 0.7f, 0, -hd * 0.2f), rng);
                    BuildProp(inst, "table", new Vector3(0, 0, 0), rng);
                    BuildProp(inst, "plant", new Vector3(-hw * 0.7f, 0, -hd * 0.6f), rng);
                    break;

                case "hospital_ward":
                case "clinic":
                    for (int i = 0; i < 5; i++)
                    {
                        BuildProp(inst, "bed", new Vector3(-hw * 0.6f + i * w * 0.28f, 0, hd * 0.4f), rng);
                        BuildProp(inst, "curtain",
                            new Vector3(-hw * 0.6f + i * w * 0.28f + 1.6f, 0, hd * 0.4f), rng);
                    }
                    break;

                case "warehouse":
                case "factory":
                    for (int x = 0; x < 5; x++)
                        for (int z = 0; z < 3; z++)
                            BuildProp(inst, rng.Next(2) == 0 ? "crate" : "shelf",
                                new Vector3(-hw * 0.6f + x * w * 0.25f, 0, -hd * 0.4f + z * d * 0.3f), rng);
                    for (int i = 0; i < 4; i++)
                        BuildProp(inst, "pipe", new Vector3(-hw * 0.5f + i * w * 0.28f, 0, hd * 0.8f), rng);
                    break;

                case "parking":
                    for (int i = 0; i < 8; i++)
                        Deco(inst, "Slot", new Vector3(-hw * 0.7f + i * w * 0.18f, 0.05f, 0),
                            new Vector3(0.2f, 0.04f, d * 0.5f), new Color(0.85f, 0.85f, 0.8f));
                    for (int i = 0; i < 4; i++)
                        BuildProp(inst, "car", new Vector3(-hw * 0.55f + i * w * 0.3f, 0, hd * 0.2f), rng);
                    for (int i = 0; i < 6; i++)
                        Box(inst, "Pillar", new Vector3(-hw * 0.6f + i * w * 0.24f, 2.1f, -hd * 0.5f),
                            new Vector3(1.1f, 4.2f, 1.1f), new Color(0.5f, 0.5f, 0.52f));
                    break;

                // ---- 户外 ----
                case "street_block":
                case "market":
                    for (int i = 0; i < 6; i++)
                        BuildProp(inst, "stall", new Vector3(-hw * 0.65f + i * w * 0.24f, 0, hd * 0.35f), rng);
                    for (int i = 0; i < 3; i++)
                        BuildProp(inst, "car", new Vector3(-hw * 0.4f + i * w * 0.36f, 0, -hd * 0.55f), rng);
                    for (int i = 0; i < 5; i++)
                        BuildProp(inst, "bench", new Vector3(-hw * 0.5f + i * w * 0.24f, 0, -hd * 0.15f), rng);
                    for (int i = 0; i < 4; i++)
                        BuildProp(inst, "trashbin", new Vector3(-hw * 0.45f + i * w * 0.3f, 0, hd * 0.1f), rng);
                    break;

                case "crossroad":
                    Deco(inst, "CrossNS", new Vector3(0, 0.05f, 0), new Vector3(14f, 0.04f, d * 0.95f),
                        new Color(0.2f, 0.2f, 0.22f));
                    Deco(inst, "CrossEW", new Vector3(0, 0.05f, 0), new Vector3(w * 0.95f, 0.04f, 14f),
                        new Color(0.2f, 0.2f, 0.22f));
                    for (int i = -4; i <= 4; i++)
                    {
                        Deco(inst, "Zebra", new Vector3(i * 1.8f, 0.07f, 9f),
                            new Vector3(1.1f, 0.05f, 6f), new Color(0.9f, 0.9f, 0.86f));
                        Deco(inst, "Zebra", new Vector3(9f, 0.07f, i * 1.8f),
                            new Vector3(6f, 0.05f, 1.1f), new Color(0.9f, 0.9f, 0.86f));
                    }
                    for (int sx = -1; sx <= 1; sx += 2)
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            Box(inst, "Signal", new Vector3(sx * 9f, 2.6f, sz * 9f),
                                new Vector3(0.35f, 5.2f, 0.35f), Trim);
                            Deco(inst, "SignalHead", new Vector3(sx * 9f, 5f, sz * 9f),
                                new Vector3(0.7f, 1.6f, 0.7f), new Color(0.9f, 0.35f, 0.3f));
                        }
                    for (int i = 0; i < 3; i++)
                        BuildProp(inst, "car", new Vector3(-hw * 0.5f + i * w * 0.4f, 0, -hd * 0.7f), rng);
                    break;

                case "rooftop":
                    for (int i = 0; i < 3; i++)
                        BuildProp(inst, "crate", new Vector3(-hw * 0.4f + i * w * 0.35f, 0, hd * 0.4f), rng);
                    for (int i = 0; i < 4; i++)
                        BuildProp(inst, "pipe", new Vector3(-hw * 0.5f + i * w * 0.28f, 0, -hd * 0.4f), rng);
                    Box(inst, "WaterTank", new Vector3(hw * 0.5f, 2.2f, 0), new Vector3(6f, 4.4f, 6f),
                        new Color(0.55f, 0.52f, 0.48f));
                    Box(inst, "Stairhead", new Vector3(-hw * 0.55f, 1.6f, -hd * 0.2f),
                        new Vector3(7f, 3.2f, 6f), new Color(0.45f, 0.45f, 0.48f));
                    break;

                case "park":
                    for (int i = 0; i < 10; i++)
                    {
                        float a = i / 10f * Mathf.PI * 2f;
                        var at = new Vector3(Mathf.Cos(a) * hw * 0.6f, 0, Mathf.Sin(a) * hd * 0.6f);
                        Box(inst, "Trunk", at + new Vector3(0, 1.6f, 0), new Vector3(0.6f, 3.2f, 0.6f),
                            new Color(0.35f, 0.26f, 0.18f));
                        Deco(inst, "Crown", at + new Vector3(0, 4.2f, 0), new Vector3(4.5f, 3.4f, 4.5f),
                            new Color(0.2f, 0.42f, 0.24f));
                    }
                    Deco(inst, "Path", new Vector3(0, 0.05f, 0), new Vector3(w * 0.8f, 0.04f, 5f),
                        new Color(0.62f, 0.58f, 0.5f));
                    for (int i = 0; i < 5; i++)
                        BuildProp(inst, "bench", new Vector3(-hw * 0.5f + i * w * 0.24f, 0, 4.2f), rng);
                    break;

                case "alley":
                    for (int i = 0; i < 5; i++)
                    {
                        BuildProp(inst, "trashbin", new Vector3(-hw * 0.6f + i * w * 0.26f, 0, hd * 0.6f), rng);
                        BuildProp(inst, "crate", new Vector3(-hw * 0.5f + i * w * 0.26f, 0, -hd * 0.6f), rng);
                    }
                    for (int i = 0; i < 4; i++)
                        Box(inst, "AcUnit", new Vector3(-hw * 0.5f + i * w * 0.3f, 3.2f, hd * 0.85f),
                            new Vector3(1.6f, 1.2f, 1f), new Color(0.5f, 0.5f, 0.54f));
                    break;

                case "mall":
                    for (int i = 0; i < 4; i++)
                    {
                        Box(inst, "ShopFront", new Vector3(-hw * 0.6f + i * w * 0.4f, 2f, hd * 0.7f),
                            new Vector3(w * 0.22f, 4f, 5f), new Color(0.6f, 0.58f, 0.56f));
                        Deco(inst, "ShopGlass", new Vector3(-hw * 0.6f + i * w * 0.4f, 1.8f, hd * 0.7f - 2.6f),
                            new Vector3(w * 0.18f, 3f, 0.12f), new Color(0.75f, 0.86f, 0.95f));
                    }
                    for (int i = 0; i < 6; i++)
                        BuildProp(inst, "bench", new Vector3(-hw * 0.55f + i * w * 0.22f, 0, -hd * 0.3f), rng);
                    BuildProp(inst, "plant", new Vector3(0, 0, 0), rng);
                    break;

                default:
                    // 没有专属套装的类型：撒一批通用陈设，至少不是空地
                    string[] generic = { "crate", "bench", "plant", "barrier", "trashbin", "pillar" };
                    for (int i = 0; i < 12; i++)
                    {
                        float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                        float r = 0.25f + (float)rng.NextDouble() * 0.45f;
                        BuildProp(inst, generic[i % generic.Length],
                            new Vector3(Mathf.Cos(a) * hw * r, 0, Mathf.Sin(a) * hd * r), rng);
                    }
                    break;
            }
        }

        /// <summary>
        /// 场景外围：一大片地面 + 一圈临街楼 + 路灯 + 一条路。
        ///
        /// 三件事各自解决一个实机问题：
        /// ① **大地面**——比场景本体大得多，且往下厚 1 米。玩家被打飞、跳出护栏、
        ///    或者落点差了几米时，脚下依然是实地，不会掉进虚空反复触发兜底；
        /// ② **临街楼**——玩家问"建筑在哪"，答案不能是"只有一间房"。围一圈带亮窗的楼，
        ///    这地方才像坐落在城市里，而不是漂在黑里；
        /// ③ **路灯**——室外场景的可见度全靠它。没有灯的夜间户外场景就是纯黑一片。
        /// </summary>
        static void BuildSurroundings(SiteInstance inst, SiteKitCatalog.SiteKindInfo kind,
            System.Random rng, float w, float d)
        {
            float gw = w + 260f, gd = d + 260f;

            // ① 大地面（厚 1m，往下压到 -1，任何落点都踩得住）
            Box(inst, "Ground", new Vector3(0, -1f, 0), new Vector3(gw, 1f, gd),
                new Color(0.24f, 0.25f, 0.27f));

            // ② 隐形边界墙：把玩家关在有地的范围内。
            //
            // 实机日志里这一行连刷十几秒、坐标一模一样：
            //   踩空捞回 掉落点 (20067,-11,-51) → 捞到 (20067,2,-51)
            // 也就是玩家走到了地面的边沿外侧。与其继续追查每一处几何缝隙，
            // 不如先让"走出去"这件事不可能发生——这是所有开放场景的通用做法，
            // 成本只有四块看不见的碰撞体。
            // 变量名避开下面建楼循环里的 bw/bh/bd（C# 不允许内层局部变量遮蔽外层同名局部变量）
            float boundX = gw / 2f - 8f, boundZ = gd / 2f - 8f;
            InvisibleWall(inst, new Vector3(0, 12f, boundZ), new Vector3(gw, 24f, 2f));
            InvisibleWall(inst, new Vector3(0, 12f, -boundZ), new Vector3(gw, 24f, 2f));
            InvisibleWall(inst, new Vector3(boundX, 12f, 0), new Vector3(2f, 24f, gd));
            InvisibleWall(inst, new Vector3(-boundX, 12f, 0), new Vector3(2f, 24f, gd));

            // 一条穿过场景南侧的路：让"外面"有方向感，不是一块空地
            Deco(inst, "Road", new Vector3(0, -0.44f, -d / 2f - 26f), new Vector3(gw, 0.08f, 14f),
                new Color(0.17f, 0.17f, 0.19f));
            for (int i = -6; i <= 6; i++)
                Deco(inst, "RoadLine", new Vector3(i * 11f, -0.4f, -d / 2f - 26f),
                    new Vector3(5f, 0.06f, 0.4f), new Color(0.78f, 0.76f, 0.6f));

            // ② 临街楼：四边各起几栋，高度随机但确定（同 seed 同结果）
            float hw = w / 2f + 26f, hd = d / 2f + 26f;
            for (int i = 0; i < 14; i++)
            {
                float t = (i + 0.5f) / 14f;
                bool alongX = i % 2 == 0;
                float along = (t * 2f - 1f) * (alongX ? hw : hd) * 1.25f;
                float side = (i % 4 < 2 ? 1f : -1f) * (alongX ? hd : hw);
                Vector3 at = alongX ? new Vector3(along, 0, side) : new Vector3(side, 0, along);

                float bh = 12f + (float)rng.NextDouble() * 26f;
                float bw = 12f + (float)rng.NextDouble() * 10f;
                float bd = 12f + (float)rng.NextDouble() * 10f;
                var tint = new Color(0.26f + (float)rng.NextDouble() * 0.12f,
                                     0.27f + (float)rng.NextDouble() * 0.12f,
                                     0.31f + (float)rng.NextDouble() * 0.14f);
                Box(inst, "Block", at + new Vector3(0, bh / 2f, 0), new Vector3(bw, bh, bd), tint);

                // 亮窗：楼要"有人住"才像城市。只做贴面装饰片，不参与碰撞与寻路
                int rows = Mathf.Clamp(Mathf.FloorToInt(bh / 4f), 2, 7);
                for (int r = 0; r < rows; r++)
                    for (int c = -1; c <= 1; c++)
                    {
                        if (rng.NextDouble() < 0.35) continue;   // 有些窗是黑的，才不像贴图
                        float zf = at.z > 0 ? -bd / 2f - 0.12f : bd / 2f + 0.12f;
                        Deco(inst, "Win", at + new Vector3(c * bw * 0.28f, 3f + r * 4f, zf),
                            new Vector3(bw * 0.18f, 1.5f, 0.1f), new Color(0.95f, 0.85f, 0.55f));
                    }
            }

            // ③ 路灯：绕场一圈，室外场景的可见度全靠它们
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Lamp(inst, new Vector3(Mathf.Cos(a) * (w / 2f + 14f), 0, Mathf.Sin(a) * (d / 2f + 14f)));
            }
        }

        /// <summary>看不见但挡得住的边界墙（只有碰撞体，不渲染、不吃绘制开销）。</summary>
        static void InvisibleWall(SiteInstance inst, Vector3 local, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Bound";
            go.transform.SetParent(inst.root.transform, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.enabled = false;
        }

        static void Shell(SiteInstance inst, float w, float d, float h)
        {
            Box(inst, "Wall", new Vector3(0, h / 2f, d / 2f), new Vector3(w, h, 0.6f), Wall);
            Box(inst, "Wall", new Vector3(w / 2f, h / 2f, 0), new Vector3(0.6f, h, d), Wall);
            Box(inst, "Wall", new Vector3(-w / 2f, h / 2f, 0), new Vector3(0.6f, h, d), Wall);
            // 南墙留出入口
            float side = (w - 6f) / 2f;
            Box(inst, "Wall", new Vector3(-(w - side) / 2f, h / 2f, -d / 2f), new Vector3(side, h, 0.6f), Wall);
            Box(inst, "Wall", new Vector3((w - side) / 2f, h / 2f, -d / 2f), new Vector3(side, h, 0.6f), Wall);
        }

        /// <summary>
        /// 户外边界：断续的护栏段 + 四角的花坛，围而不挡。
        /// 玩家看得见外面的楼与天光，走到边上又会被挡住——开阔感和可玩边界两者都要。
        /// </summary>
        static void OpenEdge(SiteInstance inst, float w, float d)
        {
            for (int i = 0; i < 5; i++)
            {
                float t = -0.4f + i * 0.2f;
                Box(inst, "Rail", new Vector3(t * w, 0.6f, d / 2f), new Vector3(w * 0.16f, 1.2f, 0.35f), Trim);
                Box(inst, "Rail", new Vector3(t * w, 0.6f, -d / 2f), new Vector3(w * 0.16f, 1.2f, 0.35f), Trim);
                Box(inst, "Rail", new Vector3(w / 2f, 0.6f, t * d), new Vector3(0.35f, 1.2f, d * 0.16f), Trim);
                Box(inst, "Rail", new Vector3(-w / 2f, 0.6f, t * d), new Vector3(0.35f, 1.2f, d * 0.16f), Trim);
            }
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Box(inst, "Planter", new Vector3(sx * w * 0.46f, 0.5f, sz * d * 0.46f),
                        new Vector3(4f, 1f, 4f), new Color(0.34f, 0.33f, 0.3f));
                    Deco(inst, "Bush", new Vector3(sx * w * 0.46f, 1.5f, sz * d * 0.46f),
                        new Vector3(3.4f, 1.2f, 3.4f), new Color(0.22f, 0.4f, 0.24f));
                }
        }

        static void Ceiling(SiteInstance inst, float w, float d, float h)
        {
            Deco(inst, "Ceiling", new Vector3(0, h, 0), new Vector3(w, 0.3f, d),
                new Color(0.3f, 0.3f, 0.32f));
        }

        static void Lamp(SiteInstance inst, Vector3 local)
        {
            Box(inst, "LampPole", local + new Vector3(0, 1.9f, 0), new Vector3(0.16f, 3.8f, 0.16f), Trim);
            var head = Deco(inst, "LampHead", local + new Vector3(0, 3.9f, 0),
                new Vector3(0.5f, 0.3f, 0.5f), new Color(1f, 0.95f, 0.8f));
            var lg = new GameObject("LampLight");
            lg.transform.SetParent(head.transform, false);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 16f;
            l.intensity = 1.1f;
            l.color = new Color(1f, 0.92f, 0.75f);
        }

        static void Sign(SiteInstance inst, Vector3 local, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var go = new GameObject("SiteSign");
            go.transform.SetParent(inst.root.transform, false);
            go.transform.localPosition = local;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = 48;
            tm.characterSize = 0.08f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.95f, 0.92f, 0.8f);
            var mr = go.GetComponent<MeshRenderer>();
            if (tm.font != null) mr.material = tm.font.material;
            go.AddComponent<FaceCamera>();
        }

        static void PaintLocal(GameObject go, Color c)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            r.sharedMaterial = m;
        }
    }

    /// <summary>忽明忽暗的灯（flicker 氛围）。</summary>
    public class SiteFlicker : MonoBehaviour
    {
        Light _l;
        float _base;

        void Start()
        {
            _l = GetComponent<Light>();
            if (_l != null) _base = _l.intensity;
        }

        void Update()
        {
            if (_l == null) return;
            float n = Mathf.PerlinNoise(Time.time * 6f, transform.position.x);
            _l.intensity = _base * Mathf.Lerp(0.35f, 1.1f, n);
        }
    }

    /// <summary>场景 NPC 的一句环境台词（非攻击性，只让世界像有人住过）。</summary>
    public class SiteAmbientLine : MonoBehaviour
    {
        public string speaker = "";
        public string line = "";
        float _next = -99f;

        public static void Attach(GameObject go, string speaker, string line)
        {
            var c = go.AddComponent<SiteAmbientLine>();
            c.speaker = speaker;
            c.line = line;
        }

        void Update()
        {
            if (Time.time < _next) return;
            var player = FindObjectOfType<Player.PlayerController>();
            if (player == null) return;
            if (Vector3.Distance(transform.position, player.transform.position) > 6f) return;
            _next = Time.time + 25f;
            Core.GameEvents.RaiseSubtitle("『" + speaker + "』：" + line);
        }
    }
}
