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

        /// <summary>
        /// 这处场景的配色（来自蓝图色板）。
        ///
        /// 以前地面/墙体/装饰是三个全局常量——所有生成场景共用同一块灰地板、
        /// 同一圈米色墙。玩家说"每一关的物理世界基本一样、像共用同一个场景"，
        /// 最直接的一层原因就在这三行常量上：布局再怎么变，颜色一样就还是"那个地方"。
        /// 现在每处场景带自己的一套颜色，由 AI 从已批准色板里挑。
        /// </summary>
        public Color cFloor = new Color(0.46f, 0.45f, 0.44f);
        public Color cWall = new Color(0.70f, 0.68f, 0.63f);
        public Color cTrim = new Color(0.32f, 0.33f, 0.36f);
        public Color cAccent = new Color(0.85f, 0.72f, 0.30f);
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

            // ---- 配色：一切几何画上去之前先定下来 ----
            // 同一套布局配不同色板就是两个地方；这也是最便宜的差异化。
            var pal = SiteKitCatalog.Palette(bp.palette);
            inst.cFloor = pal.floor;
            inst.cWall = pal.wall;
            inst.cTrim = pal.trim;
            inst.cAccent = pal.accent;
            // 地面材质再对地板色做一次偏移：同样是"水泥灰"，草地和沥青不该一个颜色
            inst.cFloor = TintForSurface(inst.cFloor, bp.groundSurface);

            float scale = bp.sizeHint == "large" ? 1.35f : bp.sizeHint == "small" ? 0.75f : 1f;
            // 户外比室内略大，但**只是略大**。
            //
            // 上一版给了 ×1.6，叠上 large 的 ×1.35 就是 ×2.16——场地宽到 130 米。
            // 后果是玩家进场只看得见一面墙，Boss 被排到九十多米外（实机目标行：↘ 91m），
            // 走过去要十几秒还什么都遇不到。"开阔"不等于"空旷到找不到人"：
            // 一处能打起来的关卡，两端距离控制在四五十米之内才对。
            if (!kind.indoor) scale *= 1.15f;
            scale = Mathf.Min(scale, 1.3f);
            float w = 60f * scale, d = 46f * scale;

            // ---- 周边街区：先把"这地方在城市里"建出来 ----
            // 只建一个盒子的话，玩家推门进去看到的是悬在黑里的一间房——
            // 那不是"模拟真实世界的场景"，那是一个测试关。先铺地、再起楼、再点灯。
            BuildSurroundings(inst, kind, rng, w, d);
            yield return null;

            // ---- 地面与外壳 ----
            Box(inst, "Site_Floor", new Vector3(0, -0.25f, 0), new Vector3(w + 8f, 0.5f, d + 8f), inst.cFloor);
            // 地面纹理：车道线 / 砖缝 / 草簇 / 积水 / 格栅……
            // 一整块纯色地板是"这几关像同一个场景"最刺眼的一处，先把它铺出层次
            PaveSurface(inst, bp.groundSurface, rng, w, d);
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
            // 边界形态：围栏/绿篱/水岸/断崖/集装箱……换一种边界，整个远景就换了。
            // 室内不建：外壳本身就是边界，再在壳外砌一圈是看不见的浪费。
            if (!kind.indoor) BuildBoundary(inst, bp.boundary, rng, w, d);
            yield return null;

            // ---- 地形起伏：平地之外还有高台、落差、下沉坑、二层挑台 ----
            BuildVerticality(inst, bp.verticality, rng, w, d);

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
            // ---- 标志物：中央那个一眼记得住的东西 ----
            BuildLandmark(inst, bp.landmark, rng, w, d);
            // ---- 杂物：把空地填成"有人用过的地方" ----
            ScatterClutter(inst, bp, rng, w, d);
            yield return null;

            // ---- 灯光与氛围 ----
            ApplyAmbience(inst, bp, kind, w, d);
            // ---- 天气：雨/雪/雾/扬尘，室外才有意义 ----
            ApplyWeather(inst, bp.weather, kind, w, d);
            yield return null;

            // ---- 出入口 ----
            // 落点必须**在布局建完之后**再定，而且要确认那儿是空的。
            //
            // 原来固定取 -d/2+4，而 BuildOpenBlock 把临街楼放在 -d/2+8、进深 10 米——
            // 两者正好重叠：玩家一传送进来就卡在那栋楼的肚子里，
            // 画面是一整面红墙，人动不了也看不见任何东西。
            inst.playerSpawn = FindClearSpawn(inst, d);
            inst.exitPoint = inst.playerSpawn - Vector3.up * 1.1f - Vector3.forward * 2.5f;
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
            EnsureSiteLighting(inst);   // 建完先把"看不看得清"验一遍，再去数构件

            int parts = inst.root.GetComponentsInChildren<Renderer>(true).Length;
            int lamps = inst.root.GetComponentsInChildren<Light>(true).Length;
            Physics.SyncTransforms();
            bool grounded = Physics.Raycast(inst.playerSpawn + Vector3.up * 2f, Vector3.down,
                out RaycastHit gh, 30f, ~0, QueryTriggerInteraction.Ignore);
            // 相貌那几项也一并打进日志：玩家说"几关看起来一样"时，
            // 这一行能直接回答"到底是真的一样，还是只是没看出差别"。
            Core.CloudDialogueService.AddLog("场景已生成：" + bp.siteName + "（" + kind.name + "·" +
                SiteKitCatalog.LayoutName(bp.layout) + "）构件 " + parts + " · 灯 " + lamps +
                " · 房间 " + bp.rooms.Count + " · NPC " + bp.npcs.Count +
                " · 相貌[" + SiteKitCatalog.Palette(bp.palette).name + "/" +
                SiteKitCatalog.SurfaceName(bp.groundSurface) + "/" +
                SiteKitCatalog.BoundaryName(bp.boundary) + "/" +
                (SiteKitCatalog.LandmarkName(bp.landmark) == "" ? "无标志物" : SiteKitCatalog.LandmarkName(bp.landmark)) + "/" +
                SiteKitCatalog.VerticalityName(bp.verticality) + "/" +
                SiteKitCatalog.WeatherName(bp.weather) + "]" +
                " · 落点" + (grounded ? "脚下有地(" + gh.collider.name + ")" : "⚠ 脚下悬空") +
                " · seed " + chapter.assemblySeed + " → 动态区域 #" + inst.zoneIndex);

            // 落点台子**无条件**补一块：一块 10×10 的板子几乎不要钱，
            // 而"玩家一进来就往下掉"是最劝退的一种失败，不值得为了省它去赌。
            // 顶面必须和主地板齐平（落点 y=1.1，板厚 0.6 → 中心 -0.3 时顶面正好 0）。
            // 差 5 公分就是一道台阶，走过去会被绊、动画也会抖。
            Box(inst, "GroundPad_Spawn",
                inst.root.transform.InverseTransformPoint(inst.playerSpawn) + new Vector3(0, -1.4f, 0),
                new Vector3(10f, 0.6f, 10f), inst.cFloor);

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
            // 房间数按**可用面积**算，而不是把固定几间房拉伸到填满场地。
            //
            // 原来是 roomW = (w-4)/ceil(count/2)：场景放大到 130 米宽、蓝图只给三间房时，
            // 每间房被拉成六十多米——那不是房间，是一片带墙的旷野，
            // 玩家在里面既找不到门也认不出这是什么地方。
            // 现在房间按 16 米左右一间，装得下几间就摆几间；蓝图里的房间循环复用。
            const float TargetRoomW = 16f;
            float corridorW = 6f;
            float roomD = Mathf.Min((d - corridorW) / 2f - 2f, 18f);
            int cols = Mathf.Clamp(Mathf.FloorToInt((w - 4f) / TargetRoomW), 1, 6);
            int count = Mathf.Clamp(Mathf.Max(bp.rooms.Count, cols * 2), 2, cols * 2);
            float roomW = (w - 4f) / cols;

            for (int i = 0; i < count; i++)
            {
                bool north = i % 2 == 0;
                int col = i / 2;
                if (col >= cols) break;   // 超出列数的房间没有位置可放
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
            Box(inst, "Wall", new Vector3(0, 2.1f, hallW / 2f), new Vector3(w, 4.2f, 0.5f), inst.cWall);
            Box(inst, "Wall", new Vector3(0, 2.1f, -hallW / 2f), new Vector3(w, 4.2f, 0.5f), inst.cWall);

            int doors = Mathf.Max(4, Mathf.Min(bp.rooms.Count * 2, 10));
            for (int i = 0; i < doors; i++)
            {
                float x = -w / 2f + 4f + (w - 8f) * i / Mathf.Max(1, doors - 1);
                bool north = i % 2 == 0;
                float z = north ? hallW / 2f - 0.4f : -hallW / 2f + 0.4f;
                Deco(inst, "DoorFrame", new Vector3(x, 1.4f, z), new Vector3(1.8f, 2.8f, 0.18f), inst.cTrim);
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
                                   : new Vector3(0.5f, 3.2f, ch * 0.9f), inst.cWall);
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

            // 围合的四边由**一排独立楼**组成，不是四块整板。
            //
            // 原来是 new Vector3(w, 10f, 10f) —— w 在户外场景已经放大到一百三十米，
            // 于是"围合院落"变成四块一百三十米宽的巨型板子：玩家看到的是一面
            // 望不到头的平墙，既找不到入口，也完全没有"这是一片街区"的感觉。
            // 楼按 14 米一栋铺过去，中间留缝当通道，高度各不相同——
            // 尺度回到人身上，围合感反而更强。
            BuildingRow(inst, rng, new Vector3(0, 0, d * inner / 2f + 6f), w, true);
            BuildingRow(inst, rng, new Vector3(0, 0, -(d * inner / 2f + 6f)), w, true);
            BuildingRow(inst, rng, new Vector3(w * inner / 2f + 6f, 0, 0), d, false);
            BuildingRow(inst, rng, new Vector3(-(w * inner / 2f + 6f), 0, 0), d, false);

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
            Box(inst, "Wall", center + new Vector3(hx, h / 2f, 0), new Vector3(0.35f, h, size.y), inst.cWall);
            Box(inst, "Wall", center + new Vector3(-hx, h / 2f, 0), new Vector3(0.35f, h, size.y), inst.cWall);
            float far = doorSouth ? hz : -hz;
            Box(inst, "Wall", center + new Vector3(0, h / 2f, far), new Vector3(size.x, h, 0.35f), inst.cWall);

            float near = doorSouth ? -hz : hz;
            float side = (size.x - 2.4f) / 2f;
            Box(inst, "Wall", center + new Vector3(-(size.x - side) / 2f, h / 2f, near),
                new Vector3(side, h, 0.35f), inst.cWall);
            Box(inst, "Wall", center + new Vector3((size.x - side) / 2f, h / 2f, near),
                new Vector3(side, h, 0.35f), inst.cWall);
            Deco(inst, "DoorHead", center + new Vector3(0, h - 0.35f, near),
                new Vector3(2.4f, 0.7f, 0.35f), inst.cWall);

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

        // ================= 物理世界差异化：地面 / 边界 / 起伏 / 标志物 / 天气 / 杂物 =================
        //
        // 【这一整段是在回答"为什么每一关的物理世界都长得差不多"】
        //
        // 之前引擎能变的只有"布局形状"和"按场所类型摆的固有陈设"，
        // 而玩家一眼看到的其实是另外几样东西：脚下什么地、四周到哪儿为止、
        // 有没有高低差、场地中央杵着什么、天上在下什么。这些原来全是写死的，
        // 所以六个关卡走进去都是同一块灰地板 + 同一圈墙 + 一片空地。
        //
        // 现在这几样全部由蓝图描述、由引擎现场搭。做法参考的是那些"实时构建物理世界"
        // 的游戏（No Man's Sky 的行星表面、Minecraft 的结构生成、Diablo 的随机地牢）：
        // **不预制成品场景，只预制零件与规则**，运行时按一份描述把零件拼起来。
        // 零件就是这些程序化构件，规则就是下面这些函数，描述来自 AI。

        /// <summary>地面材质对地板色的偏移：草地不能和沥青一个颜色。</summary>
        static Color TintForSurface(Color baseColor, string surface)
        {
            switch (surface)
            {
                case "grass": return Color.Lerp(baseColor, new Color(0.28f, 0.45f, 0.24f), 0.75f);
                case "sand": return Color.Lerp(baseColor, new Color(0.78f, 0.70f, 0.48f), 0.7f);
                case "asphalt": return Color.Lerp(baseColor, new Color(0.20f, 0.20f, 0.22f), 0.7f);
                case "wood": return Color.Lerp(baseColor, new Color(0.52f, 0.36f, 0.21f), 0.7f);
                case "carpet": return Color.Lerp(baseColor, new Color(0.38f, 0.25f, 0.28f), 0.6f);
                case "gravel": return Color.Lerp(baseColor, new Color(0.44f, 0.42f, 0.38f), 0.5f);
                case "puddle": return Color.Lerp(baseColor, new Color(0.24f, 0.26f, 0.30f), 0.5f);
                case "grate": return Color.Lerp(baseColor, new Color(0.30f, 0.32f, 0.34f), 0.6f);
                case "tile": return Color.Lerp(baseColor, Color.white, 0.18f);
                default: return baseColor;
            }
        }

        /// <summary>把地面铺出纹理：这是"脚下这块地是什么地"的可见证据。</summary>
        static void PaveSurface(SiteInstance inst, string surface, System.Random rng, float w, float d)
        {
            switch (surface)
            {
                case "tile":   // 瓷砖：淡色缝网
                    for (int i = -4; i <= 4; i++)
                    {
                        Deco(inst, "Grout", new Vector3(i * w / 9f, 0.03f, 0), new Vector3(0.18f, 0.02f, d), Lighten(inst.cFloor, 0.25f));
                        Deco(inst, "Grout", new Vector3(0, 0.03f, i * d / 9f), new Vector3(w, 0.02f, 0.18f), Lighten(inst.cFloor, 0.25f));
                    }
                    break;
                case "asphalt":   // 沥青：中央双黄线 + 两侧车道边线
                    for (int i = -6; i <= 6; i++)
                        Deco(inst, "LaneMark", new Vector3(0, 0.03f, i * d / 13f), new Vector3(w * 0.5f, 0.02f, 0.5f),
                            new Color(0.92f, 0.82f, 0.25f));
                    Deco(inst, "Curb", new Vector3(-w * 0.42f, 0.06f, 0), new Vector3(0.6f, 0.12f, d), Lighten(inst.cFloor, 0.4f));
                    Deco(inst, "Curb", new Vector3(w * 0.42f, 0.06f, 0), new Vector3(0.6f, 0.12f, d), Lighten(inst.cFloor, 0.4f));
                    break;
                case "wood":   // 木地板：长条纹
                    for (int i = -8; i <= 8; i++)
                        Deco(inst, "Plank", new Vector3(i * w / 17f, 0.03f, 0), new Vector3(w / 20f, 0.02f, d),
                            i % 2 == 0 ? Lighten(inst.cFloor, 0.12f) : Lighten(inst.cFloor, -0.10f));
                    break;
                case "carpet":   // 地毯：中央一块深色毯 + 包边
                    Deco(inst, "Carpet", new Vector3(0, 0.04f, 0), new Vector3(w * 0.7f, 0.03f, d * 0.7f), Lighten(inst.cFloor, -0.18f));
                    Deco(inst, "CarpetEdge", new Vector3(0, 0.05f, 0), new Vector3(w * 0.72f, 0.02f, d * 0.02f), inst.cAccent);
                    break;
                case "grass":   // 草地：草簇 + 两条踩出来的土路
                    for (int i = 0; i < 34; i++)
                        Deco(inst, "Tuft", new Vector3(Rand(rng, w * 0.46f), 0.16f, Rand(rng, d * 0.46f)),
                            new Vector3(0.9f, 0.3f, 0.9f), Lighten(inst.cFloor, 0.18f));
                    Deco(inst, "DirtPath", new Vector3(0, 0.03f, 0), new Vector3(3.2f, 0.02f, d), new Color(0.45f, 0.38f, 0.28f));
                    break;
                case "sand":   // 沙地：起伏沙丘
                    for (int i = 0; i < 14; i++)
                        Deco(inst, "Dune", new Vector3(Rand(rng, w * 0.45f), 0.1f, Rand(rng, d * 0.45f)),
                            new Vector3(5f + (float)rng.NextDouble() * 6f, 0.2f, 3f), Lighten(inst.cFloor, 0.1f));
                    break;
                case "gravel":   // 碎石：密集小石
                    for (int i = 0; i < 42; i++)
                        Deco(inst, "Pebble", new Vector3(Rand(rng, w * 0.46f), 0.1f, Rand(rng, d * 0.46f)),
                            new Vector3(0.4f, 0.15f, 0.4f), Lighten(inst.cFloor, (float)rng.NextDouble() * 0.3f - 0.1f));
                    break;
                case "puddle":   // 积水：反光水洼
                    for (int i = 0; i < 12; i++)
                        Deco(inst, "Puddle", new Vector3(Rand(rng, w * 0.44f), 0.035f, Rand(rng, d * 0.44f)),
                            new Vector3(3f + (float)rng.NextDouble() * 4f, 0.02f, 2f + (float)rng.NextDouble() * 3f),
                            new Color(0.35f, 0.45f, 0.55f, 1f));
                    break;
                case "grate":   // 钢格栅：条状格网
                    for (int i = -10; i <= 10; i++)
                        Deco(inst, "Grate", new Vector3(i * w / 21f, 0.04f, 0), new Vector3(w / 26f, 0.03f, d * 0.9f),
                            Lighten(inst.cTrim, 0.2f));
                    break;
                default:   // 混凝土：伸缩缝
                    for (int i = -3; i <= 3; i++)
                        Deco(inst, "Seam", new Vector3(i * w / 7f, 0.03f, 0), new Vector3(0.14f, 0.02f, d), Lighten(inst.cFloor, -0.15f));
                    break;
            }
        }

        /// <summary>场地边界：换一种边界，整块远景就换了一副样子。</summary>
        static void BuildBoundary(SiteInstance inst, string boundary, System.Random rng, float w, float d)
        {
            float hw = w / 2f + 3f, hd = d / 2f + 3f;
            switch (boundary)
            {
                case "fence":
                    for (float x = -hw; x <= hw; x += 4f)
                    {
                        Box(inst, "FencePost", new Vector3(x, 1.2f, hd), new Vector3(0.2f, 2.4f, 0.2f), inst.cTrim);
                        Box(inst, "FencePost", new Vector3(x, 1.2f, -hd), new Vector3(0.2f, 2.4f, 0.2f), inst.cTrim);
                    }
                    Deco(inst, "FenceMesh", new Vector3(0, 1.6f, hd), new Vector3(w + 6f, 1.6f, 0.06f), Lighten(inst.cTrim, 0.3f));
                    Deco(inst, "FenceMesh", new Vector3(0, 1.6f, -hd), new Vector3(w + 6f, 1.6f, 0.06f), Lighten(inst.cTrim, 0.3f));
                    break;
                case "hedge":
                    Box(inst, "Hedge", new Vector3(0, 0.9f, hd), new Vector3(w + 6f, 1.8f, 1.6f), new Color(0.22f, 0.42f, 0.24f));
                    Box(inst, "Hedge", new Vector3(0, 0.9f, -hd), new Vector3(w + 6f, 1.8f, 1.6f), new Color(0.22f, 0.42f, 0.24f));
                    Box(inst, "Hedge", new Vector3(hw, 0.9f, 0), new Vector3(1.6f, 1.8f, d + 6f), new Color(0.22f, 0.42f, 0.24f));
                    Box(inst, "Hedge", new Vector3(-hw, 0.9f, 0), new Vector3(1.6f, 1.8f, d + 6f), new Color(0.22f, 0.42f, 0.24f));
                    break;
                case "water":
                    Deco(inst, "Water", new Vector3(0, 0.02f, hd + 12f), new Vector3(w + 40f, 0.04f, 24f),
                        new Color(0.22f, 0.38f, 0.52f));
                    Box(inst, "Quay", new Vector3(0, 0.35f, hd), new Vector3(w + 8f, 0.7f, 1.4f), Lighten(inst.cFloor, 0.2f));
                    for (float x = -hw; x <= hw; x += 8f)
                        Box(inst, "Bollard", new Vector3(x, 0.9f, hd - 1.2f), new Vector3(0.5f, 1.1f, 0.5f), inst.cTrim);
                    break;
                case "cliff":
                    Box(inst, "CliffFace", new Vector3(0, 6f, hd + 4f), new Vector3(w + 20f, 12f, 8f), Lighten(inst.cWall, -0.25f));
                    Box(inst, "CliffFace", new Vector3(hw + 4f, 6f, 0), new Vector3(8f, 12f, d + 20f), Lighten(inst.cWall, -0.25f));
                    Box(inst, "CliffFace", new Vector3(-hw - 4f, 6f, 0), new Vector3(8f, 12f, d + 20f), Lighten(inst.cWall, -0.25f));
                    break;
                case "curtain":
                    for (int i = -5; i <= 5; i++)
                    {
                        Deco(inst, "Drape", new Vector3(i * w / 11f, 3.2f, hd), new Vector3(w / 12f, 6.4f, 0.15f), inst.cAccent);
                        Deco(inst, "Drape", new Vector3(i * w / 11f, 3.2f, -hd), new Vector3(w / 12f, 6.4f, 0.15f), inst.cAccent);
                    }
                    break;
                case "containers":
                    for (int i = -3; i <= 3; i++)
                    {
                        var c = i % 2 == 0 ? inst.cAccent : Lighten(inst.cWall, -0.2f);
                        Box(inst, "Container", new Vector3(i * 13f, 1.4f, hd + 1f), new Vector3(12f, 2.8f, 3f), c);
                        if (i % 2 == 0)
                            Box(inst, "Container", new Vector3(i * 13f, 4.2f, hd + 1f), new Vector3(12f, 2.8f, 3f), Lighten(c, -0.15f));
                        Box(inst, "Container", new Vector3(i * 13f, 1.4f, -hd - 1f), new Vector3(12f, 2.8f, 3f), c);
                    }
                    break;
                case "buildings":
                    // 由 BuildSurroundings 的临街楼承担，这里不重复砌
                    break;
                default:   // wall
                    Box(inst, "BoundWall", new Vector3(0, 1.6f, hd), new Vector3(w + 8f, 3.2f, 0.6f), inst.cWall);
                    Box(inst, "BoundWall", new Vector3(0, 1.6f, -hd), new Vector3(w + 8f, 3.2f, 0.6f), inst.cWall);
                    Box(inst, "BoundWall", new Vector3(hw, 1.6f, 0), new Vector3(0.6f, 3.2f, d + 8f), inst.cWall);
                    Box(inst, "BoundWall", new Vector3(-hw, 1.6f, 0), new Vector3(0.6f, 3.2f, d + 8f), inst.cWall);
                    break;
            }
        }

        /// <summary>
        /// 地形起伏：高台、半层落差、下沉坑、二层挑台。
        /// 全部带坡道或台阶——导航面要能走上去，否则就是一堵挡路的方块。
        /// </summary>
        static void BuildVerticality(SiteInstance inst, string mode, System.Random rng, float w, float d)
        {
            switch (mode)
            {
                case "platform":
                    Box(inst, "Platform", new Vector3(0, 0.55f, 0), new Vector3(w * 0.34f, 1.1f, d * 0.34f), Lighten(inst.cFloor, 0.14f));
                    Ramp(inst, new Vector3(0, 0, -d * 0.22f), 6f, 1.1f, false);
                    Ramp(inst, new Vector3(0, 0, d * 0.22f), 6f, 1.1f, true);
                    break;
                case "split":
                    Box(inst, "HighHalf", new Vector3(0, 0.6f, d * 0.26f), new Vector3(w * 0.9f, 1.2f, d * 0.44f), Lighten(inst.cFloor, 0.1f));
                    Ramp(inst, new Vector3(-w * 0.28f, 0, d * 0.02f), 7f, 1.2f, true);
                    Ramp(inst, new Vector3(w * 0.28f, 0, d * 0.02f), 7f, 1.2f, true);
                    break;
                case "pit":
                    // 坑：地板整体在 y=0，这里用围边把"下沉"读出来（不真的挖洞，免得掉下去）
                    Box(inst, "PitEdgeN", new Vector3(0, 0.45f, d * 0.16f), new Vector3(w * 0.4f, 0.9f, 0.6f), Lighten(inst.cTrim, 0.15f));
                    Box(inst, "PitEdgeS", new Vector3(0, 0.45f, -d * 0.16f), new Vector3(w * 0.4f, 0.9f, 0.6f), Lighten(inst.cTrim, 0.15f));
                    Box(inst, "PitEdgeE", new Vector3(w * 0.2f, 0.45f, 0), new Vector3(0.6f, 0.9f, d * 0.32f), Lighten(inst.cTrim, 0.15f));
                    Box(inst, "PitEdgeW", new Vector3(-w * 0.2f, 0.45f, 0), new Vector3(0.6f, 0.9f, d * 0.32f), Lighten(inst.cTrim, 0.15f));
                    Deco(inst, "PitFloor", new Vector3(0, 0.06f, 0), new Vector3(w * 0.4f, 0.04f, d * 0.32f), Lighten(inst.cFloor, -0.22f));
                    break;
                case "balcony":
                    Box(inst, "Balcony", new Vector3(0, 4.2f, d * 0.38f), new Vector3(w * 0.8f, 0.4f, d * 0.16f), Lighten(inst.cFloor, 0.1f));
                    Deco(inst, "BalconyRail", new Vector3(0, 4.9f, d * 0.31f), new Vector3(w * 0.8f, 1f, 0.12f), inst.cTrim);
                    for (float x = -w * 0.34f; x <= w * 0.34f; x += w * 0.34f)
                        Box(inst, "BalconyPost", new Vector3(x, 2.1f, d * 0.44f), new Vector3(0.5f, 4.2f, 0.5f), inst.cTrim);
                    Stairway(inst, new Vector3(-w * 0.36f, 0, d * 0.24f), 4.2f);
                    break;
            }
        }

        /// <summary>一段可走的斜坡（用薄台阶叠出来，导航面能烘上去）。</summary>
        static void Ramp(SiteInstance inst, Vector3 at, float length, float height, bool towardPlus)
        {
            int steps = Mathf.Max(4, Mathf.RoundToInt(length / 0.9f));
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 1f) / steps;
                float z = at.z + (towardPlus ? 1f : -1f) * (length * (1f - t));
                Box(inst, "RampStep", new Vector3(at.x, height * t * 0.5f, z),
                    new Vector3(5.5f, Mathf.Max(0.12f, height * t), length / steps + 0.05f),
                    Lighten(inst.cFloor, 0.06f));
            }
        }

        /// <summary>一段楼梯（通往挑台/二层）。</summary>
        static void Stairway(SiteInstance inst, Vector3 at, float height)
        {
            int steps = Mathf.Max(8, Mathf.RoundToInt(height / 0.35f));
            for (int i = 0; i < steps; i++)
                Box(inst, "Step", at + new Vector3(0, height * (i + 1) / steps * 0.5f, i * 0.55f),
                    new Vector3(3.4f, height * (i + 1) / steps, 0.6f), Lighten(inst.cFloor, 0.08f));
        }

        /// <summary>
        /// 场地标志物：中央那个记得住的东西。
        /// 这是差异化里性价比最高的一项——同为"开放街区"，一个立着舞台、
        /// 一个杵着塔吊，玩家绝不会觉得是同一个地方。
        /// </summary>
        static void BuildLandmark(SiteInstance inst, string landmark, System.Random rng, float w, float d)
        {
            Vector3 c = new Vector3(0, 0, d * 0.06f);
            switch (landmark)
            {
                case "stage":
                    Box(inst, "StageDeck", c + new Vector3(0, 0.6f, 0), new Vector3(14f, 1.2f, 8f), Lighten(inst.cFloor, 0.2f));
                    Box(inst, "StageBackdrop", c + new Vector3(0, 4f, 4.2f), new Vector3(14f, 6f, 0.4f), inst.cAccent);
                    for (float x = -6f; x <= 6f; x += 12f)
                        Box(inst, "StageTruss", c + new Vector3(x, 3.5f, -3.6f), new Vector3(0.4f, 7f, 0.4f), inst.cTrim);
                    Deco(inst, "StageBar", c + new Vector3(0, 7f, -3.6f), new Vector3(13f, 0.35f, 0.35f), inst.cTrim);
                    Lamp(inst, c + new Vector3(-5f, 0, -2f));
                    Lamp(inst, c + new Vector3(5f, 0, -2f));
                    break;
                case "fountain":
                    Box(inst, "FountainRim", c + new Vector3(0, 0.5f, 0), new Vector3(11f, 1f, 11f), Lighten(inst.cWall, -0.1f));
                    Deco(inst, "FountainWater", c + new Vector3(0, 0.95f, 0), new Vector3(9.6f, 0.1f, 9.6f), new Color(0.3f, 0.55f, 0.72f));
                    Box(inst, "FountainStem", c + new Vector3(0, 2f, 0), new Vector3(1.2f, 4f, 1.2f), Lighten(inst.cWall, 0.1f));
                    Deco(inst, "FountainTop", c + new Vector3(0, 4.2f, 0), new Vector3(3f, 0.5f, 3f), Lighten(inst.cWall, 0.2f));
                    break;
                case "clock_tower":
                    Box(inst, "TowerBody", c + new Vector3(0, 9f, 0), new Vector3(5f, 18f, 5f), inst.cWall);
                    Deco(inst, "ClockFace", c + new Vector3(0, 15f, -2.6f), new Vector3(3.4f, 3.4f, 0.3f), new Color(0.95f, 0.93f, 0.85f));
                    Deco(inst, "ClockHand", c + new Vector3(0, 15f, -2.85f), new Vector3(0.2f, 2.4f, 0.1f), inst.cTrim);
                    Box(inst, "TowerCap", c + new Vector3(0, 18.6f, 0), new Vector3(6.4f, 1.2f, 6.4f), inst.cTrim);
                    break;
                case "big_screen":
                    Box(inst, "ScreenFrame", c + new Vector3(0, 7f, 3f), new Vector3(20f, 12f, 0.8f), inst.cTrim);
                    Deco(inst, "ScreenGlow", c + new Vector3(0, 7f, 2.5f), new Vector3(18.4f, 10.4f, 0.2f), inst.cAccent);
                    for (float x = -8f; x <= 8f; x += 16f)
                        Box(inst, "ScreenLeg", c + new Vector3(x, 0.6f, 3.4f), new Vector3(1.2f, 1.2f, 1.2f), inst.cTrim);
                    break;
                case "statue":
                    Box(inst, "Plinth", c + new Vector3(0, 0.9f, 0), new Vector3(4f, 1.8f, 4f), Lighten(inst.cWall, -0.15f));
                    Box(inst, "StatueBody", c + new Vector3(0, 3.6f, 0), new Vector3(1.6f, 3.6f, 1.1f), Lighten(inst.cWall, 0.25f));
                    Deco(inst, "StatueHead", c + new Vector3(0, 5.8f, 0), new Vector3(1f, 1f, 1f), Lighten(inst.cWall, 0.25f));
                    Deco(inst, "StatueArm", c + new Vector3(0.9f, 4.6f, 0), new Vector3(0.5f, 2.2f, 0.5f), Lighten(inst.cWall, 0.25f));
                    break;
                case "crane":
                    Box(inst, "CraneMast", c + new Vector3(0, 11f, 0), new Vector3(2f, 22f, 2f), inst.cAccent);
                    Deco(inst, "CraneJib", c + new Vector3(7f, 21f, 0), new Vector3(24f, 1.2f, 1.2f), inst.cAccent);
                    Deco(inst, "CraneCable", c + new Vector3(14f, 15f, 0), new Vector3(0.15f, 11f, 0.15f), inst.cTrim);
                    Box(inst, "CraneHook", c + new Vector3(14f, 9f, 0), new Vector3(1.2f, 1.6f, 1.2f), inst.cTrim);
                    Box(inst, "CraneBase", c + new Vector3(0, 0.5f, 0), new Vector3(6f, 1f, 6f), inst.cTrim);
                    break;
                case "bonfire":
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * 51f * Mathf.Deg2Rad;
                        Box(inst, "Log", c + new Vector3(Mathf.Cos(a) * 1.2f, 0.5f, Mathf.Sin(a) * 1.2f),
                            new Vector3(0.4f, 1f, 2.6f), new Color(0.36f, 0.25f, 0.16f));
                    }
                    Deco(inst, "Flame", c + new Vector3(0, 1.8f, 0), new Vector3(2f, 2.6f, 2f), new Color(1f, 0.55f, 0.2f));
                    var fireGo = new GameObject("BonfireLight");
                    fireGo.transform.SetParent(inst.root.transform, false);
                    fireGo.transform.localPosition = c + new Vector3(0, 2.4f, 0);
                    var fl = fireGo.AddComponent<Light>();
                    fl.type = LightType.Point; fl.range = 26f; fl.intensity = 3.2f;
                    fl.color = new Color(1f, 0.6f, 0.25f);
                    fireGo.AddComponent<SiteFlicker>();
                    // 篝火四周摆一圈能坐的地方：中央有火没人坐，看着像布景而不是场所
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 90f * Mathf.Deg2Rad;
                        Box(inst, "FireSeat", c + new Vector3(Mathf.Cos(a) * 4.5f, 0.3f, Mathf.Sin(a) * 4.5f),
                            new Vector3(2.4f, 0.6f, 0.9f), new Color(0.42f, 0.32f, 0.22f));
                    }
                    break;
                case "podium":
                    Box(inst, "PodiumStep1", c + new Vector3(0, 0.3f, 0), new Vector3(9f, 0.6f, 6f), Lighten(inst.cFloor, 0.15f));
                    Box(inst, "PodiumStep2", c + new Vector3(0, 0.9f, 0), new Vector3(6f, 0.6f, 4f), Lighten(inst.cFloor, 0.22f));
                    Box(inst, "Lectern", c + new Vector3(0, 1.8f, 0), new Vector3(1.4f, 1.2f, 0.8f), inst.cTrim);
                    Deco(inst, "Mic", c + new Vector3(0, 2.6f, 0), new Vector3(0.12f, 0.7f, 0.12f), inst.cTrim);
                    break;
                case "tent":
                    Box(inst, "TentPost", c + new Vector3(-5f, 2f, -4f), new Vector3(0.3f, 4f, 0.3f), inst.cTrim);
                    Box(inst, "TentPost", c + new Vector3(5f, 2f, -4f), new Vector3(0.3f, 4f, 0.3f), inst.cTrim);
                    Box(inst, "TentPost", c + new Vector3(-5f, 2f, 4f), new Vector3(0.3f, 4f, 0.3f), inst.cTrim);
                    Box(inst, "TentPost", c + new Vector3(5f, 2f, 4f), new Vector3(0.3f, 4f, 0.3f), inst.cTrim);
                    Deco(inst, "TentRoof", c + new Vector3(0, 4.3f, 0), new Vector3(12f, 0.4f, 10f), inst.cAccent);
                    Deco(inst, "TentSkirt", c + new Vector3(0, 3.9f, 4.9f), new Vector3(12f, 1f, 0.2f), inst.cAccent);
                    break;
                case "bus":
                    Box(inst, "BusBody", c + new Vector3(0, 1.7f, 0), new Vector3(3f, 3f, 11f), inst.cAccent);
                    Deco(inst, "BusWindow", c + new Vector3(1.55f, 2.3f, 0), new Vector3(0.1f, 1f, 9f), new Color(0.3f, 0.4f, 0.45f));
                    Deco(inst, "BusWindow", c + new Vector3(-1.55f, 2.3f, 0), new Vector3(0.1f, 1f, 9f), new Color(0.3f, 0.4f, 0.45f));
                    for (float z = -4f; z <= 4f; z += 8f)
                    {
                        Box(inst, "Wheel", c + new Vector3(1.5f, 0.5f, z), new Vector3(0.5f, 1f, 1f), new Color(0.12f, 0.12f, 0.13f));
                        Box(inst, "Wheel", c + new Vector3(-1.5f, 0.5f, z), new Vector3(0.5f, 1f, 1f), new Color(0.12f, 0.12f, 0.13f));
                    }
                    break;
                case "ring":
                    Box(inst, "RingDeck", c + new Vector3(0, 0.5f, 0), new Vector3(13f, 1f, 13f), Lighten(inst.cFloor, 0.18f));
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i % 2 == 0 ? 1 : -1) * 6.2f, z = (i < 2 ? 1 : -1) * 6.2f;
                        Box(inst, "RingPost", c + new Vector3(x, 1.9f, z), new Vector3(0.35f, 2.8f, 0.35f), inst.cTrim);
                    }
                    for (float y = 1.2f; y <= 2.6f; y += 0.7f)
                    {
                        Deco(inst, "Rope", c + new Vector3(0, y, 6.2f), new Vector3(12.4f, 0.1f, 0.1f), inst.cAccent);
                        Deco(inst, "Rope", c + new Vector3(0, y, -6.2f), new Vector3(12.4f, 0.1f, 0.1f), inst.cAccent);
                        Deco(inst, "Rope", c + new Vector3(6.2f, y, 0), new Vector3(0.1f, 0.1f, 12.4f), inst.cAccent);
                        Deco(inst, "Rope", c + new Vector3(-6.2f, y, 0), new Vector3(0.1f, 0.1f, 12.4f), inst.cAccent);
                    }
                    break;
                case "shelf_maze":
                    for (int i = -2; i <= 2; i++)
                    {
                        Box(inst, "TallShelf", c + new Vector3(i * 5.5f, 2.2f, -3f), new Vector3(1.2f, 4.4f, 12f), Lighten(inst.cWall, -0.2f));
                        for (int k = 1; k <= 3; k++)
                            Deco(inst, "ShelfBoard", c + new Vector3(i * 5.5f, k * 1.1f, -3f), new Vector3(1.5f, 0.12f, 12f), inst.cAccent);
                    }
                    break;
                case "scaffold":
                    for (int ix = -1; ix <= 1; ix++)
                        for (int iz = -1; iz <= 1; iz++)
                            Box(inst, "ScaffoldPole", c + new Vector3(ix * 5f, 3.5f, iz * 4f), new Vector3(0.25f, 7f, 0.25f), inst.cAccent);
                    for (float y = 2.4f; y <= 6.6f; y += 2.1f)
                        Deco(inst, "ScaffoldDeck", c + new Vector3(0, y, 0), new Vector3(10.4f, 0.2f, 8.4f), Lighten(inst.cFloor, 0.1f));
                    Stairway(inst, c + new Vector3(-6.5f, 0, -4f), 2.4f);
                    break;
            }
        }

        /// <summary>天气：室外才建。雨雪用几十根下落的细条，便宜且一眼能认。</summary>
        static void ApplyWeather(SiteInstance inst, string weather, SiteKitCatalog.SiteKindInfo kind,
            float w, float d)
        {
            if (string.IsNullOrEmpty(weather) || weather == "clear") return;
            if (kind.indoor && weather != "fog" && weather != "dust") return;

            switch (weather)
            {
                case "rain":
                    SiteWeather.Attach(inst.root, w, d, 60, new Color(0.65f, 0.75f, 0.9f, 1f),
                        new Vector3(0.05f, 1.4f, 0.05f), 26f, 0f);
                    break;
                case "snow":
                    SiteWeather.Attach(inst.root, w, d, 45, new Color(0.95f, 0.96f, 1f, 1f),
                        new Vector3(0.22f, 0.22f, 0.22f), 4.5f, 1.2f);
                    break;
                case "dust":
                    SiteWeather.Attach(inst.root, w, d, 45, new Color(0.72f, 0.62f, 0.45f, 1f),
                        new Vector3(0.3f, 0.3f, 0.3f), 2.2f, 3.5f);
                    break;
                case "wind":
                    SiteWeather.Attach(inst.root, w, d, 30, new Color(0.8f, 0.8f, 0.72f, 1f),
                        new Vector3(0.5f, 0.14f, 0.14f), 3f, 9f);
                    break;
                case "fog":
                    // 雾用几层贴地的半透明片：不动全局 RenderSettings（那会影响整个世界）
                    for (int i = 0; i < 5; i++)
                        Deco(inst, "FogLayer", new Vector3(0, 1.2f + i * 1.6f, 0),
                            new Vector3(w + 10f, 0.05f, d + 10f), new Color(0.72f, 0.75f, 0.8f, 1f));
                    break;
            }
        }

        /// <summary>
        /// 杂物：把空地填成"有人用过的地方"。
        /// 密度由蓝图给（0 干净、3 堆满），具体摆哪儿由引擎按 seed 定。
        /// </summary>
        static void ScatterClutter(SiteInstance inst, SiteBlueprint bp, System.Random rng, float w, float d)
        {
            var pool = new List<string>();
            foreach (var p in bp.scatterProps)
                if (SiteKitCatalog.IsProp(p)) pool.Add(p);
            if (pool.Count == 0) return;

            int n = Mathf.Clamp(bp.clutter, 0, 3) * 4;
            for (int i = 0; i < n; i++)
            {
                var at = new Vector3(Rand(rng, w * 0.42f), 0f, Rand(rng, d * 0.42f));
                // 中央那圈留给标志物：舞台/喷泉正中间插一个垃圾桶就成了穿帮
                if (at.sqrMagnitude < 90f) continue;
                BuildProp(inst, pool[rng.Next(pool.Count)], at, rng);
            }
        }

        static float Rand(System.Random rng, float half) => (float)(rng.NextDouble() * 2.0 - 1.0) * half;

        static Color Lighten(Color c, float amount) => new Color(
            Mathf.Clamp01(c.r + amount), Mathf.Clamp01(c.g + amount), Mathf.Clamp01(c.b + amount), c.a);

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
                    Box(inst, "BillboardPole", at + new Vector3(0, 2f, 0), new Vector3(0.3f, 4f, 0.3f), inst.cTrim);
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
                    Deco(inst, "DoorFrame", at + new Vector3(0, 1.4f, 0), new Vector3(1.8f, 2.8f, 0.18f), inst.cTrim);
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

        /// <summary>
        /// 落点处的两块牌子：身后是出口，身前是往里走的方向。
        ///
        /// 以前这里只立了"出口"。玩家是被传送进来的，本来就没有推门而入的过程，
        /// 结果一睁眼四周只有一块写着"出口"的牌子——他的原话是
        /// 「每个关卡都提示出口几个字，没有看到一扇门的入口」。
        /// 缺的不是门，是**往哪走**：所以补一条通向深处的引导带和一块指向牌，
        /// 落点周围也点两盏灯，别让人对着一面墙醒来。
        /// </summary>
        static void BuildEntranceMarker(SiteInstance inst, SiteBlueprint bp)
        {
            float localZ = inst.exitPoint.z - inst.origin.z;
            Sign(inst, new Vector3(0, 4.2f, localZ), "◀ " + bp.siteName + " · 出口");
            Deco(inst, "ExitPad", new Vector3(0, 0.07f, localZ),
                new Vector3(5f, 0.06f, 3f), new Color(0.4f, 0.8f, 0.6f));

            // 落点照明：醒来的地方必须是亮的
            Lamp(inst, new Vector3(-5f, 0, localZ + 3f));
            Lamp(inst, new Vector3(5f, 0, localZ + 3f));

            // 引导带：从落点铺向场景深处，走上去就知道该往哪边
            float spawnZ = inst.playerSpawn.z - inst.origin.z;
            float dir = spawnZ <= 0f ? 1f : -1f;                 // 深处在落点的另一侧
            for (int i = 1; i <= 6; i++)
            {
                float z = spawnZ + dir * i * 4.5f;
                Deco(inst, "PathMark", new Vector3(0, 0.08f, z),
                    new Vector3(2.6f, 0.05f, 1.1f), new Color(0.95f, 0.82f, 0.45f, 1f));
            }
            Sign(inst, new Vector3(0, 3.4f, spawnZ + dir * 9f), "▼ 往里走");
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
                                new Vector3(0.35f, 5.2f, 0.35f), inst.cTrim);
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

        /// <summary>
        /// 一排独立的楼（沿一条边铺开，留缝、错高、带亮窗）。
        ///
        /// 场景放大之后，任何"一整条边一个 Box"的做法都会变成一面几十上百米的平板。
        /// 建筑必须按**栋**来造，尺度才回得到人身上，玩家也才看得出哪里是通道。
        /// </summary>
        static void BuildingRow(SiteInstance inst, System.Random rng, Vector3 center,
            float span, bool alongX)
        {
            const float unit = 14f;
            int count = Mathf.Max(2, Mathf.FloorToInt(span / unit));
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count - 0.5f;
                float h = 9f + (float)rng.NextDouble() * 9f;
                Vector3 at = center + (alongX ? new Vector3(t * span, 0, 0) : new Vector3(0, 0, t * span));
                Vector3 size = alongX ? new Vector3(unit - 3f, h, 10f) : new Vector3(10f, h, unit - 3f);
                var tint = new Color(0.42f + (float)rng.NextDouble() * 0.16f,
                                     0.42f + (float)rng.NextDouble() * 0.14f,
                                     0.45f + (float)rng.NextDouble() * 0.16f);
                Box(inst, "Building", at + new Vector3(0, h / 2f, 0), size, tint);

                // 朝院子那一面开窗：有窗才像楼，没窗就是块板
                float face = alongX ? Mathf.Sign(-center.z) : Mathf.Sign(-center.x);
                for (int r = 0; r < Mathf.Clamp(Mathf.FloorToInt(h / 4f), 1, 4); r++)
                {
                    Vector3 wp = at + new Vector3(0, 3f + r * 4f, 0) +
                        (alongX ? new Vector3(0, 0, face * 5.1f) : new Vector3(face * 5.1f, 0, 0));
                    Vector3 ws = alongX ? new Vector3(6f, 1.5f, 0.12f) : new Vector3(0.12f, 1.5f, 6f);
                    Deco(inst, "Win", wp, ws, new Color(0.95f, 0.86f, 0.58f));
                }
            }
        }

        /// <summary>
        /// 找一个**身体放得下**的落点：从场地南沿往里逐步试，第一个空位就用它。
        ///
        /// 布局生成器各自摆楼摆房，谁都不知道落点在哪；与其逐个去躲，
        /// 不如建完之后统一验一遍——胶囊放得下才算数。这和 ZoneBuilder 对
        /// 24 个经典关卡做 EnsureSpawnPads 是同一个思路：**兜住结果，别逐处追查**。
        /// </summary>
        static Vector3 FindClearSpawn(SiteInstance inst, float d)
        {
            Physics.SyncTransforms();   // 刚建出来的碰撞体要先同步，否则检测不到
            for (int i = 0; i < 12; i++)
            {
                float z = -d / 2f + 4f + i * 3.5f;
                if (z > d / 2f - 4f) break;
                Vector3 at = inst.origin + new Vector3(0, 1.1f, z);
                // 半径 0.9、高度约两米的空间：站得下一个人，也留出转身余地
                if (!Physics.CheckCapsule(at + Vector3.up * 0.4f, at - Vector3.up * 0.4f, 0.9f,
                        ~0, QueryTriggerInteraction.Ignore))
                    return at;
            }
            // 全被占满（极少见）：退到场地中心，中心永远是布局留出的通行区
            return inst.origin + new Vector3(0, 1.1f, 0);
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
            Box(inst, "Wall", new Vector3(0, h / 2f, d / 2f), new Vector3(w, h, 0.6f), inst.cWall);
            Box(inst, "Wall", new Vector3(w / 2f, h / 2f, 0), new Vector3(0.6f, h, d), inst.cWall);
            Box(inst, "Wall", new Vector3(-w / 2f, h / 2f, 0), new Vector3(0.6f, h, d), inst.cWall);
            // 南墙留出入口
            float side = (w - 6f) / 2f;
            Box(inst, "Wall", new Vector3(-(w - side) / 2f, h / 2f, -d / 2f), new Vector3(side, h, 0.6f), inst.cWall);
            Box(inst, "Wall", new Vector3((w - side) / 2f, h / 2f, -d / 2f), new Vector3(side, h, 0.6f), inst.cWall);
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
                Box(inst, "Rail", new Vector3(t * w, 0.6f, d / 2f), new Vector3(w * 0.16f, 1.2f, 0.35f), inst.cTrim);
                Box(inst, "Rail", new Vector3(t * w, 0.6f, -d / 2f), new Vector3(w * 0.16f, 1.2f, 0.35f), inst.cTrim);
                Box(inst, "Rail", new Vector3(w / 2f, 0.6f, t * d), new Vector3(0.35f, 1.2f, d * 0.16f), inst.cTrim);
                Box(inst, "Rail", new Vector3(-w / 2f, 0.6f, t * d), new Vector3(0.35f, 1.2f, d * 0.16f), inst.cTrim);
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
            Box(inst, "LampPole", local + new Vector3(0, 1.9f, 0), new Vector3(0.16f, 3.8f, 0.16f), inst.cTrim);
            var head = Deco(inst, "LampHead", local + new Vector3(0, 3.9f, 0),
                new Vector3(0.5f, 0.3f, 0.5f), new Color(1f, 0.95f, 0.8f));
            // 强度走统一照明口径（range=20 → ≈3.4）。原来的 1.1/16m 在生成关卡里
            // 几乎不产生可见照度，日志里"灯 12 盏"和画面上"一片黑"因此并不矛盾。
            // 这里的灯是**常亮**的：生成场景没有昼夜开关来点它。
            World.SceneLighting.MakePoint("LampLight",
                head.transform.position, new Color(1f, 0.92f, 0.75f), 20f, head.transform);
        }

        /// <summary>
        /// 生成场景的照明审计：建完之后按网格量一遍照度，暗的地方补灯。
        ///
        /// 生成关卡的布局是随机的——哪一版会把玩家丢进一片没灯的空地，事先谁也不知道。
        /// 所以不靠"每种布局都记得摆灯"，而是建完统一验一遍。露天补路灯、
        /// 有顶的地方补吊灯（生成场景没有昼夜开关，两种都常亮）。
        /// </summary>
        static void EnsureSiteLighting(SiteInstance inst)
        {
            Physics.SyncTransforms();
            Vector3 c = inst.playerSpawn;
            int added = World.SceneLighting.EnsureLit(c, 45f, p =>
            {
                Vector3 local = inst.root.transform.InverseTransformPoint(
                    new Vector3(p.x, inst.origin.y, p.z));
                if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.up,
                        out RaycastHit roof, 22f, ~0, QueryTriggerInteraction.Ignore))
                {
                    // 吊灯离地不超过 3.4m（贴顶装会把顶低的房间照爆，见 SceneLighting）
                    World.SceneLighting.MakePoint("SiteCeilingLight",
                        new Vector3(p.x, World.SceneLighting.CeilingLightY(p.y, roof.point.y), p.z),
                        new Color(0.95f, 0.93f, 0.88f),
                        World.SceneLighting.CeilingLightRange, inst.root.transform);
                    return true;
                }
                // 露天才立灯柱；这个位置被构件占住就跳过（灯柱插进墙里比暗更难看）
                if (Physics.CheckSphere(p + Vector3.up * 1.6f, 0.7f, ~0,
                        QueryTriggerInteraction.Ignore)) return false;
                Lamp(inst, local);
                return true;
            }, cell: 15f, minLux: World.SceneLighting.MinLux, maxLamps: 12);
            if (added > 0)
                Core.CloudDialogueService.AddLog("照明审计：补灯 " + added + " 盏（生成场景）");
        }

        static void Sign(SiteInstance inst, Vector3 local, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var go = new GameObject("SiteSign");
            go.transform.SetParent(inst.root.transform, false);
            go.transform.localPosition = local;
            World.WorldText.Attach(go, text, 48, 0.08f, new Color(0.95f, 0.92f, 0.8f));
            go.AddComponent<FaceCamera>();
        }

        /// <summary>
        /// 按颜色复用材质。
        ///
        /// 差异化把构件数量抬上去了（地面纹理、杂物、天气各几十个），
        /// 而原来每个构件都 new 一个 Material——同色的一百块砖就是一百份材质，
        /// 批处理直接失效，手机上是实打实的掉帧。颜色量化到 1/64 之后做缓存，
        /// 同色共用一份，画面完全一样，DrawCall 少一大截。
        /// </summary>
        static readonly Dictionary<int, Material> _matCache = new Dictionary<int, Material>();

        static Material MaterialFor(Color c)
        {
            int key = (Mathf.RoundToInt(c.r * 63) << 18) | (Mathf.RoundToInt(c.g * 63) << 12) |
                      (Mathf.RoundToInt(c.b * 63) << 6) | Mathf.RoundToInt(c.a * 63);
            if (_matCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            _matCache[key] = m;
            return m;
        }

        static void PaintLocal(GameObject go, Color c)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return;
            r.sharedMaterial = MaterialFor(c);
        }
    }

    /// <summary>
    /// 天气：一批循环下落的细条（雨丝/雪片/尘粒/风中碎屑）。
    ///
    /// 不上粒子系统：这处场景整体是可卸载的运行时对象，几十个自管的方块比
    /// 一套 ParticleSystem 更好收；而且雨天/雪天/沙尘的差别玩家一眼就看得出，
    /// 属于"最便宜的一大块差异化"。落到地面以下就回到顶上，永远循环。
    /// </summary>
    public class SiteWeather : MonoBehaviour
    {
        Transform[] _bits;
        float _w, _d, _fall, _drift;

        public static void Attach(GameObject root, float w, float d, int count, Color color,
            Vector3 size, float fallSpeed, float drift)
        {
            if (root == null) return;
            var host = new GameObject("SiteWeather");
            host.transform.SetParent(root.transform, false);
            var c = host.AddComponent<SiteWeather>();
            c._w = w; c._d = d; c._fall = fallSpeed; c._drift = drift;
            c._bits = new Transform[count];

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Bit";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(host.transform, false);
                go.transform.localScale = size;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                go.transform.localPosition = new Vector3(
                    Random.Range(-w * 0.5f, w * 0.5f),
                    Random.Range(0f, 16f),
                    Random.Range(-d * 0.5f, d * 0.5f));
                c._bits[i] = go.transform;
            }
        }

        void Update()
        {
            if (_bits == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _bits.Length; i++)
            {
                var t = _bits[i];
                if (t == null) continue;
                var p = t.localPosition;
                p.y -= _fall * dt;
                if (_drift > 0f) p.x += Mathf.Sin(Time.time * 1.7f + i) * _drift * dt;
                if (p.y < 0.2f)
                {
                    p.y = 15f + Random.value * 3f;
                    p.x = Random.Range(-_w * 0.5f, _w * 0.5f);
                    p.z = Random.Range(-_d * 0.5f, _d * 0.5f);
                }
                t.localPosition = p;
            }
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
