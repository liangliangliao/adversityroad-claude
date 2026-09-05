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

            // 【"不可击杀"不等于"刀砍不动"】
            // 方案要的是"打赢他不等于结束"——终结条件在广播室那扇门上，不在他的血条上。
            // 原来的写法把伤害压到 15%、血线又卡在 35%：两道闸门叠在一起，
            // 玩家砍十几刀血条纹丝不动，读到的只有"这敌人有问题"。
            // 现在伤害照常结算，他也照常硬直、照常被打崩——只是打崩他不会让案子结案。
            _ec.minHpFloor = 0.12f;
            // 【他不能有血条】方案 8.5.4：屏幕上方是悬案计时器与已延期次数。
            // 留着一根几乎不动的血条，玩家读到的只会是"这个敌人打不死"——
            // 而这一关想说的是"打赢他不等于结束"。这两句话差得很远。
            _ec.emotionOverride = "不结案 · 打赢他不等于结束";
            GameEvents.RaiseSubtitle("【悬案法官】他不宣判，也不撤案。他只会说「下次再说」——" +
                "打他没有用（他没有血条，只有悬案计时器）。广播室的门在长廊尽头，一直开着。");
        }

        bool _yielded;

        void Update()
        {
            if (_ec == null || _ec.State == EnemyState.Dead || _player == null) return;

            // 打到血线：他不再抵抗了。战斗有了结论——但案子没有。
            // 这一下是玩家该拿到的反馈；少了它，"打不死"就是唯一的读法。
            if (!_yielded && _ec.HpRatio <= _ec.minHpFloor + 0.01f)
            {
                _yielded = true;
                _ec.pacified = true;
                _ec.emotionOverride = "他不还手了 · 案子照样挂着";
                if (_ec.dialogue != null) _ec.dialogue.Show("……随你怎么打。", 3f);
                GameEvents.RaiseSubtitle("他坐下了，不再还手——可你手上什么都没多。" +
                    "案子要结，还得你自己走进广播室。");
                return;
            }
            if (_yielded) return;

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
