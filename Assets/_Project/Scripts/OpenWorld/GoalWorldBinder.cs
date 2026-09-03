using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Goals;
using AdversityRoad.Player;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// Goal World Binder（方案 19.2 GoalWorldBinder / 验收标准 10、19）：
    /// 把 Goal Journey Graph 绑到开放世界的具体位置上——
    /// **任何主线遭遇都能追溯到 Goal / Milestone**，并且从住宅到任务区可以连续移动，
    /// 不通过关卡菜单。
    ///
    /// 玩家走进某个区域时，当前里程碑挂在这里的章节才展开：
    /// Legacy 章节打开心理裂隙（进 V1 原关卡），AI/预设章节就地组装成遭遇。
    /// 走远了就收起——世界不会被几十个待办同时塞满。
    /// </summary>
    public class GoalWorldBinder : MonoBehaviour
    {
        public static GoalWorldBinder Instance { get; private set; }

        public float engageRadius = 46f;
        public float disengageRadius = 78f;

        PlayerController _player;
        float _nextTick;
        bool _building;
        string _buildingChapterId = "";
        readonly List<string> _announced = new List<string>();

        public static GoalWorldBinder Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("GoalWorldBinder");
            Instance = go.AddComponent<GoalWorldBinder>();
            return Instance;
        }

        void Update()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 1.2f;

            if (_player == null)
            {
                _player = AdversityRoad.Core.ActorRegistry.Player;
                if (_player == null) return;
            }
            if (!DistrictCatalog.Ready) return;

            var goal = GoalOS.Active;
            if (goal == null || goal.completed) return;

            var cur = goal.CurrentMilestone();
            Vector3 p = _player.transform.position;

            foreach (var ch in goal.chapters)
            {
                if (ch.cleared || !ch.validated) continue;
                // 只展开当前里程碑的章节：玩家永远知道哪个行为直接推进目标
                if (cur != null && ch.linkedMilestoneId != cur.milestoneId) continue;

                var d = DistrictCatalog.Get(ch.worldDistrictId);
                if (d == null) continue;
                float dist = Vector3.Distance(p, d.center);

                if (dist <= engageRadius)
                {
                    Announce(ch, d);
                    // 分帧组装：场景是上百个构件加一次导航烘焙，不能挤在同一帧里建完
                    if (!ProceduralQuestAssembler.IsAssembled(ch.chapterId) && !_building)
                        StartCoroutine(AssembleOne(ch, goal));
                }
                else if (dist > disengageRadius && ProceduralQuestAssembler.IsAssembled(ch.chapterId)
                         && _buildingChapterId != ch.chapterId
                         && !PlayerIsInside(ch))
                {
                    // 正在分帧建造的那一处不能中途收走——否则建到一半的场景会漏在世界里
                    ProceduralQuestAssembler.DespawnChapter(ch.chapterId);
                    _announced.Remove(ch.chapterId);
                }
            }
        }

        /// <summary>
        /// 玩家此刻是不是就站在这一章生成的场景里。
        ///
        /// 【这是"进去就一片黑、马上踩空"的真正成因】
        /// 上面那个远近判定量的是**玩家到城区街区中心的距离**，而生成场景建在 x=20000
        /// 之外——玩家一走进去，这个距离立刻变成一万多米，远大于 disengageRadius，
        /// 于是下一个 tick（≤1.2 秒）就把这处场景当成"玩家已经走远了"整个销毁掉。
        /// 玩家看到的正是：进去还好好的，一秒后地面、墙、灯连同一切消失，
        /// 人直接掉进虚空——而截图里悬在半空的「限时台」「行动灯台」标牌，
        /// 就是还没来得及一起销毁的残留。
        ///
        /// 所以远近判定必须先排除"人在里面"这一种情况：脚下这块地不能被收走。
        /// </summary>
        static bool PlayerIsInside(GoalChapterData ch) =>
            SiteGate.InsideSite && SiteGate.InsideChapterId == ch.chapterId;

        /// <summary>一次只组装一处场景：并行建造会把分帧的意义抵消掉。</summary>
        IEnumerator AssembleOne(GoalChapterData ch, GoalData goal)
        {
            _building = true;
            _buildingChapterId = ch.chapterId;
            yield return ProceduralQuestAssembler.AssembleRoutine(ch, goal);
            _buildingChapterId = "";
            _building = false;
        }

        /// <summary>
        /// 按需现场搭建某一关，建好后立刻把玩家送进去（"传送"面板直达走这条）。
        ///
        /// 靠近才建的策略对开放城区里的漫游是对的——世界不该被五处场景同时塞满。
        /// 但玩家在面板上**明确点了这一关**，那就不能再要求他先走到对应街区去；
        /// 也不该受"只展开当前里程碑"的限制——那是给自动展开用的引导规则，
        /// 不是给玩家主动选择用的门禁。
        /// </summary>
        public void EnterOnDemand(string chapterId)
        {
            var goal = GoalOS.Active;
            var ch = goal != null ? goal.FindChapter(chapterId) : null;
            if (ch == null) { GameEvents.RaiseSubtitle("找不到这一关的蓝图。"); return; }
            if (ProceduralQuestAssembler.IsAssembled(chapterId)) { SiteGate.EnterChapter(chapterId); return; }
            StartCoroutine(BuildThenEnter(ch, goal));
        }

        IEnumerator BuildThenEnter(GoalChapterData ch, GoalData goal)
        {
            // 别的场景正在分帧建造时先等它建完：两处同时建会把分帧摊平成一次长卡顿
            while (_building) yield return null;

            GameEvents.RaiseSubtitle("正在为「" + ch.chapterName + "」搭建场景……");
            yield return AssembleOne(ch, goal);

            if (ProceduralQuestAssembler.IsAssembled(ch.chapterId)) SiteGate.EnterChapter(ch.chapterId);
            else GameEvents.RaiseSubtitle("这处场景没能建起来——换一关，或稍后再试。");
        }

        void Announce(GoalChapterData ch, DistrictRuntime d)
        {
            if (_announced.Contains(ch.chapterId)) return;
            _announced.Add(ch.chapterId);

            var goal = GoalOS.Active;
            var ms = goal != null ? goal.FindMilestone(ch.linkedMilestoneId) : null;
            string ob = "";
            if (goal != null)
            {
                var o = goal.FindObstacle(ch.primaryObstacle);
                if (o != null) ob = o.label;
            }
            GameEvents.RaiseSubtitle("〔" + ch.SourceLabel() + "〕" + ch.chapterName +
                "  在" + d.name + "挡住了【" + (ms != null ? ms.title : "当前阶段") + "】" +
                (string.IsNullOrEmpty(ob) ? "" : "：" + ob));
        }

        /// <summary>目标切换/重新生成后清空提示缓存与已组装内容。</summary>
        public void ResetBindings()
        {
            _announced.Clear();
            ProceduralQuestAssembler.ClearAll();
        }
    }
}
