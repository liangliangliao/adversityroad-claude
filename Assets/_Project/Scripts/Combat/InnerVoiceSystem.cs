using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Core;
using AdversityRoad.Personalization;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 内部言语攻击（方案「内心敌人字幕攻击 / 脑内回声」）：
    /// 战斗的言语攻防不只来自外部敌人，也来自玩家自己的脑内回声——反刍与自我怀疑
    /// 累积到一定程度，就会以第一人称的负面念头浮出（"我是不是根本不行？"），
    /// 玩家用同一套三选一言语攻防回应；这正是方案里【内心敌人】与外部敌人的区分。
    ///
    /// 触发条件（择一，带冷却与迟滞，避免刷屏）：
    ///   · 反刍值偏高（≥55%）——反刍越高触发越频繁；
    ///   · 场上有【内心类敌人】（自我怀疑低语/羞耻镜像/反刍虫群/过去审判官…）在逼近。
    /// 结算：
    ///   · 答对——脑内回声散去，反刍下降、对应属性回补（把念头说清，它就松开）；
    ///   · 忽略/答错——照常吃这次心理伤害并额外累积反刍（越回避越回放）。
    /// 与外部敌人言语攻防、休养生息答题互斥（同一时刻只弹一个模态）。
    /// </summary>
    public class InnerVoiceSystem : MonoBehaviour
    {
        [Header("触发")]
        /// <summary>
        /// 三个条件**同时**满足才会出现内部言语攻击：反刍很高、自尊很低、意志很低。
        ///
        /// 【为什么改成"与"，而不是原来的"反刍过线就发"】
        /// 原条件是 反刍≥55% 或（内心敌人在场且反刍≥35%）。反刍在正常战斗里很容易
        /// 到五成以上，于是脑内回声在状态还很好的时候就一直冒出来——玩家读到的是
        /// "随机出现"，而不是"我现在真的撑不住了"。
        ///
        /// 这三条同时成立才是它该出现的时刻：脑子里转不停（反刍高）、
        /// 觉得自己不行（自尊低）、快撑不住了（意志低）。三者缺一，
        /// 脑内那个声音就不该有说服力——它插进来只会变成噪音。
        /// 这也让它变成一个**状态信号**：一旦响起来，玩家就知道自己进了危险区，
        /// 而不是"又随机响了一次"。
        /// </summary>
        public float ruminationTrigger = 0.70f;    // 反刍比例：高于此
        public float selfWorthCeiling = 0.40f;     // 自尊比例：低于此
        public float willCeiling = 0.40f;          // 意志比例：低于此
        public float baseInterval = 16f;           // 刚进入触发区时的间隔（秒）
        public float minInterval = 8f;             // 三项都到极端时的最短间隔

        PlayerController _player;
        float _nextAllowed;

        // 脑内回声台词（第一人称负面念头，按弱点轴分组；抽象化、服务机制，
        // 不输出现实可操控话术）——与外部敌人的第二人称嘲讽区分开
        static readonly Dictionary<WeaknessAxis, string[]> Echoes = new Dictionary<WeaknessAxis, string[]>
        {
            { WeaknessAxis.SelfDoubt, new[]
                { "我是不是根本不行？", "我是不是太计较了？", "换别人早就做好了吧。" } },
            { WeaknessAxis.Shame, new[]
                { "他们一定看穿我了。", "我出丑的样子被记住了吧。", "我不配站在这里。" } },
            { WeaknessAxis.FailureFear, new[]
                { "万一又失败了呢？", "上次不就砸了吗，这次也一样。", "失败就是我这个人。" } },
            { WeaknessAxis.WillpowerCollapse, new[]
                { "撑不下去了吧。", "放弃算了，反正没用。", "我就是坚持不了。" } },
            { WeaknessAxis.Procrastination, new[]
                { "明天再开始也一样。", "现在状态不对，等等再说。", "反正来不及了。" } },
        };
        static readonly WeaknessAxis[] Axes =
        {
            WeaknessAxis.SelfDoubt, WeaknessAxis.Shame,
            WeaknessAxis.FailureFear, WeaknessAxis.WillpowerCollapse, WeaknessAxis.Procrastination
        };

        void Awake() => _player = GetComponent<PlayerController>();

        void Update()
        {
            if (_player == null || _player.Stats == null || _player.Stats.IsDead) return;
            if (Time.unscaledTime < _nextAllowed) return;

            var vd = UI.VerbalDefenseController.Instance;
            if (vd != null && vd.IsActive) return;                    // 外部言语攻防中，不抢屏
            if (UI.QuizPanel.Instance != null && UI.QuizPanel.Instance.Active) return;
            var gm = GameManager.Instance;
            if (gm != null && gm.safety != null && gm.safety.MentalDamageMultiplier() <= 0f)
                return;                                               // 恢复模式：不发起

            var s = _player.Stats;
            float rumRatio = s.maxRumination > 0 ? s.rumination / s.maxRumination : 0f;
            float swRatio = s.maxSelfWorth > 0 ? s.selfWorth / s.maxSelfWorth : 1f;
            float willRatio = s.maxWill > 0 ? s.will / s.maxWill : 1f;
            bool innerNear = InnerEnemyNearby();

            // 三条同时成立才发：脑子转不停 + 觉得自己不行 + 快撑不住了。
            // 内心敌人在场只把门槛放宽一档（反刍 -10%、自尊/意志各 +10%），
            // 不再是"绕过条件直接发"——它是加压的，不是另一套触发。
            float rumGate = innerNear ? ruminationTrigger - 0.10f : ruminationTrigger;
            float swGate = innerNear ? selfWorthCeiling + 0.10f : selfWorthCeiling;
            float willGate = innerNear ? willCeiling + 0.10f : willCeiling;
            bool trigger = rumRatio >= rumGate && swRatio <= swGate && willRatio <= willGate;
            if (!trigger) { _nextAllowed = Time.unscaledTime + 2f; return; }

            if (Fire(s, rumRatio, innerNear))
            {
                // 越深入危险区，下一次来得越快：三项各自的"越界程度"取平均
                float tr = Mathf.InverseLerp(rumGate, 1f, rumRatio);
                float ts = 1f - Mathf.InverseLerp(0f, Mathf.Max(0.01f, swGate), swRatio);
                float tw = 1f - Mathf.InverseLerp(0f, Mathf.Max(0.01f, willGate), willRatio);
                float t = Mathf.Clamp01((tr + ts + tw) / 3f);
                float interval = Mathf.Lerp(baseInterval, minInterval, t) * (innerNear ? 0.75f : 1f);
                _nextAllowed = Time.unscaledTime + interval;
            }
            else _nextAllowed = Time.unscaledTime + 4f;
        }

        bool InnerEnemyNearby()
        {
            foreach (var e in AdversityRoad.Core.ActorRegistry.Enemies)
            {
                if (e.State == EnemyState.Dead || e.profile == null) continue;
                if (e.profile.category != EnemyCategory.Internal) continue;
                if ((e.transform.position - transform.position).sqrMagnitude < 12f * 12f)
                    return true;
            }
            return false;
        }

        bool Fire(PlayerStats s, float rumRatio, bool innerNear)
        {
            var vd = UI.VerbalDefenseController.Instance;
            if (vd == null) return false;

            // 选轴：内心敌人在场时优先用它的弱点轴（脑内回声呼应场上的内心敌人），
            // 否则按玩家画像里的高分内在弱点，退回随机
            WeaknessAxis axis = PickAxis(innerNear);
            string line = Pick(axis);

            // 忽略/答错时才吃伤害：脑内回声的心理伤害偏低但主要推高反刍
            var gm = GameManager.Instance;
            float dmg = MentalDamageSystem.Resolve(9f, axis,
                gm != null ? gm.CurrentProfile : null,
                gm != null ? gm.safety : null);
            var hit = new DamageInfo
            {
                mentalDamage = dmg,
                mentalAxis = axis,
                isMentalOnly = true,
                attackerId = "inner_voice"
            };

            // enemy 传 null：VerbalDefenseController 已对空敌人容错（答对不调用 OnVerbalCountered）
            bool started = vd.Begin(null, axis, "脑内回声", line, hit);
            if (started) GameAudio.Play(GameAudio.Sfx.Hurt, 0.35f);
            return started;
        }

        WeaknessAxis PickAxis(bool innerNear)
        {
            if (innerNear)
            {
                foreach (var e in AdversityRoad.Core.ActorRegistry.Enemies)
                    if (e.State != EnemyState.Dead && e.profile != null &&
                        e.profile.category == EnemyCategory.Internal &&
                        Echoes.ContainsKey(e.profile.targetWeakness))
                        return e.profile.targetWeakness;
            }
            var prof = GameManager.Instance != null ? GameManager.Instance.CurrentProfile : null;
            if (prof != null)
            {
                WeaknessAxis best = Axes[0];
                float bestScore = -1f;
                foreach (var a in Axes)
                {
                    float sc = prof.GetWeaknessScore(a);
                    if (sc > bestScore) { bestScore = sc; best = a; }
                }
                if (bestScore > 0.3f) return best;
            }
            return Axes[Random.Range(0, Axes.Length)];
        }

        static string Pick(WeaknessAxis axis)
        {
            // 身处 AI 生成的场景里时，脑内回声也用它为这个场景写的内部言语攻击
            string chapterLine = AI.DialogueLibrary.GetChapterInnerLine();
            if (!string.IsNullOrEmpty(chapterLine) && Random.value < 0.7f) return chapterLine;

            var arr = Echoes.TryGetValue(axis, out var a) ? a : Echoes[WeaknessAxis.SelfDoubt];
            return arr[Random.Range(0, arr.Length)];
        }
    }
}
