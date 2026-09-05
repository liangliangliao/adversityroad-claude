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

            // 【方案 8.6.4：血量不由伤害决定，由否认次数与 Exposure 峰值维持】
            //
            // 上一版我把这条实现成"血线卡在 12% + 每次否认回血"，还自己加了一条
            // 方案里没有的"磨到说不出话"的击杀路径。结果是：玩家看着血条掉到 12%，
            // 之后无论怎么打都不动，读出来就是"这敌人有无限的生命"——三轮反馈同一句话。
            //
            // 既然血量本来就不由伤害决定，那就**不该有血条**。现在伤害只造成硬直，
            // 血条不画；否认的代价改成看得见的那一种：他指认得更急（见 DenialPressure）。
            // 击败判定回到方案原文：玩家在阶段三完成目标动作，他停止发声。
            // 【产品决定：他是一个能打死的 Boss】
            // 方案 8.6.4 原本写的是"血量不由伤害决定，由否认次数与 Exposure 峰值维持"。
            // 现在口径由产品定：**和其他关卡一样，有血条、会动、打得死**。
            // 保留的是这一关真正的命题——打死他不等于通关：
            // 三个目标动作仍然要在低语链活着的时候做完，然后正常步行离场。
            // 否认仍然为他续命，只是形态换成"他指认得更急"（见 DenialPressure）。
            _ec.emotionOverride = "打得死 · 但通关靠做完三件事";
            GameEvents.RaiseSubtitle("【后排低语者】他看、他说、他指。他打得死——" +
                "但打死他不会让这一关结束：三件事仍然要在低语活着的时候做完，然后走出去。");
            SetPhase(1);
        }

        bool _deathNoted;

        void Update()
        {
            if (_ec == null || _player == null || _silenced) return;
            if (_ec.State == EnemyState.Dead)
            {
                if (!_deathNoted)
                {
                    _deathNoted = true;
                    // 低语链不会因为他倒下就散——链上还有侧目者与放大镜围观者。
                    // 这一句要说清楚，否则玩家会以为打死 Boss 就该通关了。
                    GameEvents.RaiseSubtitle("他倒下了。可低语没有停——链上还有别人。" +
                        "这一关要的仍然是：把三件事做完，然后正常走出去。");
                    EvaluateNemesis();
                }
                return;
            }
            var ctl = ShameLineController.Instance;
            if (ctl == null) return;

            DenialPressure();

            // 这里原来有一句"打到血线就 Silence"，是上一版自己加的击杀路径，
            // 方案里没有这条，而且它依赖的正是那根不该存在的血条。已删。
            // 唯一的击败判定在 ShameLineController：阶段三完成目标动作 → Silence()。

            // 阶段随目标动作推进，而不是随血量推进——这一关的进度条是"做完了几件事"
            int want = ctl.ObjectivesDone >= 2 ? 3 : ctl.ObjectivesDone >= 1 ? 2 : 1;
            if (want > _phase) SetPhase(want);

            if (_busy) return;
            if (_phase >= 2)
            {
                _nailCd -= Time.deltaTime;
                if (_nailCd <= 0f)
                {
                    _nailCd = (ShameLineController.ChallengeCeilingActive ? 22f : 15f)
                              - _denialSpeedUp;
                    StartCoroutine(AccusationTriple());
                }
            }
        }

        int _seenDenials;

        /// <summary>
        /// 否认的代价（方案 8.6.4「玩家的每一次否认都为他续命」）。
        ///
        /// 【为什么不是回血】
        /// 他没有血条——回血这件事在屏幕上根本不可见，玩家只会觉得"怎么打都没用"。
        /// 「续命」在没有血条的前提下要换成看得见的形态：**他指认得更急**。
        /// 每否认一次，指认间隔缩短，头顶把否认次数写出来。
        /// 因果链于是完整了：我否认了 → 他更凶了 → 我停止否认 → 他慢下来。
        /// </summary>
        void DenialPressure()
        {
            if (Time.time < _nextRegen) return;
            _nextRegen = Time.time + 1.5f;
            var d = ShameLine.Data;
            if (d.denialCount <= _seenDenials) return;
            _seenDenials = d.denialCount;

            // 每次否认让指认周期缩短 1.5 秒（最多累计 9 秒，即 15 → 6 秒一次），
            // 并把**当前这一次**的倒计时也往前拨——但只能拨快，不能拨慢：
            // 直接写 Mathf.Max(6, _nailCd - 1.5) 会在剩余不足 6 秒时反而把它推回 6 秒。
            _denialSpeedUp = Mathf.Min(9f, _denialSpeedUp + 1.5f);
            _nailCd = Mathf.Max(0f, _nailCd - 1.5f);
            _ec.emotionOverride = "打不倒 · 你已否认 " + d.denialCount + " 次，他更急了";
            GameEvents.RaiseSubtitle("刚才那一次否认，正好是他要的——他指得更快了。" +
                "（否认 " + d.denialCount + " 次）");
        }

        float _denialSpeedUp;

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
