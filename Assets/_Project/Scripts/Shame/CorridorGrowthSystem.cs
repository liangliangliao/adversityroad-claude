using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using AdversityRoad.AI;
using AdversityRoad.Core;
using AdversityRoad.World;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 长廊延长 / 谎言复利（方案 8.5.2 / 8.13.1 CorridorGrowthSystem）。
    ///
    /// 【为掩盖而掩盖，必须在空间上看得见】
    /// 每使用一次隐瞒类交互物：长廊 +1 段、生成 1 个「新的把柄」敌人、
    /// Exposure 上限 +10；第 4 次起改为 +2 段。
    /// 验收第 38 条明写："每一次使用隐瞒类交互物，长廊必须发生玩家可见的延长"——
    /// 所以这里是真的往世界里加几何体，并把广播室的门整体往后推，
    /// 不是改一个玩家看不到的数字。
    ///
    /// 【导航约束（8.13.4）】
    /// 分段用同一套预定义模块拼，挂在自己的根节点下，加完重新烘一次这一块的导航面——
    /// 不动主世界的导航，也不在运行时凭空生成没验证过的几何。
    /// </summary>
    public class CorridorGrowthSystem : MonoBehaviour
    {
        public static CorridorGrowthSystem Instance { get; private set; }

        /// <summary>一段长廊的长度（米）。</summary>
        public const float SegmentLength = 14f;

        /// <summary>第几次隐瞒之后改为 +2 段。</summary>
        public const int DoubleGrowthFrom = 4;

        public WorldContext ctx;
        /// <summary>长廊起点（关卡入口一侧）与延伸方向。</summary>
        public Vector3 corridorStart;
        public Vector3 growDirection = Vector3.forward;
        /// <summary>广播室整体（门 + 红灯 + 房间）：长廊变长时它整体后退。</summary>
        public Transform broadcastRoom;

        readonly List<GameObject> _segments = new List<GameObject>();
        GameObject _growthRoot;
        NavMeshSurface _surface;
        Vector3 _roomBasePos;
        bool _roomBaseCaptured;

        public int SegmentCount => ShameLine.Data.corridorSegments;

        public static CorridorGrowthSystem Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CorridorGrowthSystem");
            Instance = go.AddComponent<CorridorGrowthSystem>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>关卡建好后由 ZoneBuilder 调一次，交代长廊的起点、方向与广播室。</summary>
        public void Bind(WorldContext context, Vector3 start, Vector3 dir, Transform room)
        {
            ctx = context;
            corridorStart = start;
            growDirection = dir.normalized;
            broadcastRoom = room;
            if (room != null) { _roomBasePos = room.position; _roomBaseCaptured = true; }

            ShameLine.Data.corridorSegments = 0;
            ShameLine.Persist();
            ClearSegments();
        }

        /// <summary>
        /// 一次隐瞒。这里**只诚实结账，不说教**（8.3 禁止做法）：
        /// 当下的压力确实下降了，账单是长廊变长、把柄多一个、暴露上限抬高。
        /// </summary>
        public void NoteConcealment(string what)
        {
            var d = ShameLine.Data;
            d.concealCount++;
            int add = d.concealCount >= DoubleGrowthFrom ? 2 : 1;
            for (int i = 0; i < add; i++) AppendSegment();

            var exposure = ExposureSystem.Instance;
            if (exposure != null)
            {
                // 抬的是天花板（后面能被看得更狠），涨的是当下这一点——
                // 方案 8.3 把"使用隐瞒类交互物成功"同时列进了上升来源与上限来源。
                exposure.RaiseCap(10f, what + "——当下过去了");
                exposure.Add(6f, null);
            }

            SpawnNewHandle();
            RebakeNav();

            GameEvents.RaiseSubtitle("长廊变长了 " + add + " 段（共 " + d.corridorSegments + " 段）。" +
                "为了盖住一次，下一次还要再盖一次——这是复利，不是加法。");
            ShameLine.Persist();
        }

        void AppendSegment()
        {
            var d = ShameLine.Data;
            int index = d.corridorSegments;
            d.corridorSegments++;

            if (_growthRoot == null)
            {
                _growthRoot = new GameObject("CorridorGrowth");
                _growthRoot.transform.position = corridorStart;
                _surface = _growthRoot.AddComponent<NavMeshSurface>();
                _surface.collectObjects = CollectObjects.Children;
                _surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            }

            Vector3 baseP = corridorStart + growDirection * (SegmentLength * (index + 0.5f));
            Vector3 right = Vector3.Cross(Vector3.up, growDirection).normalized;

            var seg = new GameObject("CorridorSegment_" + index);
            seg.transform.SetParent(_growthRoot.transform, true);
            seg.transform.position = baseP;

            // 同一套预定义模块：地板 + 两侧墙 + 一扇「每周门」+ 一盏冷灯
            Piece(seg, "Corridor_Floor", baseP + Vector3.down * 0.25f,
                new Vector3(9f, 0.5f, SegmentLength), new Color(0.3f, 0.29f, 0.32f), true);
            Piece(seg, "Corridor_Wall", baseP + right * 4.6f + Vector3.up * 1.9f,
                new Vector3(0.4f, 3.8f, SegmentLength), new Color(0.26f, 0.24f, 0.28f), true);
            Piece(seg, "Corridor_Wall", baseP - right * 4.6f + Vector3.up * 1.9f,
                new Vector3(0.4f, 3.8f, SegmentLength), new Color(0.26f, 0.24f, 0.28f), true);
            Piece(seg, "Corridor_Ceiling", baseP + Vector3.up * 3.9f,
                new Vector3(9.4f, 0.3f, SegmentLength), new Color(0.22f, 0.21f, 0.24f), false);

            var door = new GameObject("WeeklyDoor_" + index);
            door.transform.SetParent(seg.transform, true);
            door.transform.position = baseP + growDirection * (SegmentLength * 0.5f - 0.4f);
            Piece(door, "WeeklyDoorFrame", door.transform.position + right * 1.5f + Vector3.up * 1.5f,
                new Vector3(0.3f, 3f, 0.3f), new Color(0.44f, 0.36f, 0.26f), true);
            Piece(door, "WeeklyDoorFrame", door.transform.position - right * 1.5f + Vector3.up * 1.5f,
                new Vector3(0.3f, 3f, 0.3f), new Color(0.44f, 0.36f, 0.26f), true);
            var gate = door.AddComponent<WeeklyDoor>();
            gate.doorIndex = index + 1;
            var col = door.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(3.2f, 3f, 1.4f);
            col.center = Vector3.up * 1.5f;

            ZoneBuilder.AddCeilingLight(baseP + Vector3.up * 3.4f,
                new Color(0.8f, 0.82f, 0.9f), 16f);

            _segments.Add(seg);

            // 广播室整体后退：门一直开着，只是越走越远
            if (_roomBaseCaptured && broadcastRoom != null)
                broadcastRoom.position = _roomBasePos +
                    growDirection * (SegmentLength * ShameLine.Data.corridorSegments);
        }

        void Piece(GameObject parent, string name, Vector3 pos, Vector3 scale, Color color, bool solid)
        {
            GameObject go = solid ? ZoneBuilder.Box(ctx, name, pos, scale, color)
                                  : ZoneBuilder.Decoration(ctx, name, pos, scale, color);
            if (go != null) go.transform.SetParent(parent.transform, true);
        }

        /// <summary>每一次隐瞒生成一个专门针对上一条隐瞒内容的「新的把柄」。</summary>
        void SpawnNewHandle()
        {
            Vector3 at = corridorStart +
                growDirection * (SegmentLength * Mathf.Max(1, ShameLine.Data.corridorSegments - 0.5f));
            EnemySpawnHook.SpawnNear(EnemyType.NewHandle, EnemyTier.Standard, at);
        }

        void RebakeNav()
        {
            if (_surface == null) return;
            Physics.SyncTransforms();
            _surface.BuildNavMesh();
        }

        void ClearSegments()
        {
            foreach (var s in _segments) if (s != null) Destroy(s);
            _segments.Clear();
            if (_growthRoot != null) { Destroy(_growthRoot); _growthRoot = null; _surface = null; }
            if (_roomBaseCaptured && broadcastRoom != null) broadcastRoom.position = _roomBasePos;
        }
    }
}
