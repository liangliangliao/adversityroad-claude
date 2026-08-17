using UnityEngine;
using AdversityRoad.Goals;
using AdversityRoad.Player;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// Goal Beacon · 目标灯塔（方案 3.5 / 17.1）：
    /// 每条主要目标在开放世界里有一个稳定的视觉与导航象征——一道远处的光柱。
    ///
    /// 它**不**意味着必须直线前进：玩家可以绕路、训练、撤退、改变策略。
    /// 它提供的是"方向恒常性"——任何时候抬头都知道当前主线指向哪里。
    /// 因此这里刻意不做满屏任务箭头（方案 17.1：弱化传统箭头，保持方向明确）。
    /// </summary>
    public class GoalBeaconController : MonoBehaviour
    {
        public static GoalBeaconController Instance { get; private set; }

        GameObject _pillar;
        Transform _pillarT;
        PlayerController _player;
        string _currentDistrict = "";
        float _nextRefresh;

        public static GoalBeaconController Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("GoalBeacon");
            Instance = go.AddComponent<GoalBeaconController>();
            return Instance;
        }

        void Update()
        {
            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + 1.5f;
                Refresh();
            }
            if (_pillarT != null)
                _pillarT.Rotate(0f, 22f * Time.deltaTime, 0f, Space.World);
        }

        void Refresh()
        {
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null) return;
            }

            var goal = GoalOS.Active;
            string district = TargetDistrict(goal);
            if (string.IsNullOrEmpty(district) || !DistrictCatalog.Ready)
            {
                Hide();
                return;
            }

            var d = DistrictCatalog.Get(district);
            if (d == null) { Hide(); return; }

            if (_pillar == null) Build();
            _pillar.SetActive(true);
            _pillar.transform.position = d.center + new Vector3(0, 0, 0);
            _currentDistrict = district;
        }

        void Hide()
        {
            if (_pillar != null) _pillar.SetActive(false);
            _currentDistrict = "";
        }

        /// <summary>当前主干节点所挂的章节落在哪个区域——那就是灯塔的位置。</summary>
        static string TargetDistrict(GoalData goal)
        {
            if (goal == null || goal.completed) return "";
            var node = GoalJourneyGraph.CurrentTarget(goal);
            if (node != null && !string.IsNullOrEmpty(node.linkedChapterId))
            {
                var ch = goal.FindChapter(node.linkedChapterId);
                if (ch != null) return ch.worldDistrictId;
            }
            // 主干节点没有直接绑定章节时，用当前里程碑下第一个未通关章节
            var cur = goal.CurrentMilestone();
            foreach (var ch in goal.chapters)
                if (!ch.cleared && (cur == null || ch.linkedMilestoneId == cur.milestoneId))
                    return ch.worldDistrictId;
            foreach (var ch in goal.chapters)
                if (!ch.cleared) return ch.worldDistrictId;
            return "";
        }

        void Build()
        {
            _pillar = new GameObject("GoalBeaconPillar");
            _pillarT = _pillar.transform;

            // 光柱：高耸的加色薄片十字（远处可见、近处不糊脸）
            for (int i = 0; i < 2; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "BeaconBlade";
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.SetParent(_pillarT, false);
                q.transform.localPosition = new Vector3(0, 45f, 0);
                q.transform.localRotation = Quaternion.Euler(0, i * 90f, 0);
                q.transform.localScale = new Vector3(5f, 90f, 1f);
                q.GetComponent<MeshRenderer>().sharedMaterial =
                    Combat.CombatFeedback.EnergyMaterial(new Color(0.98f, 0.85f, 0.42f), 0.32f);
            }

            // 地面环：走到脚下时也能看出"就是这里"
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "BeaconBase";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(_pillarT, false);
            ring.transform.localPosition = new Vector3(0, 0.08f, 0);
            ring.transform.localScale = new Vector3(7f, 0.04f, 7f);
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                Combat.CombatFeedback.EnergyMaterial(new Color(0.98f, 0.8f, 0.35f), 0.28f);
        }

        /// <summary>HUD 用：当前灯塔方向的一句话（弱化箭头，只给方向与距离感）。</summary>
        public string DirectionLine()
        {
            if (_player == null || string.IsNullOrEmpty(_currentDistrict)) return "";
            var d = DistrictCatalog.Get(_currentDistrict);
            if (d == null) return "";
            Vector3 delta = d.center - _player.transform.position;
            float dist = new Vector2(delta.x, delta.z).magnitude;
            if (dist < 12f) return "灯塔 · 你已经到了" + d.name;
            return "灯塔 · " + d.name + " " + Mathf.RoundToInt(dist) + "m " + Compass(delta);
        }

        static string Compass(Vector3 delta)
        {
            float ang = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            if (ang < 0) ang += 360f;
            string[] names = { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };
            return names[Mathf.RoundToInt(ang / 45f) % 8];
        }
    }
}
