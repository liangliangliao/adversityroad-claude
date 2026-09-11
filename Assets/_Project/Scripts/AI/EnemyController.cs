using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.UI;
using AdversityRoad.World;

namespace AdversityRoad.AI
{
    public enum EnemyState { Idle, Patrol, Chase, Attack, MentalAttack, Stagger, Dead }

    /// <summary>
    /// 敌人控制器：FSM（待机/巡逻/追击/物理攻击/心理攻击/硬直/死亡）。
    /// 心理攻击 = 数值伤害 + 实时恶意台词（气泡/字幕）+ 情绪状态展示。
    /// 受击有闪红/伤害数字/击退/削韧，死亡有倒地消散演出。
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyController : MonoBehaviour
    {
        public EnemyProfile profile = new EnemyProfile();
        /// <summary>已经乘进 profile.maxHealth 的「敌人强度」倍率（见 ApplyToughness）。</summary>
        float _toughApplied = 1f;
        public Hitbox attackHitbox;
        public Transform[] patrolPoints;

        [HideInInspector] public HumanoidAnimator poser;
        [HideInInspector] public EnemyStatusBar statusBar;
        [HideInInspector] public EnemyDialogue dialogue;

        public EnemyState State { get; private set; } = EnemyState.Idle;

        /// <summary>诊断：这一帧量出来的真实位移速度（m/s）。
        /// HUD 的敌人行用它判"确实在移动却没在演走路"——寻路速度不算数，
        /// 被击退、侧闪、绕圈同样是移动。</summary>
        public float MeasuredSpeed => _measuredSpeed;

        NavMeshAgent _agent;
        Animator _anim;
        Transform _player;
        float _hp, _posture;
        // 普通敌人的攻击距离硬上限（Boss 机制关的专属弹幕不走这里）：
        // 远程心念弹 ≤8m、心理攻击 ≤7m——超距只能追近，禁止超远距离隔空输出
        const float MaxRangedReach = 8f;
        const float MaxMentalReach = 7f;

        float _attackCd, _mentalCd, _rangedCd, _staggerTimer, _tauntTimer;
        float _flinchCd;          // 受击霸体冷却：期间轻击不再打断（防无限硬直）
        // ---- 连续硬直的三道闸（见 TakeHit 里「防连锁硬直」那段的推导）----
        /// <summary>硬直连锁的统计窗口（秒）：窗口内被打进硬直越多次，硬直越短、霸体越长。</summary>
        public const float StaggerChainWindow = 6f;

        /// <summary>
        /// 【硬直占空比上限】任意 6 秒里最多 2 秒可以处于硬直，超过就一律拒绝进硬直。
        ///
        /// 前四版我加的都是**渐近**的规则（递减、霸体冷却、起身窗、保底窗），
        /// 每一条单独看都对，合起来却仍然挡不住"一直被压着"——因为它们都留了余地，
        /// 而玩家手快就能把每一条余地填满。四轮实机反馈说的都是同一件事。
        ///
        /// 这一条不留余地：它是一个**硬上限**，与打了几下、打中哪儿、
        /// 走的是哪条进硬直的路（受击/破防/打断前摇）统统无关。
        /// 超预算之后敌人照常掉血、照常播受击反应，但不再进硬直状态——
        /// 也就是说它在任何 6 秒里都至少有 4 秒是能动、能走、能出手的。
        /// 唯一的例外是完美闪避/精准格挡打出的破绽（ForceBreak 直接调用）：
        /// 那是玩家读招的确定奖励，本来就该无条件成立。
        /// </summary>
        // ③ 2.0 → 1.2 秒（占空比 33% → 20%）。实测硬直仍是挡住敌人的第一大项（34.7%）。
        // 预算是硬夹在时长上的，改小立刻见效；玩家的输出一点没少，
        // 少的是"对方被按在地上的时间"。
        public const float StaggerBudget = 1.2f;

        /// <summary>
        /// 受击反应四档的硬直时长（秒）。索引 = HitReactionTier 的返回值。
        ///   0 微颤   —— 不进硬直（只播 0.1 秒的一颤，正在出的招照常挥完）
        ///   1 小踉跄 —— 四肢命中
        ///   2 大踉跄 —— 胸腹命中 / 头部轻击 / 重击打四肢
        ///   3 击倒   —— 头部重击，或一击打掉超过一成血
        /// </summary>
        public static readonly float[] StaggerSeconds = { 0f, 0.35f, 0.75f, 1.5f };

        /// <summary>
        /// 受击反应分档：打中哪儿 + 这一下真正打进去多少 → 反应有多大。
        ///
        /// 【为什么用「真正打进去的伤害」而不是招式的原始数值】
        /// 旧的判据是 DamageResolver.IsHeavy(dmg)，读的是招式表里写的原始削韧/伤害，
        /// 那个数在挥出去之前就定了——**与打中哪个部位、对方防御多高全都无关**。
        /// 于是砍中小腿和砍中脑袋是同一个反应，一记被高防御吃掉大半的重击
        /// 也照样把人打倒。这里改用 final（过完防御与部位系数之后真正掉的血），
        /// 再叠一层部位本身的基准档：这才是"按伤害程度区分"该有的读法。
        ///
        /// 部位基准取自 BodyPartTable 的设计意图：头是唯一的会心区（反应最大），
        /// 胸腹是基准，四肢伤害低但削韧高（所以是小踉跄而不是没反应），
        /// 手脚末端只是擦到（微颤，连招都打不断）。
        /// </summary>
        int HitReactionTier(BodyPart part, float landed, bool heavy) =>
            HitReactionTierOf(part, landed, heavy, profile.maxHealth);

        /// <summary>同上，静态版：CI 诊断要在不生成敌人的情况下把这张表打出来。</summary>
        public static int HitReactionTierOf(BodyPart part, float landed, bool heavy, float maxHealth)
        {
            int t;
            switch (part)
            {
                case BodyPart.Head: t = 2; break;
                case BodyPart.Chest:
                case BodyPart.Abdomen: t = 1; break;
                case BodyPart.Extremity: t = -1; break;
                default: t = 0; break;   // 四肢：伤害低，反应也小
            }
            if (heavy) t += 1;
            // 一击打掉超过一成血：不论打中哪儿都算大事
            if (maxHealth > 0.01f && landed >= maxHealth * 0.12f) t += 1;
            return Mathf.Clamp(t, 0, 3);
        }

        /// <summary>受击反应档位的中文名（诊断与调试叠层共用一份说法）。</summary>
        public static string TierLabel(int tier)
        {
            switch (Mathf.Clamp(tier, 0, 3))
            {
                case 0: return "微颤(不打断)";
                case 1: return "小踉跄";
                case 2: return "大踉跄";
                default: return "击倒";
            }
        }
        float _poiseArmorUntil;   // 起身/破防恢复后的霸体窗：期间照常掉血，但不再被打进硬直
        bool _wakeArmor;          // 起身后的**第一记反击**带霸体：不打断它的前摇（见 TakeHit 的打断段）

        // ---- 战斗实况统计（滚动 6 秒窗口）----
        // 【为什么要有这一组】前四版我都是读代码推理、改参数、在 CI 里算一张表说
        // "这样应该就对了"，然后实机反馈"没变化"。四次都这样，说明我对运行时到底
        // 发生了什么的模型是错的，而错在哪儿光看代码看不出来。
        // 这组计数器把一场真实战斗里的关键事实直接量出来：敌人有多少比例的时间
        // 在硬直、进硬直分别走的哪条路、它到底挥出过几刀、霸体挡下过几次打断。
        // 打开「调试数据」就显示在右上角。有了它，下一轮就不必再猜。
        float _winStart;
        float _winStagger;          // 窗口内处于硬直的累计秒数
        int _winFlinch;             // 因受击进硬直的次数
        int _winPosture;            // 因韧性击破进硬直的次数
        int _winInterrupt;          // 因前摇被打断进硬直的次数
        int _winSwing;              // 真正挥出去的招数
        int _winArmorSave;          // 霸体/预算挡下打断的次数
        int _winTeleStart;          // 亮起过几次前摇
        int _winTeleCancel;         // 其中被打断了几次（前摇没演完 = 玩家看不到规律）
        bool _swingFiring;          // 正在"前摇结束→挥出"这一瞬：此时熄灭前摇不算被打断
        // 上一个"有内容"的窗口的底稿：打完停手之后截图仍读得到（见 TraceLine）
        float _lastStagger, _lastAt = -99f;
        int _lastFlinch, _lastPosture, _lastInterrupt, _lastSwing, _lastArmorSave;

        /// <summary>
        /// "刚挨打不能立刻还手"这条规则最多能连续压制多久（秒）。
        /// 超过就无条件放行——否则只要玩家不停手，它就永远等不到那 0.55 秒的空档。
        /// </summary>
        public const float DizzySuppressCap = 0.5f;
        /// <summary>"刚挨打不还手"的窗口长度。**出手判据与日志判据必须读同一个数**——
        /// 之前判据写 0.25、日志里 WhyNoSwing 写 0.55，于是日志把一批**根本没被挡住**
        /// 的帧记成了"挨打眩晕"，那栏 48% 是虚高的。分成两个字面量就一定会再次漂移。</summary>
        public const float DizzyWindow = 0.25f;
        float _dizzyBlocked;        // 已经被上面那条规则连续挡了多久

        /// <summary>脱手多久之后韧性开始回复（秒）。</summary>
        public const float PostureCalm = 1.2f;
        /// <summary>韧性每秒回复的比例（占满值）。</summary>
        public const float PostureRegenPerSec = 0.25f;
        float _lastPostureHitAt = -99f;

        /// <summary>硬直预算是否已经用完（用完则本窗口内不再进硬直）。</summary>
        bool PoiseBudgetSpent => _winStagger >= StaggerBudget;

        /// <summary>
        /// 把一次硬直的时长按【本窗口剩余预算】截断。
        ///
        /// 【为什么"入口拦一道"不够，必须在时长上硬夹】上一版我只在进硬直的入口
        /// 判 PoiseBudgetSpent。实机数据（玩家截图）打脸得很干脆：
        ///     硬直占比 43% (2.6/6.0s)  进硬直 受击1/破防2/打断0  出手0
        /// 上限写的是 33%（2.0/6.0s），实际 43%。原因是入口判据看的是**进的那一刻**：
        /// 一次 2.4 秒的破防在预算还剩着的时候获批，进去之后一路烧到 2.4 秒——
        /// 预算是在它已经进去之后才被烧穿的，入口那一道根本管不着。
        /// 夹在时长上就没有这个缝：无论从哪条路进、进几次，
        /// 一个窗口里的硬直总时长都不可能超过预算。
        /// </summary>
        float ClampStagger(float seconds) =>
            Mathf.Min(seconds, Mathf.Max(0f, StaggerBudget - _winStagger));

        /// <summary>此刻是否被"刚挨打不还手"挡住（纯判定，无副作用）。
        /// forced=被连续挡满 DizzySuppressCap 秒后的无条件放行。
        /// 出手分支与日志栏都调这一个，不再各写一套。</summary>
        bool DizzyNow(out bool forced)
        {
            forced = _dizzyBlocked > DizzySuppressCap;
            if (forced) return false;
            bool dizzy = Time.time - _lastHurtT <= DizzyWindow;
            if (dizzy && (Time.time < _poiseArmorUntil || PoiseBudgetSpent)) dizzy = false;
            return dizzy;
        }

        /// <summary>
        /// 「出手 0」的时候，把**是什么在挡着它**直接写出来。
        /// 两次实机取样出手都是 0，而我只能靠猜是冷却、是令牌、还是别的——
        /// 结果真正的原因（连续挨打就永远等不到那段空档）猜了六版都没猜到。
        /// 这一栏之后就不用猜了。判据一律走 DizzyNow / 同一组状态，
        /// **不允许在这里另写一套阈值**——那会让日志描述一个并不存在的代码。
        /// </summary>
        string WhyNoSwing()
        {
            if (State == EnemyState.Dead) return "";
            if (holdPosition) return "[候场]";
            if (passive || undying) return "[非战型]";
            if (State == EnemyState.Stagger) return "[硬直中]";
            if (_player != null &&
                Vector3.Distance(transform.position, _player.position) > profile.AttackRange * 1.2f)
                return "[够不到]";
            bool forcedNow;
            if (DizzyNow(out forcedNow))
                return "[挨打眩晕" + _dizzyBlocked.ToString("0.0") + "s]";
            if (_attackCd > 0f) return "[冷却" + _attackCd.ToString("0.0") + "s]";
            // 在够得着的距离上却还没进入 Attack 状态——这一档以前没有，
            // 于是这些帧被上面那条 0.55 秒的错判吸收成了"挨打眩晕"。
            if (State != EnemyState.Attack) return "[未进攻击态]";
            return "[无令牌]";
        }

        // ---- 供日志逐帧落盘的只读视图（见 MoveLogger.FoeColumns）----
        // 截图只能给一个瞬时快照，且常常不是在交手中截的；落进日志才有时间序列。
        public float StaggerWindowSeconds => _winStagger;
        public int WinFlinch => _winFlinch;
        public int WinPosture => _winPosture;
        public int WinInterrupt => _winInterrupt;
        public int WinSwing => _winSwing;
        public int WinArmorSave => _winArmorSave;
        public int WinTeleStart => _winTeleStart;
        public int WinTeleCancel => _winTeleCancel;
        /// <summary>此刻是否处于前摇，以及还剩多久（日志用；不在前摇时为 0）。</summary>
        public float TelegraphLeft => _telegraphing ? Mathf.Max(0f, _windupTotal - _telegraphT) : 0f;
        public float HealthNow => _hp;
        public float PoiseNow => _posture;
        public float AttackCooldown => Mathf.Max(0f, _attackCd);
        /// <summary>此刻是什么在挡着它出手（不出手时才有值）。</summary>
        public string SwingBlockReason => WhyNoSwing();

        /// <summary>最近这一窗口里处于硬直的时间占比（右上角据此标红）。</summary>
        // 分母是整个窗口，不是"已经过去多久"——后者在窗口开头会算出 >1 的占比，
        // 实测日志里 foeStaggerPct 出现过 2.002，那是分母的错，不是硬直真的超了。
        public float StaggerDuty => _winStagger / StaggerChainWindow;

        /// <summary>右上角诊断行：这一个敌人最近 6 秒的战斗实况。</summary>
        public string TraceLine()
        {
            // 本窗口还没打起来，就显示上一个有内容的窗口（60 秒内有效）——
            // 打完停手再截图也读得到，标上「上一段」以免和当下混淆。
            bool live = _winFlinch + _winPosture + _winInterrupt + _winSwing > 0;
            if (!live && Time.time - _lastAt < 60f)
                return "【实况·上一段】硬直 " + _lastStagger.ToString("0.0") + "/"
                     + StaggerBudget.ToString("0.0") + "s预算 (整窗 "
                     + (_lastStagger / StaggerChainWindow * 100f).ToString("0") + "%)"
                     + "  进硬直 受击" + _lastFlinch + "/破防" + _lastPosture
                     + "/打断" + _lastInterrupt
                     + "  出手" + _lastSwing + "  霸体挡下" + _lastArmorSave;
            // 【分母必须是整窗，不能是"已过去多久"】上一版写的是
            // _winStagger / (Time.time - _winStart)：窗口刚开头时分母极小，
            // 于是 1.8 秒硬直在第 1.9 秒被显示成 94%，看着像上限完全失效。
            // 实际那一次是 1.8/2.0 的预算、正好被夹在上限上——数字没错，是分母错了。
            // 现在统一按整个 6 秒窗口算，并把预算用了多少直接写出来。
            return "【实况】硬直 " + _winStagger.ToString("0.0") + "/"
                 + StaggerBudget.ToString("0.0") + "s预算 (整窗 "
                 + (_winStagger / StaggerChainWindow * 100f).ToString("0") + "%)"
                 + "  进硬直 受击" + _winFlinch + "/破防" + _winPosture + "/打断" + _winInterrupt
                 + "  出手" + _winSwing + (_winSwing > 0 ? "" : WhyNoSwing())
                 + "  霸体挡下" + _winArmorSave
                 + (PoiseBudgetSpent ? "  [预算用尽]" : "")
                 + (Time.time < _poiseArmorUntil ? "  [霸体窗]" : "")
                 + (_wakeArmor ? "  [起身霸体]" : "");
        }
        int _staggerChain;        // 6 秒窗口内已经被打进硬直几次
        float _staggerChainUntil; // 这个窗口什么时候过期
        float _defendCd;          // 防御冷却：闪避/格挡后短时间内不再防（防无敌化）
        PoseState _attackPose = PoseState.Attack;   // 本次出手选中的招式（多样化）
        int _comboLeft;           // 精英/首领的连击追加段数
        float _strafeDir = 1f, _strafeFlipT;        // 交战游走（像人一样找角度）
        Vector3 _lastSelfPos;     // 位移驱动动画：任何来源的移动都要迈脚，不许滑行
        float _measuredSpeed;
        bool _posInit;
        float _lastHurtT = -99f;  // 受击眩晕：刚被打中短时间内没有反击能力
        float _legHurtUntil;      // 腿部被打中：一段时间内移动变慢（打腿＝打机动力）
        float _armHurtUntil;      // 手臂被打中：一段时间内出手变慢、伤害下降（打手＝打攻势）
        float _swingUntil;        // 挥击动作进行中（此间不游走、不被小受击打断画面）
        bool _downed;             // 被击倒趴地中（恢复时要播起身过程）
        int _patrolIndex;
        TextMesh _alertMark;      // 前摇警示「！」
        GameObject _dangerRing;   // 前摇地面红圈
        Material _dangerRingMat;  // 红圈材质（危险攻击时染更亮的橙红）
        bool _perilous;           // 本次攻击为「危险攻击」：不可格挡，只能闪避（大作红光警示）

        /// <summary>起手前摇下限（秒）：对玩家的可反应性承诺，任何敌人都不得低于此值。</summary>
        public const float MinWindup = 0.5f;

        /// <summary>连击追加段的前摇（秒）：比起手短，但绝不为零。</summary>
        public const float ComboWindup = 0.34f;
        /// <summary>远程发弹的前摇时长（与进度条、形体征兆共用同一个数）。</summary>
        public const float RangedWindup = 0.5f;

        /// <summary>是否正处于攻击前摇（威胁指示器据此在屏幕边缘标出看不见的敌人）。</summary>
        public bool Telegraphing => _telegraphing;

        /// <summary>本次前摇是否为不可格挡的危险攻击。</summary>
        public bool PerilousTelegraph => _telegraphing && _perilous;

        // 征兆的颜色语言统一由 TelegraphTable 定义（颜色即应对答案），
        // 这里只保留一个建材色供红圈材质初始化。
        static readonly Color TeleNormal = new Color(0.9f, 0.15f, 0.1f);
        bool _telegraphing;       // 是否处于前摇（脉冲放大红圈/警示，让读招更醒目）
        Vector3 _dangerRingBaseScale;
        float _telegraphT;

        /// <summary>敌方武术类型（方案 10.5）：决定它的动作语言与它考验玩家的那一项能力。</summary>
        [HideInInspector] public MartialArchetype archetype = MartialArchetype.Fist;

        /// <summary>兵器/心念弹的主题色（生成时由外部注入）。</summary>
        [HideInInspector] public Color themeColor = new Color(0.7f, 0.4f, 0.9f);
        [HideInInspector] public Material baseMaterial;

        /// <summary>外部伤害倍率（Boss 机制：明天之王泥壳护体、旧我整合阶段等）。</summary>
        [HideInInspector] public float externalDamageMult = 1f;

        /// <summary>安抚状态（旧我整合阶段）：停止一切攻击与追击，站在原地等待整合。</summary>
        [HideInInspector] public bool pacified;

        /// <summary>
        /// 候场状态（群战人数上限）：这一个还没轮到上场——不追击、不出手、不喊话，
        /// 在自己那一带待着。但它**照样能被打到**，而且挨一下就立刻入场（见 TakeHit）。
        ///
        /// 与 pacified 的区别：pacified 是剧情态（连伤害都免疫），
        /// 这个只是排队。玩家反馈"四个以上敌人加大 Boss 一起围上来，不公平"——
        /// 攻击令牌只挡住了同时**出手**的人数，挡不住同时**围过来喊话**的人数，
        /// 场面上仍然是六个打一个。真正要限的是同时参战的人头。
        /// </summary>
        [HideInInspector] public bool holdPosition;

        /// <summary>
        /// 被动单位（第八章的注视 / 低语 / 追问类）：不主动出手、不追击、不喊话，
        /// 但**照常吃伤害、照常会被打倒**。
        ///
        /// 【为什么不能用 pacified】
        /// pacified 是剧情态，连伤害都免疫（旧我整合阶段"无需再战"）。
        /// 侧目者、旁观耳语者、每周追问者只是不动手——玩家挥刀过去必须有反应，
        /// 否则整关读作"敌人打不死"。
        ///
        /// 【与 holdPosition 的区别】
        /// 候场是临时的，挨一下就入场；这个是这类单位的常态，被打也不还手——
        /// 它们的威胁本来就不是拳脚，是那道视线。
        /// </summary>
        [HideInInspector] public bool passive;

        /// <summary>玩家主动打过它：从此不再被排进候场队列（打了就得认真打完）。</summary>
        [HideInInspector] public bool provoked;

        /// <summary>血线保护（0-1）：血量不会被打到该比例以下（旧我必须走整合结局而非击杀）。</summary>
        [HideInInspector] public float minHpFloor = 0f;

        /// <summary>
        /// 【不可击杀】：伤害只造成硬直与韧性削减，**永远不减血、不显示血条**。
        ///
        /// 方案对这四个单位写的是"不可击杀 / 无法直接击杀 / 血量不由伤害决定"：
        /// 悬案法官（8.5.4）、后排低语者（8.6.4）、讨好回声（8.5.3）、心虚投影（8.6.3）。
        /// 8.5.4 更进一步写死了表现："没有传统血条。屏幕上方是悬案计时器与已延期次数"，
        /// 以及"对他的所有物理攻击只造成硬直，**不推进任何进度条**"。
        ///
        /// 【为什么不能用 minHpFloor 表达这件事】
        /// 我之前用血线卡在 12% 来实现它——那是最坏的一种做法：
        /// 玩家先看着血条掉掉掉（被教会"伤害有用"），到 12% 突然纹丝不动。
        /// 一条会动又动不完的血条，读出来就是"这敌人有无限的生命"，
        /// 这也正是连续三轮实机反馈说的那句话。血条本身就是那条错误的承诺。
        /// 所以这里不是把血锁住，是**根本不给血条**，并且在头顶常驻写清楚什么才推进进度。
        ///
        /// 与 pacified 的区别：pacified 是"攻击完全无效"（连硬直都没有）的剧情态；
        /// undying 照常吃硬直、照常被打断、照常有打击反馈，只是血不掉。
        /// </summary>
        [HideInInspector] public bool undying;

        /// <summary>不可击杀单位被打时，隔一段时间提醒一次"什么才推进进度"。</summary>
        [HideInInspector] public string undyingHint;

        float _lastUndyingHintT = -99f;

        /// <summary>当前血量比例（Boss 阶段机 0-1）。</summary>
        public float HpRatio => profile.maxHealth > 0 ? Mathf.Clamp01(_hp / profile.maxHealth) : 0f;

        // 登记表：替掉各处 Update 里的全场景敌人扫描（见 Core.ActorRegistry）
        //（每次调用都是全场景扫描 + 一次数组分配，见 Core.ActorRegistry）
        void OnEnable() => Core.ActorRegistry.Register(this);
        void OnDisable() => Core.ActorRegistry.Unregister(this);

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _anim = GetComponentInChildren<Animator>();
        }

        // 属性初始化放在 Start：运行时动态生成敌人时，profile 在 AddComponent
        // 之后才注入，Awake 里读取会拿到默认值。
        void Start()
        {
            ApplyToughness(true);
            _posture = profile.posture;
            _agent.speed = profile.MoveSpeed;
            _tauntTimer = Random.Range(4f, 9f);
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p != null) _player = p.transform;
            if (statusBar != null)
            {
                // 不可击杀单位从一开始就不画血条：血条是一句"打它会有进展"的承诺，
                // 对这些单位那句承诺是假的（见 undying 的注释）
                if (undying) statusBar.HideHealthBar();
                statusBar.SetHealth(_hp, profile.maxHealth);
                statusBar.SetPosture(_posture, profile.posture);
            }
            BuildTelegraph();
        }

        /// <summary>前摇警示物：头顶红色「！」+ 脚下红圈，出手前亮起（读招窗口）。</summary>
        void BuildTelegraph()
        {
            var markGo = new GameObject("AlertMark");
            markGo.transform.SetParent(transform, false);
            markGo.transform.localPosition = new Vector3(0, 3.3f, 0);
            _alertMark = markGo.AddComponent<TextMesh>();
            _alertMark.text = "";
            _alertMark.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _alertMark.fontSize = 110;
            _alertMark.characterSize = 0.05f;
            _alertMark.anchor = TextAnchor.MiddleCenter;
            _alertMark.color = new Color(1f, 0.2f, 0.15f);
            var mr = markGo.GetComponent<MeshRenderer>();
            if (_alertMark.font != null) mr.material = _alertMark.font.material;
            markGo.AddComponent<World.FaceCamera>();

            _dangerRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_dangerRing.GetComponent<Collider>());
            _dangerRing.transform.SetParent(transform, false);
            _dangerRing.transform.localPosition = new Vector3(0, -0.95f, 0);
            _dangerRing.transform.localScale = new Vector3(2.6f, 0.03f, 2.6f);
            _dangerRingBaseScale = _dangerRing.transform.localScale;
            var rr = _dangerRing.GetComponent<MeshRenderer>();
            // 【必须用 Unlit】：红圈是给玩家看的警示 UI，不是场景里的一块地板。
            // 之前跟着敌人的 Lit 材质走，夜间/暗巷关卡里环境光一低，红圈就跟着变成
            // 一圈几乎看不见的深褐色——玩家反馈的"看不清敌人的状况、突然就掉血"
            // 有一半是这么来的：警示确实亮了，只是在那种光照下看不见。
            // Unlit 不受光照影响，白天黑夜一样醒目；再关掉投影与接收阴影，避免它自己发黑。
            //
            // 【为什么不能直接 Shader.Find("Universal Render Pipeline/Unlit")】
            // 那个名字在编辑器里找得到，打进安卓包以后是 null——URP/Unlit 既不在
            // Always Included 名单里，也没有任何随包材质引用它，于是 shader 根本没进包。
            // 备选的 Unlit/Color 同理。两个都拿不到时 new Material(null) 的结果是一块洋红，
            // 表现出来就是"每个起手的敌人脚下亮起一坨洋红椭圆"。
            // SafeShader 会优先用确定进包的 Sprites/Default，并且永远兜得住。
            // SafeShader 返回的是按颜色缓存的共享材质，而红圈要按招式逐个染色
            //（见下面 SetTelegraph 里对 _dangerRingMat.color 的写入），所以复制一份自己的。
            Material m = new Material(World.SafeShader.Unlit(TeleNormal, "tele"));
            rr.sharedMaterial = m;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rr.receiveShadows = false;
            _dangerRingMat = m;
            _dangerRing.SetActive(false);
        }

        /// <summary>
        /// 亮起/收起前摇征兆。征兆规格全部来自 <see cref="TelegraphTable"/>：
        /// 形状按招式轨迹、颜色按应对答案、时长按招式族恒定——三层信息各说一件事，
        /// 玩家因此可以真正"学会"某个敌人的出手规律，而不是只知道"它要打了"。
        /// </summary>
        void ShowTelegraph(bool on, bool perilous = false)
        {
            // 统计前摇有没有演完：亮起算一次，还没打出去就熄掉算一次被打断。
            // "前兆没有前兆"这件事光看代码看不出来——前兆的代码明明齐全，
            // 要看的是它有多少次真的演到底。这两个数直接落进日志（见 MoveLogger）。
            if (on && !_telegraphing) _winTeleStart++;
            else if (!on && _telegraphing && !_swingFiring) _winTeleCancel++;
            _telegraphing = on;
            _telegraphT = 0f;
            if (!on)
            {
                _perilous = false;
                if (_alertMark != null) _alertMark.text = "";
                if (_dangerRing != null) _dangerRing.SetActive(false);
                if (poser != null) poser.SetWindup(-1, 0f);   // 形体征兆平滑退回
                return;
            }

            _spec = TelegraphTable.Get(_attackPose, perilous);

            if (_alertMark != null)
            {
                _alertMark.text = _spec.mark;
                _alertMark.color = _spec.color;
            }
            if (_dangerRingMat != null)
            {
                _dangerRingMat.color = _spec.color;
                if (_dangerRingMat.HasProperty("_BaseColor"))
                    _dangerRingMat.SetColor("_BaseColor", _spec.color);
            }
            if (_dangerRing != null)
            {
                _dangerRing.SetActive(true);
                // 指示器按招式轨迹取形：横斩是横向宽弧、突刺是细长直线、扫腿是贴地宽环。
                // 「往哪躲」于是有画面依据，而不是全场统一一个圆圈。
                _dangerRingBaseScale = new Vector3(_spec.ring.x, 0.03f, _spec.ring.y);
                _dangerRing.transform.localScale = _dangerRingBaseScale;
                // 指示器中心随纵深前移：突刺的危险区在身前四米，不在脚下
                _dangerRing.transform.localPosition =
                    new Vector3(0, -0.95f + _spec.ringHeight, _spec.ring.y * 0.28f);
            }
            // 教学提示：只在玩家还没见过这一族时说一次（说多了就成了噪声）
            if (NoteTelegraphSeen(_spec.kind, perilous))
                GameEvents.RaiseSubtitle("【" + _spec.name + "】" + _spec.answer);
        }

        TelegraphSpec _spec;

        /// <summary>某一族征兆是否第一次出现（全局记一次，教学提示只讲一遍）。</summary>
        static readonly HashSet<int> _seenTelegraphs = new HashSet<int>();
        static bool NoteTelegraphSeen(TelegraphKind k, bool perilous)
        {
            int key = (int)k * 2 + (perilous ? 1 : 0);
            return _seenTelegraphs.Add(key);
        }

        /// <summary>前摇脉冲：红圈由小放大到出手、警示「！」跳动，读招窗口一目了然。</summary>
        void TickTelegraph(float dt)
        {
            if (!_telegraphing)
            {
                if (poser != null && _windupTotal > 0f) { poser.SetWindup(-1, 0f); _windupTotal = 0f; }
                return;
            }
            _telegraphT += dt;
            // 前摇进度：指示器不再是无意义的来回脉动，而是**从小涨到满**——
            // 涨满 = 出手。这条"进度条"是玩家判断出手时机的直接依据（读招的第三层信息）。
            float p01 = _windupTotal > 0.01f ? Mathf.Clamp01(_telegraphT / _windupTotal) : 0f;
            if (_dangerRing != null)
            {
                float grow = Mathf.Lerp(0.45f, 1.12f, p01);
                _dangerRing.transform.localScale = new Vector3(
                    _dangerRingBaseScale.x * grow, _dangerRingBaseScale.y,
                    _dangerRingBaseScale.z * grow);
            }
            if (_alertMark != null)
                _alertMark.characterSize = 0.05f * (1f + 0.35f * p01);
            // 形体征兆：随进度加深的预备姿态（高举/后拉/压低……）——
            // 这是关了 UI 也读得出来的那一层
            if (poser != null) poser.SetWindup((int)_spec.kind, p01);
            // 【前摇走满 ⇒ 出手】这是这一刀唯一的触发点。
            // 好处是它和"前摇还剩多久"用的是同一个时钟：玩家看到的进度条涨满，
            // 和刀真正落下来，永远是同一瞬间——不可能再出现"圈才涨到一半，刀已经到了"。
            if (_teleMelee && _telegraphT >= _windupTotal) OpenAttackHitbox();
        }

        float _windupTotal;   // 本次前摇的总时长（进度条与形体征兆按它归一化）
        /// <summary>本次前摇走满之后接的是近战挥击（true）还是远程发弹（false）。
        /// 远程那一发仍由 Invoke(FireProjectile) 排，时钟不许替它出刀。</summary>
        bool _teleMelee;
        float _lastCastShot = -99f;   // 上次绝招特写的时刻（节流：特写贵在稀有）

        void Update()
        {
            if (State == EnemyState.Dead) return;
            float dt = Time.deltaTime;
            _attackCd -= dt; _mentalCd -= dt; _rangedCd -= dt; _flinchCd -= dt; _defendCd -= dt;
            // 滚动窗口：既给硬直占空比上限当分母，也给右上角那行实况当数据源
            if (Time.time - _winStart > StaggerChainWindow)
            {
                // 【窗口翻页前先留一份底】否则打完手一停，6 秒一到全部归零，
                // 截图永远只截得到一排 0——上一版玩家发来的正是这样一张图。
                // 有内容才留底：安静的窗口不该把上一场真实数据冲掉。
                if (_winFlinch + _winPosture + _winInterrupt + _winSwing > 0)
                {
                    _lastStagger = _winStagger; _lastFlinch = _winFlinch;
                    _lastPosture = _winPosture; _lastInterrupt = _winInterrupt;
                    _lastSwing = _winSwing; _lastArmorSave = _winArmorSave;
                    _lastAt = Time.time;
                }
                _winStart = Time.time;
                _winStagger = 0f;
                _winFlinch = _winPosture = _winInterrupt = _winSwing = _winArmorSave = 0;
                _winTeleStart = _winTeleCancel = 0;
            }
            if (State == EnemyState.Stagger) _winStagger += dt;

            // ---- 韧性回复（此前完全没有，这是"一套连段就破防"的根）----
            // 【实测的账】标准杂兵韧性 40，而玩家一套完整剑连的削韧是
            // 10+12+14+28 = 64，还没算部位系数（打四肢是 ×1.35~1.5）。
            // 也就是说**一套连段必定破防，还多出二十几点带进下一次**。
            // 而破防 = 2.4 秒破绽 + 韧性回满，于是"打一套→破防→破绽里再打一套→
            // 再破防"可以一直转下去。玩家截图里的「破防 2 次、占比 43%」就是它。
            //
            // 所有有韧性/架势系统的作品都给它回复（只狼的躯干值会自己降、
            // 魂系的 poise 有恢复窗、仁王的气会回）：不回复的韧性条不是"架势"，
            // 是一根**只减不增的第二血条**，破防也就成了必然事件而不是打出来的成果。
            // 脱手 1.2 秒后按每秒 25% 回复；被打就重新计时。
            // 【"挨打眩晕"的累加放在顶层】只要它最近挨过打、又不在硬直里，
            // 就一直在被那条"刚挨打不还手"的规则压着——不管它此刻处于哪个状态。
            // 放在 Attack 分支里累加是错的（见那里的注释）。
            if (Time.time - _lastHurtT <= DizzyWindow && State != EnemyState.Stagger)
                _dizzyBlocked += dt;
            else if (State != EnemyState.Stagger && Time.time - _lastHurtT > 1.0f)
                _dizzyBlocked = 0f;

            if (State != EnemyState.Stagger && Time.time - _lastPostureHitAt > PostureCalm)
                _posture = Mathf.Min(profile.posture,
                    _posture + profile.posture * PostureRegenPerSec * dt);
            TickTelegraph(dt);

            // 实时同步生命值/韧性到头顶状态条（不依赖事件，任何来源的变化都可见）
            if (statusBar != null)
            {
                statusBar.SetHealth(_hp, profile.maxHealth);
                statusBar.SetPosture(Mathf.Max(0, _posture), profile.posture);
            }

            // 部位伤情：腿伤减速、手伤削弱攻势（每帧同步到寻路速度上）
            if (_agent != null)
                _agent.speed = profile.MoveSpeed * (Time.time < _legHurtUntil ? 0.62f : 1f);

            // 把移动速度喂给人形动画（步行/奔跑步态）。
            // 用【真实每帧位移】而非寻路速度：追击/游走/击退/侧闪，无论位移来自
            // 哪个系统，脚都会迈动——消灭"人在滑、脚不动"的漂移感。
            if (poser != null)
            {
                if (!_posInit) { _lastSelfPos = transform.position; _posInit = true; }
                Vector3 planar = transform.position - _lastSelfPos;
                planar.y = 0;
                _lastSelfPos = transform.position;
                float inst = dt > 0.0001f ? planar.magnitude / dt : 0f;
                _measuredSpeed = Mathf.Lerp(_measuredSpeed, inst, 12f * dt);
                float refSpeed = Mathf.Max(2.5f, profile.MoveSpeed);
                // 移动方向相对身体正面的夹角：敌人交战时脸始终对着玩家，
                // 而脚下在绕圈/后撤/横挪——不把这个角度喂给动画层，播的就永远是
                // "向前走"，于是横着挪动时脚在原地倒腾＝**漂移**。
                float moveAngle = 0f;
                if (planar.sqrMagnitude > 1e-6f)
                    moveAngle = Vector3.SignedAngle(transform.forward, planar.normalized, Vector3.up);
                // 敌人交战中脸始终对着玩家、脚下在绕圈/后撤，夹角恒为有意为之
                poser.SetLocomotion(Mathf.Clamp01(_measuredSpeed / refSpeed) * 0.9f,
                    false, true, _measuredSpeed, moveAngle, true);
                // 交战中静立时摆出格斗预备架势（而非松垮站立）
                // 十类武术里只有刀术/棍术/重武器是持械的，其余七类是拳脚。
                // 持剑动作集（临战架势、蹲伏、踢击、受击、倒下）双手都是握着东西的，
                // 空手型敌人套上去会像凭空攥着一把剑。
                poser.SetArmed(archetype == MartialArchetype.Blade ||
                               archetype == MartialArchetype.Staff ||
                               archetype == MartialArchetype.Heavy);
                poser.SetCombatReady(State == EnemyState.Chase || State == EnemyState.Attack
                    || State == EnemyState.MentalAttack);
            }

            // ===== 出招立足（本轮修的"敌人边跑边打时在漂移"）=====
            //
            // 漂移的成因不是动画，是**位移与动作各走各的**：
            // NavMeshAgent 带着速度与一条未作废的路径继续把人往前送，
            // 而身上正在播一段原地的挥击动作——脚不动、人在飘，就是漂移。
            // 之前只在 DoPhysicalAttack 的那一瞬调了一次 StopMoving：
            // 那一帧之后 Chase 分支照样会 SetDestination 把 isStopped 重新打开，
            // 于是"跑动中发起的攻击"整段都在滑行。
            //
            // 规则改成硬性的一条：**从亮起前摇到收招结束，脚钉在地上**。
            // 攻击是一次承诺——出手前站定、出手后收势，这既是所有格斗游戏的通例，
            // 也是玩家能够读招、绕背、抓破绽的前提（会移动的攻击等于没有破绽）。
            // 需要"移动着打"的招式走 AttackStep 的可控前踏，而不是让寻路顺带把人推过去。
            bool committed = _telegraphing || Time.time < _swingUntil;
            if (committed && AgentReady)
            {
                if (!_agent.isStopped) _agent.isStopped = true;
                _agent.velocity = Vector3.zero;
                if (_agent.hasPath) _agent.ResetPath();
            }

            // 安抚状态（旧我整合阶段）：收势站定，不再攻击、不再追击
            if (pacified)
            {
                if (State != EnemyState.Idle)
                {
                    State = EnemyState.Idle;
                    CancelInvoke();
                    ShowTelegraph(false);
                    if (attackHitbox != null) attackHitbox.DisableHitbox();
                    StopMoving();
                    if (poser != null) poser.SetPose(PoseState.Idle);
                }
                UpdateEmotion("平静");
                return;
            }

            // 【不可击杀 ⇒ 必然不近战】
            // 上一轮我把这四个单位改成 undying（不掉血、不画血条），却没有关掉它们的
            // 追击与近战——结果比改之前更糟：以前还能把它们磨到血线让它们停手，
            // 现在既打不倒、又一直追着打，玩家躲不开也清不掉，站着长按目标动作必死。
            //
            // 而方案对这四个的描述里根本没有近战：
            //   悬案法官「他不追求击败玩家」，四招是改期/追加/要求当众/身份钉·轻；
            //   后排低语者「他不与玩家正面战斗。他只做三件事：看、说、指」；
            //   讨好回声是玩家讨好行为的具象化；心虚投影「抢占回避路线」而不是打人。
            // 它们的压力全部走各自的组件（视线、指认、抢位），不走这套近战 AI。
            //
            // 所以这里把它写成结构性的一条：**undying 一定 passive**，
            // 不再依赖每个单位各自记得去设一次——那正是上一轮漏掉的地方。
            if ((passive || undying) && State != EnemyState.Stagger)
            {
                if (State == EnemyState.Chase || State == EnemyState.Attack ||
                    State == EnemyState.MentalAttack)
                {
                    Combat.CombatDirector.Release(this);
                    ShowTelegraph(false);
                    if (attackHitbox != null) attackHitbox.DisableHitbox();
                    StopMoving();
                    State = EnemyState.Idle;
                }
                UpdateEmotion("旁观");
                return;
            }

            // 候场：还没轮到上场的那几个，站在自己那一带，不追不打不喊话。
            // 硬直中不打断——被打飞的那一下要播完，否则会看到人被打中却瞬间站直。
            if (holdPosition && State != EnemyState.Stagger)
            {
                if (State == EnemyState.Chase || State == EnemyState.Attack ||
                    State == EnemyState.MentalAttack)
                {
                    Combat.CombatDirector.Release(this);
                    ShowTelegraph(false);
                    if (attackHitbox != null) attackHitbox.DisableHitbox();
                    StopMoving();
                    State = EnemyState.Idle;
                }
                PatrolTick();
                UpdateEmotion("旁观");
                return;
            }

            if (State == EnemyState.Stagger)
            {
                _staggerTimer -= dt;
                UpdateEmotion("慌乱");
                if (_staggerTimer <= 0)
                {
                    State = EnemyState.Chase;
                    // 【起身霸体窗】倒地爬起来的那一下不能再被打回去。
                    // 这是动作游戏的通行规则（起身无敌帧 / 受身）：没有它，
                    // 玩家只要贴着倒地的敌人一直按，敌人就永远停在"倒下—爬起—又倒下"，
                    // 一次还手都没有。这里不给无敌（伤害照吃），只给**不被打进硬直**——
                    // 追打仍然有收益，但对方拿回了出招的权利。
                    // 倒地起身比普通踉跄长：爬起来本来就更慢、更该被保护。
                    // 【保底还手窗】被连续打进硬直 3 次以上之后，这一次恢复必定给到
                    // 1.2 秒不受硬直的时间。前面的递减是"越来越短"，这一条是"一定有"——
                    // 递减是渐近的，玩家手快就仍然可能把每一次间隙都填满；
                    // 有了这条硬保证，"根本起不来"在规则上就不可能成立。
                    float guard = _downed ? 0.9f : 0.45f;
                    if (_staggerChain >= 3) guard = Mathf.Max(guard, 1.2f);
                    _poiseArmorUntil = Time.time + guard;
                    // 【起身反击】光有霸体窗还不够：出手冷却是 1.1~3.0 秒，
                    // 硬直结束时它多半还在冷却里，于是"拿回了控制权却依然不还手"，
                    // 玩家看到的仍然是一路被压着打。大作里敌人是**带着招爬起来的**
                    // （魂系起身挥刀、只狼的兵卒起身反击），这里对齐：
                    // 硬直结束把出手冷却压到 0.3 秒，配合上面的霸体窗，
                    // 它这一刀能真的挥出来而不是刚抬手又被打断。
                    _attackCd = Mathf.Min(_attackCd, 0.3f);
                    // 起身反击必须**带霸体**，否则它就是一个更快的挨打循环：
                    // 见 TakeHit 打断段里那条注释——起身→出招→前摇被打断→再硬直。
                    _wakeArmor = true;
                    if (poser != null)
                    {
                        // 被击倒的要先播"起身过程"（倒地片段倒放：腿脚先动、身体渐立），
                        // 绝不原地瞬间站直；普通踉跄直接回架势
                        if (_downed) { _downed = false; poser.PlayGetUp(); }
                        else poser.SetPose(PoseState.Idle);
                    }
                }
                return;
            }

            if (_player == null) { PatrolTick(); return; }
            float dist = Vector3.Distance(transform.position, _player.position);

            // 追击/交战中周期性低语（语言层面的持续心理压迫）
            if (State == EnemyState.Chase || State == EnemyState.Attack)
            {
                _tauntTimer -= dt;
                if (_tauntTimer <= 0)
                {
                    _tauntTimer = Random.Range(6f, 12f);
                    if (dialogue != null)
                        dialogue.Taunt(profile.targetWeakness, ZoneBuilder.CurrentZoneId, false);
                }
            }

            switch (State)
            {
                case EnemyState.Idle:
                case EnemyState.Patrol:
                    PatrolTick();
                    UpdateEmotion("窥伺");
                    if (dist < profile.detectRange) State = EnemyState.Chase;
                    break;

                case EnemyState.Chase:
                {
                    UpdateEmotion("紧逼");
                    if (committed) break;   // 出招承诺期间不追不挪（见上方"出招立足"）
                    bool isBoss = profile.category == EnemyCategory.Boss;
                    // 围攻礼让（大作群战规则）：远处先逼近到「待战环」；只有抢到攻击令牌的
                    // 敌人才继续挤进近身发动攻击，其余在待战环外绕圈施压，不堆挤玩家身体。
                    // 待战环 +1.8 → #52 收到 +0.8 → 现在回到 **+1.4 米**。
                    // 收到 0.8 是我收过头了：前摇从 2.6 米处起手，而不是 3.6 米，
                    // 手机屏幕上那 0.6 秒几乎读不出来——这直接加重了"没有前兆"的观感。
                    // 1.4 米是折中：比 #52 之前逼得近（"够不到"仍然会明显下降），
                    // 但前摇重新起在一个看得清的距离上。
                    // 往回收的只有敌人这一侧的数，玩家侧一个字没动。
                    float standoff = profile.AttackRange + 1.4f;
                    if (dist > standoff)
                    {
                        MoveTowards(_player.position, dt);   // 尚在环外：拉近到待战环，无需令牌
                    }
                    else if (isBoss || Combat.CombatDirector.TryAcquire(this, isBoss))
                    {
                        MoveTowards(_player.position, dt);   // 抢到攻击位：贴身
                        RefreshReach();
                        if (dist <= ReachGate) State = EnemyState.Attack;
                    }
                    else
                    {
                        MaintainStandoff(standoff, dt);      // 没轮到：环上绕圈候场
                    }
                    // 中距离言语攻击（内心/混合敌人的主要远程手段——话语弹幕，非物理弹丸）
                    if (dist < Mathf.Min(profile.detectRange * 0.7f, MaxMentalReach) &&
                        _mentalCd <= 0 && profile.mentalDamage > 0)
                        DoMentalAttack();
                    else if (dist > profile.detectRange * 1.5f)
                    {
                        Combat.CombatDirector.Release(this);
                        State = EnemyState.Patrol;
                    }
                    break;
                }

                case EnemyState.Attack:
                    UpdateEmotion("狰狞");
                    StopMoving();
                    FaceTarget();
                    // 【已经起手就把这一招打完】原本只要 dist 超过 AttackRange×1.2
                    // 就退回 Chase——而玩家每一刀都把它推过这条线（实测 54%~82%），
                    // 于是前摇一次次被距离判定取消。承诺之后不再中途退出：
                    // 够不着就在落刀的一瞬踏前一步补上（AttackStep 本来就在做这件事）。
                    bool committed2 = _telegraphing || Time.time < _swingUntil;
                    if (dist > profile.AttackRange * 1.2f && !committed2)
                    {
                        Combat.CombatDirector.Release(this);   // 脱离攻击态：归还攻击令牌
                        State = EnemyState.Chase; break;
                    }
                    // 受击眩晕：刚被打中 0.55s 内头脑发懵，没有能力立即反击——
                    // 攻势停止后才逐步恢复出手（被打了不能若无其事地还手）；
                    // 攻击令牌（围攻礼让）：取到令牌才真正出手，否则只在下方走位伺机
                    // ============ 这里是"敌人从不还手"的真正出口 ============
                    // 实机两次取样，出手都是 0。而原因和硬直无关：
                    // `Time.time - _lastHurtT > 0.55f` 要求它**连续 0.55 秒没有挨打**
                    // 才允许出手。玩家一套剑连的链取消间隔是 0.19~0.32 秒——
                    // 也就是说只要玩家不停手，这个条件**永远不可能成立**。
                    // 它不需要被打进硬直，只要挨打比每 0.55 秒更密就够了。
                    // 我前六版全部在改硬直，而真正把它按住的是这一行。
                    //
                    // 这条规则的本意（"刚被打中不能若无其事地还手"）是对的，
                    // 但它没有上限，于是变成了无条件的压制。补两个出口，
                    // 两个都对齐大作里"霸体期就是反击窗"的做法：
                    //   ① 已经进入霸体窗 / 硬直预算用尽——那正是它该反击的时刻；
                    //   ② 被这条规则连续挡住超过 DizzySuppressCap 秒，无条件放行。
                    // 放行时一并给起身霸体，否则这一刀刚抬手就又被打断（#44 的教训）。
                    // ② "刚挨打不还手"的窗口 0.55 → 0.25 秒（实测挡住 23.7% 的帧）。
                    // 0.55 秒本来就长过玩家的连打间隔，等于永久压制；
                    // 0.25 秒仍然保留"被打中的一瞬间不能若无其事地挥回来"这个观感，
                    // 但不再是一条只要对方不停手就永不打开的闸。
                    bool forced;
                    bool dizzy = DizzyNow(out forced);
                    // 【累加不在这里做】见 Update 顶层：这段代码只在 Attack 状态跑，
                    // 而实机日志里敌人 35% 的时间在 Stagger、还有 Chase/MentalAttack，
                    // 那些帧根本走不到这儿，累加器攒不起来；一回到 Attack 又被清零。
                    // 实测 1.2 秒的保底放行**整场只触发过 1 帧**（[挨打眩晕1.1s] × 1）。
                    if (forced) { _wakeArmor = true; _dizzyBlocked = 0f; }
                    // 被玩家正在打的这一个，令牌也不该跟别人抢——它就是当前的交战对象。
                    if (_attackCd <= 0 && !dizzy &&
                        (forced || Time.time - _lastHurtT < 3f ||
                         Combat.CombatDirector.TryAcquire(this, profile.category == EnemyCategory.Boss)))
                    { DoPhysicalAttack(); break; }
                    // 出手间隙像人一样左右游走找角度（而非钉在原地干等）；
                    // 挥击动作进行中绝不游走——脚下滑动会毁掉出招画面（漂移感）
                    if (_attackCd > 0.45f && !_telegraphing && Time.time > _swingUntil && AgentReady)
                    {
                        _strafeFlipT -= dt;
                        if (_strafeFlipT <= 0)
                        {
                            _strafeFlipT = Random.Range(1.2f, 2.6f);
                            _strafeDir = Random.value < 0.5f ? -1f : 1f;
                        }
                        Vector3 toP = _player.position - transform.position; toP.y = 0;
                        Vector3 side = Vector3.Cross(Vector3.up, toP.normalized) * _strafeDir;
                        _agent.Move(side * profile.MoveSpeed * 0.32f * dt);
                    }
                    break;
            }
        }

        /// <summary>
        /// 头顶那行字的常驻覆盖（非空时压过每帧的情绪文本）。
        ///
        /// 给"打不死是设计、不是 Bug"的那几个用：悬案法官没有血条、
        /// 后排低语者的血由否认次数维持、讨好回声只能靠降讨好度削。
        /// 不写在头顶的话，玩家读到的只有"砍半天不掉血"——那是同一个画面，
        /// 却是完全相反的两句话。
        /// 破绽 / 语塞这类**即时反馈**仍然直接写 statusBar，压在覆盖之上一闪而过。
        /// </summary>
        [HideInInspector] public string emotionOverride;

        void UpdateEmotion(string emotion)
        {
            if (statusBar == null) return;
            statusBar.SetEmotion(string.IsNullOrEmpty(emotionOverride) ? emotion : emotionOverride);
        }

        void PatrolTick()
        {
            if (patrolPoints == null || patrolPoints.Length == 0 || !AgentReady) { State = EnemyState.Idle; return; }
            State = EnemyState.Patrol;
            _agent.isStopped = false;
            if (!_agent.pathPending && _agent.remainingDistance < 0.5f)
            {
                _patrolIndex = (_patrolIndex + 1) % patrolPoints.Length;
                _agent.SetDestination(patrolPoints[_patrolIndex].position);
            }
        }

        /// <summary>Agent 是否可用：未落在 NavMesh 上时调用 isStopped/SetDestination 会抛异常。</summary>
        bool AgentReady => _agent != null && _agent.enabled && _agent.isOnNavMesh;

        /// <summary>
        /// 待战环走位（围攻礼让）：没抢到攻击令牌的敌人保持在玩家周围一圈候场——
        /// 太近则后撤、太远则贴到环上、在环上则绕圈游走，绝不堆挤进玩家身体。
        /// 让群战像大作那样"一两个进攻、其余围而不攻"，而非一拥而上。
        /// </summary>
        void MaintainStandoff(float standoff, float dt)
        {
            FaceTarget();
            if (_player == null) return;
            Vector3 toP = _player.position - transform.position; toP.y = 0;
            float d = toP.magnitude;
            if (d < 0.01f) return;
            Vector3 dir;
            if (d < standoff - 0.4f) dir = -toP.normalized;          // 太近：后撤
            else if (d > standoff + 0.9f) dir = toP.normalized;      // 太远：贴回环上
            else
            {
                _strafeFlipT -= dt;
                if (_strafeFlipT <= 0)
                {
                    _strafeFlipT = Random.Range(1.5f, 3f);
                    _strafeDir = Random.value < 0.5f ? -1f : 1f;
                }
                dir = Vector3.Cross(Vector3.up, toP.normalized) * _strafeDir;   // 环上绕圈
            }
            if (AgentReady) _agent.Move(dir * profile.MoveSpeed * 0.5f * dt);
            else transform.position += dir * profile.MoveSpeed * 0.5f * dt;
        }

        void MoveTowards(Vector3 target, float dt)
        {
            if (AgentReady)
            {
                _agent.isStopped = false;
                _agent.SetDestination(target);
                return;
            }
            // NavMesh 不可用时的直线追击兜底
            Vector3 dir = target - transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude < 0.04f) return;
            transform.position += dir.normalized * profile.MoveSpeed * dt;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir), 8f * dt);
        }

        void StopMoving()
        {
            if (!AgentReady) return;
            _agent.isStopped = true;
            // 清残余速度并作废当前路径：施法/聚气/出招前摇时，NavMeshAgent 的惯性会
            // 让敌人继续前滑一小段（"漂移"）。硬停(速度归零+作废路径)后基本原地不动。
            _agent.velocity = Vector3.zero;
            if (_agent.hasPath) _agent.ResetPath();
        }

        void FaceTarget()
        {
            Vector3 dir = _player.position - transform.position; dir.y = 0;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir), 8f * Time.deltaTime);
        }

        // 出手招式池：普通敌人用基础拳脚剑技，精英/首领追加重斩/旋风/腿法大招
        static readonly PoseState[] BasicMoves =
            { PoseState.Attack, PoseState.AttackUp, PoseState.SwordThrust,
              PoseState.PunchCross, PoseState.AttackKick };
        static readonly PoseState[] EliteMoves =
            { PoseState.HeavyAttack, PoseState.AttackSpin, PoseState.SpinKick,
              PoseState.SideKick, PoseState.JumpKick };

        /// <summary>
        /// 敌人招式的伤害/击退：统一从 EnemyMoveTable 取，不再散落魔数。
        /// 与玩家表结构相同（轨迹/范围/伤害/削韧/击退），调值各自独立——
        /// 玩家的大招付蓄力与意势的代价，敌人不付，照抄玩家系数会强出一截。
        /// </summary>
        static void MoveStats(PoseState p, out float dmgMul, out float knock)
        {
            var m = Combat.EnemyMoveTable.Get(p);
            dmgMul = m.damageMult;
            knock = m.knockback;
        }

        /// <summary>各招式从起手到真正接触目标的时间：判定框在动画"打到"的那一帧
        /// 才开启——刀/脚碰到身体的瞬间与伤害同步，打击所见即所得。
        /// （动画层加了起手偏移+提速，接触帧整体提前）</summary>
        static float ContactDelay(PoseState p)
        {
            switch (p)
            {
                case PoseState.HeavyAttack:
                case PoseState.AttackSpin:  return 0.2f;
                case PoseState.SpinKick:
                case PoseState.JumpKick:
                case PoseState.SideKick:    return 0.16f;
                case PoseState.AttackKick:  return 0.13f;
                case PoseState.PunchJab:    return 0.08f;
                case PoseState.PunchCross:
                case PoseState.SwordThrust: return 0.12f;
                default:                    return 0.14f;   // 横斩/撩斩等剑技
            }
        }

        void DoPhysicalAttack()
        {
            // ① 出手冷却：3.0~1.1 秒 → 1.5~0.6 秒。
            // 实测（movelog 131 秒 / 交战 41 秒）敌人只出手 6 次 = 0.15 次/秒，
            // 而玩家 1.38 次/秒——攻防比 9.5 : 1。冷却本身挡掉了 12.6% 的帧。
            // 玩家侧不做任何限制（他的原话：这个游戏是开放能力的），
            // 差距只能从敌人这一侧补，这是四项里最直接的一项。
            _attackCd = Mathf.Lerp(1.5f, 0.6f, profile.aggression);
            StopMoving();   // 蓄势前摇(Charge/聚气)即刻硬停：前摇期间原地不动，不前滑漂移
            TriggerAnim("Attack");

            // 招式多样化：像人一样换招——精英/首领概率掏出重斩/旋风/腿法，
            // 且有概率追加 1-2 段连击（高手连招压制）
            bool elite = profile.category == EnemyCategory.Boss || profile.aggression >= 0.6f;
            bool useElite = elite && Random.value < (profile.category == EnemyCategory.Boss ? 0.45f : 0.25f);
            var pool = useElite ? EliteMoves : BasicMoves;
            _attackPose = pool[Random.Range(0, pool.Length)];
            _comboLeft = profile.category == EnemyCategory.Boss ? Random.Range(1, 3)
                       : elite && Random.value < 0.4f ? 1 : 0;

            // 危险攻击（大作红光警示）：精英重招/Boss 有概率使出【不可格挡】的危险一击——
            // 头顶亮「危」、红圈更大更亮，只能闪避不能格挡，教玩家读招而非无脑格挡
            _perilous = useElite && Random.value < (profile.category == EnemyCategory.Boss ? 0.5f : 0.35f);

            // ===== 前摇：可观测、且**同族恒定**（这是"可学习"的前提）=====
            //
            // 上一版的前摇时长是 Lerp(0.75, 0.5, 攻击性)——同一个横斩，攻击性 0.3 的
            // 杂兵给 0.68 秒、攻击性 0.9 的精英给 0.53 秒。玩家永远学不会这种东西：
            // 规律必须能被记住，而"每个敌人各有各的节拍、还随机换招"是记不住的。
            // 现在时长由**招式族**决定（TelegraphTable），全场统一：
            // 见到"高举过顶"就知道还有 0.78 秒、见到"收刀到腰"就知道是 0.62 秒。
            // 敌人的强弱改由出手频率/连段长度/招式选择体现，而不是偷玩家的反应时间。
            float windup = Mathf.Max(MinWindup, TelegraphTable.Get(_attackPose, _perilous).windup);
            _windupTotal = windup;
            _teleMelee = true;
            ShowTelegraph(true, _perilous);
            GameAudio.Play(GameAudio.Sfx.Alert, _perilous ? 0.75f : 0.55f, _spec.pitch);
            // 【绝招特写】不可格挡的大招值得一个镜头：把施展者框进画面、报出招名与应对，
            // 玩家看得见"它在做什么"，才有可能防住（见 ThirdPersonCamera.FocusOn）。
            // 不夺走操作权——特写期间玩家照常可以闪避/格挡/走位。
            //
            // 两道克制：只有【首领】的不可格挡技才给特写，且同一个敌人 9 秒内最多一次。
            // 特写贵在稀有——杂兵每记红光都推一次镜头，镜头就成了噪声，
            // 玩家反而更看不清战场（这是"知道何时不特写"的那一半）。
            if (_perilous && profile.category == EnemyCategory.Boss &&
                Time.time - _lastCastShot > 9f)
            {
                _lastCastShot = Time.time;
                CombatFeedback.EnemyCastShot(transform, Mathf.Min(windup, 1.1f),
                    profile.displayName + " · " + _spec.name, _spec.answer);
            }
            if (poser != null) poser.SetPose(PoseState.Charge);
            // 【出手由前摇时钟驱动，不再用 Invoke 排队】见 TickTelegraph 末尾。
            // 原来这里排一个 Invoke(OpenAttackHitbox, windup)，而好几条中止路径
            // （安抚、候场、被打进硬直、转 passive）只调了 ShowTelegraph(false)，
            // **没有取消那个已经排进队列的 Invoke**。那一刀于是会在下一次前摇
            // 刚亮起 0.1~0.4 秒时落下来——前摇还剩半截，刀已经到脸上。
            // 实测就是这样：10 次前摇里 6 次在还剩 0.21~0.52 秒时就出手了。
            // 让时钟当唯一的裁判，这一整类"排队残留"就不存在了。
        }

        /// <summary>起手：播挥击动作。判定框延迟到动画的接触帧才开启（FireHitbox），
        /// 刀/脚真正碰到对方身体的那一刻伤害与特效同步出现。</summary>
        void OpenAttackHitbox()
        {
            // 【_swingFiring 必须在 ShowTelegraph(false) 之前置位】
            // 它原本写在这行下面第六行，也就是说 ShowTelegraph(false) 执行时它永远是
            // false —— 于是**每一次打出去的招都被记成了一次"前摇被打断"**。
            // #53/#54 我据此算出的"前摇打断率 86%"是这个计数错误的产物，不是实机事实：
            // 三份日志里 teleCancel 恒等于 teleStart 就是这么来的。
            if (State == EnemyState.Dead || attackHitbox == null) { ShowTelegraph(false); return; }
            // 自保：这一刀只认"本次前摇走满"这一个来源。
            // 没在前摇里、或前摇还没走满，就不是这一次该出的刀（历史上的排队残留）。
            if (!_telegraphing || _telegraphT < _windupTotal - 0.02f) return;
            _swingFiring = true;   // 这一次 ShowTelegraph(false) 是"打出去了"，不是被打断
            ShowTelegraph(false);
            _swingFiring = false;
            GameAudio.Play(GameAudio.Sfx.Swing, 0.55f);
            if (poser != null) poser.SetPose(_attackPose);
            _wakeArmor = false;   // 这一刀已经挥出来了，起身霸体到此为止
            _winSwing++;
            float contact = ContactDelay(_attackPose);
            _swingUntil = Time.time + contact + 0.45f;
            StartCoroutine(AttackStep(contact));   // 踏前一步接上距离（替代滑行）
            Invoke(nameof(FireHitbox), contact);
            Invoke(nameof(CloseHitbox), contact + 0.25f);
        }

        /// <summary>
        /// 出招前踏（大作里的 attack lunge）：把"够不着就滑过去"换成一次**可控的踏步**。
        ///
        /// 差别不在位移量，在**归属**：寻路推过来的位移与动作无关，所以看着像漂移；
        /// 这一步是攻击自己的一部分——只在挥击的接触帧之前推进、总量封顶、
        /// 够得着就完全不推。玩家看到的是"它踏前一步一刀劈过来"，
        /// 而不是"它一边滑行一边挥了一下"。
        /// </summary>
        System.Collections.IEnumerator AttackStep(float dur)
        {
            if (_player == null) yield break;
            Vector3 to = _player.position - transform.position; to.y = 0;
            float gap = to.magnitude - profile.AttackRange * 0.75f;
            if (gap <= 0.05f || dur <= 0.01f) yield break;
            Vector3 dir = to.normalized;
            // 封顶 0.9 → 1.3m：击退把它推开 0.26~0.49 米是常态，
            // 0.9 米的一步在"被推出去之后还要补回来"的情况下常常差一点点。
            // 仍然是一步，不是冲刺。
            float total = Mathf.Min(gap, 1.3f);
            float moved = 0f, t = 0f;
            while (t < dur && State != EnemyState.Dead)
            {
                float dt = Time.deltaTime;
                t += dt;
                // 前快后慢：起步就把身位吃掉大半，收尾自然停住（不是匀速滑行）
                float want = total * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                float step = want - moved;
                moved = want;
                if (step > 0f)
                {
                    if (AgentReady) _agent.Move(dir * step);
                    else transform.position += dir * step;
                }
                yield return null;
            }
        }

        // ===== 敌人这一侧的"够不够得着"，规矩与玩家完全一致 =====
        // 只给玩家上几何距离约束、敌人照旧凭空够到，那就是单方面削玩家。
        float _reachAt = -1f;

        /// <summary>
        /// 它真正够得到的距离：实测的最长一条（兵器或腿）＋ 对方身体半径。
        ///
        /// 【为什么必须有这条】判定框现在按几何裁剪了，而 AI 判"该不该出手"用的是
        /// profile.AttackRange（1.8~2.3m），那是当年按**没有裁剪**的判定框调出来的数。
        /// 两边一旦对不上，敌人就会在自己够不到的距离上挥空——
        /// 而"进入攻击距离却打不到人"正是 EnemyMoveTable 开头那段注释警告过的事。
        /// 取两者的较小值：手里有兵器时这条线约 2.2m，压根不会限制它（AttackRange 最大 2.3）；
        /// 只有在它确实够不到的时候才把它逼得再走近一点——那本来就该走近。
        /// </summary>
        float ReachGate
        {
            get
            {
                if (attackHitbox == null || !attackHitbox.reach.valid) return profile.AttackRange;
                var r = attackHitbox.reach;
                float best = Mathf.Max(r.LimitOf(Combat.ReachLimb.Weapon),
                                       r.LimitOf(Combat.ReachLimb.Leg));
                // 对方的受击体半径：刀碰到的是身体表面，不是他的中心点。
                best += Combat.MecanimCharacter.TargetHeight * 0.17f;
                return Mathf.Min(profile.AttackRange, best);
            }
        }

        /// <summary>刷新这个敌人的实测攻击距离（手臂/腿/刃长）并写进判定框。</summary>
        void RefreshReach()
        {
            if (attackHitbox == null) return;
            if (Mathf.Approximately(_reachAt, Time.time)) return;
            _reachAt = Time.time;
            Transform model = poser != null && poser.MocapModel != null
                ? poser.MocapModel
                : (transform.childCount > 0 ? transform.GetChild(0) : null);
            // 【用游戏自己认定的那把兵器】poser.weaponPivot 就是刀光挂上去的那个节点，
            // 装配时由 MecanimCharacter.TryBuild / WeaponFactory 写入。
            // 之前这里另找了一套（FindWeaponInModel），和装配用的不是同一条路——
            // 判定距离认的兵器，必须就是画面上那把。
            Transform weapon = poser != null ? poser.weaponPivot : null;
            // 认不出兵器时 bladeKnown=false：兵器系招式不裁，保持原设计。
            // 把"认不出"当成"空手"，会让一个握着长刀的敌人够不到人——
            // 玩家实测到的正是这个：赤手空拳比敌人的长刀还够得远。
            attackHitbox.reach = Combat.ReachModel.Measure(
                transform, model, weapon, weapon != null);
        }

        void FireHitbox()
        {
            if (State == EnemyState.Dead || attackHitbox == null) return;
            MoveStats(_attackPose, out float dmgMul, out float knock);
            // 判定框按招式轨迹取形：突刺细长（侧移可躲开）、横斩横宽、回旋斩环身 360°、
            // 重砸罩住一片。此前敌人所有招共用一个固定方盒，玩家读了招也无从"往哪躲"。
            var spec = Combat.EnemyMoveTable.Get(_attackPose);
            // 【敌人走同一条规矩】够不够得着按它自己的手臂/腿/刃长算。
            // 只给玩家上这条规则等于单方面削玩家，和「差距一律从敌人侧补」是反的。
            RefreshReach();
            attackHitbox.SetShape(spec.Size, spec.center, _attackPose);
            // 危险攻击：不可格挡（须闪避）+ 伤害/击退加成，兑现红光警示的威胁
            // 手臂伤情：出手明显变弱（打手臂的收益在这里兑现，玩家看得到）
            float armWeak = Time.time < _armHurtUntil ? 0.7f : 1f;
            attackHitbox.EnableHitbox(new DamageInfo
            {
                physicalDamage = profile.physicalDamage * dmgMul * armWeak * (_perilous ? 1.5f : 1f),
                mentalDamage = profile.mentalDamage * 0.3f,
                mentalAxis = profile.targetWeakness,
                knockback = knock + (_perilous ? 3f : 0f),
                unblockable = _perilous,
                attackerId = profile.enemyId
            });
        }

        void CloseHitbox()
        {
            if (attackHitbox != null) attackHitbox.DisableHitbox();
            // 连击追加段：紧凑衔接下一招（间隔短，读作一套连招）
            if (_comboLeft > 0 && State == EnemyState.Attack && _player != null &&
                Vector3.Distance(transform.position, _player.position) < profile.AttackRange * 1.6f)
            {
                _comboLeft--;
                var pool = Random.value < 0.5f ? BasicMoves : EliteMoves;
                _attackPose = pool[Random.Range(0, pool.Length)];
                FaceTarget();
                // 【连击段同样要有前摇】——此前这里直接排 OpenAttackHitbox，
                // 也就是说敌人一套连招里只有第一下亮「！」，第二、三下是**零征兆**打到脸上。
                // 玩家反馈的"毫无理由、毫无征兆就掉血"，绝大多数就是这两下。
                // 连击段的前摇比起手短（保留连招的压迫感），但绝不为零：
                // 本作对玩家的承诺是「任何一次会造成伤害的攻击，出手前必定有可见警示」。
                _perilous = false;   // 连击段不做不可格挡（不可格挡只出现在有完整前摇的起手）
                _windupTotal = ComboWindup;
                _teleMelee = true;
                ShowTelegraph(true, false);
                GameAudio.Play(GameAudio.Sfx.Alert, 0.45f, _spec.pitch + 0.1f);
                if (poser != null) poser.SetPose(PoseState.Charge);
                // 同样交给前摇时钟（见 DoPhysicalAttack 末尾的说明）
            }
            // 一套连招收尾：归还攻击令牌（让别的敌人有机会进攻——围攻礼让）
            else
            {
                Combat.CombatDirector.Release(this);
                // 攻防步法：收尾后有概率快速撤步拉开身位（进退有据的剑斗节奏）
                if (State == EnemyState.Attack && Random.value < 0.4f && AgentReady)
                    StartCoroutine(DodgeSlide(-transform.forward * 1.3f));
            }
        }

        /// <summary>Animator 触发器兜底：动捕路径下 Animator 无控制器，SetTrigger 会刷警告。</summary>
        void TriggerAnim(string name)
        {
            if (_anim != null && _anim.runtimeAnimatorController != null) _anim.SetTrigger(name);
        }

        /// <summary>格挡后收架势（保持型格挡姿态由此解除，回到移动/预备）。</summary>
        void GuardRecover()
        {
            if (State != EnemyState.Dead && State != EnemyState.Stagger && poser != null)
                poser.SetPose(PoseState.Idle);
        }

        /// <summary>远程攻击：前摇警示后朝玩家胸口发射心念弹。</summary>
        void DoRangedAttack()
        {
            _rangedCd = Mathf.Lerp(6f, 3f, profile.aggression);
            StopMoving();
            FaceTarget();
            if (poser != null) poser.SetPose(PoseState.Cast);
            UpdateEmotion("凝念");
            // 【远程这一发以前没设 _windupTotal】于是进度归一化的分母是 0，
            // p01 恒为 0——红圈从头到尾停在最小那一档、形体征兆也不推进，
            // 等于把"涨满即出手"这层信息整个抹掉了。补上它自己的 0.5 秒。
            _windupTotal = RangedWindup;
            _teleMelee = false;   // 这一次前摇后面接的是弹，不是刀
            ShowTelegraph(true);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.4f);
            Invoke(nameof(FireProjectile), RangedWindup);
        }

        void FireProjectile()
        {
            ShowTelegraph(false);
            if (State == EnemyState.Dead || _player == null) return;
            // 凝念能量门禁：每发耗能，打空须停火回气（不能无限制零成本连发）
            if (!EnemyRangedEnergy.TryFire(this, _player.position, 20f, MaxRangedReach + 2f))
                return;
            Vector3 origin = transform.position + Vector3.up * 1.3f + transform.forward * 0.8f;
            Vector3 targetPos = _player.position + Vector3.up * 1.0f;
            Projectile.Launch(transform, origin, targetPos - origin,
                new DamageInfo
                {
                    physicalDamage = 0f,   // 言语弹幕：只压心神，不做远程物理削血
                    mentalDamage = profile.mentalDamage * 0.6f,
                    mentalAxis = profile.targetWeakness,
                    knockback = 0f,
                    attackerId = profile.enemyId
                }, 11f, themeColor, baseMaterial);
        }

        /// <summary>心理攻击：凝视/低语 + 实时恶意台词。可被定心格挡反制。</summary>
        void DoMentalAttack()
        {
            State = EnemyState.MentalAttack;
            _mentalCd = Mathf.Lerp(8f, 4f, profile.aggression);
            StopMoving();   // 施法凝念时停步：施法动画不许边滑边播（漂移感）
            TriggerAnim("MentalAttack");
            if (poser != null) poser.SetPose(PoseState.Cast);
            UpdateEmotion("讥讽");

            // 取出这句恶意台词（气泡+字幕都用同一句，便于言语攻防面板复述）
            string line = DialogueLibrary.GetTaunt(profile.targetWeakness, ZoneBuilder.CurrentZoneId);
            if (dialogue != null)
            {
                dialogue.Show(line, 3.5f);
                GameEvents.RaiseSubtitle("『" + dialogue.displayName + "』：" + line);
            }

            var pc = _player.GetComponent<PlayerCombatController>();
            if (pc != null)
            {
                var gm = GameManager.Instance;
                float dmg = MentalDamageSystem.Resolve(
                    profile.mentalDamage,
                    profile.targetWeakness,
                    gm != null ? gm.CurrentProfile : null,
                    gm != null ? gm.safety : null);
                var mentalHit = new DamageInfo
                {
                    mentalDamage = dmg,
                    mentalAxis = profile.targetWeakness,
                    isMentalOnly = true,
                    attackerId = profile.enemyId
                };

                // 言语攻防：优先交给玩家三选一回应；接管失败（冷却/恢复模式/已在进行）时照常落伤害。
                bool challenged = UI.VerbalDefenseController.Instance != null &&
                    UI.VerbalDefenseController.Instance.Begin(this, profile.targetWeakness,
                        dialogue != null ? dialogue.displayName : profile.displayName, line, mentalHit);
                if (!challenged) pc.TakeHit(mentalHit);
            }
            Invoke(nameof(BackToChase), 1.2f);
        }

        void BackToChase() { if (State != EnemyState.Dead) State = EnemyState.Chase; }

        /// <summary>被玩家正确回击（言语攻防）：语塞、削韧、短暂破绽——奖励用言语克制言语。</summary>
        public void OnVerbalCountered()
        {
            if (State == EnemyState.Dead) return;
            CancelInvoke(nameof(OpenAttackHitbox));
                CancelInvoke(nameof(FireHitbox));
            CancelInvoke(nameof(FireProjectile));
            ShowTelegraph(false);
            if (attackHitbox != null) attackHitbox.DisableHitbox();

            _posture -= profile.posture * 0.5f;
            CombatFeedback.HitSpark(transform.position + Vector3.up * 1.4f,
                new Color(0.5f, 0.85f, 1f));
            if (dialogue != null) dialogue.Show(ResponseLibrary.GetBrokenLine(), 2.2f);

            State = EnemyState.Stagger;
            Combat.CombatDirector.Release(this);   // 进入硬直/破绽：立即让出攻击令牌
            StopMoving();
            if (poser != null) poser.SetPose(PoseState.Stagger);

            if (_posture <= 0)
            {
                // 语塞击破韧性=大破绽
                _posture = profile.posture;
                _staggerTimer = 2.4f;
                if (statusBar != null) statusBar.SetEmotion("破绽！！猛攻！");
                if (dialogue != null) dialogue.Show("【破绽】", 2.2f);
                CombatFeedback.SlowMo(0.5f, 0.15f);
            }
            else
            {
                _staggerTimer = 1.2f;
                if (statusBar != null) statusBar.SetEmotion("语塞");
            }
            if (statusBar != null) statusBar.SetPosture(Mathf.Max(0, _posture), profile.posture);
        }

        public void TakeHit(DamageInfo dmg)
        {
            if (State == EnemyState.Dead) return;

            // 安抚状态（旧我整合阶段）：不再需要战斗，攻击无效——请走向整合圆环
            if (pacified)
            {
                CombatFeedback.DamageNumber(transform.position, "无需再战",
                    new Color(0.7f, 0.85f, 1f), 1.15f);
                return;
            }
            // 候场中被打：立刻入场。排队是为了不围殴玩家，不是给玩家一个
            // 站着挨打不还手的木桩——主动上去打它，它当然该还手。
            holdPosition = false;
            provoked = true;

            // Boss 护体（明天之王泥壳等）：伤害大幅削减时给出机制提示
            if (externalDamageMult <= 0.35f && Time.time - _lastHurtT > 3f)
                CombatFeedback.DamageNumber(transform.position + Vector3.up * 0.4f, "护体",
                    new Color(0.7f, 0.7f, 0.5f), 1.05f);

            // 不可击杀单位：每一下都要当场说清楚"打得动，但打不倒；进度不在这里"。
            // 沉默地不掉血是最糟的表现——玩家只会以为这敌人有无限的生命。
            if (undying)
            {
                CombatFeedback.DamageNumber(transform.position + Vector3.up * 0.5f, "打不倒",
                    new Color(0.85f, 0.8f, 0.7f), 1.1f);
                if (Time.time - _lastUndyingHintT > 12f)
                {
                    _lastUndyingHintT = Time.time;
                    if (!string.IsNullOrEmpty(undyingHint))
                        Core.GameEvents.RaiseSubtitle(undyingHint);
                }
            }

            // ---- 偷袭：敌人未察觉（待机/巡逻）时被打 = 趁其不备，1.8 倍伤害且无法防御 ----
            bool unaware = State == EnemyState.Idle || State == EnemyState.Patrol;
            float sneakMult = 1f;
            if (unaware)
            {
                sneakMult = 1.8f;
                CombatFeedback.DamageNumber(transform.position, "偷袭！",
                    new Color(1f, 0.85f, 0.3f), 1.35f);
            }
            // ---- 防御：交战中的敌人有概率闪避（完全躲开）或格挡（大幅减伤），
            //      概率随敌人级别/攻击性上升（Boss 更像高手），带冷却防无敌化 ----
            bool guardedHit = false;
            if (!unaware && _defendCd <= 0f && State != EnemyState.Stagger && !dmg.unblockable)
            {
                float chance = (profile.category == EnemyCategory.Boss ? 0.34f : 0.1f)
                             + 0.18f * profile.aggression;
                if (Random.value < chance)
                {
                    _defendCd = 1.7f;
                    if (Random.value < 0.5f && AgentReady)
                    {
                        if (poser != null) poser.SetPose(PoseState.Dodge);
                        // 平滑侧滑而非一帧瞬移：瞬移会被战斗镜头焦点复制成画面跳动
                        StartCoroutine(DodgeSlide(
                            transform.right * (Random.value < 0.5f ? 1.7f : -1.7f)));
                        CombatFeedback.DamageNumber(transform.position, "闪避",
                            new Color(0.55f, 0.8f, 1f), 1.1f);
                        _attackCd = Mathf.Min(_attackCd, 0.55f);   // 闪开即寻机反击
                        return;   // 侧闪成功：完全不受伤
                    }
                    if (poser != null) poser.SetPose(PoseState.Guard);
                    CombatFeedback.DamageNumber(transform.position, "格挡",
                        new Color(0.5f, 0.9f, 0.6f), 1.1f);
                    // 兵器相撞：玩家的刀砍在敌人举起的兵器上——接触点金白火花四溅
                    Vector3 toSrcW = dmg.sourcePosition - transform.position; toSrcW.y = 0;
                    Vector3 guardDir = toSrcW.sqrMagnitude > 0.01f ? toSrcW.normalized : transform.forward;
                    CombatFeedback.WeaponClash(transform.position + guardDir * 0.7f + Vector3.up * 1.25f);
                    dmg.physicalDamage *= 0.25f;
                    dmg.postureDamage *= 0.55f;
                    guardedHit = true;   // 砍在兵器上：不出血花
                    // 格挡成功立即反击（挡+还手=像人一样的攻防转换）；架势片刻后收起
                    _attackCd = Mathf.Min(_attackCd, 0.35f);
                    CancelInvoke(nameof(GuardRecover));
                    Invoke(nameof(GuardRecover), 0.6f);
                }
            }

            // ---- 打断前摇：敌人在出招前摇里被打中，出招被打断 ----
            //
            // 【这里原来是本作最严重的一处设计错误】
            // 旧逻辑是"对攻"：只要在敌人前摇期间打中它，就【无条件反弹一份伤害给玩家】。
            // 它同时踩了三个大忌，玩家的"防不胜防、做什么防御都没用"基本全出自这里：
            //   ① 这份伤害【躲不掉】。它由玩家自己的攻击触发，而玩家正在出招——
            //      既不可能同时按住格挡，也不在翻滚无敌帧里。跳、闪、挡一个都用不上。
            //   ② 这份伤害【看不出来源】。玩家的招式判定框长达 2~4 米，站在三米外
            //      挥一刀照样触发，于是屏幕上就是"离得老远突然掉血"。
            //   ③ 它【惩罚了游戏自己教的东西】。全套读招系统（前摇警示、危险攻击、
            //      威胁指示器）都在教玩家"看见前摇就抢攻"，抢攻却要挨一下无法规避的伤害。
            //
            // 成熟动作游戏在这一格的通行规则是相反的：**打中前摇＝打断它**，这就是读招的奖励；
            // 精英/首领可以有霸体（轻击打不断），但那时它的攻击【依然带着完整前摇打出来】，
            // 玩家仍然能闪能挡——风险始终是可规避的，而不是凭空扣血。照此重写：
            // 破绽期要在【打断之前】就判定完：下面的打断会把敌人推进 Stagger，
            // 如果之后再看 State，等于"每一次成功打断都算处决"——处决横幅与慢镜会满屏刷，
            // 且把削韧破防这条正经循环的收益冲淡。处决只认真正靠削韧打出来的破绽。
            bool wasStaggered = State == EnemyState.Stagger;

            if (_telegraphing && !dmg.unblockable)
            {
                // 【这里原来是"一直在踉跄"的真正出口，而且是我上一版亲手造的】
                // 打断前摇走的是 ForceBreak(0.9f)，它**不看霸体窗、不吃硬直递减**，
                // 是一条独立于上面那三道闸之外的进硬直通路。
                // 而我上一版加的"起身反击"把出手冷却压到 0.3 秒，等于让敌人
                // 一爬起来就进前摇——于是循环变成：
                //     起身 → 0.3 秒后进前摇 → 玩家下一刀打断 → 硬直 0.9 秒 → 起身 …
                // 它比改之前更起不来。这是我的回归，不是原有的老问题。
                // 修法与大作一致：**起身反击自带霸体**（魂系起身挥刀打不断、
                // 只狼兵卒起身反击有韧性），霸体窗内的前摇也一样打不断。
                // 【前摇霸体改成全体，不再只给精英/首领】
                // 玩家的原话："敌人的攻击没有任何前兆，很难预测和躲避。"
                // 而前兆这套东西是齐的（同族恒定的 0.58~0.82 秒起手、
                // 颜色即应对、头顶记号、地面红圈、起手音效、画面外方向箭头）。
                // 真正的问题是**前兆几乎从来没有演完过**：玩家每秒出手 1.38 次，
                // 而一次前摇要 0.6~0.8 秒——绝大多数前摇刚亮起就被下一刀打断，
                // ShowTelegraph(false)，攻击取消。于是玩家从没机会把"高举过顶=0.78 秒后横斩"
                // 这条规律看完整一次，自然也就学不会；偶尔漏过来的那一下，
                // 读起来就是"毫无征兆"。
                // **一个从没被看完的前兆，等于没有前兆。**
                // 大作的通行做法是让敌人一旦起手就基本吃定这一招（魂系/只狼的敌人
                // 极少被轻击打断），玩家的收益来自闪/挡/弹反，而不是"用连打把它的招洗掉"。
                // 所以：普通攻击不再能打断前摇；**重击与绝招仍然打得断**，
                // 完美闪避/精准格挡的破绽也照旧——读招抢攻的正收益一点没少。
                bool superArmor = !DamageResolver.IsHeavy(dmg)
                                  || _wakeArmor
                                  || Time.time < _poiseArmorUntil
                                  || PoiseBudgetSpent;
                Vector3 mid = _player != null
                    ? (transform.position + _player.position) * 0.5f + Vector3.up * 1.3f
                    : transform.position + Vector3.up * 1.3f;
                if (superArmor)
                {
                    // 打不断：明确告诉玩家"这一下没能打断，它的招还会出来"——
                    // 招还带着前摇，所以仍然躲得掉，玩家知道该准备闪了
                    _winArmorSave++;
                    CombatFeedback.DamageNumber(mid, "霸体·未打断", new Color(0.8f, 0.8f, 0.85f), 1.1f);
                }
                else
                {
                    // 打断的硬直也走硬直递减：短时间内反复打断它的前摇，
                    // 每次能定住的时间越来越短（与被打进硬直共用同一个 6 秒窗口）。
                    // 读招抢攻的收益仍在（招被取消掉了），只是不能靠它把人钉死。
                    if (Time.time > _staggerChainUntil) _staggerChain = 0;
                    _staggerChainUntil = Time.time + StaggerChainWindow;
                    _staggerChain++;
                    _winInterrupt++;
                    ForceBreak(ClampStagger(
                        0.9f * Mathf.Max(0.35f, Mathf.Pow(0.72f, _staggerChain - 1))));
                    CombatFeedback.DamageNumber(mid, "打断！", new Color(1f, 0.85f, 0.35f), 1.4f);
                    CombatFeedback.WeaponClash(mid);
                }
            }

            // ---- 部位精准伤害（大作惯例：打得准有实际收益）----
            // 部位由**挂在骨骼上的受击框**落款（见 BodyPartHurtboxes），不再靠命中点
            // 高度去猜——下蹲、倒地、腾空时同一个高度对应的部位完全不同。
            // 系数集中在 BodyPartTable：头会心、四肢伤害低但削韧高（扫腿打失衡）。
            var part = dmg.bodyPart;
            if (part == BodyPart.None && dmg.hasContact)
                part = BodyPartTable.FromHeight(dmg.contactPoint.y - transform.position.y);
            var pp = BodyPartTable.Get(part, false);
            float partDmgMult = pp.damage, partPostureMult = pp.posture;
            bool headshot = pp.critical;
            // 打腿＝打机动力，打手＝打攻势：四肢命中除了削韧，还落到**它该影响的能力**上。
            // 这是"打哪儿有什么用"能被玩家学会的关键——只改数字学不会，行为变化才学得会。
            if (BodyPartTable.IsLeg(part)) _legHurtUntil = Time.time + 2.2f;
            else if (BodyPartTable.IsArm(part)) _armHurtUntil = Time.time + 2.2f;

            // 命中质量（接触体积 × 刃位，见 Hitbox.ApplyHitQuality）：
            // 擦到边、用剑柄怼、够到极限距离都打不透；刃中段罩满才吃满伤害。
            // 0 表示这一击没走判定框（投射物/心理攻击），按 1 处理。
            float quality = dmg.hitQuality > 0.001f ? dmg.hitQuality : 1f;
            float final = DamageResolver.ResolvePhysical(dmg.physicalDamage, profile.defense)
                * sneakMult * partDmgMult * quality;
            // 破绽期（韧性击破硬直）吃 1.6 倍伤害：奖励削韧打法。
            // 处决（大作破韧终结）：破绽期用重击/大招命中 = 巨额增伤 + 横幅 + 强顿帧慢镜，
            // 把「削韧破防→抓破绽猛攻」的循环做成有仪式感的收益。
            bool execHeavy = DamageResolver.IsHeavy(dmg);
            bool execution = false;
            if (wasStaggered)
            {
                if (execHeavy)
                {
                    final *= 2.8f;
                    execution = true;
                }
                else final *= 1.6f;
            }
            // 调试模式：敌人耐揍，大幅削减实际伤害（方便测试，不被秒杀）
            if (Core.GameDebug.TankyEnemies) final *= Core.GameDebug.TankyDamageScale;
            // Boss 护体倍率：血量与韧性同步受保护（破防要走机制，不能硬磨）
            final *= externalDamageMult;
            // 不可击杀：伤害不进血。硬直、韧性、打击感全部照常（方案：只造成硬直），
            // 但"进度条"一格都不动——因为它的进度本来就不在血上。
            if (!undying)
            {
                _hp -= final;
                if (minHpFloor > 0f) _hp = Mathf.Max(_hp, profile.maxHealth * minHpFloor);
            }
            // 削韧同样吃命中质量：擦到边不该和扎实的一刀削掉一样多的架势
            _posture -= dmg.postureDamage * partPostureMult * externalDamageMult * quality;
            _lastPostureHitAt = Time.time;   // 被削就重新计时，脱手才回（见 PostureCalm）

            // 受击反馈：命中点冲击（火花+白闪盘+顿帧）/ 闪红 / 伤害数字 / 血花 / 击退
            Color sparkCol = State == EnemyState.Stagger
                ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.78f, 0.4f);
            Vector3 toAtk = dmg.sourcePosition - transform.position; toAtk.y = 0;
            Vector3 dirA = toAtk.sqrMagnitude > 0.01f ? toAtk.normalized : transform.forward;
            // 命中点：优先用判定框算出的【真实接触身体点】，退回估算（朝攻击者一侧胸口）
            Vector3 contact = dmg.hasContact ? dmg.contactPoint
                : transform.position + dirA * 0.55f + Vector3.up * 1.25f;
            // 重击判定用原始招式数值（不受调试减伤影响），保证打击手感稳定；
            // 力度 0-1 连续分级：火花密度、闪核大小、顿帧时长、受击冲量都按它给，
            // 一记直拳与一记裂地跳劈的反馈差距一眼可辨
            bool fbHeavy = DamageResolver.IsHeavy(dmg);
            float power = DamageResolver.Power01(dmg);
            CombatFeedback.HitImpact(contact, sparkCol, fbHeavy, true, power);
            // 血花：兵器/拳脚实打实击中血肉（格挡住的不出血）——从接触点顺打击方向外喷
            if (!guardedHit && dmg.physicalDamage > 0.5f)
                CombatFeedback.BloodSpray(contact, -dirA);
            // 部位受击反应：命中头就甩头、命中腿就屈弯，接触点弹部位标签；
            // 连续两次打中同一部位就有两次可见反应（冲量叠加，打几下动几下）
            if (!guardedHit)
                HitReactionOverlay.Trigger(transform, contact, -dirA, fbHeavy, 0.85f + power * 0.75f);
            CombatFeedback.HitFlash(gameObject);
            _lastHurtT = Time.time;   // 受击眩晕计时：攻势未停就没能力还手
            // 处决命中：仪式感反馈——横幅「处决」+ 强顿帧 + 短慢镜 + 能量爆发
            if (execution)
            {
                CombatFeedback.HitStop(0.13f);
                CombatFeedback.SlowMo(0.4f, 0.16f);
                CombatFeedback.EnergyBurst(contact, new Color(1f, 0.8f, 0.3f), 1.2f);
                CombatFeedback.CloseUp(1.0f, 0.75f);   // 破韧终结＝高光时刻，值得推近
                GameEvents.RaiseSkillBanner("处决");
            }
            // 伤害数字按部位分色分号：头部会心最大最亮，四肢偏冷色且带削韧提示。
            // （部位名由 HitReactionOverlay 在接触点弹出，这里不重复文字，只统一颜色语言）
            // 命中质量够好/够差时在数字后面缀一个短标签：让"为什么这一下打得多/打得少"
            // 从画面上就读得出来，而不是只在代码里成立（"贴身·力不透" / "刃中·扎实" / "擦到"）。
            string qtag = Combat.Hitbox.QualityLabel(dmg);
            CombatFeedback.DamageNumber(transform.position,
                Mathf.RoundToInt(final).ToString() + (qtag.Length > 0 ? "  " + qtag : ""),
                execution ? new Color(1f, 0.6f, 0.15f)
                : headshot ? new Color(1f, 0.55f, 0.25f)
                : State == EnemyState.Stagger ? new Color(1f, 0.85f, 0.25f)
                : part != BodyPart.Chest && part != BodyPart.None ? BodyPartTable.TintOf(part)
                : new Color(1f, 0.9f, 0.5f),
                execution ? 1.9f : headshot ? 1.45f : final >= 35f ? 1.6f : 1f);
            if (dmg.knockback > 0.1f && !fbHeavy)
            {
                // 击退平滑化：瞬移是"打地鼠式漂移"的最大来源——改为 0.22s 快滑退，
                // 位移驱动的步态会同步迈脚，读作"被打得连退几步"。
                // 重击不走这里：位移完全交给 KnockFly 与倒地动画同步（否则双重
                // 位移=先漂移一段再倒下，不真实）
                // knockback 是【力度记号】(1~16)，不是米数——这里必须换算。
                // 上一版直接 ×0.85 当米用：巨剑横斩 knockback 4.5 → 一记轻击把人推开
                // 3.8 米，连招直接脱靶。动作游戏的普通连段推开量在 0.2~0.8 米，
                // 目的是「打得动」而不是「打飞」——推远了反而接不上下一段。
                // 【出招承诺期间几乎不吃击退】这是三份日志找了九轮才找到的那条。
                //
                // 日志（183 秒）逐敌人算下来：每挨一下把敌人往后推 0.26~0.49 米，
                // 而**54%~82% 的命中把它推到了自己够不着的距离之外**（>2.6 米）。
                // 敌人的中位距离是 2.17~2.56 米，正好卡在 AttackRange×1.2 的边界上。
                // 于是循环是：走进距离 → 起手 → 挨一刀被推出距离 →
                // Attack 状态因 dist 超限直接退回 Chase、前摇取消 → 再走回来。
                //
                // 这一条同时解释了三个一直对不上的数：
                //   前摇打断率 86%（它不是被"打断"，是被**推出去**的）
                //   够不到 31%、Chase 占交战时间 44%
                //   以及出手次数在三份日志里纹丝不动地都是 6 次——
                //   我前面改的硬直/眩晕/冷却全都不是瓶颈，位移才是。
                // 大作的通行做法：敌人一旦进入出招承诺，击退大幅衰减
                //（魂系/怪猎里正在出招的敌人几乎推不动）。
                bool committedNow = _telegraphing || Time.time < _swingUntil;
                float kbScale = committedNow ? 0.15f : 1f;
                Vector3 kb = DamageResolver.KnockbackDir(dmg.sourcePosition, transform.position)
                             * Mathf.Min(dmg.knockback * 0.09f, 0.8f) * kbScale;
                if (kb.sqrMagnitude > 1e-4f) StartCoroutine(KnockSlide(kb));
            }

            if (statusBar != null)
            {
                statusBar.SetHealth(_hp, profile.maxHealth);
                statusBar.SetPosture(Mathf.Max(0, _posture), profile.posture);
            }

            TriggerAnim("Hit");

            // 被打醒：立即进入追击
            if (State == EnemyState.Idle || State == EnemyState.Patrol) State = EnemyState.Chase;

            if (_hp <= 0) { Die(); return; }

            // 受击反应（去掉"铁桩感"的关键）：
            // 轻击=踉跄小硬直并打断正在进行的攻击；重击=直接击倒趴地；
            // 受击霸体冷却防止无限连打硬直，Boss 霸体更长（可打出但不能锁死）
            bool heavyHit = fbHeavy;
            // ================= 防连锁硬直（这一段是新的，理由写在这里） =================
            // 玩家反馈：「敌人被打倒、进防御状态之后，只要一直追打就基本无还手之力，
            // 一路被动到死」。旧代码里有三处让这件事必然发生：
            //   ① 这行原本是 (_flinchCd <= 0f || heavyHit)——**重击无条件绕过霸体冷却**。
            //      而"重击"的门槛是削韧≥22 或伤害≥34，旋风绝斩、蓄力跳劈、所有绝招
            //      全都够线。于是连着放重招 = 每一下都进硬直，每次 1.5 秒，永远起不来。
            //   ② 倒地起身没有任何保护，爬起来的那一帧就能被打回去（见起身霸体窗）。
            //   ③ 硬直没有递减：第七次被打进硬直和第一次一样长。
            // 大型动作游戏对这三件事都有成文的做法，这里逐条对应：
            //   ① 韧性/霸体（魂系 poise、只狼躯干）：重击不再免检，只是把霸体冷却
            //      **削掉一截**——连着放重招仍然更容易打出硬直，但不再是每下必中。
            //   ② 起身无敌帧 / 受身（几乎所有格斗与动作游戏）：见上面的 _poiseArmorUntil。
            //   ③ 硬直递减 / 连段比例衰减（格斗游戏的 proration、God of War 的眩晕衰减）：
            //      同一个 6 秒窗口内每多被打进一次硬直，硬直时间乘 0.72、霸体冷却加长，
            //      于是连段一定会结束，敌人一定会拿回一次出手机会。
            // 三条都**不减少伤害**：追打的收益一点没变，变的只是"对方还有没有还手的机会"。
            bool poiseArmored = Time.time < _poiseArmorUntil;
            // 硬直预算用尽 = 本窗口内一律不再进硬直（见 StaggerBudget）
            if (PoiseBudgetSpent) poiseArmored = true;
            bool canFlinch = !poiseArmored && _flinchCd <= 0f;
            // 重击打在霸体上：不进硬直，但把霸体冷却削掉 0.35 秒——
            // "重招更容易打断对方"这条直觉保留下来，只是不再是必然。
            if (!canFlinch && heavyHit && !poiseArmored) _flinchCd -= 0.35f;

            // ---- 受击反应分档：按【打中哪儿 + 这一下真正打进去多少】决定反应大小 ----
            // 玩家的原话：「踉跄状态没有根据伤害程度做区分，头部被打中应当非常明显，
            // 胸部次之，依次类推，不是每次踉跄反应都非常大」。这条判断是对的：
            // 旧代码只有两档（重击 1.5 秒击倒 / 其余一律 0.42 秒踉跄），
            // 而"重击"的门槛读的是**招式的原始数值**，与打中哪个部位完全无关——
            // 砍中小腿和砍中脑袋给出的是同一个反应。
            // 分档之后：擦到手脚只是微微一颤（而且**不打断它正在出的招**），
            // 四肢是小踉跄，胸腹是标准踉跄，头部才是大反应，头部重击才击倒。
            int tier = HitReactionTier(part, final, heavyHit);
            // 【0 档不进硬直】这是"不是每次反应都很大"的关键一半：
            // 微颤只播一个 0.1 秒的一颤，敌人正在挥的那一刀照常挥完。
            // 大作里打四肢/末端本来就打不断一记已经挥出去的重招。
            bool microFlinch = tier == 0 && _posture > 0 && State != EnemyState.Stagger;
            if (microFlinch)
            {
                // 正在挥招的当口不要把它的招从画面上抹掉（与下面那段同理）
                if (poser != null && Time.time > _swingUntil) poser.SetPose(PoseState.Flinch);
                canFlinch = false;   // 不消耗霸体冷却，也不进下面的硬直分支
            }
            if (_posture > 0 && State != EnemyState.Stagger && canFlinch)
            {
                // 受击霸体冷却 1.1→0.7s（Boss 2.4→1.9s）：原值下杂兵在一整套连段里
                // 只踉跄一次，剩下四五下全程站着不动——这是"打上去没反应/攻击力弱"
                // 最刺眼的一处。缩短后普通敌人几乎每两下就吃一次硬直，但仍保留
                // 霸体窗口，不至于被彻底连到死。
                // 连锁计数：6 秒窗口内每多被打进一次硬直，霸体冷却越长、硬直越短
                if (Time.time > _staggerChainUntil) _staggerChain = 0;
                _staggerChainUntil = Time.time + StaggerChainWindow;
                _staggerChain++;
                _winFlinch++;
                float chainDecay = Mathf.Max(0.35f, Mathf.Pow(0.72f, _staggerChain - 1));
                _flinchCd = (profile.category == EnemyCategory.Boss ? 1.9f : 0.7f)
                            * (1f + 0.45f * (_staggerChain - 1));
                CancelInvoke(nameof(OpenAttackHitbox));
                CancelInvoke(nameof(FireHitbox));
                CancelInvoke(nameof(FireProjectile));
                ShowTelegraph(false);
                if (attackHitbox != null) attackHitbox.DisableHitbox();
                State = EnemyState.Stagger;
            Combat.CombatDirector.Release(this);   // 进入硬直/破绽：立即让出攻击令牌
                // 1 档=小踉跄 0.35s、2 档=大踉跄 0.75s、3 档=击倒 1.5s
                _staggerTimer = ClampStagger(StaggerSeconds[Mathf.Clamp(tier, 1, 3)] * chainDecay);
                StopMoving();
                // 只有 3 档（头部重击 / 一击超过一成血的重击）才真的被打倒在地。
                // 此前是"任何重击都倒地"，于是一套连段里人一直在地上，起来又倒——
                // 那正是"一路被压着打"最直接的来源。
                // 击飞很远（大击退）时播【腾空后翻滚】——飞出去是真实空翻而非僵直漂移
                if (tier >= 3)
                {
                    _downed = true;
                    // 「击飞」是少数招式的特权，不是重击的默认表现。
                    // 门槛从 knockback≥4 抬到 ≥8：4 这条线连巨剑横斩(4.5)都算数，
                    // 于是普通连段每隔几下就把人抛出去一次——既接不上连招，
                    // 也不是大作的做法（大作里只有专门的吹飞技/终结技才击飞）。
                    // ≥8 之后只剩旋身空翻踢(9)与成招终结(×1.8)能触发。
                    bool bigLaunch = dmg.knockback >= 8f || dmg.physicalDamage >= 60f;
                    Vector3 flyDir = DamageResolver.KnockbackDir(dmg.sourcePosition, transform.position);
                    if (bigLaunch)
                    {
                        float flyDur = 0.55f;
                        _staggerTimer = Mathf.Max(_staggerTimer, flyDur + 0.6f);
                        if (poser != null) poser.PlayTumble(flyDur);
                        // 距离同样要换算并封顶：原式 5.5+knockback*0.6 在 knockback=16
                        // （成招终结的旋身空翻踢）时算出 15 米，人直接飞出视野。
                        StartCoroutine(KnockFly(flyDir,
                            Mathf.Clamp(1.6f + dmg.knockback * 0.22f, 1.6f, 4.2f), flyDur));
                    }
                    else
                    {
                        if (poser != null) poser.SetPose(PoseState.Knockdown);
                        StartCoroutine(KnockFly(flyDir, 1.4f, 0.15f));
                    }
                }
                else if (poser != null)
                {
                    // 反应大小也要**看得见**：2 档走重受击片段，1 档走普通受击。
                    // 只改时长不改动作，画面上仍然是"每次都一样大"。
                    if (tier >= 2) poser.SetHitPose(dmg.physicalDamage * 2f, dmg.knockback);
                    else poser.SetHitPose(dmg.physicalDamage * 0.5f, dmg.knockback);
                }
            }
            // 霸体冷却期间也要【看得出挨了打】：不打断攻防逻辑，但受击动作必播
            //（此前霸体期间连受击动画都不播，就是"被踢了一脚却站着没反应"的原因）。
            // 仅在自己不处于挥击相位时播，避免把正在出的招从画面上抹掉。
            else if (!microFlinch && !guardedHit && State != EnemyState.Stagger &&
                     Time.time > _swingUntil && poser != null)
            {
                poser.SetHitPose(dmg.physicalDamage, dmg.knockback);
            }

            // 【加了 State != Stagger 这一条】此前没有它，于是破防可以在**已经处于硬直中**
            // 再次触发，并把 _staggerTimer 重新拨回 2.4 秒。而韧性一破就回满、
            // 玩家一套连段的削韧（10+12+14+28=64）本来就高过标准杂兵的韧性（40），
            // 于是"打一套 -> 破防 -> 硬直里继续打 -> 再破防 -> 计时器重置"闭环成立。
            // 玩家截图里的「破防2、受击1、占比 43%」正是这个形状：
            // 进硬直只有三次，时间却烧掉 2.6 秒。
            // 【韧性不许欠债】实机日志里 foePoise 最低到过 **-90.8**。
            // 原因是这个分支被 poiseArmored / State==Stagger 挡下时，韧性**不重置**，
            // 于是继续往负数里减，攒出一大笔"破防债"；等霸体窗一过、
            // 硬直一解除，_posture <= 0 立刻成立，当场兑现一次破防。
            // 也就是说霸体窗和硬直预算并没有真的挡住破防，只是把它**推迟**了。
            // 破防被霸体吸收掉就该是吸收掉：韧性回满，债一笔勾销。
            if (_posture <= 0 && (poiseArmored || State == EnemyState.Stagger))
            {
                _posture = profile.posture;
                if (statusBar != null) statusBar.SetPosture(_posture, profile.posture);
            }
            if (_posture <= 0 && !poiseArmored && State != EnemyState.Stagger)
            {
                _winPosture++;
                // 韧性击破=破绽：明确提示 + 破绽期吃 1.6 倍伤害
                //
                // 【也走同一套递减】破防本来就是"削韧打法"的正收益，不该削弱；
                // 但破防之后韧性是**回满**的，如果 2.4 秒的破绽每次都一样长，
                // 削韧流就变成了另一条无限连——玩家一直打，敌人一直在破绽里。
                // 递减之后第一次破防仍是完整的 2.4 秒（该给的仪式感一点不少），
                // 短时间内反复破防才会缩短。破绽结束同样给一个霸体窗。
                if (Time.time > _staggerChainUntil) _staggerChain = 0;
                _staggerChainUntil = Time.time + StaggerChainWindow;
                _staggerChain++;
                _posture = profile.posture;
                State = EnemyState.Stagger;
            Combat.CombatDirector.Release(this);   // 进入硬直/破绽：立即让出攻击令牌
                _staggerTimer = ClampStagger(2.4f * Mathf.Max(0.4f, Mathf.Pow(0.75f, _staggerChain - 1)));
                StopMoving();
                CancelInvoke(nameof(OpenAttackHitbox));
                CancelInvoke(nameof(FireHitbox));
                ShowTelegraph(false);
                TriggerAnim("Stagger");
                if (poser != null) poser.SetPose(PoseState.Stagger);
                if (statusBar != null)
                {
                    statusBar.SetPosture(_posture, profile.posture);
                    statusBar.SetEmotion("破绽！！猛攻！");
                }
                if (dialogue != null) dialogue.Show("【破绽】", 2.2f);
                CombatFeedback.SlowMo(0.5f, 0.15f);
            }
        }

        /// <summary>Boss 机制回血（两元赖账王耍赖回血等）：按最大生命比例恢复。</summary>
        public void HealFraction(float frac)
        {
            if (State == EnemyState.Dead) return;
            _hp = Mathf.Min(profile.maxHealth, _hp + profile.maxHealth * Mathf.Clamp01(frac));
            if (statusBar != null) statusBar.SetHealth(_hp, profile.maxHealth);
            CombatFeedback.DamageNumber(transform.position, "耍赖回血",
                new Color(0.6f, 0.9f, 0.6f), 1.1f);
        }

        /// <summary>机制性强制破绽（火种齐燃/整合触发等 Boss 事件）：进入长硬直吃增伤。</summary>
        public void ForceBreak(float duration)
        {
            if (State == EnemyState.Dead) return;
            CancelInvoke(nameof(OpenAttackHitbox));
            CancelInvoke(nameof(FireHitbox));
            CancelInvoke(nameof(FireProjectile));
            ShowTelegraph(false);
            if (attackHitbox != null) attackHitbox.DisableHitbox();
            _posture = profile.posture;
            State = EnemyState.Stagger;
            Combat.CombatDirector.Release(this);   // 进入硬直/破绽：立即让出攻击令牌
            _staggerTimer = duration;
            StopMoving();
            if (poser != null) poser.SetPose(PoseState.Stagger);
            if (statusBar != null)
            {
                statusBar.SetPosture(_posture, profile.posture);
                statusBar.SetEmotion("破绽！！猛攻！");
            }
            if (dialogue != null) dialogue.Show("【破绽】", 2.2f);
            CombatFeedback.SlowMo(0.5f, 0.15f);
        }

        /// <summary>蓄力气场外推：玩家蓄力风场把靠近的敌人持续推出半径外（无法近身攻击）。
        /// 越靠近中心推力越强；同时打断正在进行的出手前摇。</summary>
        public void Repel(Vector3 center, float radius, float strength, float dt)
        {
            if (State == EnemyState.Dead) return;
            Vector3 away = transform.position - center; away.y = 0;
            float d = away.magnitude;
            if (d > radius || d < 0.01f) return;
            float k = 1f - d / radius;
            Vector3 step = away.normalized * (strength * (0.35f + k)) * dt;
            if (AgentReady) _agent.Move(step);
            else transform.position += step;
        }

        /// <summary>受击退步：0.22 秒滑完击退量（快出慢收），不瞬移。</summary>
        System.Collections.IEnumerator KnockSlide(Vector3 offset)
        {
            offset.y = 0;
            float t = 0, dur = 0.22f;
            while (t < dur && State != EnemyState.Dead)
            {
                float dt = Time.deltaTime;
                t += dt;
                Vector3 step = offset * Mathf.Min(dt / dur, 1f);
                if (AgentReady) _agent.Move(step);
                else transform.position += step;
                yield return null;
            }
        }

        /// <summary>侧闪滑步：0.18 秒滑到位（快出慢收），镜头软跟随不产生跳动。</summary>
        System.Collections.IEnumerator DodgeSlide(Vector3 offset)
        {
            float t = 0, dur = 0.18f;
            while (t < dur && State != EnemyState.Dead)
            {
                float dt = Time.deltaTime;
                t += dt;
                if (AgentReady) _agent.Move(offset * Mathf.Min(dt / dur, 1f));
                yield return null;
            }
        }

        /// <summary>重击击飞：受击位移（二次强减速）。distance=总飞行距离、dur=时长。
        /// 小击退=极短栽倒（≈1.4m/0.15s，当场倒地）；大击退=飞很远（配合腾空后翻滚，
        /// 5m+/0.55s），位移与空翻同步，不再是僵直漂移。</summary>
        System.Collections.IEnumerator KnockFly(Vector3 dir, float distance, float dur)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 0.01f) yield break;
            dir = dir.normalized;
            // 二次减速位移积分 ∫3k²=1 → 峰值速度系数使总位移=distance
            float peak = distance * 3f / dur;
            float t = 0;
            while (t < dur && State == EnemyState.Stagger)
            {
                t += Time.deltaTime;
                float k = 1f - t / dur;
                float sp = peak * k * k;
                if (AgentReady) _agent.Move(dir * sp * Time.deltaTime);
                else transform.position += dir * sp * Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// 把设置面板里的「敌人强度」落到这一个敌人的生命上。
        ///
        /// profile 是每个实例各自一份（EnemyCatalog.Create 每次 new 一个），
        /// 所以直接改它的 maxHealth 是安全的，血条、minHpFloor、回血都会跟着走。
        /// _toughApplied 记着已经乘过多少，改档时先除回去再乘新的——
        /// 否则连按几次开关会把生命累乘上天，而那种 bug 在实机上表现为
        /// "调了一下就再也打不死了"，和这次要修的问题正好是一对。
        ///
        /// full=false 时保留当前血量**百分比**：战斗中途改档不会把一个残血敌人
        /// 直接治满，也不会把满血敌人一秒打死。
        /// </summary>
        public void ApplyToughness(bool full)
        {
            float want = Mathf.Clamp(Core.GameDebug.EnemyToughness, 0.25f, 12f);
            if (!full && Mathf.Approximately(want, _toughApplied)) return;
            float frac = profile.maxHealth > 0.01f ? Mathf.Clamp01(_hp / profile.maxHealth) : 1f;
            profile.maxHealth = profile.maxHealth / Mathf.Max(0.01f, _toughApplied) * want;
            _toughApplied = want;
            _hp = full ? profile.maxHealth : profile.maxHealth * frac;
            if (statusBar != null) statusBar.SetHealth(_hp, profile.maxHealth);
        }

        /// <summary>场上所有敌人立刻按新档位重算生命（设置面板改档时调）。返回处理了几个。</summary>
        public static int ApplyToughnessAll()
        {
            var all = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            int n = 0;
            foreach (var e in all) { if (e == null) continue; e.ApplyToughness(false); n++; }
            return n;
        }

        void Die()
        {
            State = EnemyState.Dead;
            Combat.CombatDirector.Release(this);   // 死亡：归还攻击令牌
            CancelInvoke();
            ShowTelegraph(false);
            StopMoving();
            if (_agent != null) _agent.enabled = false;
            TriggerAnim("Death");
            if (poser != null) poser.SetPose(PoseState.Death);
            if (statusBar != null) statusBar.Hide();
            if (dialogue != null) dialogue.Show("不……可能……", 2f);
            CombatFeedback.Debris(transform.position, new Color(0.4f, 0.2f, 0.45f), 6);
            // 击杀落幕（电影语言）：短促时缓 + 镜头缓推特写，看清敌人倒下的瞬间
            CombatFeedback.SlowMo(0.45f, 0.28f);
            // 击杀是【轻推】而非大招级满推，且交由镜头节流/群战抑制裁决——
            // 群战里每杀一个就贴脸一次，会让镜头长期焊死在特写位
            CombatFeedback.CloseUp(0.9f, 0.55f);
            GameAudio.Play(GameAudio.Sfx.Death, 0.9f);
            GameEvents.RaiseEnemyKilled(profile.enemyId);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Destroy(gameObject, 3f);
        }
    }
}
