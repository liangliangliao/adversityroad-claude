using UnityEngine;
using AdversityRoad.Adversity;
using AdversityRoad.AI;
using AdversityRoad.Core;
using AdversityRoad.OpenWorld;
using AdversityRoad.Player;

namespace AdversityRoad.Goals
{
    /// <summary>
    /// 把世界里发生的事回写给 Goal OS（验收标准 10 / 17）。
    ///
    /// 三件事：
    /// 1. 内心世界里打赢 V1 经典 Boss → 对应的 Legacy 章节判通关，玩家回到出发的那条街；
    /// 2. 全部里程碑完成 → 开启**目标终局**：最终胜利与玩家自己写下的完成条件绑定，
    ///    而不是统一只打旧我；
    /// 3. 终局守门人被击败 → 目标达成，写进 Hall of Goals。
    /// </summary>
    public class GoalProgressHook : MonoBehaviour
    {
        public static GoalProgressHook Instance { get; private set; }

        /// <summary>敌人生成器（由 GameBootstrap 注入）。</summary>
        public System.Func<EnemyType, EnemyTier, Vector3, bool, GameObject> spawner;

        GameObject _finalGateEnemy;
        string _finalGateEnemyId = "";
        float _nextCheck;

        public static GoalProgressHook Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("GoalProgressHook");
            Instance = go.AddComponent<GoalProgressHook>();
            return Instance;
        }

        void OnEnable() => GameEvents.OnEnemyKilled += OnEnemyKilled;
        void OnDisable() => GameEvents.OnEnemyKilled -= OnEnemyKilled;

        void Update()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 2f;
            TryOpenFinalGate();
        }

        // ================= Legacy 章节通关 =================

        void OnEnemyKilled(string enemyId)
        {
            var goal = GoalOS.Active;
            if (goal == null || goal.completed) return;

            if (!string.IsNullOrEmpty(_finalGateEnemyId) && enemyId == _finalGateEnemyId)
            {
                CompleteJourney(goal);
                return;
            }

            // Legacy：内心世界里的经典 Boss 被击败 → 该章节判通关 → 回到现实层原地点
            foreach (var ch in goal.chapters)
            {
                if (ch.cleared || ch.source != ChapterSource.Legacy) continue;
                if (!MatchesBoss(ch.bossArchetype, enemyId)) continue;

                GoalOS.ChapterCleared(ch.chapterId);
                CourageSystem.NoteGoalAction("在内心世界里正面拆掉了「" + ch.chapterName + "」");
                GameEvents.RaiseSubtitle("〔经典章节完成〕" + ch.chapterName + " —— " +
                    GoalJourneyGraph.ProgressLine(goal));
                if (WorldLayerController.HasReturnPoint)
                    Invoke(nameof(ReturnHome), 3.5f);
                return;
            }
        }

        void ReturnHome() => WorldLayerController.ReturnToReality();

        static bool MatchesBoss(string archetype, string enemyId)
        {
            if (string.IsNullOrEmpty(archetype) || string.IsNullOrEmpty(enemyId)) return false;
            foreach (var e in FindObjectsOfType<EnemyController>())
                if (e != null && e.profile != null && e.profile.enemyId == enemyId)
                    return ChapterModuleLibrary.TryEnemy(archetype, out var t) &&
                           EnemyCatalog.TypeLabel(t) == e.profile.displayName;
            // 敌人已销毁：退回按 id 词根匹配
            return ChapterModuleLibrary.TryEnemy(archetype, out var tt) &&
                   enemyId.ToLowerInvariant().Contains(tt.ToString().ToLowerInvariant());
        }

        // ================= 目标终局 =================

        /// <summary>
        /// 全部里程碑完成后，在当前区域立起终局守门人。
        /// 它不是"统一的旧我"——它守的是玩家自己写下的完成条件。
        /// </summary>
        void TryOpenFinalGate()
        {
            if (_finalGateEnemy != null) return;
            if (!GoalOS.FinalGateOpen() || spawner == null) return;

            var goal = GoalOS.Active;
            var player = FindObjectOfType<PlayerController>();
            if (goal == null || player == null || player.Stats.IsDead) return;

            var d = DistrictCatalog.NearestTo(player.transform.position, 90f);
            if (d == null) return;   // 还没走进开放城区就先不立

            Vector3 pos = d.center;
            if (UnityEngine.AI.NavMesh.SamplePosition(pos + player.transform.forward * 12f,
                    out var hit, 12f, UnityEngine.AI.NavMesh.AllAreas))
                pos = hit.position + Vector3.up * 1.1f;

            var type = FinalGateTypeFor(goal);
            _finalGateEnemy = spawner(type, EnemyTier.Chief, pos, true);
            if (_finalGateEnemy == null) return;

            var ec = _finalGateEnemy.GetComponent<EnemyController>();
            if (ec != null && ec.profile != null)
            {
                _finalGateEnemyId = ec.profile.enemyId;
                ec.profile.displayName = "终局守门人 · " + goal.title;
            }
            AdaptiveEnemyController.Attach(_finalGateEnemy, AdversityProfile.TargetableEdges());

            GameEvents.RaiseSubtitle("〔目标终局〕所有里程碑都过了——" +
                "最后挡在这里的东西，守的是「" + goal.successCondition + "」。");
        }

        /// <summary>终局守门人的原型由目标最后一个未消除障碍决定（没有则用旧我）。</summary>
        static EnemyType FinalGateTypeFor(GoalData goal)
        {
            for (int i = goal.obstacles.Count - 1; i >= 0; i--)
            {
                ChapterModuleLibrary.SuggestEnemies(goal.obstacles[i].axis, out _, out _, out var boss);
                return boss;
            }
            return EnemyType.OldSelf;
        }

        void CompleteJourney(GoalData goal)
        {
            _finalGateEnemy = null;
            _finalGateEnemyId = "";

            var primary = NemesisSystem.Primary();
            string comeback = primary != null && primary.finalVictory
                ? primary.finalVictoryRecord
                : (primary != null ? "还有一个宿敌没有翻过去：" + primary.displayName : "");
            string shift = primary != null && primary.strategyShifts.Count > 0
                ? primary.strategyShifts[0] : "";

            GoalOS.CompleteGoal(goal.successCondition, WorldState.TotalDefeats,
                NemesisSystem.Count, comeback, shift);
            CourageSystem.NoteGoalAction("走完了整条旅程");

            var d = DistrictCatalog.NearestTo(
                FindObjectOfType<PlayerController>() != null
                    ? FindObjectOfType<PlayerController>().transform.position : Vector3.zero, 120f);
            if (d != null) WorldState.OnMilestoneReached(d.id);

            GameEvents.RaiseSubtitle("我知道我要去哪里。困难可以阻挡道路，但不能替我决定方向。");
        }
    }
}
