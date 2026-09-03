using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 讨好度 Appeasement（方案 8.3.1 / 8.13.1）——8-1 的关卡局部计量。
    ///
    /// 【讨好在短期内确实有效，这一点必须真实】
    /// 用「顺从应答」这一次遭遇的心理伤害 -30%、追问更快结束。这是真收益，
    /// 不是陷阱，也不许用弹窗劝阻。
    ///
    /// 【它的代价是：你越讨好，对方越确认这个把柄有效】
    /// 每上升一档，Boss 阶段的伤害减免 -10%，悬案计时器缩短——案子不会因为
    /// 你更顺从而结案，只会挂得更久、条件更多。
    ///
    /// 【不做成进度条】
    /// 8.3.1 明写：以角色低头幅度、镜头高度与语音音量三个**非数值信号**表现。
    /// 玩家应该是"感觉到自己越来越低"，而不是"看到一根条涨了"。
    /// </summary>
    public class AppeasementSystem : MonoBehaviour
    {
        public static AppeasementSystem Instance { get; private set; }

        /// <summary>一档 = 25 点。四档满。</summary>
        public const float TierSize = 25f;

        float _cameraBaseY = float.NaN;

        public static AppeasementSystem Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("AppeasementSystem");
            Instance = go.AddComponent<AppeasementSystem>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; RestoreCamera(); }

        public float Value => ShameLine.Data.appeasement;

        /// <summary>当前档位 0-4。</summary>
        public int Tier => Mathf.Clamp(Mathf.FloorToInt(Value / TierSize), 0, 4);

        /// <summary>本次遭遇的心理伤害倍率：顺从确实少挨打。</summary>
        public float IncomingMentalMultiplier() => Value > 0f ? 0.7f : 1f;

        /// <summary>Boss 阶段的伤害减免衰减：每档 -10%（讨好的长期代价）。</summary>
        public float BossMitigationMultiplier() => Mathf.Clamp01(1f - 0.1f * Tier);

        /// <summary>悬案计时器缩短系数：讨好越多，案子挂得越紧。</summary>
        public float TimerShrinkMultiplier() => 1f + 0.12f * Tier;

        /// <summary>一次顺从应答。</summary>
        public void Appease(float amount, string what)
        {
            var d = ShameLine.Data;
            int before = Tier;
            d.appeasement = Mathf.Clamp(d.appeasement + amount, 0f, 100f);
            ShameLine.Persist();

            // 系统不评判玩家的选择，只诚实展示账单
            GameEvents.RaiseSubtitle(what + "——这次的压力确实小了。");
            if (Tier > before)
                GameEvents.RaiseSubtitle("他记下了这一次的顺从：这个把柄，对你是有效的。");

            ApplySignals();
            var timer = PendingCaseTimer.Instance;
            if (timer != null) timer.OnAppeasementChanged();
            Adversity.AdversityProfile.Observe("长期悬案", "讨好类交互使用频率上升", true,
                ShameLine.CurrentLevelId, "自行陈述");
        }

        /// <summary>讨好度下降（唯一能削弱讨好回声的方式）。</summary>
        public void Reduce(float amount, string why)
        {
            var d = ShameLine.Data;
            if (d.appeasement <= 0f) return;
            d.appeasement = Mathf.Max(0f, d.appeasement - amount);
            ShameLine.Persist();
            if (!string.IsNullOrEmpty(why)) GameEvents.RaiseSubtitle(why);
            ApplySignals();
        }

        public void ResetForLevel()
        {
            ShameLine.Data.appeasement = 0f;
            ShameLine.Persist();
            RestoreCamera();
        }

        /// <summary>
        /// 三个非数值信号：低头幅度、镜头高度、说话音量。
        /// 一根进度条都不要——玩家要感觉到，不是读出来。
        /// </summary>
        void ApplySignals()
        {
            var player = AdversityRoad.Core.ActorRegistry.Player;
            if (player == null) return;
            float t = Mathf.Clamp01(Value / 100f);

            // ① 低头：讨好度越高，站姿越沉
            var poser = player.GetComponent<HumanoidAnimator>();
            if (poser != null && t > 0.24f)
                poser.PlayFirstClip(1f, 0.25f, "Sad Idle", "Kneeling Down", "Defeated");

            // ② 镜头高度：往下压。人低头的时候，看见的世界本来就矮一截
            var cam = FindObjectOfType<ThirdPersonCamera>();
            if (cam != null)
            {
                if (float.IsNaN(_cameraBaseY)) _cameraBaseY = cam.offset.y;
                var o = cam.offset;
                o.y = _cameraBaseY - 0.32f * t;
                cam.offset = o;
            }

            // ③ 语音音量：越讨好，说话越小声
            _voiceVolume = Mathf.Lerp(0.85f, 0.35f, t);
        }

        float _voiceVolume = 0.85f;

        /// <summary>玩家在追问里开口时的音量（讨好度越高越小声）。</summary>
        public float VoiceVolume => _voiceVolume;

        void RestoreCamera()
        {
            if (float.IsNaN(_cameraBaseY)) return;
            var cam = FindObjectOfType<ThirdPersonCamera>();
            if (cam != null)
            {
                var o = cam.offset;
                o.y = _cameraBaseY;
                cam.offset = o;
            }
            _cameraBaseY = float.NaN;
        }
    }
}
