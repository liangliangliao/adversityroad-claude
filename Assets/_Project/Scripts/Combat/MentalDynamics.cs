using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 战斗驱动的心理能量动态（对齐方案「心理数值管理」与大型动作游戏的资源循环）：
    /// 让意志/专注/自尊/边界/行动力在战斗中真实起伏，而不是只有 HP 在动。
    ///
    /// 流失（压力面）：
    /// · 被物理命中：意志/专注下降、反刍上升（节奏被打断的挫感）；背后偷袭额外打专注；
    /// · 被击倒：自尊大幅下降、反刍上升（狼狈感）；
    /// · 被围攻（≥2 名敌人近身）：边界持续流失（人数越多越快）——被包围就是被挤压；
    /// · 战斗中站桩犹豫（3 秒不动不出手）：行动力持续流失（拖延的具象）；
    /// · 低生命（&lt;30%）：意志持续流失（生存恐慌——低谷线主题）；
    /// · 反刍过高（&gt;70%）：专注被持续侵蚀（越想越无法专注）。
    ///
    /// 回复（行动面）：
    /// · 命中敌人：行动力+、自尊微+（行动带来行动力，命中带来确认感）；
    /// · 击杀敌人：意志/自尊/行动力显著回复（穿过一个困境）；
    /// · 完美闪避：专注回复（看清了这一下）。
    /// 战斗中的被动回复同时调低（PlayerStats.TickRegen），让这些变化真实可见。
    /// </summary>
    public class MentalDynamics : MonoBehaviour
    {
        [Header("流失速率（每秒）")]
        public float surroundBoundaryDrain = 1.4f;   // 每多一名围攻者再 +0.7
        public float idleActionDrain = 2.2f;
        public float lowHpWillDrain = 0.9f;
        public float ruminationFocusDrain = 0.7f;

        [Header("回复量（每次）")]
        public float hitActionGain = 1.6f;
        public float hitSelfWorthGain = 0.6f;
        public float killWillGain = 9f;
        public float killSelfWorthGain = 7f;
        public float killActionGain = 6f;
        public float perfectDodgeFocusGain = 10f;

        PlayerController _player;
        CombatStateMachine _fsm;
        Vector3 _lastPos;
        float _idleT;
        float _nextSurroundScan;
        int _surrounders;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _fsm = GetComponent<CombatStateMachine>();
            _lastPos = transform.position;
        }

        void OnEnable() => GameEvents.OnEnemyKilled += OnEnemyKilled;
        void OnDisable() => GameEvents.OnEnemyKilled -= OnEnemyKilled;

        void Update()
        {
            if (_player == null || _player.Stats == null || _player.Stats.IsDead) return;
            var s = _player.Stats;
            float dt = Time.deltaTime;
            bool inCombat = _fsm != null && _fsm.InCombat;

            // ---- 围攻压迫：边界流失（0.4s 扫一次敌人数，避免每帧全场遍历）----
            if (Time.time >= _nextSurroundScan)
            {
                _nextSurroundScan = Time.time + 0.4f;
                _surrounders = CountNearbyEnemies(6f);
            }
            if (_surrounders >= 2)
                Drain(s, "boundary", (surroundBoundaryDrain + 0.7f * (_surrounders - 2)) * dt);

            // ---- 站桩犹豫：战斗中 3 秒不移动不出手 → 行动力流失（拖延具象）----
            bool moving = (transform.position - _lastPos).sqrMagnitude > 0.0004f;
            _lastPos = transform.position;
            bool acting = _fsm != null && _fsm.Current != CombatState.Locomotion &&
                          _fsm.Current != CombatState.Idle;
            if (inCombat && !moving && !acting)
            {
                _idleT += dt;
                if (_idleT > 3f) Drain(s, "actionPower", idleActionDrain * dt);
            }
            else _idleT = 0f;

            // ---- 低生命生存恐慌：意志持续流失 ----
            if (inCombat && s.hp < s.maxHp * 0.3f)
                Drain(s, "will", lowHpWillDrain * dt);

            // ---- 反刍侵蚀专注：越想越无法专注 ----
            if (s.rumination > s.maxRumination * 0.7f)
                Drain(s, "focus", ruminationFocusDrain * dt);
        }

        int CountNearbyEnemies(float radius)
        {
            int n = 0;
            foreach (var e in FindObjectsOfType<AI.EnemyController>())
            {
                if (e.State == AI.EnemyState.Dead) continue;
                if ((e.transform.position - transform.position).sqrMagnitude < radius * radius) n++;
            }
            return n;
        }

        // ===================== 事件钩子（PlayerCombatController 调用） =====================

        /// <summary>被物理命中：意志/专注下降、反刍上升；偷袭与击倒额外惩罚。</summary>
        public void OnHitTaken(float physicalDamage, bool backstab, bool knockdown)
        {
            var s = Stats(); if (s == null) return;
            Drain(s, "will", Mathf.Min(6f, physicalDamage * 0.15f));
            Drain(s, "focus", backstab ? 6f : 3f);
            s.AddRumination(backstab ? 4f : 2.5f);
            if (knockdown)
            {
                Drain(s, "selfWorth", 8f);
                s.AddRumination(5f);
            }
        }

        /// <summary>命中敌人：行动带来行动力，命中带来确认感。</summary>
        public void OnHitLanded(bool heavy)
        {
            var s = Stats(); if (s == null) return;
            Gain(s, "actionPower", heavy ? hitActionGain * 1.6f : hitActionGain);
            Gain(s, "selfWorth", hitSelfWorthGain);
        }

        /// <summary>完美闪避：看清了这一下——专注回复。</summary>
        public void OnPerfectDodge()
        {
            var s = Stats(); if (s == null) return;
            Gain(s, "focus", perfectDodgeFocusGain);
        }

        void OnEnemyKilled(string enemyId)
        {
            var s = Stats(); if (s == null) return;
            Gain(s, "will", killWillGain);
            Gain(s, "selfWorth", killSelfWorthGain);
            Gain(s, "actionPower", killActionGain);
        }

        PlayerStats Stats() =>
            _player != null && _player.Stats != null && !_player.Stats.IsDead ? _player.Stats : null;

        // ===================== 数值读写（直改字段 + 抛 UI 事件，不经过反刍联动） =====================

        static void Drain(PlayerStats s, string key, float amount) => Modify(s, key, -amount);
        static void Gain(PlayerStats s, string key, float amount) => Modify(s, key, amount);

        static void Modify(PlayerStats s, string key, float delta)
        {
            switch (key)
            {
                case "will":
                    s.will = Mathf.Clamp(s.will + delta, 0, s.maxWill);
                    GameEvents.RaiseMentalStatChanged("will", s.will, s.maxWill);
                    break;
                case "focus":
                    s.focus = Mathf.Clamp(s.focus + delta, 0, s.maxFocus);
                    GameEvents.RaiseMentalStatChanged("focus", s.focus, s.maxFocus);
                    break;
                case "selfWorth":
                    s.selfWorth = Mathf.Clamp(s.selfWorth + delta, 0, s.maxSelfWorth);
                    GameEvents.RaiseMentalStatChanged("selfWorth", s.selfWorth, s.maxSelfWorth);
                    break;
                case "boundary":
                    s.boundary = Mathf.Clamp(s.boundary + delta, 0, s.maxBoundary);
                    GameEvents.RaiseMentalStatChanged("boundary", s.boundary, s.maxBoundary);
                    break;
                case "actionPower":
                    s.actionPower = Mathf.Clamp(s.actionPower + delta, 0, s.maxActionPower);
                    GameEvents.RaiseMentalStatChanged("actionPower", s.actionPower, s.maxActionPower);
                    break;
            }
        }
    }
}
