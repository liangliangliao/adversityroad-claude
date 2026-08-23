using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.World;

namespace AdversityRoad.Adversity
{
    /// <summary>
    /// Resolve Window 与 Adversity Surge（方案 12.4）。
    ///
    /// 在濒临崩溃下，如果玩家完成一次**高难度但公平**的精准格挡、闪避、
    /// 目标相关动作或保护行为，就会打开一个短暂的 Resolve Window；
    /// 成功后进入 Adversity Surge：意志部分恢复、短时抗硬直、音乐层上升、允许特殊反击。
    ///
    /// 关键设计约束：**触发不应随机送胜利**——它奖励的是"压力下的高质量行动"。
    /// 没有这条约束，逆转就变成了运气；有了它，逆转才是玩家自己挣来的。
    /// </summary>
    public class ResolveSystem : MonoBehaviour
    {
        public static ResolveSystem Instance { get; private set; }

        public const float WindowDuration = 4.5f;
        public const float SurgeDuration = 8f;

        float _windowUntil = -1f;
        float _surgeUntil = -1f;
        PlayerController _player;

        public bool WindowOpen => Time.time < _windowUntil;
        public bool SurgeActive => Time.time < _surgeUntil;
        public float SurgeRemaining01 =>
            SurgeActive ? Mathf.Clamp01((_surgeUntil - Time.time) / SurgeDuration) : 0f;

        public event System.Action SurgeStarted;
        public event System.Action SurgeEnded;

        bool _surgeAnnounced;

        public static ResolveSystem Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ResolveSystem");
            Instance = go.AddComponent<ResolveSystem>();
            return Instance;
        }

        void Update()
        {
            if (_surgeAnnounced && !SurgeActive)
            {
                _surgeAnnounced = false;
                SurgeEnded?.Invoke();
                GameEvents.RaiseSubtitle("那股劲过去了——但你已经从崩溃边缘走回来了一次。");
            }
        }

        /// <summary>濒临崩溃时由 StressStateMachine 打开窗口。</summary>
        public void OpenWindow()
        {
            if (SurgeActive) return;
            _windowUntil = Time.time + WindowDuration;
            GameEvents.RaiseSubtitle("〔逆转窗口〕现在做出一次漂亮的格挡、闪避或目标动作——" +
                "就能把这一场翻过来。");
        }

        /// <summary>
        /// 高质量行动（精准格挡 / 闪避成功 / 完成目标相关动作 / 保护 NPC）。
        /// 只有窗口开着时才算数——平时做得再好也不会白送一次逆转。
        /// </summary>
        public void NoteQualityAction(string what)
        {
            if (!WindowOpen || SurgeActive) return;
            _windowUntil = -1f;
            BeginSurge(what);
        }

        void BeginSurge(string what)
        {
            _surgeUntil = Time.time + SurgeDuration;
            _surgeAnnounced = true;

            if (_player == null) _player = AdversityRoad.Core.ActorRegistry.Player;
            if (_player != null && _player.Stats != null)
            {
                // 部分恢复，而不是回满：逆转是喘一口气，不是重开一局
                _player.Stats.will = Mathf.Min(_player.Stats.maxWill,
                    _player.Stats.will + _player.Stats.maxWill * 0.35f);
                _player.Stats.RestoreMental(20f);
                GameEvents.RaiseMentalStatChanged("will", _player.Stats.will, _player.Stats.maxWill);
            }

            GameAudio.Play(GameAudio.Sfx.Cast, 1.1f);
            SurgeStarted?.Invoke();
            GameEvents.RaiseSubtitle("〔逆转 · Adversity Surge〕" + what +
                "——意志回来了一截，硬直抗性提升，反击窗口打开。");

            // 勇气来自"恐惧/压力存在时仍做目标相关行动"，这次逆转本身就是最强的样本
            CourageSystem.NoteCourage("濒临崩溃下完成了" + what, true);
            AdversityProfile.ObserveStrength(PlayerBehaviorAnalyzer.StrengthRecovery,
                ZoneBuilder.CurrentZoneId);
        }

        /// <summary>Surge 期间的抗硬直系数（供受击处理读取）。</summary>
        public float PoiseMultiplier() => SurgeActive ? 0.35f : 1f;

        /// <summary>Surge 期间允许的特殊反击加成。</summary>
        public float SurgeDamageMultiplier() => SurgeActive ? 1.25f : 1f;
    }

    /// <summary>
    /// Courage / Perseverance（方案 12.5）。
    ///
    /// 勇气不来自"答对选项"，而来自**恐惧与压力存在时仍然做目标相关的行动**；
    /// 坚持来自失败之后再次进入挑战**并改变策略**。
    /// 系统明确禁止通过无脑送死刷勇气——所以每一次记账都要检查当时是否真的有压力，
    /// 以及这个行动是否真的与目标有关。
    /// </summary>
    public static class CourageSystem
    {
        const string CourageKey = "adversity_courage_v2";
        const string PerseveranceKey = "adversity_perseverance_v2";
        const string LastFailKey = "adversity_last_fail_strategy";

        public static int Courage => PlayerPrefs.GetInt(CourageKey, 0);
        public static int Perseverance => PlayerPrefs.GetInt(PerseveranceKey, 0);

        /// <summary>
        /// 记一次勇气。goalRelated=false 或当时压力不足时不计分——
        /// 这就是"无脑送死刷不到勇气"的实现方式。
        /// </summary>
        public static void NoteCourage(string what, bool goalRelated)
        {
            if (!goalRelated) return;
            var stress = StressStateMachine.Instance;
            bool underPressure = stress != null && stress.Stage >= StressStage.Destabilized;
            if (!underPressure) return;

            PlayerPrefs.SetInt(CourageKey, Courage + 1);
            PlayerPrefs.Save();
            GameEvents.RaiseSubtitle("勇气 +1：" + what + "——害怕还在，但你还是往前走了。");
        }

        /// <summary>
        /// 记一次坚持：失败后再次进入同一挑战**并且换了策略**才算。
        /// 只是重复送死不会累积，因为策略没有变化。
        /// </summary>
        public static void NoteRetry(string strategyTag)
        {
            string last = PlayerPrefs.GetString(LastFailKey, "");
            PlayerPrefs.SetString(LastFailKey, strategyTag ?? "");
            if (string.IsNullOrEmpty(strategyTag) || strategyTag == last) return;

            PlayerPrefs.SetInt(PerseveranceKey, Perseverance + 1);
            PlayerPrefs.Save();
            GameEvents.RaiseSubtitle("坚持 +1：这一次你换了打法——重来不等于重复。");
            AdversityProfile.ObserveStrength(PlayerBehaviorAnalyzer.StrengthRetry,
                ZoneBuilder.CurrentZoneId);
        }

        /// <summary>目标相关的困难行动（旅程节点推进时调用）。</summary>
        public static void NoteGoalAction(string label) => NoteCourage(label, true);

        public static string Summary() =>
            "勇气 " + Courage + " · 坚持 " + Perseverance;

        public static void DeleteAll()
        {
            PlayerPrefs.DeleteKey(CourageKey);
            PlayerPrefs.DeleteKey(PerseveranceKey);
            PlayerPrefs.DeleteKey(LastFailKey);
        }
    }
}
