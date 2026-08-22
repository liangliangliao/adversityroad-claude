using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// SelfWorth 归零的羞耻状态（方案 8.10.2）。
    ///
    /// 【剥夺事实之刃是一个主题性的机制选择】
    /// 在羞耻里，人不认为自己有资格陈述事实。把这句话做成一条真的操作限制，
    /// 玩家才会亲身体验到它，而不是读到它。
    ///
    /// 【但它必须是短时且可自解的】
    /// 遵 §12.3：Breakdown 类状态不得长时间剥夺控制权。上限 12 秒，
    /// 或完成任意一次目标相关行动即解除——两者取先。
    /// </summary>
    public static class ShameBreakdown
    {
        public const float MaxSeconds = 12f;

        static float _until = -1f;

        public static bool FactBladeSuppressed => Time.time < _until;

        public static void Enter()
        {
            if (FactBladeSuppressed) return;
            _until = Time.time + MaxSeconds;
            GameEvents.RaiseSubtitle("【羞耻状态】事实之刃暂时拔不出来——" +
                "完成任意一次与目标相关的行动即可解除（最长 12 秒）。");
        }

        /// <summary>完成一次目标相关行动：立即解除。</summary>
        public static void ResolveByAction(string what)
        {
            if (!FactBladeSuppressed) return;
            _until = -1f;
            GameEvents.RaiseSubtitle(what + "——做了一件事，说话的资格就回来了。");
        }

        public static void Reset() => _until = -1f;
    }

    /// <summary>
    /// 聚光灯校准（方案 8.8.2，自尊锚点并列节点）。
    ///
    /// 【它不打人，也不让人闭嘴】
    /// 它只做一件事：把"场内敌人真实的注意力值"与"玩家感知到的注意力值"
    /// 同时摆出来，让高估的那一部分显形。
    /// 这正是 8.15 里给"低语链令人窒息"开的那一味药——不是消音，是校准。
    ///
    /// 【不上庭】（真实一击并列节点）
    /// 拒绝进入「判词」类交互一次；对悬案法官的延期招式免疫一次，并产生反击窗口。
    /// 它处理的是另一种消耗：被拖进一场根本不该由对方主持的评审。
    /// </summary>
    public class ShameSkills : MonoBehaviour
    {
        public static ShameSkills Instance { get; private set; }

        public const float SpotlightCooldown = 26f;
        public const float SpotlightDuration = 6f;
        public const float RefuseCooldown = 30f;
        public const float RefuseArmedSeconds = 12f;

        float _spotlightReadyAt;
        float _refuseReadyAt;
        float _refuseArmedUntil = -1f;

        public bool SpotlightReady => Time.time >= _spotlightReadyAt;
        public bool RefuseReady => Time.time >= _refuseReadyAt;
        public float SpotlightCooldownLeft => Mathf.Max(0f, _spotlightReadyAt - Time.time);
        public float RefuseCooldownLeft => Mathf.Max(0f, _refuseReadyAt - Time.time);

        /// <summary>「不上庭」已举起：下一次判词类交互/延期招式会被它挡掉。</summary>
        public bool RefuseArmed => Time.time < _refuseArmedUntil;

        public static ShameSkills Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ShameSkills");
            Instance = go.AddComponent<ShameSkills>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (!ShameLine.InChapter) return;
            if (Input.GetKeyDown(KeyCode.G)) CastSpotlight();
            if (Input.GetKeyDown(KeyCode.H)) CastRefuse();
        }

        // ================= 聚光灯校准 =================

        public bool CastSpotlight()
        {
            if (!SpotlightReady)
            {
                GameEvents.RaiseSubtitle("「聚光灯校准」还在调息（" +
                    Mathf.CeilToInt(SpotlightCooldownLeft) + "s）。");
                return false;
            }
            _spotlightReadyAt = Time.time + SpotlightCooldown;

            var player = FindObjectOfType<PlayerController>();
            if (player == null) return false;

            // 玩家感知到的注意力：被暴露度整体抬高——这正是"高估"的来源
            float exposure01 = ExposureSystem.Instance != null ? ExposureSystem.Instance.Ratio : 0f;
            int counted = 0, overestimated = 0;

            foreach (var e in FindObjectsOfType<EnemyController>())
            {
                if (e == null || e.State == EnemyState.Dead) continue;
                float dist = Vector3.Distance(e.transform.position, player.transform.position);
                if (dist > 26f) continue;

                // 真实注意力：只由可观察的量决定——它有没有在追你、离你多远、是不是在看你
                float real = Mathf.Clamp01(
                    (e.State == EnemyState.Chase || e.State == EnemyState.Attack ? 0.55f : 0.12f) +
                    Mathf.Clamp01(1f - dist / 26f) * 0.3f);
                float perceived = Mathf.Clamp01(real + exposure01 * 0.55f);

                counted++;
                if (perceived - real > 0.18f) overestimated++;

                var color = perceived - real > 0.18f
                    ? new Color(0.95f, 0.86f, 0.45f) : new Color(0.7f, 0.85f, 0.8f);
                CombatFeedback.DamageNumber(e.transform.position + Vector3.up * 2.4f,
                    "在看你 " + Mathf.RoundToInt(real * 100f) +
                    "%　你以为 " + Mathf.RoundToInt(perceived * 100f) + "%", color, 0.9f);
            }

            CombatFeedback.ShockRing(player.transform.position, new Color(0.95f, 0.9f, 0.6f), 6f);
            GameAudio.Play(GameAudio.Sfx.Cast, 0.7f);
            GameEvents.RaiseSubtitle(counted == 0
                ? "场上没有人在看你——这一条也是事实。"
                : "【聚光灯校准】" + counted + " 人在场，其中 " + overestimated +
                  " 人的注意力被你高估了。高估的部分，持续 " +
                  Mathf.RoundToInt(SpotlightDuration) + " 秒可见。");
            return true;
        }

        // ================= 不上庭 =================

        public bool CastRefuse()
        {
            if (!RefuseReady)
            {
                GameEvents.RaiseSubtitle("「不上庭」还在调息（" +
                    Mathf.CeilToInt(RefuseCooldownLeft) + "s）。");
                return false;
            }
            _refuseReadyAt = Time.time + RefuseCooldown;
            _refuseArmedUntil = Time.time + RefuseArmedSeconds;

            var player = FindObjectOfType<PlayerController>();
            if (player != null)
            {
                CombatFeedback.MoveName(player.transform.position + Vector3.up * 2.2f, "不上庭", false);
                CombatFeedback.ShockRing(player.transform.position, new Color(0.6f, 0.8f, 0.95f), 3.2f);
            }
            GameAudio.Play(GameAudio.Sfx.Cast, 0.8f);
            GameEvents.RaiseSubtitle("「这件事不归你审。」——下一次判词类交互与延期招式被拒绝一次。");
            return true;
        }

        /// <summary>
        /// 判词类交互试图发生。返回 true 表示被「不上庭」挡下——
        /// 调用方应当放弃这次交互，并给玩家一个反击窗口。
        /// </summary>
        public bool TryRefuse(EnemyController src, string what)
        {
            if (!RefuseArmed) return false;
            _refuseArmedUntil = -1f;
            GameEvents.RaiseSubtitle("【不上庭】" + what + "被当场拒绝——他愣住了，这是你的窗口。");
            if (src != null) src.ForceBreak(2.2f);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.9f);
            Adversity.AdversityProfile.ObserveStrength("拒绝低价值评审", ShameLine.CurrentLevelId);
            return true;
        }
    }
}
