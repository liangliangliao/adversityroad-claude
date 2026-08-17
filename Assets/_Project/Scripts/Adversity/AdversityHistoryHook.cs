using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Core;
using AdversityRoad.Goals;
using AdversityRoad.Player;

namespace AdversityRoad.Adversity
{
    /// <summary>
    /// Adversity History（方案 9.5 / 15.4）：把每一场"谁赢了"变成可回看的历史。
    ///
    /// 记录项：首次遭遇时间地点、失败/胜利次数、最短与最长生存时间、
    /// 主要失败原因、玩家策略变化、宿敌进化、最终胜利关键动作。
    /// 它的用途不是排行榜，而是**证明成长不是单纯数值提升**——
    /// 逆袭必须能被指出来："第一次你输在这里，这一次你换了打法。"
    /// </summary>
    public class AdversityHistoryHook : MonoBehaviour
    {
        public static AdversityHistoryHook Instance { get; private set; }

        /// <summary>每个在场敌人的交战开始时间（生存时长统计）。</summary>
        readonly Dictionary<string, float> _engagedAt = new Dictionary<string, float>();
        float _lastFightStart = -1f;

        public static AdversityHistoryHook Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("AdversityHistory");
            Instance = go.AddComponent<AdversityHistoryHook>();
            return Instance;
        }

        void OnEnable()
        {
            GameEvents.OnPlayerDied += OnPlayerDied;
            GameEvents.OnEnemyKilled += OnEnemyKilled;
        }

        void OnDisable()
        {
            GameEvents.OnPlayerDied -= OnPlayerDied;
            GameEvents.OnEnemyKilled -= OnEnemyKilled;
        }

        void Update()
        {
            // 交战计时：第一个进入追击状态的敌人开始计时
            if (_lastFightStart > 0f) return;
            foreach (var e in FindObjectsOfType<EnemyController>())
                if (e.State == EnemyState.Chase || e.State == EnemyState.Attack)
                {
                    _lastFightStart = Time.time;
                    return;
                }
        }

        float Survival() => _lastFightStart > 0f ? Time.time - _lastFightStart : 0f;

        void OnPlayerDied(string reason)
        {
            var killer = ResolveKiller(FailureLog.LastAttackerId);
            var pc = FindObjectOfType<PlayerController>();
            var diag = FailureAnalyzer.Diagnose(pc != null ? pc.Stats : null);

            NemesisSystem.NoteDefeatedBy(killer, Survival(), diag.cause);
            _lastFightStart = -1f;

            // 失败要有重量，但必须支持学习：把诊断挂回当前旅程的恢复节点
            var goal = GoalOS.Active;
            if (goal != null && !goal.completed)
                GameEvents.RaiseSubtitle("这条旅程记下了这次失败——恢复节点已开放，" +
                    "训练、找情报、换路线或再战，由你决定。");
        }

        void OnEnemyKilled(string enemyId)
        {
            var e = ResolveKiller(enemyId);
            string archetype = e != null && e.profile != null ? e.profile.displayName : null;
            if (string.IsNullOrEmpty(archetype)) archetype = ArchetypeFromLiveList(enemyId);
            if (string.IsNullOrEmpty(archetype)) return;

            var n = NemesisSystem.Find(archetype);
            if (n == null) return;

            // 新策略判定：这次用的战术是不是之前没在这个宿敌身上用过的
            string keyAction = LastKeyAction();
            bool newStrategy = !string.IsNullOrEmpty(keyAction) && !n.strategyShifts.Contains(keyAction);
            NemesisSystem.NoteVictoryOver(archetype, Survival(), keyAction, newStrategy);
            _lastFightStart = -1f;
        }

        static EnemyController ResolveKiller(string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId)) return null;
            foreach (var e in FindObjectsOfType<EnemyController>())
                if (e != null && e.profile != null && e.profile.enemyId == enemyId) return e;
            return null;
        }

        /// <summary>敌人已被销毁时，从 id 前缀反查它的类型标签。</summary>
        static string ArchetypeFromLiveList(string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId)) return null;
            foreach (var n in NemesisSystem.All)
                if (!string.IsNullOrEmpty(n.archetypeId) && enemyId.Contains(SlugOf(n.archetypeId)))
                    return n.archetypeId;
            return null;
        }

        static string SlugOf(string s) => string.IsNullOrEmpty(s) ? "" : s;

        /// <summary>
        /// 这一场里玩家做出的关键动作：优先取最近一次被观察到的优势行为，
        /// 它是"逆袭靠的是策略变化而不是数值碾压"的证据来源。
        /// </summary>
        static string LastKeyAction()
        {
            StrengthEdge best = null;
            foreach (var s in AdversityProfile.Strengths)
                if (best == null || string.CompareOrdinal(s.lastObservedAt, best.lastObservedAt) > 0)
                    best = s;
            return best != null ? best.strengthTag : "";
        }

        /// <summary>逆袭回放文本（Adversity History 面板用）。</summary>
        public static string ReplayText(NemesisData n)
        {
            if (n == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append("【").Append(n.RankLabel()).Append(" · ").Append(n.displayName).Append("】\n");
            sb.Append("首次遭遇：").Append(string.IsNullOrEmpty(n.firstEncounterPlace)
                ? "未知地点" : n.firstEncounterPlace);
            sb.Append("    遭遇 ").Append(n.encounterCount).Append(" 次");
            sb.Append("（负 ").Append(n.playerLosses).Append(" · 胜 ").Append(n.playerWins).Append("）\n");

            if (n.shortestSurvival > 0f)
                sb.Append("生存时间：最短 ").Append(Mathf.RoundToInt(n.shortestSurvival))
                  .Append("s → 最长 ").Append(Mathf.RoundToInt(n.longestSurvival)).Append("s\n");

            if (n.failureCauses.Count > 0)
                sb.Append("主要失败原因：").Append(n.failureCauses[0]).Append('\n');
            if (n.learnedPlayerPatterns.Count > 0)
                sb.Append("它学到的你：").Append(string.Join("、", n.learnedPlayerPatterns.ToArray())).Append('\n');
            if (n.enemyAdaptations.Count > 0)
                sb.Append("它做出的调整：").Append(string.Join("、", n.enemyAdaptations.ToArray())).Append('\n');
            if (n.strategyShifts.Count > 0)
                sb.Append("你的策略变化：").Append(string.Join("、", n.strategyShifts.ToArray())).Append('\n');

            sb.Append(n.finalVictory
                ? "◆ 逆袭：" + n.finalVictoryRecord
                : "◇ 还没赢下它——可以训练、找情报、换路线，也可以暂时绕开。");
            return sb.ToString();
        }
    }
}
