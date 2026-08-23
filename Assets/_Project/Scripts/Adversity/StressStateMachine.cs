using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Adversity
{
    /// <summary>压力六阶段（方案 12.3）。</summary>
    public enum StressStage
    {
        Stable,        // 正常：建立基线
        Strained,      // 呼吸/音效变化、轻度压力
        Destabilized,  // 部分稳定性降低、心理攻击更明显
        Overloaded,    // 多个压力轴叠加但仍可操作
        NearCollapse,  // 恢复变慢、动作反馈更沉重
        Breakdown      // 短暂跪地/解除锁定/失去姿态——失败态但不长时间剥夺控制
    }

    /// <summary>
    /// Stress State Machine（方案 12.3）：把"快撑不住"做成可玩的体验，而不是一个失败弹窗。
    ///
    /// 设计目的按阶段递进：建立基线 → 让玩家感觉正在承压 → 迫使重新调整 →
    /// 制造真正逆境 → 制造"快撑不住" → 短暂失败态。
    /// Breakdown 必须短：失败可以有重量，但不能长时间剥夺控制权。
    /// </summary>
    public class StressStateMachine : MonoBehaviour
    {
        public static StressStateMachine Instance { get; private set; }

        public StressStage Stage { get; private set; } = StressStage.Stable;
        /// <summary>综合压力 0-1（由多条心理轴与生命共同决定）。</summary>
        public float Pressure { get; private set; }

        public event System.Action<StressStage> StageChanged;

        PlayerController _player;
        float _breakdownUntil = -1f;
        float _nextTick;

        public static StressStateMachine Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("StressStateMachine");
            Instance = go.AddComponent<StressStateMachine>();
            return Instance;
        }

        void Update()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 0.35f;

            if (_player == null)
            {
                _player = AdversityRoad.Core.ActorRegistry.Player;
                if (_player == null) return;
            }
            var s = _player.Stats;
            if (s == null) return;

            Pressure = ComputePressure(s);
            var next = StageFor(Pressure);
            if (next != Stage) SetStage(next);
        }

        /// <summary>
        /// 压力不是单条血量：它是多轴叠加的结果。
        /// 这正是 Overloaded 的定义——"多个压力轴叠加但仍可操作"。
        /// </summary>
        float ComputePressure(PlayerStats s)
        {
            float hp = 1f - Ratio(s.hp, s.maxHp);
            float will = 1f - Ratio(s.will, s.maxWill);
            float focus = 1f - Ratio(s.focus, s.maxFocus);
            float self = 1f - Ratio(s.selfWorth, s.maxSelfWorth);
            float bound = 1f - Ratio(s.boundary, s.maxBoundary);
            float rum = Ratio(s.rumination, s.maxRumination);

            // 生命与意志权重最高，其余轴每条都算"一根压上来的手指"
            float p = 0.30f * hp + 0.24f * will + 0.12f * focus +
                      0.12f * self + 0.12f * bound + 0.10f * rum;

            // 多轴同时吃紧的额外惩罚：叠加感正是"逆境"和"挨打"的区别
            int strained = 0;
            if (hp > 0.5f) strained++;
            if (will > 0.5f) strained++;
            if (focus > 0.5f) strained++;
            if (self > 0.5f) strained++;
            if (bound > 0.5f) strained++;
            if (strained >= 3) p += 0.08f * (strained - 2);

            return Mathf.Clamp01(p);
        }

        StressStage StageFor(float p)
        {
            if (Time.time < _breakdownUntil) return StressStage.Breakdown;
            if (Stage == StressStage.Breakdown && Time.time >= _breakdownUntil)
                return StressStage.NearCollapse;   // 崩溃之后回到濒临，而不是直接归零

            if (p >= 0.86f) return StressStage.NearCollapse;
            if (p >= 0.68f) return StressStage.Overloaded;
            if (p >= 0.48f) return StressStage.Destabilized;
            if (p >= 0.28f) return StressStage.Strained;
            return StressStage.Stable;
        }

        void SetStage(StressStage next)
        {
            var prev = Stage;
            Stage = next;
            StageChanged?.Invoke(next);

            switch (next)
            {
                case StressStage.Strained:
                    if (prev == StressStage.Stable)
                        GameEvents.RaiseSubtitle("呼吸变重了——压力上来了，但还稳得住。");
                    break;
                case StressStage.Destabilized:
                    GameEvents.RaiseSubtitle("有东西开始松动——该重新调整打法了。");
                    break;
                case StressStage.Overloaded:
                    GameEvents.RaiseSubtitle("几条压力线同时压上来——还能动，但每一步都更费力。");
                    break;
                case StressStage.NearCollapse:
                    GameEvents.RaiseSubtitle("〔濒临崩溃〕恢复变慢，动作变沉——" +
                        "但只要还能做出一次高质量的动作，就还有翻盘的窗口。");
                    ResolveSystem.Instance?.OpenWindow();
                    break;
                case StressStage.Breakdown:
                    GameEvents.RaiseSubtitle("〔短暂失守〕撑不住了——但这只是几秒钟，不是终点。");
                    break;
            }
        }

        /// <summary>触发一次短暂崩溃态（心理硬直类事件调用）。</summary>
        public void TriggerBreakdown(float seconds = 2.2f)
        {
            _breakdownUntil = Time.time + Mathf.Clamp(seconds, 1f, 3f);
            SetStage(StressStage.Breakdown);
            if (_player != null)
            {
                var fsm = _player.GetComponent<CombatStateMachine>();
                if (fsm != null) fsm.TriggerMentalStagger();
                var lockOn = _player.GetComponent<LockOnSystem>();
                if (lockOn != null) lockOn.Release();
            }
        }

        /// <summary>濒临崩溃下恢复变慢（由属性回复调用的系数）。</summary>
        public float RegenMultiplier()
        {
            switch (Stage)
            {
                case StressStage.NearCollapse: return 0.45f;
                case StressStage.Breakdown: return 0.3f;
                case StressStage.Overloaded: return 0.75f;
                default: return 1f;
            }
        }

        public static string StageLabel(StressStage s)
        {
            switch (s)
            {
                case StressStage.Strained: return "承压";
                case StressStage.Destabilized: return "动摇";
                case StressStage.Overloaded: return "过载";
                case StressStage.NearCollapse: return "濒临崩溃";
                case StressStage.Breakdown: return "短暂失守";
                default: return "稳定";
            }
        }

        static float Ratio(float v, float max) => max > 0f ? Mathf.Clamp01(v / max) : 0f;
    }
}
