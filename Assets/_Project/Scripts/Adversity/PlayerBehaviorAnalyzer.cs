using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.World;

namespace AdversityRoad.Adversity
{
    /// <summary>
    /// Player Behavior Analyzer（方案 8.1 / 9.2）：只看**可观察的游戏行为**。
    ///
    /// 它统计的是"挑衅之后玩家有没有追上去""被围时翻滚频率""低血量时攻击节奏"
    /// 这类可以被回放验证的事实，从不推断玩家是什么样的人。
    /// 敌人学习的是游戏行为，不是读取玩家隐私——这条界线是自适应敌人能存在的前提。
    /// </summary>
    public class PlayerBehaviorAnalyzer : MonoBehaviour
    {
        public static PlayerBehaviorAnalyzer Instance { get; private set; }

        // ---- 可观察标签（弱点图谱的边就由这些名字构成，玩家在画像面板能原样看到）----
        public const string TriggerTaunt = "公开挑衅";
        public const string TriggerSurrounded = "多敌围攻";
        public const string TriggerLowHp = "低血量";
        public const string TriggerVerbalDenial = "语言否定";
        public const string TriggerRangedPressure = "远程压制";

        public const string BehaviorChase = "追击过深";
        public const string BehaviorOverRoll = "过度翻滚";
        public const string BehaviorRhythmBreak = "攻击节奏崩坏";
        public const string BehaviorGoalDrift = "目标偏移";
        public const string BehaviorPassiveGuard = "站桩格挡";

        public const string StrengthParry = "精准格挡";
        public const string StrengthRetry = "坚持重试";
        public const string StrengthRoute = "路线发现";
        public const string StrengthResource = "资源规划";
        public const string StrengthFactCall = "事实判断";
        public const string StrengthRecovery = "从失败中恢复";
        public const string StrengthRetreat = "战略撤退";

        PlayerController _player;
        CombatStateMachine _fsm;

        // 挑衅 → 追击
        EnemyController _taunter;
        float _tauntAt = -99f;
        float _tauntStartDist;

        // 围攻 → 翻滚
        float _surroundedSince = -1f;
        int _dodgesWhileSurrounded;
        bool _wasDodging;

        // 低血量 → 节奏
        bool _lowHpWindowOpen;
        float _lowHpSince;
        int _attacksInLowHp, _hitsTakenInLowHp;

        float _nextTick;

        public static PlayerBehaviorAnalyzer Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PlayerBehaviorAnalyzer");
            Instance = go.AddComponent<PlayerBehaviorAnalyzer>();
            return Instance;
        }

        void OnEnable()
        {
            GameEvents.OnPlayerDied += OnDied;
            GameEvents.OnEnemyKilled += OnEnemyKilled;
        }

        void OnDisable()
        {
            GameEvents.OnPlayerDied -= OnDied;
            GameEvents.OnEnemyKilled -= OnEnemyKilled;
        }

        void Update()
        {
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null) return;
                _fsm = _player.GetComponent<CombatStateMachine>();
            }
            if (_player.Stats == null || _player.Stats.IsDead) return;

            TrackDodge();

            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 0.25f;

            TrackTauntChase();
            TrackSurrounded();
            TrackLowHpRhythm();
        }

        string Scene => ZoneBuilder.CurrentZoneId;

        // ================= 挑衅 → 追击 =================

        /// <summary>
        /// 敌人发动心理攻击 = 一次公开挑衅。之后 3.5 秒内玩家是否明显缩短了与它的距离？
        /// 这是"挑衅后追击率"的唯一数据来源——没有任何现实层面的推断。
        /// </summary>
        void TrackTauntChase()
        {
            if (_taunter == null)
            {
                foreach (var e in FindObjectsOfType<EnemyController>())
                {
                    if (e.State != EnemyState.MentalAttack) continue;
                    float d = Vector3.Distance(e.transform.position, _player.transform.position);
                    if (d > 22f) continue;
                    _taunter = e;
                    _tauntAt = Time.time;
                    _tauntStartDist = d;
                    break;
                }
                return;
            }

            if (Time.time - _tauntAt < 3.5f) return;

            bool chased = false;
            if (_taunter != null && _taunter.State != EnemyState.Dead)
            {
                float now = Vector3.Distance(_taunter.transform.position, _player.transform.position);
                chased = now < _tauntStartDist - 3.5f;
            }
            AdversityProfile.Observe(TriggerTaunt, BehaviorChase, chased, Scene,
                "解除锁定离开", "等它挑衅落空后再打破绽", "先处理与目标相关的敌人");
            if (!chased) AdversityProfile.ObserveStrength(StrengthRetreat, Scene);
            _taunter = null;
        }

        // ================= 围攻 → 过度翻滚 =================

        void TrackDodge()
        {
            if (_fsm == null) return;
            bool dodging = _fsm.Current == CombatState.Dodge;
            if (dodging && !_wasDodging && _surroundedSince > 0f) _dodgesWhileSurrounded++;
            _wasDodging = dodging;
        }

        void TrackSurrounded()
        {
            int near = 0;
            foreach (var e in FindObjectsOfType<EnemyController>())
            {
                if (e.State == EnemyState.Dead) continue;
                if (Vector3.Distance(e.transform.position, _player.transform.position) < 9f) near++;
            }

            if (near >= 3)
            {
                if (_surroundedSince < 0f) { _surroundedSince = Time.time; _dodgesWhileSurrounded = 0; }
                return;
            }
            if (_surroundedSince < 0f) return;

            float dur = Time.time - _surroundedSince;
            _surroundedSince = -1f;
            if (dur < 4f) return;

            float perSec = _dodgesWhileSurrounded / dur;
            AdversityProfile.Observe(TriggerSurrounded, BehaviorOverRoll, perSec > 0.55f, Scene,
                "拉到墙角只面对一侧", "用定心护体清一次场", "先打断远程再处理近战");
            if (perSec <= 0.55f && _dodgesWhileSurrounded > 0)
                AdversityProfile.ObserveStrength(StrengthRoute, Scene);
        }

        // ================= 低血量 → 攻击节奏 =================

        void TrackLowHpRhythm()
        {
            var s = _player.Stats;
            bool low = s.hp <= s.maxHp * 0.3f;

            if (low && !_lowHpWindowOpen)
            {
                _lowHpWindowOpen = true;
                _lowHpSince = Time.time;
                _attacksInLowHp = 0;
                _hitsTakenInLowHp = 0;
            }
            else if (low)
            {
                if (_fsm != null && (_fsm.Current == CombatState.LightAttack ||
                    _fsm.Current == CombatState.HeavyAttack)) _attacksInLowHp++;
                if (_fsm != null && _fsm.Current == CombatState.HitReaction) _hitsTakenInLowHp++;
            }
            else if (_lowHpWindowOpen)
            {
                _lowHpWindowOpen = false;
                float dur = Time.time - _lowHpSince;
                if (dur < 3f) return;
                // 低血下"挨打次数明显多于出手次数" = 节奏崩坏；反之说明能稳住
                bool broke = _hitsTakenInLowHp > _attacksInLowHp;
                AdversityProfile.Observe(TriggerLowHp, BehaviorRhythmBreak, broke, Scene,
                    "先脱离到安全距离回气", "用格挡换窗口而不是硬换血", "退出战斗也是一种胜利");
                if (!broke) AdversityProfile.ObserveStrength(StrengthRecovery, Scene);
            }
        }

        // ================= 事件 =================

        void OnDied(string reason)
        {
            _taunter = null;
            _surroundedSince = -1f;
            _lowHpWindowOpen = false;
            // 死亡本身不写弱点边：一次死亡说明不了模式，只有反复出现的条件-行为关系才算
        }

        void OnEnemyKilled(string enemyId)
        {
            // 击败强敌后仍继续推进目标 = 坚持重试的正向样本
            AdversityProfile.ObserveStrength(StrengthRetry, Scene);
        }

        // ================= 外部可调用的观察点 =================

        /// <summary>精准格挡成功（由战斗系统调用）。</summary>
        public static void NoteParrySuccess() =>
            AdversityProfile.ObserveStrength(StrengthParry, ZoneBuilder.CurrentZoneId);

        /// <summary>言语攻防选对了回应（事实判断优势）。</summary>
        public static void NoteVerbalCounter() =>
            AdversityProfile.ObserveStrength(StrengthFactCall, ZoneBuilder.CurrentZoneId);

        /// <summary>心理攻击命中后玩家是否离开了目标方向（目标偏移的唯一判据）。</summary>
        public static void NoteVerbalDenial(bool driftedFromGoal) =>
            AdversityProfile.Observe(TriggerVerbalDenial, BehaviorGoalDrift, driftedFromGoal,
                ZoneBuilder.CurrentZoneId, "用不读心盾挡下这一句", "把注意力收回当前里程碑");

        /// <summary>使用了补给/恢复点（资源规划优势）。</summary>
        public static void NoteResourceUse() =>
            AdversityProfile.ObserveStrength(StrengthResource, ZoneBuilder.CurrentZoneId);
    }
}
