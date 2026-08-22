using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// Exposure 暴露度（方案 8.3 / 8.13.1）。
    ///
    /// 【它不是第十根常驻血条】
    /// 遵 §12.2 的前台精简原则：底层数值一直在跑并写进复盘页，
    /// 但 HUD 上**只在被有效注视时**才短暂显现，其余时间完全隐藏（验收第 43 条）。
    ///
    /// 【有效注视的定义】
    /// 存在一个能够指认玩家的观察者，且玩家在其视线范围内。
    /// 所以"屋里有人"不算，"有人在看你"才算——这条判定由 GazeConeSystem 提供。
    ///
    /// 【与 SelfWorth 的联动】
    /// ≥60 时心理攻击对自尊的伤害 ×1.5；≥85 时 ×2.0。
    /// 这是本章"被看见会放大一切"的机制表达，不是难度调参。
    /// </summary>
    public class ExposureSystem : MonoBehaviour
    {
        public static ExposureSystem Instance { get; private set; }

        /// <summary>满值后的『显形』持续时间。</summary>
        public const float RevealDuration = 20f;

        /// <summary>离开视线锥后的缓降速率（缓慢——注视的余味不会立刻散）。</summary>
        public const float DecayPerSec = 3.5f;

        /// <summary>锥内稳态优势窗口：连续行动不回避满这么久就触发（8.7.2）。</summary>
        public const float SteadyConeSeconds = 8f;

        float _revealUntil = -1f;
        float _lastGazedAt = -99f;
        float _steadyConeSince = -1f;
        float _steadyRewardAt = -99f;
        bool _steadyActive;

        public static ExposureSystem Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ExposureSystem");
            Instance = go.AddComponent<ExposureSystem>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public float Value => ShameLine.Data.exposure;
        public float Cap => ShameLine.Data.exposureCap;
        public float Ratio => Cap > 0 ? Mathf.Clamp01(Value / Cap) : 0f;

        /// <summary>『显形』状态：全场敌人获得追踪、锁定被强制解除、脚步声放大。</summary>
        public bool Revealed => Time.time < _revealUntil;

        /// <summary>最近 1.2 秒内被有效注视——HUD 的情境显示判据。</summary>
        public bool RecentlyGazed => Time.time - _lastGazedAt < 1.2f;

        /// <summary>锥内稳态：在视线锥内连续行动而未回避，Exposure 增速减半并回自尊。</summary>
        public bool SteadyInCone => _steadyActive;

        /// <summary>心理伤害对自尊的放大倍率（8.3）。不在本章内恒为 1。</summary>
        public static float SelfWorthDamageMultiplier()
        {
            var s = Instance;
            if (s == null || !ShameLine.InChapter) return 1f;
            if (s.Value >= 85f) return 2f;
            if (s.Value >= 60f) return 1.5f;
            return 1f;
        }

        void Update()
        {
            if (!ShameLine.InChapter) { _steadyActive = false; return; }
            float dt = Time.deltaTime;
            var d = ShameLine.Data;

            var gaze = GazeConeSystem.Instance;
            float rate = gaze != null ? gaze.ExposureRateOnPlayer() : 0f;
            bool gazed = rate > 0.01f;
            if (gazed) _lastGazedAt = Time.time;

            if (gazed)
            {
                // 锥内稳态（8.7.2 优势窗口）：站在注视里继续做事，压力反而降下来。
                // 这是本章防"回避成为最优解"的正向反馈——绕开永远拿不到它。
                if (_steadyConeSince < 0) _steadyConeSince = Time.time;
                bool steady = Time.time - _steadyConeSince >= SteadyConeSeconds;
                if (steady && !_steadyActive) EnterSteady();
                _steadyActive = steady;
                Add(rate * (_steadyActive ? 0.5f : 1f) * dt, null);
                if (_steadyActive) TickSteadyReward();
            }
            else
            {
                _steadyConeSince = -1f;
                _steadyActive = false;
                if (d.exposure > 0) Add(-DecayPerSec * dt, null);
            }

            if (Revealed) TickRevealed();
        }

        void EnterSteady()
        {
            GameEvents.RaiseSubtitle("【锥内稳态】你在注视里站住了 8 秒没有回避——暴露增速减半，自尊开始回补。");
            Adversity.AdversityProfile.ObserveStrength("锥内完成率", ShameLine.CurrentLevelId);
            var resolve = Adversity.ResolveSystem.Instance;
            if (resolve != null) resolve.NoteQualityAction("在视线锥内持续行动未回避");
        }

        void TickSteadyReward()
        {
            if (Time.time - _steadyRewardAt < 1f) return;
            _steadyRewardAt = Time.time;
            var p = FindObjectOfType<PlayerController>();
            if (p != null) p.Stats.RestoreAxis(Personalization.WeaknessAxis.Shame, 2.5f);
        }

        /// <summary>
        /// 增减暴露度。reason 非空时给一句字幕——玩家必须能看见账单是怎么来的。
        /// </summary>
        public void Add(float amount, string reason)
        {
            var d = ShameLine.Data;
            // 被动减速只作用于"涨"：自述套与自述之证让你被看见时涨得慢，
            // 但不会让已经涨上去的部分凭空掉下来
            if (amount > 0f) amount *= GrowthSystem.ExposureGainMult();
            float before = d.exposure;
            d.exposure = Mathf.Clamp(d.exposure + amount, 0f, d.exposureCap);
            if (d.exposure > d.exposurePeak) d.exposurePeak = d.exposure;
            if (Mathf.Approximately(before, d.exposure)) return;

            if (!string.IsNullOrEmpty(reason))
            {
                GameEvents.RaiseSubtitle(reason + "（暴露度 " + Mathf.RoundToInt(d.exposure) +
                    "/" + Mathf.RoundToInt(d.exposureCap) + "）");
                ShameLine.Persist();
            }
            else ShameLine.Touch();

            if (before < d.exposureCap && d.exposure >= d.exposureCap) EnterRevealed();
        }

        /// <summary>抬高上限（隐瞒成功、搜查回响）——注意抬的是天花板，当下反而更"安全"。</summary>
        public void RaiseCap(float amount, string reason)
        {
            var d = ShameLine.Data;
            d.exposureCap = Mathf.Clamp(d.exposureCap + amount, 40f, 200f);
            if (!string.IsNullOrEmpty(reason))
                GameEvents.RaiseSubtitle(reason + "（暴露度上限升至 " +
                    Mathf.RoundToInt(d.exposureCap) + "）");
            ShameLine.Persist();
        }

        /// <summary>自行陈述完成：暴露度清零。</summary>
        public void Clear(string reason)
        {
            ShameLine.Data.exposure = 0f;
            _revealUntil = -1f;
            if (!string.IsNullOrEmpty(reason)) GameEvents.RaiseSubtitle(reason);
            ShameLine.Persist();
        }

        /// <summary>离开关卡：按 60% 衰减带入下一关（8.3）。</summary>
        public void CarryToNextLevel()
        {
            var d = ShameLine.Data;
            d.exposure *= 0.6f;
            _revealUntil = -1f;
            ShameLine.Persist();
        }

        // ================= 显形 =================

        void EnterRevealed()
        {
            _revealUntil = Time.time + RevealDuration;
            GameEvents.RaiseSubtitle("【显形】所有人都看向你了——锁定失效，全场敌人开始追踪。" +
                "在下一次指认里认领不终审，可以提前解除。");
            GameAudio.Play(GameAudio.Sfx.Alert, 0.9f);
            var lockOn = FindObjectOfType<LockOnSystem>();
            if (lockOn != null) lockOn.Release();
        }

        float _nextRevealTick;

        void TickRevealed()
        {
            // 「全场敌人获得追踪」：不加伤害、不加血，只是没有人再看不见你。
            // 每 0.5 秒刷一次就够——这是状态，不是逐帧特效。
            if (Time.time < _nextRevealTick) return;
            _nextRevealTick = Time.time + 0.5f;

            var player = FindObjectOfType<PlayerController>();
            if (player == null) return;
            foreach (var e in FindObjectsOfType<AI.EnemyController>())
            {
                if (e == null || e.State == AI.EnemyState.Dead) continue;
                e.provoked = true;
            }
        }

        /// <summary>认领不终审成功：提前解除显形。</summary>
        public void ClearRevealed()
        {
            if (!Revealed) return;
            _revealUntil = -1f;
            GameEvents.RaiseSubtitle("显形解除——你没有躲开注视，你只是不再需要躲。");
        }
    }
}
