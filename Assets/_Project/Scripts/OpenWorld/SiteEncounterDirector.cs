using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Player;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 场景遭遇导演：管一件事——**同一时刻最多几个敌人在跟你打**。
    ///
    /// 玩家的原话是"某些 AI 自动生成的关卡会出现 4 个以上敌人围攻，包括大 Boss 一起，
    /// 我觉得这不太公平"。查下来这不是敌人刷多了那么简单：
    /// 战斗导演（CombatDirector）限的是同时**出手**的人数（2 个令牌），
    /// 但没轮到出手的那些照样在贴身绕圈、照样在丢言语攻击——
    /// 心理伤害根本不过令牌。于是场面上永远是六个人围着一个人喊，
    /// 玩家读不出任何节奏，只感觉被糊住。
    ///
    /// 所以要限的是**参战人头**，不是出手次数：
    /// · 同时最多 3 个敌人参战（含 Boss），其余在自己那一带候场；
    /// · Boss 不和杂兵一起上——场上还剩两个以上杂兵时它按兵不动，
    ///   等清到只剩一个才压上来。"先解决喽啰再面对它"是一场仗该有的形状；
    /// · 谁离得近谁先上；玩家主动打过的那个永远算参战（打了就得打完）；
    /// · 死一个补一个——补位在 0.4 秒内完成，压迫感不会断。
    ///
    /// 这条规则只作用于 AI 现场生成的关卡（经典 24 关的编排是手工调过的，不动它）。
    /// </summary>
    public class SiteEncounterDirector : MonoBehaviour
    {
        /// <summary>同时参战的敌人上限（含 Boss）。</summary>
        public const int MaxEngaged = 3;

        /// <summary>场上还剩这么多杂兵时，Boss 继续按兵不动。</summary>
        public const int BossJoinsWhenMinionsAtMost = 1;

        readonly List<EnemyController> _all = new List<EnemyController>();
        Transform _player;
        float _next;

        public static SiteEncounterDirector Attach(GameObject host)
        {
            if (host == null) return null;
            return host.GetComponent<SiteEncounterDirector>() ??
                   host.AddComponent<SiteEncounterDirector>();
        }

        /// <summary>把一个刚生成的敌人纳入编排（Boss 也要登记，它同样受人数规则约束）。</summary>
        public void Register(GameObject enemy)
        {
            if (enemy == null) return;
            var ec = enemy.GetComponent<EnemyController>();
            if (ec != null && !_all.Contains(ec)) _all.Add(ec);
        }

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.4f;   // 死一个补一个的延迟上限，感觉不出来
            Rebalance();
        }

        void Rebalance()
        {
            if (_player == null)
            {
                var pc = AdversityRoad.Core.ActorRegistry.Player;
                if (pc == null) return;
                _player = pc.transform;
            }

            EnemyController boss = null;
            var minions = new List<EnemyController>();
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                var e = _all[i];
                if (e == null || e.State == EnemyState.Dead) { _all.RemoveAt(i); continue; }
                if (e.GetComponent<ChapterGateEnemy>() != null) boss = e;
                else minions.Add(e);
            }
            if (_all.Count == 0) return;

            // Boss 何时入场：杂兵清得差不多了，或者玩家自己找上门打了它
            bool bossEngaged = boss != null &&
                (boss.provoked || minions.Count <= BossJoinsWhenMinionsAtMost);
            if (boss != null) boss.holdPosition = !bossEngaged;

            int budget = MaxEngaged - (bossEngaged ? 1 : 0);

            // 近的先上；被玩家打过的那个排在最前，不会被重新按回候场
            minions.Sort((a, b) =>
            {
                if (a.provoked != b.provoked) return a.provoked ? -1 : 1;
                float da = (a.transform.position - _player.position).sqrMagnitude;
                float db = (b.transform.position - _player.position).sqrMagnitude;
                return da.CompareTo(db);
            });

            for (int i = 0; i < minions.Count; i++)
                minions[i].holdPosition = i >= budget;
        }
    }
}
