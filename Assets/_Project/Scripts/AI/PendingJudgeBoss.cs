using System.Collections;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.Shame;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 悬案法官（未宣判者）——8-1 的 Boss（方案 8.5.4）。
    ///
    /// 【他不追求击败玩家。他追求的是永远不结案】
    /// 所以他没有传统血条：屏幕上方是悬案计时器与已延期次数。
    /// 对他的所有物理攻击只造成硬直，不推进任何进度条——
    /// 玩家可以打他，但打赢不等于结束。
    ///
    /// 四招，每一招都是延期的一种形态：
    ///   一【改期】宣布追问延后，Exposure 保持，计时器 -1 段。Telegraph：合上账本。
    ///   二【追加】在既有条件上追加新条件，长廊 +1 段。反制：边界盾。
    ///   三【要求当众】宣布安排一次公开说明，随后在玩家接近时临时取消——
    ///      这是本关最重的一击：它同时抬高与撤走结算。
    ///   四【身份钉·轻】指认招式，truthTag = true。唯一解是认领不终审。
    ///
    /// 终结条件不是打死他：玩家走进广播室并完成自行陈述，他失去权限、
    /// 从场景中消失，**没有战斗动画**。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class PendingJudgeBoss : MonoBehaviour
    {
        public float deferInterval = 22f;
        public float addendumInterval = 30f;
        public float publicCallInterval = 46f;
        public float nailInterval = 18f;

        EnemyController _ec;
        Transform _player;
        float _deferCd = 12f, _addCd = 20f, _publicCd = 34f, _nailCd = 9f;
        bool _busy;
        bool _publicPending;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p != null) _player = p.transform;

            // 【方案 8.5.4 的原文，两句都要照做】
            //   「不可击杀：对他的所有物理攻击只造成硬直，不推进任何进度条。」
            //   「血条替代：没有传统血条。屏幕上方是悬案计时器与已延期次数。」
            //
            // 上一版我只照做了第一句的一半：用 minHpFloor 把血线卡在 12%。
            // 那等于给了玩家一条会掉 88% 然后突然停住的血条——先教会他"伤害有用"，
            // 再当场推翻。连续三轮实机反馈说"敌人有无限的生命"，说的就是这根血条。
            // 血条本身就是那个错误的承诺。所以现在：不画血条，伤害不进血，
            // 硬直与打击反馈照常（这才是"只造成硬直"）。
            // 【产品决定：他是一个能打死的 Boss】
            // 方案 8.5.4 原本写的是"不可击杀 / 没有传统血条"，上面那段注释解释了
            // 我此前怎么把它实现成了一条掉到 12% 就卡死的血条。
            // 现在的口径由产品定：**和其他关卡一样，有血条、会动、打得死**。
            // 保留的是方案真正的那条命题——打死他不等于结案：
            // 案子照样挂着，要结案仍然得自己走进广播室做一次自行陈述。
            // 这一条写在他的头顶，也写在他倒下时那句话里。
            _ec.emotionOverride = "打得死 · 但打死他不等于结案";
            GameEvents.RaiseSubtitle("【悬案法官】他不宣判，也不撤案，只会说「下次再说」。" +
                "他打得死——但案子不会因为他倒下就结了。" +
                "广播室的门在长廊尽头，从第一秒起就开着。");
        }

        bool _deathNoted;

        void Update()
        {
            // 玩家不在本章（或离得远）时一律不动：这些区域在进游戏时就建好了，
            // 不加这一句，教室里的单位会在玩家家里播字幕、加暴露度（见 ShameLine.ActiveNear）
            if (!ShameLine.ActiveNear(transform.position)) return;
            if (_ec == null || _player == null) return;
            if (_ec.State == EnemyState.Dead)
            {
                // 打死他是有意义的：追问停了，压力源没了。但案子还挂着——
                // 这一句必须当场说清楚，否则玩家会以为"Boss 死了怎么还没通关"。
                if (!_deathNoted)
                {
                    _deathNoted = true;
                    GameEvents.RaiseSubtitle("他倒下了。追问停了——可案子还挂着。" +
                        "要结案，还得你自己走进长廊尽头那间广播室，做一次自行陈述。");
                }
                return;
            }

            // 这里原来有一段"打到血线就坐下"的处理，是上一版为了给"打不死"一个交代加的。
            // 那段现在删掉了：它依赖的正是那条不该存在的血条。
            // 方案给的终结条件只有一条——玩家走进广播室完成自行陈述，
            // 那一刻他失去权限、从场景中消失（见 ShameLineController 的结算）。

            var timer = PendingCaseTimer.Instance;
            if (timer == null || !timer.Running) return;
            if (_busy || _ec.State == EnemyState.Stagger) return;
            if (Vector3.Distance(transform.position, _player.position) > 28f) return;

            float dt = Time.deltaTime;
            _deferCd -= dt; _addCd -= dt; _publicCd -= dt; _nailCd -= dt;

            if (_nailCd <= 0f) { _nailCd = nailInterval; StartCoroutine(LightNail()); return; }
            if (_publicCd <= 0f) { _publicCd = publicCallInterval; StartCoroutine(DemandPublic()); return; }
            if (_addCd <= 0f) { _addCd = addendumInterval; StartCoroutine(Addendum()); return; }
            if (_deferCd <= 0f) { _deferCd = deferInterval; StartCoroutine(Defer()); }
        }

        /// <summary>招式 1｜改期。Telegraph：合上账本。</summary>
        IEnumerator Defer()
        {
            var skills = ShameSkills.Instance;
            if (skills != null && skills.TryRefuse(_ec, "这次改期")) yield break;

            _busy = true;
            if (_ec.dialogue != null) _ec.dialogue.Show("这次先不谈了，下次再说。", 2.6f);
            if (_ec.poser != null) _ec.poser.PlayFirstClip(1f, 0.2f, "Button Pushing", "Talking");
            yield return new WaitForSeconds(1.1f);

            var timer = PendingCaseTimer.Instance;
            if (timer != null) timer.NoteDeferral();
            // Exposure 保持：改期不会让你被少看一眼，只是把结算又推远一点
            _busy = false;
        }

        /// <summary>招式 2｜追加。反制：边界盾。</summary>
        IEnumerator Addendum()
        {
            var skills = ShameSkills.Instance;
            if (skills != null && skills.TryRefuse(_ec, "这条追加条件")) yield break;

            _busy = true;
            if (_ec.dialogue != null) _ec.dialogue.Show("再加一条：这次要有个准话。", 2.8f);
            yield return new WaitForSeconds(1.2f);

            // 欠条残片凑齐三片 = 条款、日期、金额都对得上：追加条件当场失效。
            // 这就是"收集情报可缩短 Boss 阶段"落到机制上的样子——
            // 不是给一个增伤 buff，而是让他的一整招失去着力点。
            if (Shame.DebtFragment.Collected >= 3)
            {
                GameEvents.RaiseSubtitle("【追加落空】条款、日期、金额你都对过了——这一条加不上去。");
                if (_ec != null) _ec.ForceBreak(1.8f);
                _busy = false;
                yield break;
            }

            var corridor = CorridorGrowthSystem.Instance;
            if (corridor != null) corridor.NoteConcealment("又答应了一条做不到的");
            GameEvents.RaiseSubtitle("【追加】条件多了一条，长廊也长了一段。举盾（边界）可以挡下下一条。");
            _busy = false;
        }

        /// <summary>
        /// 招式 3｜要求当众——本关最重的一击。
        ///
        /// 它先宣布安排一次公开说明（把结算抬起来），随后在玩家接近时临时取消（把结算撤走）。
        /// 注意：当众处罚只能作为**威胁与压力来源**存在，不得作为已执行的演出呈现（8.12.2）。
        /// 所以这一招从头到尾没有一个围观特写镜头——它取消的那一刻就是全部内容。
        /// </summary>
        IEnumerator DemandPublic()
        {
            var skills = ShameSkills.Instance;
            if (skills != null && skills.TryRefuse(_ec, "这次「当众说明」")) yield break;

            _busy = true;
            _publicPending = true;
            if (_ec.dialogue != null) _ec.dialogue.Show("安排一次当面说清楚吧，就这周。", 3f);
            GameEvents.RaiseSubtitle("【要求当众】他宣布了一场公开说明——地点、时间由他定。");
            var ex = ExposureSystem.Instance;
            if (ex != null) ex.Add(12f, "还没开始，你已经先被看见了");

            float wait = 0f;
            while (wait < 12f && _publicPending)
            {
                wait += Time.deltaTime;
                if (_player != null && Vector3.Distance(transform.position, _player.position) < 5f)
                    break;
                yield return null;
            }

            _publicPending = false;
            if (_ec.dialogue != null) _ec.dialogue.Show("算了，这次先不用了。", 2.6f);
            GameEvents.RaiseSubtitle("【临时取消】说明不办了。抬起来的东西被撤走时，比办了还重——" +
                "但要不要说、什么时候说，本来就不该由他定。");
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p != null)
            {
                float dmg = 12f;
                var gm = GameManager.Instance;
                if (gm != null && gm.safety != null) dmg *= gm.safety.MentalDamageMultiplier();
                var appease = AppeasementSystem.Instance;
                if (appease != null) dmg *= appease.IncomingMentalMultiplier();
                // 手里有残片 = 手里有事实：这一击照样重，但不再是"完全没准备"
                if (Shame.DebtFragment.Collected >= 3) dmg *= 0.5f;
                p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.Shame, dmg);
                p.Stats.AddRumination(10f);
            }
            _busy = false;
        }

        /// <summary>招式 4｜身份钉·轻：指认招式，truthTag = true。唯一解是认领不终审。</summary>
        IEnumerator LightNail()
        {
            _busy = true;
            var claim = ClaimRegistry.Draw(_ec.profile != null ? _ec.profile.enemyId : "", true);
            if (claim != null)
            {
                var own = OwnNotFinalSystem.Instance;
                float dmg = _ec.profile != null ? _ec.profile.mentalDamage : 14f;
                var appease = AppeasementSystem.Instance;
                if (appease != null) dmg *= appease.IncomingMentalMultiplier();
                if (own != null) own.Accuse(_ec, claim, dmg);
            }
            yield return new WaitForSeconds(1.6f);
            _busy = false;
        }
    }
}
