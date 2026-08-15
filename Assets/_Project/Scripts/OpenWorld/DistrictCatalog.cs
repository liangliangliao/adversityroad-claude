using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Goals;

namespace AdversityRoad.OpenWorld
{
    /// <summary>开放世界连续城区里的一块区域（运行时坐标 + 静态描述）。</summary>
    public class DistrictRuntime
    {
        public string id;
        public string name;
        /// <summary>区域中心（世界坐标，BuildCity 时写入）。</summary>
        public Vector3 center;
        /// <summary>遭遇位（Encounter Slot）：确定性组装时按 assemblySeed 选取。</summary>
        public readonly List<Vector3> encounterSlots = new List<Vector3>();
        /// <summary>心理裂隙锚点：现实地点上长出内心世界入口的位置。</summary>
        public Vector3 riftAnchor;
        /// <summary>恢复/资源点（安全区、便利店、电话亭、公园长椅）。</summary>
        public readonly List<Vector3> recoverySpots = new List<Vector3>();
    }

    /// <summary>
    /// District Manager（方案 6.2）：住宅/商业/工作/交通/生活服务/边缘六大区域，
    /// 加上在现实地点上生成的心理裂隙。
    ///
    /// 它同时是 AI 章节的落地表——ProceduralQuestAssembler 只能把蓝图放进这里的
    /// EncounterSlot，AI 永远拿不到坐标，也就不可能生成未经导航验证的几何。
    /// </summary>
    public static class DistrictCatalog
    {
        static readonly Dictionary<string, DistrictRuntime> _runtime =
            new Dictionary<string, DistrictRuntime>();

        public static IEnumerable<DistrictRuntime> All => _runtime.Values;

        public static void Clear() => _runtime.Clear();

        public static DistrictRuntime Register(string id, string name, Vector3 center)
        {
            var d = new DistrictRuntime { id = id, name = name, center = center, riftAnchor = center };
            _runtime[id] = d;
            return d;
        }

        public static DistrictRuntime Get(string id)
        {
            if (id != null && _runtime.TryGetValue(id, out var d)) return d;
            foreach (var v in _runtime.Values) return v;
            return null;
        }

        public static bool Ready => _runtime.Count > 0;

        public static string NameOf(string id)
        {
            var d = Get(id);
            if (d != null) return d.name;
            return ChapterModuleLibrary.District(id).name;
        }

        /// <summary>玩家当前所在的区域（按最近中心判定；不在城区内返回 null）。</summary>
        public static DistrictRuntime NearestTo(Vector3 pos, float maxDistance = 70f)
        {
            DistrictRuntime best = null;
            float bestSqr = maxDistance * maxDistance;
            foreach (var d in _runtime.Values)
            {
                float sqr = (d.center - pos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = d; }
            }
            return best;
        }

        /// <summary>按 seed 确定性地取一个遭遇位（相同 seed 必须选到同一个点）。</summary>
        public static Vector3 EncounterSlot(string districtId, int seed, int index)
        {
            var d = Get(districtId);
            if (d == null || d.encounterSlots.Count == 0) return Vector3.zero;
            int i = Mathf.Abs(seed / 7 + index * 31) % d.encounterSlots.Count;
            return d.encounterSlots[i];
        }

        public static Vector3 RecoverySpot(string districtId, int index)
        {
            var d = Get(districtId);
            if (d == null || d.recoverySpots.Count == 0) return Vector3.zero;
            return d.recoverySpots[Mathf.Abs(index) % d.recoverySpots.Count];
        }
    }
}
