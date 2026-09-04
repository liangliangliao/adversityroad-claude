using System.Collections;
using UnityEngine;
using AdversityRoad.Adversity;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.Shame;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 后排低语者——8-2 的 Boss，T5 宿敌候选（方案 8.6.4）。
    ///
    /// 【他不与玩家正面战斗。他只做三件事：看、说、指】
    /// 血量不由伤害决定，而由玩家的**否认次数**与 **Exposure 峰值**共同维持：
    /// 否认越多，他回得越快。这条规则把"每一次辩解都在给他续命"做成了可读的数字。
    ///
    /// 三阶段：
    ///   一【凝视】全场视线锥收拢至玩家，Exposure 增速 ×2。
    ///      反制不是躲——是进锥内把目标动作做完。
    ///   二【指认】连续三次指认招式，每次挂一枚钉。唯一解是三次连续的认领不终审。
    ///   三【扩散】低语链全场重建并锁定；玩家必须在链条活跃时完成最后一个目标动作。
    ///
    /// 【结算的关键约束】
    /// 他不得道歉、不得改口、不得承认误会。任何形式的"他其实一直误会你"
    /// 都会摧毁本章的核心命题。玩家拿到的不是清白，是行动能力：
    /// 他只是停下来了，玩家继续走出去。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class BackRowWhispererBoss : MonoBehaviour
    {
        EnemyController _ec;
        Transform _player;
        int _phase;
        bool _busy;
        bool _silenced;
        float _nextRegen;
        float _nailCd = 6f;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p != null) _player = p.transform;

            // 伤害推不动他：他的血由玩家的否认与暴露峰值维持，不由刀剑决定
            _ec.externalDamageMult = 0.1f;
            _ec.minHpFloor = 0.05f;
            // 他的血不由伤害决定，而由否认次数与暴露峰值维持（方案 8.6.4）。
            // 这一条必须写在头顶，否则"砍半天不掉血"只会被读成打不死。
            _ec.emotionOverride = "血由你的否认维持 · 刀砍不动";
            GameEvents.RaiseSubtitle("【后排低语者】他不动手。他看、他说、他指——" +
                "刀砍不动他：他的血由你的否认次数维持，不由伤害决定。");
            SetPhase(1);
        }

        void Update()
        {
            if (_ec == null || _ec.State == EnemyState.Dead || _player == null || _silenced) return;
            var ctl = ShameLineController.Instance;
            if (ctl == null) return;

            TickBloodFromDenial();

            // 阶段随目标动作推进，而不是随血量推进——这一关的进度条是"做完了几件事"
            int want = ctl.ObjectivesDone >= 2 ? 3 : ctl.ObjectivesDone >= 1 ? 2 : 1;
            if (want > _phase) SetPhase(want);

            if (_busy) return;
            if (_phase >= 2)
            {
                _nailCd -= Time.deltaTime;
                if (_nailCd <= 0f)
                {
                    _nailCd = ShameLineController.ChallengeCeilingActive ? 22f : 15f;
                    StartCoroutine(AccusationTriple());
                }
            }
        }

        int _seenDenials;

        /// <summary>
        /// 血量随否认次数与 Exposure 峰值回复：这是他唯一的"补给线"。
        ///
        /// 只在**玩家刚刚否认过**的时候回——回血必须和那一次否认对得上，
        /// 否则玩家看到的只是一个莫名其妙一直回血的 Boss，读不出因果。
        /// </summary>
        void TickBloodFromDenial()
        {
            if (Time.time < _nextRegen) return;
            _nextRegen = Time.time + 1.5f;
            var d = ShameLine.Data;
            if (d.denialCount <= _seenDenials) return;
            _seenDenials = d.denialCount;

            float peak01 = Mathf.Clamp01(d.exposurePeak / 100f);
            _ec.HealFraction(0.06f + 0.05f * peak01);
            GameEvents.RaiseSubtitle("他缓过来了——刚才那一次否认，正好是他要的。");
        }

        void SetPhase(int phase)
        {
            _phase = phase;
            switch (phase)
            {
                case 1:
                    GameEvents.RaiseSubtitle("【阶段一 · 凝视】全场的视线收拢过来了——" +
                        "别躲。走进锥里，把该做的那件事做完。");
                    var gaze = GazeConeSystem.Instance;
                    if (gaze != null) gaze.FocusOn(_player, 2f);
                    break;
                case 2:
                    GameEvents.RaiseSubtitle("【阶段二 · 指认】他要连指三次——" +
                        "三次连续的认领不终审，一次都不能换成辩解。");
                    break;
                case 3:
                    GameEvents.RaiseSubtitle("【阶段三 · 扩散】低语铺满全场。" +
                        "最后一件事，要在它活着的时候做完。");
                    var whisper = WhisperChainSystem.Instance;
                    if (whisper != null) whisper.FieldWideRebuild();
                    break;
            }
        }

        /// <summary>阶段二：连续三次指认，每次挂一枚钉。</summary>
        IEnumerator AccusationTriple()
        {
            _busy = true;
            var own = OwnNotFinalSystem.Instance;
            for (int i = 0; i < 3 && !_silenced; i++)
            {
                if (_ec == null || _ec.State == EnemyState.Dead) break;
                var claim = ClaimRegistry.Draw(_ec.profile != null ? _ec.profile.enemyId : "", true);
                if (claim == null)
                {
                    // 认过的事他一条都拿不起来了——「指控复用失效」，逆袭判定第一条
                    if (_ec.dialogue != null) _ec.dialogue.Show("……", 2f);
                    GameEvents.RaiseSubtitle("他张了张嘴，没有词了——认过的事，本章内他再也用不上。");
                    break;
                }
                if (own != null)
                    own.Accuse(_ec, claim, _ec.profile != null ? _ec.profile.mentalDamage : 16f);
                yield return new WaitForSeconds(4.2f);
            }
            _busy = false;
        }

        /// <summary>
        /// 玩家在阶段三完成目标动作：他停止发声。
        /// 没有击杀动画，没有道歉，没有和解台词。
        /// </summary>
        public void Silence()
        {
            if (_silenced) return;
            _silenced = true;
            _ec.pacified = true;
            if (_ec.dialogue != null) _ec.dialogue.Show("", 0.1f);
            GameEvents.RaiseSubtitle("他停下来了。没有道歉，也没有解释——他只是不再说了。");
            EvaluateNemesis();
        }

        /// <summary>
        /// 宿敌升格（8.6.4）：否认 ≥5 次或触发搜查回响 → 标记为 Nemesis 候选，
        /// 可升格为 T7『未播出的广播』。
        ///
        /// 他学到的只有**行为标签**（否认频率、暴露峰值、回避路线），
        /// 绝不含玩家在游戏外输入的原文（验收第 46 条）。
        /// </summary>
        void EvaluateNemesis()
        {
            var d = ShameLine.Data;
            bool promote = d.denialCount >= 5 || d.searchEchoTaken;
            if (!promote) return;

            var nem = NemesisSystem.NoteDefeatedBy(_ec, 0f, "羞耻线 · 后排低语者");
            if (nem != null)
            {
                if (!nem.learnedPlayerPatterns.Contains("否认优先于认领"))
                    nem.learnedPlayerPatterns.Add("否认优先于认领");
                if (d.searchEchoTaken && !nem.learnedPlayerPatterns.Contains("搜查回响已执行"))
                    nem.learnedPlayerPatterns.Add("搜查回响已执行");
                nem.displayName = "未播出的广播";
                nem.currentRank = Mathf.Max(nem.currentRank, 3);
            }
            GameEvents.RaiseSubtitle("【宿敌候选】后排低语者被登记为宿敌——" +
                "他记住的只是你的打法，不是你这个人。");
        }
    }
}
