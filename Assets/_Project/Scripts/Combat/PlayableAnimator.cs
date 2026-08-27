using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 动捕动画驱动（基于 Playables，纯代码，无需 AnimatorController 手工连线）。
    ///
    /// 直接吃 Mixamo 原始文件：把动作 FBX（形如 `角色@Side Kick.fbx`）放进
    /// Resources/Characters/Anims/ 即可——Unity 会按 `@后缀` 命名内部动画片段
    /// （"Side Kick"/"Great Sword Slash"/"Idle"…），本类用 LoadAll 全取出来，
    /// 按片段名映射到各招式，**无需重命名**。
    ///
    /// 结构：top[0]=locomotion(idle/临战idle/走/跑) 混合、top[1]=招式层交叉淡入。
    /// 缺 idle/walk/run 任一则判定无效，上层回退程序化骨骼。
    ///
    /// 目录 19 个片段全量启用：
    ///   Idle/Fighting Idle/Walking/Running → 移动层；
    ///   Lead Jab/Cross Punch/Kicking/Side Kick/Spin Flip Kick/Flying Kick → 拳腿；
    ///   Great Sword Slash/(1)/High Spin/Jump Attack/Stabbing → 剑技；
    ///   Hit Reaction→受击、Knocked Down→击倒(保持倒地帧)、Dying→死亡(保持)、
    ///   Spell Casting→施法+蓄力前摇、Hit Reaction(慢放)→踉跄、Fighting Idle→格挡架势。
    /// </summary>
    public class PlayableAnimator
    {
        // Mixamo 片段名（小写）→ 招式。前面的候选优先精确匹配，找不到再按包含匹配。
        // speed=播放速度下限；hold=播完保持最后一帧（倒地/死亡等持续状态，直到切换姿态）；
        // start/end=有效发力窗（跳过片段头尾这两个比例之外的部分）。
        struct ActionDef
        {
            public PoseState pose;
            public string[] keys;
            public float speed;
            public bool hold;
            public float start, end;
            public bool pool;   // true=keys 里【每一条】都接进来，轮流播（变体池）
        }

        static ActionDef A(PoseState p, float speed, float start, float end, bool hold,
            params string[] keys) =>
            new ActionDef { pose = p, keys = keys, speed = speed, hold = hold, start = start, end = end };

        /// <summary>
        /// 收起兵器时的替身片段。
        ///
        /// 持剑与空手是两套身体语言——大作里它们是两套完整的动作集（武器收起
        /// 走路的摆臂、蹲伏的重心、踢击的发力都不一样）。本作只对差别最扎眼的
        /// 三个做替换，加上移动层的临战架势（见 combatIdleUnarmed），
        /// 已经足以让"收刀之后人明显松下来"这件事被看见。
        /// 表里没有的姿态一律沿用持剑版本。
        /// </summary>
        static readonly ActionDef[] UnarmedMap =
        {
            A(PoseState.CrouchIdle,  1.0f, 0f,    1f,    true,  "crouching idle"),
            A(PoseState.AttackKick,  1.8f, 0.18f, 0.70f, false, "kicking"),
            A(PoseState.Death,       1.0f, 0f,    1f,    true,  "dying"),
            // 空手受击：持剑受击（Great Sword Impact 系列）双手是握着东西的，
            // 拳法/腿法型敌人用它会像凭空攥着一把剑挨打
            A(PoseState.Hit,         1.45f, 0.10f, 0.78f, false, "hit reaction"),
        };

        /// <summary>
        /// 变体池：同一个姿态挂多条片段，每次出招轮换。
        ///
        /// 这是动作游戏里最省事也最有效的一条"不重复"规则。受击尤其明显：
        /// 一套连招打在同一个敌人身上五下，若五下播的是同一条受击片段，
        /// 那不叫"连了五下"，读作"抖了五下"。斩击、原地掉头同理。
        /// A() 是**候选链**（第一条找得到的就用），AP() 是**变体池**（全都要，轮着来）。
        /// </summary>
        static ActionDef AP(PoseState p, float speed, float start, float end, params string[] keys) =>
            new ActionDef { pose = p, keys = keys, speed = speed, hold = false,
                            start = start, end = end, pool = true };

        // 手感三板斧（根治"出招慢、软绵绵、连点糊成一团"）：
        // ① start 起手偏移——Mixamo 原始攻击片段前 12%~22% 是缓慢的摆架势预备，
        //    从偏移处起播，一按键立刻进入发力挥击相位，出手零迟滞；
        // ② end 收招裁剪——片段尾部 20%~32% 是"慢慢走回站姿"的回位段，动作游戏
        //    里这一段由下一招或移动层直接接管。裁掉它，出招不再拖一条尾巴。
        //    巨剑类片段最长，尾巴也裁得最狠：发力窗越短，反推出的倍速要求越低，
        //    「刃真正扫过的那一瞬」也就越早落在判定窗（windup~windup+open）里——
        //    否则倍速被可读性上限压住时，判定会明显早于画面上的挥击；
        // ③ speed 只是**下限**：真正的播放速度由招式的帧数据反推（见 PlayIndex），
        //    让发力窗尽量在这一招的窗口内播完。此前是固定倍速，片段有效时长
        //    （≈0.6~1.0s）普遍是招式时长（0.30~0.35s）的两三倍，于是每一击都只播到
        //    一半就被下一击重置：看起来永远是"慢吞吞地举手、还没打到就换了个动作"。
        //    注意反推出的倍速要再过一道【可读性上限】MaxDrivenSpeed——
        //    短片段（拳腿类 ≈1s）能真的在 0.3 秒内打完一拳；长片段（巨剑类 2 秒以上）
        //    做不到，会被下一招切在半路。这是刻意的取舍，理由见 MaxDrivenSpeed。
        static readonly ActionDef[] ActionMap =
        {
            // 同 idle/walk/run：工程里没有精确的 "Great Sword Slash"，这个键会在
            // 4 个候选（Slash 3 / Slash 5 / Maria@Slash / Maria@Slash (1)）里
            // 按字典顺序挑一个——主力轻击靠遍历顺序决定，不可接受。写死实际文件名。
            AP(PoseState.Attack,     1.75f, 0.20f, 0.68f,        "maria wprop j j ong@great sword slash", "great sword slash 5"),
            A(PoseState.HeavyAttack, 1.5f,  0.12f, 0.78f, false, "great sword attack", "great sword slash 5", "great sword high spin attack"),
            A(PoseState.AttackUp,    1.75f, 0.20f, 0.68f, false, "great sword slash (1)", "great sword high spin attack"),
            A(PoseState.SwordThrust, 1.85f, 0.18f, 0.66f, false, "stabbing", "stab"),
            // 跃劈与空中下劈【分家】：此前两者都指向 Great Sword Jump Attack，
            // 于是四套技能的终结段播的是同一段动画，"最后一击"没有独占记忆点。
            // 现在跃劈走 Slash 3（大开大合的下劈），Jump Attack 留给真正的空中终结。
            A(PoseState.AttackLeap,  1.55f, 0.12f, 0.80f, false, "great sword slash 3", "great sword jump attack", "jump attack"),
            A(PoseState.JumpAttack,  1.6f,  0.15f, 0.84f, false, "great sword jump attack", "great sword jump", "jump attack"),
            A(PoseState.AttackSpin,  1.6f,  0.15f, 0.72f, false, "great sword high spin attack", "spin attack", "great sword slash (1)"),
            A(PoseState.PunchJab,    1.95f, 0.15f, 0.64f, false, "lead jab", "jab"),
            A(PoseState.PunchCross,  1.85f, 0.15f, 0.64f, false, "cross punch"),
            A(PoseState.AttackKick,  1.8f,  0.18f, 0.70f, false, "great sword kick", "kicking"),
            A(PoseState.SideKick,    1.8f,  0.18f, 0.70f, false, "side kick"),
            A(PoseState.SpinKick,    1.65f, 0.12f, 0.78f, false, "spin flip kick", "spin kick"),
            A(PoseState.JumpKick,    1.7f,  0.12f, 0.80f, false, "flying kick"),
            A(PoseState.Sweep,       1.6f,  0.12f, 0.80f, false, "leg sweep", "spin flip kick"),
            AP(PoseState.Hit,        1.45f, 0.10f, 0.78f,        "great sword impact", "great sword impact 2",
                                                                   "great sword impact 3"),
            // 重受击：大伤害/大击退时换一条更夸张的受击（见 HitPoseFor）——
            // "10 点伤害和 42 点伤害看起来一模一样"是打击感最大的漏洞，
            // 分档换片段是大作里最省事也最有效的一招。
            AP(PoseState.HitHeavy,   1.2f,  0.05f, 0.86f,        "great sword impact 4", "great sword impact 5"),
            A(PoseState.GuardHit,    1.5f,  0.08f, 0.80f, false, "great sword block hit", "great sword blocking"),

            // ===== 移动层过渡 =====
            A(PoseState.StartMove,   1.25f, 0f,    0.72f, false, "start walking"),
            A(PoseState.StopMove,    1.35f, 0f,    0.85f, false, "run to stop"),
            A(PoseState.TurnLeft,    1.3f,  0.05f, 0.95f, false, "left turn 90"),
            A(PoseState.TurnRight,   1.3f,  0.05f, 0.95f, false, "right turn 90"),
            AP(PoseState.Turn180,    1.35f, 0.05f, 0.95f,        "quick 180 turn", "quick 180 turn (1)"),
            A(PoseState.StepBack,    1.4f,  0.05f, 0.90f, false, "step backward"),

            // ===== 腾空 =====
            A(PoseState.JumpUp,      1.2f,  0.05f, 0.85f, false, "jumping up"),
            A(PoseState.FallLoop,    1.0f,  0f,    1f,    true,  "falling idle"),
            A(PoseState.Land,        1.3f,  0f,    0.85f, false, "falling to landing"),
            A(PoseState.LandHard,    1.15f, 0f,    0.90f, false, "hard landing"),

            // ===== 方向闪避（面向目标时的左右闪身；前后仍走翻滚/后撤步）=====
            A(PoseState.DodgeLeft,   1.6f,  0.05f, 0.92f, false, "standing dodge left"),
            A(PoseState.DodgeRight,  1.6f,  0.05f, 0.92f, false, "dodging right"),

            // ===== 蹲伏待机（持剑优先）=====
            A(PoseState.CrouchIdle,  1.0f,  0f,    1f,    true,  "great sword crouching", "crouching idle"),

            // ===== 技能补位 =====
            // 突进斩：技能位移段专用——脚在动的那 0.1 秒交给它，判定段再切斩击，
            // 否则 2.2 米的突进期间播的是原地斩，读作"人在飘"。
            A(PoseState.DashAttack,  1.5f,  0.05f, 0.80f, false, "great sword slide attack"),
            // 蓄力循环：可保持——蓄多久播多久，替掉"停在末帧的冻结姿势"
            A(PoseState.ChargeLoop,  1.0f,  0f,    1f,    true,  "great sword power up", "great sword casting"),
            A(PoseState.CastProjectile, 1.35f, 0.08f, 0.80f, false, "great sword casting", "spell casting"),
            // 击倒提速：受了重击身体应当干脆地倒下去，而不是慢悠悠飘倒
            A(PoseState.Knockdown,   1.3f,  0.04f, 1f,    true,  "knocked down", "sweep fall", "knockdown", "falling back"),
            A(PoseState.Death,       1.0f,  0f,    1f,    true,  "two handed sword death", "dying", "death"),
            A(PoseState.Cast,        1.0f,  0f,    1f,    false, "spell casting", "cast"),
            // ===== 动作库覆盖面补位（下载对应片段放入 Anims/ 后自动生效）=====
            // 每项前面的候选是【专用片段】，末尾候选是没有专用片段时的替代：
            // 格挡=Great Sword Blocking（替代=格斗架势收紧）；
            // 踉跄=Stunned（替代=受击慢放）；蓄力=Great Sword Casting（替代=聚气施法）；
            // 翻滚=Stand To Roll / Forward Roll（无片段时由 HumanoidAnimator 程序化翻滚）；
            // 扫堂腿=Leg Sweep（替代=空翻踢低位）。
            A(PoseState.Guard,       1.0f,  0f,    1f,    true,  "great sword blocking", "blocking", "block", "fighting idle"),
            A(PoseState.Stagger,     0.55f, 0.10f, 1f,    false, "stunned", "dizzy", "stagger", "hit reaction"),
            A(PoseState.Charge,      0.85f, 0f,    1f,    true,  "great sword power up", "great sword casting", "warming up", "charge"),
            // 翻滚：闪避时长会自动匹配片段长度（PlayerController），完整呈现整个滚翻
            A(PoseState.Dodge,       1.7f,  0.10f, 1f,    false, "stand to roll", "forward roll", "sprinting forward roll", "dive roll"),
        };

        /// <summary>
        /// 由帧数据反推播放速度的【可读性上限】。
        ///
        /// 上一版取 3.4——那是按"一定要在招式窗口内把整段发力窗播完"倒推的，
        /// 但它超过了眼睛能连成动作的极限：30fps 下 3.4 倍意味着相邻两帧之间
        /// 跳过 0.11 秒的动作数据，肢体位置一跳一跳地闪，大脑读不出"运动"，
        /// 只读出"位置在变的静止姿势"——这就是"快到看似角色静止"的来源。
        /// 2.6 倍下每帧推进 0.087 秒，动作仍然连得起来（2.2 偏保守，实测可以再快）。
        /// 真正让"快"和"顺"同时成立的不是这个数，而是**发力窗裁得更短**：
        /// 只演核心一击、把摆架势和回位都交给上下一招，观感上快得多，
        /// 每帧推进量却不增加——提倍速会闪，裁窗口不会。
        ///
        /// 代价要说清楚：长片段（巨剑类 2 秒以上）因此无法在 0.35 秒内播完整个
        /// 发力窗，会被下一招切断。这是对的——动作游戏里连段本来就是"一刀砍到
        /// 一半接下一刀"，看得清比播得完重要。
        /// </summary>
        const float MaxDrivenSpeed = 2.6f;

        /// <summary>单次出招在画面上至少要占的时间（≈30fps 下 5-6 帧）。
        /// 技能连招里 0.14 秒一段的节拍若原样反推，动作只剩三四帧就换下一个，
        /// 同样会读成"抖了一下"而不是"打了一下"。低于此值的节拍按此值给动画，
        /// 动作因此会略微溢出节拍、被下一段切断——连续，但不空转。</summary>
        const float MinShownMotion = 0.18f;

        readonly Animator _animator;
        PlayableGraph _graph;
        AnimationMixerPlayable _top;      // 0=loco 1=action
        AnimationMixerPlayable _loco;     // 0=idle 1=combatIdle 2.. = 方向移动片段
        AnimationMixerPlayable _actions;

        // ===== 方向移动混合（前/后/左/右/斜向）=====
        //
        // 这是动作游戏移动层的标准结构：一圈**按方向摆开的移动片段**，
        // 按摇杆方向在相邻两个之间混合，按速度在走档与跑档之间混合。
        // 之所以要这么做，而不是"播个向前走再把骨盆拧过去"：
        //   · 侧步和后退的**上身姿态本来就不一样**（侧步是横向蹬地、后退是脚跟先落），
        //     拧骨盆只能骗过下半身，上半身还在做向前走的摆臂；
        //   · 真片段自带正确的步幅与重心，脚不打滑。
        //
        // 两条关键工程细节，缺一个都会露馅：
        //   ① **共享归一化相位**：混合中的每个片段都停在同一个步态相位上
        //      （都在左脚触地的那一刻），否则两条腿会互相打架，糊成"滑步"；
        //   ② **按片段实测的自然速度做步幅同步**：侧步的步幅比前进小得多，
        //      拿前进的自然速度去算侧步的播放速率，脚必然打滑。
        struct LocoDir
        {
            public AnimationClipPlayable cp;
            public float angle;      // 该片段的行进方向（度，0=正前，+90=右）
            public int tier;         // 0=走档 1=跑档
            public float natSpeed;   // 该片段自身的位移速度（m/s，世界尺度）
            public float len;        // 片段时长
            public string name;      // 片段名（只给屏幕上的调试叠层用）
        }

        /// <summary>速度档数：0=走 1=慢跑 2=跑 3=冲刺。
        /// 四档而非两档的理由很实际：动作库里前/后/左/右各有四种速度的片段，
        /// 两档时其中一半永远用不上，而且主导片段的自然速度与真实移速差得远，
        /// 步幅同步只能靠 0.5~2.0 的倍速去凑——那正是"脚在滑"的来源。
        /// 档位分得细，倍速就贴近 1.0，脚自然踩得实。</summary>
        const int TierCount = 4;
        /// <summary>蹲伏环的下标（不参与速度档混合，蹲下时整体接管）。</summary>
        const int CrouchTier = TierCount;
        /// <summary>移动混合器里方向片段的起始输入口：0=待机 1=持剑临战架势 2=空手临战架势。</summary>
        const int DirBase = 3;

        readonly List<LocoDir> _dirs = new List<LocoDir>();
        // 每一档一圈，按角度排好序的下标；最后一圈是蹲伏
        readonly List<int>[] _rings = new List<int>[TierCount + 1];
        readonly float[] _tierW = new float[TierCount];
        /// <summary>每档的代表自然速度（m/s，实测）。档位插值按它分段，
        /// 而不是把 runSpeed 均分——见 Tick 里的推导。</summary>
        readonly float[] _tierNat = new float[TierCount];
        readonly float[] _tierNatFwd = new float[TierCount];
        float[] _dirW;               // 每帧算出来的方向权重
        // 因"同档同方向已有片段"而未接入的候选（CI 诊断打出来，见 Add）
        readonly List<string> _dropped = new List<string>();
        float _phase01;              // 共享步态相位 [0,1)
        float _moveAngle;            // 行进方向相对角色正面（度）

        /// <summary>动作库里有没有成套的方向移动片段（≥3 个不同方向）。
        /// 有的话，上层就不必再用"拧骨盆 + 倒放"那套合成手段了。</summary>
        public bool HasDirectionalSet { get; private set; }

        /// <summary>
        /// 把方向移动表打成一行行可读的文字（CI 诊断用）。
        ///
        /// 存在的理由很实在：方向片段"接没接上、方向测得对不对"是**光看代码看不出来的**，
        /// 而在真机上发现不对再回头查，一轮就是一天。让 CI 每次构建都把这张表打出来，
        /// 少一条、角度反了、自然速度离谱，都能在日志里当场看见。
        /// </summary>
        public string DescribeDirectionalSet()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("方向移动片段 ").Append(_dirs.Count).Append(" 条，成套=")
              .Append(HasDirectionalSet ? "是" : "否").Append('\n');
            foreach (var d in _dirs)
            {
                string name = d.cp.IsValid() && d.cp.GetAnimationClip() != null
                    ? d.cp.GetAnimationClip().name : "?";
                sb.Append("    ").Append(TierName(d.tier))
                  .Append(" 角度=").Append(d.angle.ToString("F0")).Append("°")
                  .Append(" 自然速度=").Append(d.natSpeed.ToString("F2")).Append("m/s")
                  .Append(" 时长=").Append(d.len.ToString("F2")).Append("s")
                  .Append("  [").Append(name).Append("]").Append(NL);
            }
            foreach (var d in _dropped)
                sb.Append("    ⚠ 未接入：").Append(d).Append(NL);
            return sb.ToString();
        }

        static string TierName(int tier)
        {
            switch (tier)
            {
                case 0: return "走  ";
                case 1: return "慢跑";
                case 2: return "跑  ";
                case 3: return "冲刺";
                default: return "蹲伏";
            }
        }

        /// <summary>
        /// 把招式 → 片段的实际映射打成一行行可读的文字（CI 诊断用）。
        ///
        /// 与方向表同理：某个姿态"有没有真的拿到片段、拿到的是不是预期那条"，
        /// 光看代码看不出来——候选链有先后、变体池会被先到的招式抢走片段、
        /// 空手替身还有第二套。让 CI 每次构建都把这张表打出来，
        /// 少一条、串到别的片段上，都能在日志里当场看见。
        /// </summary>
        const char NL = '\n';

        public string DescribeActionSet()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("招式片段 ").Append(_actionIndex.Count).Append(" 个姿态有片段，动作层共 ")
              .Append(_actionCount).Append(" 条；移动层 ").Append(_dirs.Count + DirBase)
              .Append(" 条；本角色 AnimationClipPlayable 合计 ")
              .Append(_actionCount + _dirs.Count + DirBase)
              .Append("（每帧都要过一遍，场上每个角色各一份）").Append(NL);
            foreach (var kv in _actionIndex)
            {
                sb.Append("    ").Append(kv.Key.ToString().PadRight(16));
                if (_actionVariants.TryGetValue(kv.Key, out var vs) && vs.Count > 1)
                {
                    sb.Append("[变体×").Append(vs.Count).Append("] ");
                    for (int i = 0; i < vs.Count; i++)
                    {
                        if (i > 0) sb.Append(" | ");
                        sb.Append(ClipNameAt(vs[i]));
                    }
                }
                else sb.Append(ClipNameAt(kv.Value));
                if (_unarmedIndex.TryGetValue(kv.Key, out int ua))
                    sb.Append("   （空手：").Append(ClipNameAt(ua)).Append('）');
                // 保持型姿态（蓄力/蹲伏/格挡/倒地/死亡）必须报出【片段自己循不循环】。
                // 这一栏是被一个真实的坑逼出来的：代码里 hold=true 的语义只是
                // "权重保持、等外部切换"，片段播到末尾之后循不循环由**导入设置**
                // （loopTime）决定。两者对不上时，蓄力就是一张静止的照片，
                // 而这件事不报任何错——只能靠把它打出来看。
                if (_actionHold[kv.Value])
                    sb.Append("   [保持 循环=")
                      .Append(ClipLoops(kv.Value) ? "是" : "否").Append(']');
                sb.Append(NL);
            }
            return sb.ToString();
        }

        bool ClipLoops(int idx)
        {
            if (idx < 0 || idx >= _actionCount) return false;
            var cp = (AnimationClipPlayable)_actions.GetInput(idx);
            var c = cp.IsValid() ? cp.GetAnimationClip() : null;
            return c != null && c.isLooping;
        }

        string ClipNameAt(int idx)
        {
            if (idx < 0 || idx >= _actionCount) return "?";
            var cp = (AnimationClipPlayable)_actions.GetInput(idx);
            var c = cp.IsValid() ? cp.GetAnimationClip() : null;
            return c != null ? c.name : "?";
        }

        /// <summary>驱动中的 Animator（供脚踝校准等后处理访问骨骼）。</summary>
        public Animator Animator => _animator;

        // 步幅同步基准：走/跑动画在标准体型下的自然位移速度（m/s）随 TargetHeight
        // 等比缩放——模型被缩放后，动画里烘焙的位移也同比缩放，自然速度必须跟着变，
        // 否则改身高就会脚打滑。系数为原基准 3.6/8.6 相对 4.1m 的比值。
        // 播放速率 = 真实速度 / 自然速度 → 步频与实际位移匹配，脚不打滑。
        static float WalkNaturalSpeed => MecanimCharacter.TargetHeight * 0.878f;
        static float RunNaturalSpeed => MecanimCharacter.TargetHeight * 2.098f;
        readonly Dictionary<PoseState, int> _actionIndex = new Dictionary<PoseState, int>();
        string[] _actionName;   // 动作层每个槽位的片段名（只给调试叠层用）
        // 变体池：同一姿态的多条片段 + 轮换游标（见 AP / NextVariant）
        readonly Dictionary<PoseState, List<int>> _actionVariants =
            new Dictionary<PoseState, List<int>>();
        readonly Dictionary<PoseState, int> _variantCursor = new Dictionary<PoseState, int>();
        // 空手替身（见 UnarmedMap）：收刀状态下这些姿态改播空手版本
        readonly Dictionary<PoseState, int> _unarmedIndex = new Dictionary<PoseState, int>();
        bool _armed = true;
        // 动作库全量索引（片段名→输入口）：未映射到招式的片段也接入，供预览试播
        readonly Dictionary<string, int> _clipIndex = new Dictionary<string, int>();
        float[] _actionLen;
        float[] _actionSpeed;
        bool[] _actionHold;
        float[] _actionStart;    // 起手偏移（片段比例）
        float[] _actionEnd;      // 收招裁剪（片段比例，1=播到底）
        float[] _actionRawLen;   // 片段原始时长（起身反播等按原始长度计算）
        int _actionCount;
        float _playLen;    // 本次播放的有效时长/保持标志（起身反播时与默认不同）
        bool _playHold;
        float _fadeDur;    // 本次播放的淡入时长（0=按招式默认。坐下/躺下这类慢动作要长淡入）

        int _cur = -1;

        // ---- 动作起播记录：给屏幕提示用（见 Tick 里的跃迁判据）----
        int _lastNotedAction = -1;
        /// <summary>最近一次起播的动作片段名（动作层）。没播过是空串。</summary>
        public string LastActionClip { get; private set; } = "";
        /// <summary>那一段计划播多久（秒）。</summary>
        public float LastActionLen { get; private set; }
        /// <summary>起播时刻（Time.time）。</summary>
        public float LastActionAt { get; private set; } = -999f;
        float _actionT, _actionW, _fadeFrom;
        float _speed01;
        float _actualSpeed = -1f;   // 真实移速 m/s（<0 = 未提供，按 speed01 折算）
        bool _ready;
        float _readyW;   // 普通待机↔格斗架势的平滑过渡权重（瞬切会"弹一下"）

        static int _graphSerial;

        public bool Valid { get; private set; }

        readonly string _folder;   // 动作库目录（不同角色可有各自的动作库）

        /// <summary>
        /// 公共补充库：主库里没有的片段名从这里再取一遍。
        ///
        /// 拔剑/收剑这类**与角色无关**的通用动作放在 Anims2 下，两个角色都该用得上；
        /// 而 Anims2 本身缺 idle/walk/run，作为"主库"是不成立的（会整体回退）。
        /// 于是把它降级成补充库：谁做主库都行，主库没有的名字从这里补进来。
        /// </summary>
        const string ExtraFolder = "Characters/Anims2";

        /// <summary>
        /// 动作库文件清单（**按文件名寻址**，与片段内部叫什么无关）。
        ///
        /// 为什么需要这张表：Mixamo 导出的 FBX 里 take 一律叫 "mixamo.com"，
        /// 只有 .meta 显式改名后 Resources.LoadAll 拿到的片段才有正经名字。
        /// 新丢进目录、还没被 Unity 生成过命名 .meta 的文件，按名字一条都找不到
        /// （Build 里那句 `k != "mixamo.com"` 就是被这件事逼出来的）。
        /// 而 Resources.Load(路径) 取的是**文件里的第一个 AnimationClip**，
        /// 与内部名无关，一定拿得到——于是把清单里的每个文件按路径加载一遍、
        /// 用**文件名**注册进 byName，下游（招式表、变体池、PlayNamed、
        /// 方向移动表）就统一按名字工作，不必各自再写一套按路径的兜底。
        ///
        /// 清单必须与目录内容一致：多写了（文件不存在）只是加载不到、静默跳过；
        /// 少写了则那个文件永远不会被用上。CI 诊断会把两边对不上的地方打出来。
        /// </summary>
        static readonly string[] LibraryFiles =
        {
            // —— 移动：走档 / 慢跑档 / 跑档 / 冲刺档 ——
            "Walking Backwards", "Left Strafe Walking", "Right Strafe Walking",
            "Jog Forward", "Jog Backward", "Jog Strafe Left", "Jog Strafe Right",
            "Jog Forward Diagonal", "Jog Backward Diagonal", "Slow Jog Backwards",
            "Run Forward", "Running Backward", "Left Strafe", "Right Strafe", "Fast Run",
            // —— 移动过渡：起步 / 急停 / 原地转身 / 后撤步 ——
            "Start Walking", "Run To Stop", "Left Turn 90", "Right Turn 90",
            "Quick 180 Turn", "Quick 180 Turn (1)", "Step Backward",
            // —— 腾空 ——
            "Jumping Up", "Falling Idle", "Falling To Landing", "Hard Landing",
            // —— 方向闪避 ——
            "Standing Dodge Left", "Dodging Right",
            // —— 蹲伏 ——
            "Crouching Idle", "Crouch Walk Back", "Crouched Sneaking Left", "Crouched Sneaking Right",
            "Great Sword Crouching",
            // —— 持剑战斗 ——
            "Great Sword Idle", "Great Sword Attack", "Great Sword Slash 3", "Great Sword Slash 5",
            "Great Sword Kick", "Great Sword Slide Attack", "Great Sword Power Up",
            "Great Sword Block Hit", "Two Handed Sword Death",
            // —— 受击变体池 ——
            "Great Sword Impact", "Great Sword Impact 2", "Great Sword Impact 3",
            "Great Sword Impact 4", "Great Sword Impact 5",
        };

        public PlayableAnimator(Animator animator, string animsFolder = null)
        {
            _animator = animator;
            _folder = string.IsNullOrEmpty(animsFolder) ? "Characters/Anims" : animsFolder;
            Build();
        }

        /// <summary>此刻是否正在播一段招式（而不是移动层）。
        /// 招式播完会把 _cur 置回 -1，所以这是"身体归谁管"的**实时**答案——
        /// 比看姿态字段可靠：姿态是"最后一次设的招"，招播完了它也不会自己变回去。</summary>
        public bool ActionPlaying => _cur >= 0;

        /// <summary>该招式是否有对应的动捕片段（如翻滚：有专用片段就播片段，
        /// 没有则由上层程序化翻滚兜底）。</summary>
        public bool HasAction(PoseState p) => Valid && _actionIndex.ContainsKey(p);

        /// <summary>招式片段的有效播放时长（考虑起手偏移与倍速；无片段返回 0）。</summary>
        public float ActionLength(PoseState p) =>
            Valid && _actionIndex.TryGetValue(p, out int i) ? _actionLen[i] : 0f;

        static string Norm(string s) => (s ?? "").Trim().ToLowerInvariant();

        static AnimationClip Pick(Dictionary<string, AnimationClip> d, params string[] keys)
        {
            foreach (var k in keys) { if (d.TryGetValue(Norm(k), out var c)) return c; }   // 精确优先
            foreach (var k in keys)
            {
                string n = Norm(k);
                foreach (var kv in d) if (kv.Key.Contains(n)) return kv.Value;              // 再按包含
            }
            return null;
        }

        void Build()
        {
            if (_animator == null) { Valid = false; return; }   // Generic：按路径绑定，无需人形 Avatar

            var byName = new Dictionary<string, AnimationClip>();
            foreach (var c in Resources.LoadAll<AnimationClip>(_folder))
            {
                if (c == null) continue;
                string k = Norm(c.name);
                if (k.Length > 0 && k != "mixamo.com" && !byName.ContainsKey(k)) byName[k] = c;
            }
            // 补充库（见 ExtraFolder）：主库同名的不覆盖，只补主库没有的
            if (_folder != ExtraFolder)
                foreach (var c in Resources.LoadAll<AnimationClip>(ExtraFolder))
                {
                    if (c == null) continue;
                    string k = Norm(c.name);
                    if (k.Length > 0 && k != "mixamo.com" && !byName.ContainsKey(k)) byName[k] = c;
                }
            // 按文件名补齐（见 LibraryFiles）：没有命名 .meta 的 FBX 只能这样寻址
            foreach (var f in LibraryFiles)
            {
                string fileKey = Norm(f);
                if (fileKey.Length == 0 || byName.ContainsKey(fileKey)) continue;
                var byPath = Resources.Load<AnimationClip>(_folder + "/" + f)
                             ?? Resources.Load<AnimationClip>(ExtraFolder + "/" + f);
                if (byPath != null) byName[fileKey] = byPath;
            }

            // ===== 基础 idle / walk / run：候选必须能【精确】命中 =====
            //
            // 审计发现这三条——整个移动层最基础的三条——在本工程里**没有任何
            // 精确文件名匹配**，全部落到 Pick 的"包含匹配"兜底，而那一步是在
            // Dictionary 上遍历取第一个命中的，遍历顺序在 .NET 里并不保证。
            // 候选池里都有什么：
            //   "idle" → Crouching Idle / Falling Idle / Great Sword Idle /
            //            Sleeping Idle / Maria…@Idle
            //   "walk" → Walking Backwards / Left Strafe Walking / Start Walking / Maria…@Walking
            //   "run"  → Running Backward / Maria…@Running
            //
            // 后果不是"可能选错一条片段"那么轻：walk / run 会被直接当成方向环的
            // **0°（正前）**槽位。万一 walk 命中 Walking Backwards，实测会把它
            // 校正到 180°，接着真正的后退片段就被判为同档同向【重复丢弃】——
            // 走档因此没有正前方片段，而这件事不报任何错。
            //
            // 修法：把工程里**实际存在的文件名**放在候选链最前面，让精确匹配
            // 稳定命中；后面的模糊候选保留，作为换素材时的兼容。
            var idle = Pick(byName, "maria wprop j j ong@idle",
                            "breathing idle", "standing idle", "idle");
            var walk = Pick(byName, "maria wprop j j ong@walking",
                            "great sword walk", "walking", "walk");
            var run  = Pick(byName, "maria wprop j j ong@running",
                            "great sword run", "running", "run");
            if (idle == null || walk == null || run == null) { Valid = false; return; }
            // 临战架势有两套：持剑（Great Sword Idle）与空手（Fighting Idle）。
            // 收刀之后仍端着持剑架势，人会显得手里凭空还握着什么。
            var combatIdle = Pick(byName, "great sword idle", "fighting idle", "combat idle", "sword and shield idle") ?? idle;
            var combatIdleUnarmed = Pick(byName, "fighting idle", "combat idle") ?? combatIdle;

            // 解析招式片段；目录中未被映射的片段也全部接入（动作库预览可逐个试播）
            // ---- 方向移动片段先收集：它们要从动作层里【排除】掉 ----
            // 这一步此前排在 actionList 之后，于是每条方向片段都被连接了**两次**：
            // 一次进移动混合器（正经用途），一次又被下面"未映射片段全部接入"
            // 那一轮塞进动作混合器（永远不会被播到）。
            // 动作库从 43 条涨到 84 条之后，白连的那份从十来个变成二十来个，
            // 每个角色的 AnimationClipPlayable 总数因此逼近 105——而场上同时有
            // 玩家 + 若干敌人 + 路人，每一帧都要过这些 playable。
            // 掉帧本身就会放大所有"单帧位移过大"的问题（见 CharacterMotion.StepMove），
            // 所以这里省下的不只是动画开销。
            var dirDefs = CollectDirectional(byName, walk, run);
            var locoOnly = new HashSet<AnimationClip> { idle, combatIdle, combatIdleUnarmed };
            foreach (var d in dirDefs) locoOnly.Add(d.clip);

            var actionList =
                new List<(PoseState? pose, AnimationClip clip, float speed, bool hold, float start, float end)>();
            var connected = new HashSet<AnimationClip>();
            foreach (var m in ActionMap)
            {
                if (m.pool)
                {
                    // 变体池：keys 里每一条都接进来（已被别的招式占用的片段跳过），
                    // 播放时轮换（见 NextVariant）
                    foreach (var k in m.keys)
                    {
                        var v = Pick(byName, k);
                        if (v == null || connected.Contains(v)) continue;
                        actionList.Add((m.pose, v, m.speed, m.hold, m.start, m.end));
                        connected.Add(v);
                    }
                    continue;
                }
                var clip = Pick(byName, m.keys);
                if (clip != null)
                {
                    actionList.Add((m.pose, clip, m.speed, m.hold, m.start, m.end));
                    connected.Add(clip);
                }
            }
            // 空手替身：与持剑版本挂在同一个姿态上，但走单独的索引（见 NextVariant）
            int unarmedFrom = actionList.Count;
            var unarmedPose = new List<PoseState>();
            foreach (var m in UnarmedMap)
            {
                var uc = Pick(byName, m.keys);
                if (uc == null || connected.Contains(uc)) continue;
                actionList.Add((null, uc, m.speed, m.hold, m.start, m.end));
                connected.Add(uc);
                unarmedPose.Add(m.pose);
            }

            // 未映射到招式的片段也全部接入（休息动作、拔刀收刀、动作库预览按名字播）。
            // 按【片段对象】去重：同一片段可能同时以内部名和文件名两个键存在于 byName，
            // 不去重会给同一个片段开两个混合器输入口。
            var listed = new HashSet<AnimationClip>(connected);
            foreach (var kv in byName)
                if (!locoOnly.Contains(kv.Value) && listed.Add(kv.Value))
                    actionList.Add(((PoseState?)null, kv.Value, 1f, false, 0f, 1f));

            // ---- 方向移动片段：收集 + 实测每一条的行进方向与自然速度 ----
            // 必须在**建图之前**做：实测走的是 clip.SampleAnimation，它直接往骨骼上写姿态，
            // 而图一旦接上 Animator 输出就开始争夺同一批骨骼的写入权。先量完再建图，
            // 两者不重叠。
            _graph = PlayableGraph.Create("CharAnim_" + (_graphSerial++));
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);   // 手动推进，配合 timeScale/顿帧
            var output = AnimationPlayableOutput.Create(_graph, "out", _animator);

            _actionCount = actionList.Count;
            _actions = AnimationMixerPlayable.Create(_graph, Mathf.Max(1, _actionCount));
            _actionLen = new float[Mathf.Max(1, _actionCount)];
            _actionSpeed = new float[Mathf.Max(1, _actionCount)];
            _actionHold = new bool[Mathf.Max(1, _actionCount)];
            _actionStart = new float[Mathf.Max(1, _actionCount)];
            _actionEnd = new float[Mathf.Max(1, _actionCount)];
            _actionRawLen = new float[Mathf.Max(1, _actionCount)];
            _actionName = new string[Mathf.Max(1, _actionCount)];   // 调试叠层用
            var clipToInput = new Dictionary<AnimationClip, int>();
            for (int i = 0; i < _actionCount; i++)
            {
                var (pose, clip, speed, hold, start, end) = actionList[i];
                var cp = AnimationClipPlayable.Create(_graph, clip);
                cp.SetApplyFootIK(false);
                cp.SetDuration(clip.length);
                cp.SetTime(clip.length);
                cp.SetSpeed(speed);
                _graph.Connect(cp, 0, _actions, i);
                _actions.SetInputWeight(i, 0f);
                if (pose.HasValue)
                {
                    if (!_actionIndex.ContainsKey(pose.Value)) _actionIndex[pose.Value] = i;
                    if (!_actionVariants.TryGetValue(pose.Value, out var vs))
                        _actionVariants[pose.Value] = vs = new List<int>();
                    vs.Add(i);
                }
                string ck = Norm(clip.name);
                if (!_clipIndex.ContainsKey(ck)) _clipIndex[ck] = i;
                if (!clipToInput.ContainsKey(clip)) clipToInput[clip] = i;
                _actionStart[i] = Mathf.Clamp01(start);
                _actionEnd[i] = Mathf.Clamp(end, _actionStart[i] + 0.1f, 1f);
                _actionLen[i] = Mathf.Max(0.05f,
                    clip.length * (_actionEnd[i] - _actionStart[i]) / Mathf.Max(0.05f, speed));
                _actionSpeed[i] = speed;
                _actionHold[i] = hold;
                _actionRawLen[i] = clip.length;
                _actionName[i] = clip.name;
            }

            for (int u = 0; u < unarmedPose.Count; u++) _unarmedIndex[unarmedPose[u]] = unarmedFrom + u;

            // 按名字播（PlayNamed/HasClip）的索引再按 byName 补一遍：
            // 没有命名 .meta 的 FBX，clip.name 一律是 "mixamo.com"，上面那一轮
            // 只能登记到第一条——补这一遍之后，它们用**文件名**也能播了。
            foreach (var kv in byName)
                if (!_clipIndex.ContainsKey(kv.Key) && clipToInput.TryGetValue(kv.Value, out int ci))
                    _clipIndex[kv.Key] = ci;

            for (int r = 0; r < _rings.Length; r++) _rings[r] = new List<int>();
            // 档位自然速度表要先给出**兜底值并把"最正前"距离置为无穷**，
            // 否则 fwdness < _tierNatFwd[t] 永远不成立，整张表留在 0——
            // 而 0 会让下面的插值把所有速度都判给最慢一档，不报任何错。
            // 兜底值按"走 / 慢跑 / 跑 / 冲刺"的常识梯度给，实测到就覆盖掉。
            for (int t = 0; t < TierCount; t++)
            {
                _tierNatFwd[t] = float.MaxValue;
                _tierNat[t] = WalkNaturalSpeed *
                    (t == 0 ? 1f : t == 1 ? 1.45f : t == 2 ? 2.6f : 3.1f);
            }
            _loco = AnimationMixerPlayable.Create(_graph, DirBase + dirDefs.Count);
            ConnectLoco(idle, 0); ConnectLoco(combatIdle, 1); ConnectLoco(combatIdleUnarmed, 2);
            for (int i = 0; i < dirDefs.Count; i++)
            {
                var d = dirDefs[i];
                var cp = ConnectLoco(d.clip, DirBase + i);
                cp.SetSpeed(0f);   // 时间由 Tick 手动推（共享相位 + 步幅同步）
                _dirs.Add(new LocoDir
                {
                    cp = cp, angle = d.angle, tier = d.tier,
                    natSpeed = d.natSpeed, len = Mathf.Max(0.05f, d.clip.length),
                    name = d.clip.name
                });
                _rings[Mathf.Clamp(d.tier, 0, CrouchTier)].Add(i);
                // 每档的"代表自然速度"取该档【最接近正前方】那条片段的实测值：
                // 档位插值必须按片段自己的速度来分段（见 Tick 里的说明）。
                if (d.tier >= 0 && d.tier < TierCount)
                {
                    float fwdness = Mathf.Abs(Mathf.DeltaAngle(d.angle, 0f));
                    if (fwdness < _tierNatFwd[d.tier])
                    {
                        _tierNatFwd[d.tier] = fwdness;
                        _tierNat[d.tier] = Mathf.Max(0.3f, d.natSpeed);
                    }
                }
            }
            _dirW = new float[_dirs.Count];
            foreach (var r in _rings) r.Sort((a, b) => _dirs[a].angle.CompareTo(_dirs[b].angle));
            HasDirectionalSet = CountDistinctDirections() >= 3;
            _loco.SetInputWeight(0, 1f);

            _top = AnimationMixerPlayable.Create(_graph, 2);
            _graph.Connect(_loco, 0, _top, 0);
            _graph.Connect(_actions, 0, _top, 1);
            _top.SetInputWeight(0, 1f);
            _top.SetInputWeight(1, 0f);

            output.SetSourcePlayable(_top);
            Valid = true;
        }

        struct DirDef { public AnimationClip clip; public float angle; public int tier; public float natSpeed; }

        /// <summary>
        /// 方向移动片段的候选表。
        ///
        /// 【角度怎么定】四个正方向（前/后/左/右）的片段名是**无歧义的**，直接按名字给角度；
        /// 斜向片段（Jog Forward/Backward Diagonal）的朝向各家素材不一样——是左前还是右前，
        /// 光看名字**猜不出来**，猜错的后果是"推右前方却播了个左前方的片段"，比不用还糟。
        /// 所以斜向片段一律**实测**：真去采样片段里髋骨走了哪个方向，测到才用，测不到就不用
        /// （那两个方向由相邻的正方向片段混出来，本来也够用）。
        ///
        /// 正方向片段也会被实测**校正**：万一某个文件名与内容对不上，以实测为准。
        /// </summary>
        List<DirDef> CollectDirectional(Dictionary<string, AnimationClip> byName,
            AnimationClip walk, AnimationClip run)
        {
            var list = new List<DirDef>();
            Transform root = _animator != null ? _animator.transform : null;
            Transform hips = FindHips(root);

            void Add(AnimationClip clip, float nameAngle, int tier, bool requireMeasured)
            {
                if (clip == null) return;
                foreach (var d in list) if (d.clip == clip) return;   // 同一片段不重复接入
                float angle = nameAngle;
                // 实测失败时的兜底自然速度：蹲行远慢于走，套用跑档会把播放速率
                // 压到下限（人在挪、脚在爬），比不做步幅同步还糟
                float nat = tier == 0 ? WalkNaturalSpeed
                          : tier == CrouchTier ? WalkNaturalSpeed * 0.6f
                          : RunNaturalSpeed;
                bool measured = MeasureClipMotion(clip, root, hips, out float mA, out float mS);
                if (measured) { angle = mA; nat = mS; }
                else if (requireMeasured) return;   // 斜向片段测不出方向就不用（见方法注释）
                // 同一档、同一方向已经有片段了就跳过。动作库里难免有等价片段
                //（Running 与 Run Forward、Jog Backward 与 Slow Jog Backwards），
                // 两条都塞进同一圈会让 Bracket 拿到跨度为 0 的相邻对，插值出 NaN 权重。
                // 先来的优先——候选表的顺序就是取舍顺序。
                foreach (var d in list)
                    if (d.tier == tier && Mathf.Abs(Mathf.DeltaAngle(d.angle, angle)) < 20f)
                    {
                        // 丢弃要留痕。这里静默丢掉的可能是"两个文件名说左右、实测却
                        // 在同一侧"——那不是重复片段，是**少了一个方向**，而它在
                        // 画面上只表现为"某个方向的步法不对"，不会报任何错。
                        _dropped.Add(string.Format("{0}（{1} {2:F0}° 已由 {3} 占据）",
                            clip.name, TierName(tier), angle, d.clip.name));
                        return;
                    }
                list.Add(new DirDef { clip = clip, angle = angle, tier = tier, natSpeed = nat });
            }

            // 档 0「走」：前 / 后 / 左 / 右
            Add(walk, 0f, 0, false);
            Add(PickFile(byName, "walking backwards", "Walking Backwards"), 180f, 0, false);
            Add(PickFile(byName, "left strafe walking", "Left Strafe Walking"), -90f, 0, false);
            Add(PickFile(byName, "right strafe walking", "Right Strafe Walking"), 90f, 0, false);

            // 档 1「慢跑」：八方向最全的一档（含两条实测斜向）
            Add(PickFile(byName, "jog forward", "Jog Forward"), 0f, 1, false);
            Add(PickFile(byName, "jog backward", "Jog Backward"), 180f, 1, false);
            Add(PickFile(byName, "jog strafe left", "Jog Strafe Left"), -90f, 1, false);
            Add(PickFile(byName, "jog strafe right", "Jog Strafe Right"), 90f, 1, false);
            Add(PickFile(byName, "jog forward diagonal", "Jog Forward Diagonal"), 45f, 1, true);
            Add(PickFile(byName, "jog backward diagonal", "Jog Backward Diagonal"), 135f, 1, true);
            Add(PickFile(byName, "slow jog backwards", "Slow Jog Backwards"), 180f, 1, false);

            // 档 2「跑」：前 / 后 / 左 / 右
            Add(run, 0f, 2, false);
            Add(PickFile(byName, "run forward", "Run Forward"), 0f, 2, false);
            Add(PickFile(byName, "running backward", "Running Backward"), 180f, 2, false);
            Add(PickFile(byName, "left strafe", "Left Strafe"), -90f, 2, false);
            Add(PickFile(byName, "right strafe", "Right Strafe"), 90f, 2, false);

            // 档 3「冲刺」：只有正前方有片段——其余方向由下一档兜底（见 BlendDirectional）。
            // 这与大作一致：冲刺本来就只有向前，横着冲刺不存在。
            Add(PickFile(byName, "fast run", "Fast Run"), 0f, 3, false);

            // 蹲伏圈：后退 / 左右潜行（没有蹲行前进的片段，正前方交给走档，
            // 蹲姿由 HumanoidAnimator 的蹲伏压低叠加表达）
            Add(PickFile(byName, "crouch walk back", "Crouch Walk Back"), 180f, CrouchTier, false);
            Add(PickFile(byName, "crouched sneaking left", "Crouched Sneaking Left"), -90f, CrouchTier, false);
            Add(PickFile(byName, "crouched sneaking right", "Crouched Sneaking Right"), 90f, CrouchTier, false);
            return list;
        }

        /// <summary>
        /// 按名字取片段；名字取不到就**按文件路径**取。
        ///
        /// 后一条是必须的：Mixamo 导出的 FBX 里，take 一律叫 "mixamo.com"，
        /// 只有在 .meta 里显式改名才会变成 "Walking Backwards"。新丢进目录的
        /// FBX 如果还没有 .meta，按名字一个都找不到（上面 Build 里那句
        /// `k != "mixamo.com"` 就是被这件事逼出来的）。而按路径取的是
        /// **文件里的第一个 AnimationClip**，与它内部叫什么无关，所以一定拿得到。
        /// </summary>
        AnimationClip PickFile(Dictionary<string, AnimationClip> byName, string key, string file)
        {
            if (byName.TryGetValue(Norm(key), out var c) && c != null) return c;
            var byPath = Resources.Load<AnimationClip>(_folder + "/" + file);
            if (byPath != null) return byPath;
            return Resources.Load<AnimationClip>(ExtraFolder + "/" + file);
        }

        /// <summary>
        /// 实测一个移动片段的行进方向与自然速度：采样片段的首尾两帧，量髋骨在
        /// 模型局部空间里走了多远、朝哪走。这样接入的方向表是**片段自己说了算**的，
        /// 不依赖文件名的措辞，也不依赖素材商的左右习惯。
        /// 原地片段（位移几乎为零）返回 false。
        /// </summary>
        // 实测结果按【片段】缓存：方向与"每秒走多少个模型单位"是片段自己的属性，
        // 与是哪个角色在用无关。不缓存的话，每生成一个敌人就要把整套移动片段
        // 重新采样一遍（每次两帧 × 十来个片段），群战刷怪时会看到明显的卡顿。
        // 只有世界尺度要按角色的缩放另算，所以缓存里存的是**局部空间**的位移。
        struct ClipMotion { public bool ok; public float angle; public float localDist; public float dur; }
        static readonly Dictionary<AnimationClip, ClipMotion> _motionCache =
            new Dictionary<AnimationClip, ClipMotion>();

        static bool MeasureClipMotion(AnimationClip clip, Transform root, Transform hips,
            out float angleDeg, out float natSpeed)
        {
            angleDeg = 0f; natSpeed = 0f;
            if (clip == null || root == null || hips == null || clip.length < 0.1f) return false;

            if (!_motionCache.TryGetValue(clip, out var m))
            {
                m = new ClipMotion();
                try
                {
                    float t1 = clip.length * 0.98f;
                    clip.SampleAnimation(root.gameObject, 0f);
                    Vector3 a = root.InverseTransformPoint(hips.position);
                    clip.SampleAnimation(root.gameObject, t1);
                    Vector3 b = root.InverseTransformPoint(hips.position);
                    Vector3 d = b - a; d.y = 0f;
                    m.localDist = d.magnitude;
                    m.dur = t1;
                    m.angle = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    // 局部位移 0.2 以下当作"原地片段"：Mixamo 的 in-place 版本位移就是零，
                    // 拿噪声去定方向只会把片段摆到一个随机角度上。
                    m.ok = m.localDist > 0.2f;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[PlayableAnimator] 片段方向实测失败（跳过）：" +
                                     clip.name + " — " + e.Message);
                    m.ok = false;
                }
                _motionCache[clip] = m;
            }

            if (!m.ok) return false;
            float scale = Mathf.Abs(root.lossyScale.x);
            if (scale < 1e-4f) scale = 1f;
            angleDeg = m.angle;
            natSpeed = m.localDist * scale / Mathf.Max(0.01f, m.dur);
            // 合理性校验：位移量是在**模型局部空间**里量的，而各家 FBX 的单位口径
            // 不一定是米（有的一单位等于一厘米）。量出个每秒几十米的"走路"，
            // 说明单位对不上——这时只信方向，速度退回按身高推算的常数。
            // 不做这道校验的话，步幅同步会把播放速率压到下限，
            // 人快速移动而脚在慢慢挪，滑得比不做同步还厉害。
            if (natSpeed < 0.3f || natSpeed > 12f) { natSpeed = 0f; return false; }
            return true;
        }

        static Transform FindHips(Transform root)
        {
            if (root == null) return null;
            Transform best = null; int bestLen = int.MaxValue;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string n = Norm(t.name);
                if ((!n.Contains("hips") && !n.Contains("pelvis")) || n.Length >= bestLen) continue;
                bestLen = n.Length; best = t;
            }
            return best;
        }

        int CountDistinctDirections()
        {
            int n = 0;
            var seen = new List<float>();
            foreach (var d in _dirs)
            {
                bool dup = false;
                foreach (float a in seen) if (Mathf.Abs(Mathf.DeltaAngle(a, d.angle)) < 25f) dup = true;
                if (dup) continue;
                seen.Add(d.angle); n++;
            }
            return n;
        }

        AnimationClipPlayable ConnectLoco(AnimationClip clip, int idx)
        {
            var cp = AnimationClipPlayable.Create(_graph, clip);
            // 不开 Foot IK：模型被 FitAndGround 缩放后 IK 目标与骨架比例不匹配，
            // 会把双脚持续向下/向内拽（站立"踮脚尖并腿"、跑步"脚朝向畸形"的根因）。
            // 纯 FK 原样播放 Mixamo 数据，所见即所得。
            cp.SetApplyFootIK(false);
            _graph.Connect(cp, 0, _loco, idx);
            return cp;
        }

        /// <summary>speed01=相对满速的比例；actualSpeed=真实移速 m/s（供步幅同步）；
        /// moveAngleDeg=行进方向相对角色正面（0=正前、±90=横移、180=后退）。</summary>
        public void SetLocomotion(float speed01, float actualSpeed = -1f, float moveAngleDeg = 0f,
            bool crouch = false, bool strafing = false)
        {
            _speed01 = Mathf.Clamp01(speed01);
            _actualSpeed = actualSpeed;
            _moveAngle = moveAngleDeg;
            _crouch = crouch;
            _strafing = strafing;
        }
        bool _crouch;
        /// <summary>此刻是不是"脸锁着目标、脚独立走位"——夹角为有意为之而非转身残差。</summary>
        bool _strafing;
        public void SetReady(bool ready) => _ready = ready;

        /// <summary>兵器此刻在不在手上（收刀后改用空手架势与空手替身片段）。</summary>
        public void SetArmed(bool armed) => _armed = armed;

        /// <summary>触发一次招式（有对应片段才生效，否则维持 locomotion）。
        /// targetDuration&gt;0 时按【招式帧数据】反推播放速度：这一招在游戏里占多久，
        /// 画面就在多久之内把发力窗打完——动作与判定从此对得上拍。</summary>
        public void PlayAction(PoseState p, float targetDuration = 0f)
        {
            if (!Valid) return;
            int idx = NextVariant(p);
            if (idx < 0) return;
            PlayIndex(idx, targetDuration);
        }

        /// <summary>这一招该播哪一条片段：有变体池就轮换，否则就是唯一那条。</summary>
        int NextVariant(PoseState p)
        {
            // 收刀状态优先用空手版本（见 UnarmedMap）
            if (!_armed && _unarmedIndex.TryGetValue(p, out int ua)) return ua;
            if (_actionVariants.TryGetValue(p, out var list) && list.Count > 1)
            {
                _variantCursor.TryGetValue(p, out int c);
                _variantCursor[p] = (c + 1) % list.Count;
                return list[c % list.Count];
            }
            return _actionIndex.TryGetValue(p, out int i) ? i : -1;
        }

        /// <summary>按片段名试播动作库中任一动作（测试面板的逐个动作预览）。</summary>
        public bool PlayClip(string clipName)
        {
            if (!Valid || !_clipIndex.TryGetValue(Norm(clipName), out int idx)) return false;
            PlayIndex(idx);
            return true;
        }

        /// <summary>动作库中全部片段名（预览面板动态生成按钮用）。</summary>
        public IEnumerable<string> ClipNames => _clipIndex.Keys;

        /// <summary>按关键词试播动作库中第一个匹配片段（如 "draw"/"sheath" 拔刀/收刀）。</summary>
        public bool PlayClipContaining(string key)
        {
            if (!Valid || string.IsNullOrEmpty(key)) return false;
            key = Norm(key);
            foreach (var kv in _clipIndex)
                if (kv.Key.Contains(key)) { PlayIndex(kv.Value); return true; }
            return false;
        }

        /// <summary>按关键词返回第一个匹配片段的有效时长（考虑起手偏移/倍速）；无匹配返回 0。</summary>
        public float ClipLengthContaining(string key)
        {
            if (!Valid || string.IsNullOrEmpty(key)) return 0f;
            key = Norm(key);
            foreach (var kv in _clipIndex)
                if (kv.Key.Contains(key)) return _actionLen[kv.Value];
            return 0f;
        }

        /// <summary>片段名（精确优先、再按包含）→ 动作层输入口；找不到返回 -1。</summary>
        int IndexOfClip(string key)
        {
            key = Norm(key);
            if (key.Length == 0) return -1;
            if (_clipIndex.TryGetValue(key, out int i)) return i;
            foreach (var kv in _clipIndex)
                if (kv.Key.Contains(key)) return kv.Value;
            return -1;
        }

        /// <summary>片段原始时长（秒，不掐头去尾、不算倍速）；无此片段返回 0。</summary>
        public float RawClipLength(string key)
        {
            if (!Valid) return 0f;
            int i = IndexOfClip(key);
            return i >= 0 ? _actionRawLen[i] : 0f;
        }

        /// <summary>动作库里是否有这个片段。</summary>
        public bool HasClip(string key) => Valid && IndexOfClip(key) >= 0;

        /// <summary>
        /// 按名字播任意片段：可倒放、可保持末帧、可指定倍速与淡入、可只播其中一段。
        ///
        /// 【为什么不走 PoseState】坐下、躺下、睡觉、起身都不是招式：没有判定窗、
        /// 没有发力窗裁剪、也不该在头顶弹招式名。它们要的恰恰是招式层不提供的东西——
        /// **完整播完**（不裁头尾）、**停在末帧**（坐着/躺着是持续状态）、
        /// **倒放**（从椅子上站起来 = 坐下反过来）、**长淡入**（4 秒的慢动作
        /// 用 0.07 秒硬切上来会"弹"一下）。所以单开这条通路。
        ///
        /// 返回本次播放时长（秒）；0 = 动作库里没有这个片段，上层自行兜底。
        /// </summary>
        public float PlayNamed(string key, bool reverse = false, bool hold = false,
            float speed = 1f, float fade = 0.3f, float start01 = 0f, float end01 = 1f)
        {
            if (!Valid) return 0f;
            int idx = IndexOfClip(key);
            if (idx < 0) return 0f;

            float raw = Mathf.Max(0.05f, _actionRawLen[idx]);
            start01 = Mathf.Clamp01(start01);
            end01 = Mathf.Clamp(end01, start01 + 0.02f, 1f);
            speed = Mathf.Max(0.05f, speed);

            for (int i = 0; i < _actionCount; i++) _actions.SetInputWeight(i, i == idx ? 1f : 0f);
            var cp = (AnimationClipPlayable)_actions.GetInput(idx);
            cp.SetSpeed(reverse ? -speed : speed);
            cp.SetTime(raw * (reverse ? end01 : start01));
            cp.SetDone(false);
            _cur = idx;
            _actionT = 0f;
            _playLen = raw * (end01 - start01) / speed;
            _playHold = hold;
            _fadeDur = Mathf.Max(0f, fade);
            _fadeFrom = _actionW;   // 连着上一段休息动作接下去，权重不掉回 0
            return _playLen;
        }

        void PlayIndex(int idx, float targetDuration = 0f)
        {
            for (int i = 0; i < _actionCount; i++) _actions.SetInputWeight(i, i == idx ? 1f : 0f);
            var cp = (AnimationClipPlayable)_actions.GetInput(idx);
            // 发力窗（掐头去尾后的片段时长）→ 按招式时长反推播放速度。
            // 下限是这一招的默认倍速（绝不比现在慢），上限 MaxDrivenSpeed（不糊成残影）。
            float window = _actionRawLen[idx] * (_actionEnd[idx] - _actionStart[idx]);
            float speed = _actionSpeed[idx];
            if (targetDuration > 0.02f && !_actionHold[idx] && window > 0.01f)
            {
                // 节拍再短，动画也按 MinShownMotion 给（短于此就看不成一个动作）
                float shown = Mathf.Max(targetDuration, MinShownMotion);
                speed = Mathf.Clamp(window / shown, speed, MaxDrivenSpeed);
            }
            cp.SetSpeed(speed);
            // 从起手偏移处起播：跳过片段开头缓慢的摆架势，按键即入发力挥击相位
            cp.SetTime(_actionRawLen[idx] * _actionStart[idx]);
            cp.SetDone(false);
            _cur = idx;
            _actionT = 0f;
            _playLen = _actionHold[idx] ? _actionLen[idx] : Mathf.Max(0.05f, window / speed);
            _playHold = _actionHold[idx];
            _fadeDur = 0f;          // 招式一律用短淡入（快、脆），与休息动作区分开
            _fadeFrom = _actionW;   // 连招接招：从当前权重继续淡入，不掉回 0（消除断档感）
        }

        /// <summary>
        /// 从倒地爬起来。
        ///
        /// 有专用的爬起片段（Standing Up：从地上撑起来）就播它——那是真人怎么
        /// 从地上站起来的动作数据。没有才退回【倒放倒地片段】那套合成手段：
        /// 倒放的方向是对的（腿先动、身体后立），但它终究是"倒着摔下去"，
        /// 手撑地、发力的重心转移都没有。
        /// </summary>
        public void PlayGetUp()
        {
            if (Valid && PlayNamed("standing up", false, false, 1.25f, 0.18f) > 0f) return;
            if (!Valid || !_actionIndex.TryGetValue(PoseState.Knockdown, out int idx))
            {
                StopAction();
                return;
            }
            for (int i = 0; i < _actionCount; i++) _actions.SetInputWeight(i, i == idx ? 1f : 0f);
            var cp = (AnimationClipPlayable)_actions.GetInput(idx);
            float clipLen = _actionRawLen[idx];                     // 原始片段时长
            const float getUpSpeed = 1.4f;                          // 起身比倒下利落
            cp.SetSpeed(-getUpSpeed);
            cp.SetTime(clipLen);
            cp.SetDone(false);
            _cur = idx;
            _actionT = 0f;
            _playLen = clipLen / getUpSpeed;
            _playHold = false;   // 播完（站起）即淡回移动层
            _fadeDur = 0f;
            _fadeFrom = Mathf.Max(_actionW, 0.9f);   // 从躺地姿态无缝续接，不闪回站立
        }

        /// <summary>结束保持型动作（倒地爬起/收架势），淡回移动层。</summary>
        public void StopAction()
        {
            _cur = -1;
        }

        /// <summary>
        /// 方向混合：按摇杆方向在相邻两个片段之间取权重，按速度在走档/跑档之间取权重，
        /// 全部片段共享同一个步态相位，播放速率按**主导片段自己的**自然速度算。
        /// </summary>
        void BlendDirectional(float[] tierW, float actual, float dt)
        {
            if (_dirs.Count == 0) return;
            for (int i = 0; i < _dirW.Length; i++) _dirW[i] = 0f;

            // 方向平滑：摇杆方向可以瞬变，但脚不能——90° 的方向切换给 ≈0.12 秒过渡，
            // 否则贴身绕圈时会看到左右步法来回"啪啪"硬切。
            // 用 MoveTowardsAngle：普通 MoveTowards 不懂 ±180 的回绕，
            // 从 170° 转到 -170°（只差 20°）会被它当成 340° 一路扫过去。
            // 方向平滑用**非对称**速率：偏离正前方要慢，回到正前方要快。
            //
            // 为什么必须非对称：非锁定时身体本来就会转向行进方向，转身那零点几秒里
            // 行进夹角会从 0 冲到 ±90°（甚至 180°）再退回 0。用对称速率的话，
            // 腿会先甩出一个侧步/倒步再弹回来——那就是"掉头时闪一下侧步片段"，
            // 也是当初把夹角硬写成 0 的原因（而那个做法让 13 条方向片段永远用不上）。
            //
            // 非对称之后两边都对：
            //   · **持续**的横移/后撤（锁定绕圈、贴身走位）夹角一直大 ⇒ 攒够时间，
            //     完整切到侧步/倒步片段；
            //   · **瞬时**的转身残差只存在 0.2s 左右 ⇒ 按 240°/s 只推进到 ≈48°，
            //     腿上只带一点点侧步的意思就回正了，读作"转身时垫了一步"，不是闪。
            // 回正给 720°/s：残差一消失就立刻回到正前方，不留尾巴。
            // 【锁定态例外】慢速 commit 是为了滤掉"转身残差"，而锁定时身体压根不会
            // 转向行进方向——夹角是玩家主动要的，而且会一直保持。此时还慢慢攒，
            // 意味着往后拉杆之后要等 0.75 秒腿才切成倒步，前面那大半秒人在后退、
            // 腿却在向前走。锁定时按"释放"那档的速率直接切满。
            float wantAbs = Mathf.Abs(Mathf.DeltaAngle(0f, _moveAngle));
            float haveAbs = Mathf.Abs(_blendAngle);
            float slew = (!_strafing && wantAbs > haveAbs)
                ? BlendCommitDegPerSec : BlendReleaseDegPerSec;
            _blendAngle = Mathf.DeltaAngle(0f,
                Mathf.MoveTowardsAngle(_blendAngle, _moveAngle, slew * dt));

            // 蹲伏：整套权重交给蹲伏圈。它只有后/左/右三条，正前方由它覆盖不了——
            // 那一份会在下面的"未覆盖转交"里落回走档（配合蹲伏压低叠加，
            // 读作"猫着腰往前走"，而不是站起来走）。
            if (_crouch && _rings[CrouchTier].Count > 0)
            {
                if (RingCovers(_rings[CrouchTier], _blendAngle))
                    AddRing(_rings[CrouchTier], _blendAngle, 1f);
                else
                    AddRing(_rings[0], _blendAngle, 1f);
                LeadAndPhase(actual, dt);
                ApplyDirWeights();
                return;
            }

            // 某一档在这个方向上没有片段（比如冲刺档只有正前方）：把它那一份交给
            // **最近的、覆盖得了的那一档**，由步幅同步按真实移速提速/降速播。
            // 硬套一个方向不对的片段远比换一档难看。
            for (int t = 0; t < TierCount; t++)
            {
                float w = tierW[t];
                if (w <= 0.0001f) continue;
                int use = NearestCoveringTier(t);
                AddRing(_rings[use], _blendAngle, w);
            }

            LeadAndPhase(actual, dt);
            ApplyDirWeights();
        }

        /// <summary>这一档在该方向上没片段时，改用最近的、覆盖得了的那一档。
        /// 一档都不覆盖时退回档 0——前进片段是 Build 的硬性前提，那一圈必定非空。</summary>
        int NearestCoveringTier(int tier)
        {
            if (RingCovers(_rings[tier], _blendAngle)) return tier;
            for (int d = 1; d < TierCount; d++)
            {
                int lo = tier - d, hi = tier + d;
                if (lo >= 0 && RingCovers(_rings[lo], _blendAngle)) return lo;
                if (hi < TierCount && RingCovers(_rings[hi], _blendAngle)) return hi;
            }
            return 0;
        }

        /// <summary>
        /// 步态相位推进。
        ///
        /// 【为什么不能用"权重最大的那一条"来算】
        /// 相位速率 = rate / len，而 rate = 真实移速 / 该片段的自然速度。
        /// 两条不同档的片段（走 nat=1.75 len=1.03、慢跑 nat=2.52 len=0.83）
        /// 在档位交界处权重各约 50%，此时"谁是最大"由浮点噪声决定——
        /// 而两者算出的相位速率差着 17%。于是每帧在两个速率之间来回跳，
        /// 腿一会儿快一会儿慢，读作**抖动/卡顿**。档位从 2 档细分到 4 档之后
        /// 交界点变成 4 个，这个抖动就从偶发变成常态。
        ///
        /// 正确做法是**按权重加权平均**：混合里每条片段各自算自己的相位速率，
        /// 再按它在混合中的份额加权。权重连续变化 ⇒ 相位速率连续变化，
        /// 没有"谁当选"这回事，也就没有跳变。
        /// </summary>
        void LeadAndPhase(float actual, float dt)
        {
            float wSum = 0f, rateSum = 0f;
            _slipW = 0f; _slipClip = null;
            for (int i = 0; i < _dirW.Length; i++)
            {
                float w = _dirW[i];
                if (w <= 0.001f) continue;
                var d = _dirs[i];
                float nat = Mathf.Max(0.3f, d.natSpeed);
                // 上限 2.0：侧移/后退的自然速度低于前进，真实移速比它快时步频得跟着提。
                // 再高就成了小碎步快放，不如让它稍微滑一点。
                float rWant = actual / nat;
                // ===== 下限必须是 0，不能是 0.5——录屏把这一条钉死了 =====
                // 实机读数：「步幅 0.95m/步（应2.06）×0.46 ⚠滑 Walking 需0.33×得0.50×」
                // 地面只要 0.33 倍的步频，腿却被强制转 0.50 倍——**腿比地面快五成**，
                // 这就是"起步会滑动"。步幅同步的定义就是 r = actual / nat，
                // 任何夹持都是在破坏它，而下限根本没有存在的理由：
                // 慢就该慢，慢到极低时待机混合（idleTot）本来就接管了。
                // 上限 2.0 保留：那一条防的是"小碎步快放"，是可读性问题，不是同步问题。
                float r = Mathf.Clamp(rWant, 0f, 2.0f);
                // 夹到边界＝**这一帧的步频没法跟上地面**，脚必然打滑：
                // 撞上限（腿转不够快）读作"大步往前飘"，撞下限（腿转太快）读作"太空步"。
                // 记下来打到屏幕上——"看上去在漂移"就此变成一个可读的数。
                if (w > _slipW && !Mathf.Approximately(r, rWant))
                { _slipW = w; _slipClip = d.name; _slipWant = rWant; _slipGot = r; }
                // 该片段的方向与实际行进方向差 120° 以上 = 这一圈里没有能表达该方向的
                // 片段（动作库不全时的兜底）：把它倒着播，步态每帧都对，只是时间轴反过来。
                float sgn = Mathf.Abs(Mathf.DeltaAngle(d.angle, _blendAngle)) > 120f ? -1f : 1f;
                rateSum += w * r * sgn / Mathf.Max(0.05f, d.len);
                wSum += w;
            }
            if (wSum > 0.001f)
            {
                DbgPhaseRate = rateSum / wSum;          // 步态周期数/秒（诊断用，只读）
                _phase01 = Mathf.Repeat(_phase01 + dt * DbgPhaseRate, 1f);
            }
            else DbgPhaseRate = 0f;
        }

        /// <summary>诊断：共享步态相位的推进速率（周期/秒）。腿在不在按移速倒腾，看它。</summary>
        public float DbgPhaseRate { get; private set; }

        float _slipW, _slipWant, _slipGot;
        string _slipClip;

        /// <summary>
        /// 诊断：这一帧【每一步实际跨了多远】，以及它跟片段自带步幅差多少。
        ///
        /// "一步一个脚印" 是可以算的：
        ///     每步距离 = 实际速度 ÷ 步频        （步频就是 DbgPhaseRate，周期/秒）
        ///     片段自带 = 片段自然速度 × 片段时长
        /// 两者的比值就是脚在地面上滑了多少。1.00 = 脚踩实；1.30 = 每一步比腿
        /// 能迈的多出三成，屏幕上就是"大步往前飘"；0.80 = 太空步。
        /// 后面那半句只在播放速率被 [0.5, 2.0] 夹住时才会有内容——夹住即代表
        /// 步频已经跟不上地面了，那是打滑的**充分条件**，一眼可判。
        /// </summary>
        public string DbgStride(float actual)
        {
            if (!Valid || DbgPhaseRate <= 0.001f) return "步幅 —";
            float per = actual / DbgPhaseRate;
            // 【上一版这个"应"取的是主导片段一条的步幅，读数是错的】
            // 录屏里读到 ×1.15，我差点当成"脚滑了 15%"。实际上混合中的脚在局部
            // 空间里走的是**各片段步幅的加权平均**（算术平均），而 actual/步频
            // 给出的是调和平均——两条片段步幅差得远时，这两个平均本来就差一截。
            // 拿其中一条去当基准，等于凭空造出一个不存在的偏差。
            // 正确的基准是加权平均：Σ wᵢ·(natᵢ×lenᵢ)。
            // 实测代入（Jog Forward 2.40×0.62 + Running 3.70×0.38 = 2.89
            // 对 actual/步频 = 2.77）真实打滑只有 4%，不是 15%。
            float wsum = 0f, natStride = 0f;
            for (int i = 0; i < _dirW.Length; i++)
            {
                float w = _dirW[i];
                if (w <= 0.001f) continue;
                var di = _dirs[i];
                natStride += w * Mathf.Max(0.05f, di.natSpeed) * Mathf.Max(0.05f, di.len);
                wsum += w;
            }
            if (wsum <= 0.001f) return "步幅 —";
            natStride = Mathf.Max(0.05f, natStride / wsum);
            string tail = _slipClip != null
                ? string.Format("  ⚠滑 {0} 需{1:F2}×得{2:F2}×", _slipClip, _slipWant, _slipGot)
                : "";
            return string.Format("步幅 {0:F2}m/步（应{1:F2}）×{2:F2}{3}",
                per, natStride, per / natStride, tail);
        }

        /// <summary>
        /// 调试用：此刻**画面上真正在播的东西**——动作层片段名与权重，
        /// 以及移动层里权重最高的两条方向片段。
        ///
        /// "横移/后退的动画放进去了却看不到效果"这类问题，光看代码永远说不清：
        /// 片段可能没接上、可能接上了但权重恒为 0、也可能被动作层整个盖住。
        /// 把这三件事同时打在屏幕上，一眼就能分辨是哪一种。
        /// </summary>
        public string DbgNowPlaying()
        {
            if (!Valid) return "—";
            var sb = new System.Text.StringBuilder(96);
            if (_cur >= 0 && _actionW > 0.01f &&
                _actionName != null && _cur < _actionName.Length)
                sb.Append("动作 ").Append(_actionName[_cur])
                  .Append(' ').Append(_actionW.ToString("F2")).Append("  ");
            else sb.Append("动作 —  ");

            int a1 = -1, a2 = -1;
            for (int i = 0; i < _dirW.Length; i++)
            {
                if (a1 < 0 || _dirW[i] > _dirW[a1]) { a2 = a1; a1 = i; }
                else if (a2 < 0 || _dirW[i] > _dirW[a2]) a2 = i;
            }
            sb.Append("移动 ");
            if (a1 >= 0 && _dirW[a1] > 0.01f)
                sb.Append(_dirs[a1].name).Append(' ').Append(_dirW[a1].ToString("F2"));
            if (a2 >= 0 && _dirW[a2] > 0.01f)
                sb.Append(" + ").Append(_dirs[a2].name).Append(' ')
                  .Append(_dirW[a2].ToString("F2"));
            if (a1 < 0 || _dirW[a1] <= 0.01f) sb.Append("待机");
            return sb.ToString();
        }
        /// <summary>
        /// 这个行进夹角上，动作库里【最快的那条片段】的自然速度（m/s）；没有片段则返回 0。
        ///
        /// 给横移封顶用。横移速度必须 ≤ 该方向片段能撑起来的速度，否则脚打滑；
        /// 但也不该比它慢——慢了就是白白把玩家钉死在走路速度上。
        /// 此前那张封顶表是**手写死的**（walkSpeed×1.45 / ×1.0 / ×0.8），
        /// 和库里到底有什么片段完全无关：库里明明有 Left/Right Strafe（跑档横移）
        /// 和 Running Backward（跑档后退），封顶却照旧按走档给，于是一进战斗
        /// 就整整慢一倍。改成按实测自然速度反推——库里加了更快的片段，
        /// 封顶自动跟着放开，不用再回来改一张表。
        /// </summary>
        public float NaturalSpeedAt(float angle)
        {
            float best = 0f;
            for (int i = 0; i < _dirs.Count; i++)
            {
                var d = _dirs[i];
                if (d.tier == CrouchTier) continue;                 // 蹲伏不参与
                if (Mathf.Abs(Mathf.DeltaAngle(d.angle, angle)) > 50f) continue;
                if (d.natSpeed > best) best = d.natSpeed;
            }
            return best;
        }

        // ===== 结构化诊断（给调试日志用）=====
        // HUD 那几行是给人眼看的拼接字符串；日志要的是**可解析的列**，
        // 否则每次都得靠正则从中文里抠数字。同一份数据，两种出口。
        public string DbgActionName =>
            _cur >= 0 && _actionName != null && _cur < _actionName.Length ? _actionName[_cur] : "";
        public float DbgActionW => _actionW;
        public string DbgSlipClip => _slipClip ?? "";
        public float DbgSlipWant => _slipWant;
        public float DbgSlipGot => _slipGot;

        /// <summary>权重最高的两条方向片段（名字 + 权重）。</summary>
        public void DbgTopDirs(out string n1, out float w1, out string n2, out float w2)
        {
            n1 = ""; w1 = 0f; n2 = ""; w2 = 0f;
            if (!Valid || _dirW == null) return;
            int a = -1, b = -1;
            for (int i = 0; i < _dirW.Length; i++)
            {
                if (a < 0 || _dirW[i] > _dirW[a]) { b = a; a = i; }
                else if (b < 0 || _dirW[i] > _dirW[b]) b = i;
            }
            if (a >= 0 && _dirW[a] > 0.005f) { n1 = _dirs[a].name; w1 = _dirW[a]; }
            if (b >= 0 && _dirW[b] > 0.005f) { n2 = _dirs[b].name; w2 = _dirW[b]; }
        }

        /// <summary>这一帧实际每个步态周期走了多远（米）。</summary>
        public float DbgStrideActual(float actual) =>
            DbgPhaseRate > 0.001f ? actual / DbgPhaseRate : 0f;

        /// <summary>混合中各片段自带步幅的加权平均（米）——脚"应该"走多远。</summary>
        public float DbgStrideWant()
        {
            if (!Valid || _dirW == null) return 0f;
            float w = 0f, s2 = 0f;
            for (int i = 0; i < _dirW.Length; i++)
            {
                if (_dirW[i] <= 0.001f) continue;
                var d = _dirs[i];
                s2 += _dirW[i] * Mathf.Max(0.05f, d.natSpeed) * Mathf.Max(0.05f, d.len);
                w += _dirW[i];
            }
            return w > 0.001f ? s2 / w : 0f;
        }

        /// <summary>诊断：方向混合实际落在的角度（度）。恒 ≈0 就说明方向片段全没用上。</summary>
        public float DbgBlendAngle => _blendAngle;

        /// <summary>把算好的方向权重与共享相位写进混合器。</summary>
        void ApplyDirWeights()
        {
            for (int i = 0; i < _dirs.Count; i++)
            {
                var d = _dirs[i];
                if (!d.cp.IsValid()) continue;
                _loco.SetInputWeight(DirBase + i, _dirW[i]);
                // 共享归一化相位：每个片段都停在自己周期里的同一个百分比上
                if (_dirW[i] > 0.001f) d.cp.SetTime(_phase01 * d.len);
            }
        }

        float _blendAngle;
        /// <summary>朝"偏离正前方"的方向切换的速率：慢，只有持续的横移才攒得满。</summary>
        const float BlendCommitDegPerSec = 240f;
        /// <summary>朝"回到正前方"的方向切换的速率：快，转身残差一消失就回正。</summary>
        const float BlendReleaseDegPerSec = 720f;

        /// <summary>找出夹住该方向的前后两条片段（按角度排好序，环形回绕）。</summary>
        bool Bracket(List<int> ring, float angle, out int a, out int b, out float t, out float span)
        {
            a = b = -1; t = 0f; span = 360f;
            if (ring.Count == 0) return false;
            if (ring.Count == 1) { a = b = ring[0]; span = 0f; return true; }

            int nextIdx = -1;
            for (int k = 0; k < ring.Count; k++)
                if (_dirs[ring[k]].angle > angle) { nextIdx = k; break; }
            if (nextIdx < 0) nextIdx = 0;                       // 绕回第一条
            int prevIdx = (nextIdx - 1 + ring.Count) % ring.Count;

            a = ring[prevIdx]; b = ring[nextIdx];
            span = Mathf.DeltaAngle(_dirs[a].angle, _dirs[b].angle);
            if (span <= 0f) span += 360f;                       // 环形回绕的那一段
            float off = Mathf.DeltaAngle(_dirs[a].angle, angle);
            if (off < 0f) off += 360f;
            t = span > 0.01f ? Mathf.Clamp01(off / span) : 0f;
            return true;
        }

        /// <summary>
        /// 这一圈在该方向上覆盖得够不够好。
        ///
        /// 判据是**夹住它的那两条片段离得远不远**，而不是"最近的一条有多近"。
        /// 举例：跑档只有 前(0°)/斜前(45°)/后(180°) 三条，此时正右方(90°)最近的一条
        /// 只差 45°，看着挺近——但真去插值是在 45° 与 180° 之间取中间，
        /// 混出来是个"斜着往后跑"的四不像。跨度太大就该把这一档让给覆盖更全的那一圈。
        /// </summary>
        bool RingCovers(List<int> ring, float angle)
        {
            if (!Bracket(ring, angle, out int a, out _, out _, out float span)) return false;
            if (ring.Count == 1)
                return Mathf.Abs(Mathf.DeltaAngle(_dirs[a].angle, angle)) <= 70f;
            return span <= 100f;
        }

        /// <summary>把一圈的权重按方向分给相邻的两个片段（环形线性插值）。</summary>
        void AddRing(List<int> ring, float angle, float weight)
        {
            if (weight <= 0.0001f) return;
            if (!Bracket(ring, angle, out int a, out int b, out float t, out _)) return;
            if (a == b) { _dirW[a] += weight; return; }
            _dirW[a] += weight * (1f - t);
            _dirW[b] += weight * t;
        }

        public void Tick(float dt)
        {
            if (!Valid) return;

            // ===== 速度 → 档位权重：按【片段实测自然速度】分段 =====
            //
            // 原来是把 speed01 均分成 TierCount 段（x = speed01 * 4，跨过一个整数
            // 换一档）。那个映射与片段毫无关系——speed01 的分母是 runSpeed(5.2)，
            // 一个纯玩法数值，而四档片段的实测自然速度是 1.75 / 2.52 / 4.65 / 5.53。
            //
            // 代入实机读数（实际 3.7m/s）：均分给出 15% 慢跑 + 85% 跑，
            // 也就是**主要用自然速度 4.65 的片段去表现 3.7 的移动**，
            // 播放速率 0.8 倍。脚不打滑（步幅同步是对的），但步幅仍是 4.65 那一档的
            // 步幅——真人降速时会同时缩短步幅与降低步频，我们只降了步频，
            // 于是"大步慢迈"，读作转一圈步数变少、人在飘。
            //
            // 算过一遍（步幅/步）：
            //     3.3m/s  均分 1.33m   按自然速度 1.22m   真人 ≈1.21m
            //     3.7m/s  均分 1.57m   按自然速度 1.34m   真人 ≈1.31m
            //     4.5m/s  均分 1.72m   按自然速度 1.65m   真人 ≈1.52m
            //     5.2m/s  均分 1.71m   按自然速度 1.72m   真人 ≈1.70m
            // 中速段超出 20%，而冲刺档正好对得上——这正是"非冲刺移动才有问题"。
            //
            // 改为按自然速度找**相邻的两档**线性插值：移速落在哪两条片段之间，
            // 就用那两条，播放速率因此始终贴近 1.0，步幅与步频一起随速度变化。
            for (int t = 0; t < TierCount; t++) _tierW[t] = 0f;
            float vNow = _actualSpeed >= 0f ? _actualSpeed : _speed01 * RunNaturalSpeed;
            // 【单调性是插值的前提】实测值来自素材，不能假定它天然递增：
            // 万一某档的正前片段测出来比下一档还快（素材放错/测量失败），
            // bracket 就会错乱，表现为某个速度段永远选不到正确的片段。
            // 就地夹一遍，代价只有几次比较。
            for (int m = 1; m < TierCount; m++)
                if (_tierNat[m] <= _tierNat[m - 1]) _tierNat[m] = _tierNat[m - 1] * 1.12f;
            float nat0 = _tierNat[0];
            // 站立混合：低于最慢一档自然速度的 0.9 倍时按比例淡入待机
            float idleTot = Mathf.Clamp01(1f - vNow / Mathf.Max(0.05f, nat0 * 0.9f));
            float moveTot = 1f - idleTot;
            if (moveTot > 0.0001f)
            {
                int hi = -1;
                for (int b = 1; b < TierCount; b++)
                    if (vNow <= _tierNat[b]) { hi = b; break; }
                if (hi < 0) _tierW[TierCount - 1] = moveTot;          // 比最快一档还快
                else if (vNow <= nat0) _tierW[0] = moveTot;           // 比最慢一档还慢
                else
                {
                    int lo = hi - 1;
                    float span = Mathf.Max(0.05f, _tierNat[hi] - _tierNat[lo]);
                    float f = Mathf.Clamp01((vNow - _tierNat[lo]) / span);
                    _tierW[lo] = moveTot * (1f - f);
                    _tierW[hi] = moveTot * f;
                }
            }
            float s = _speed01;
            _readyW = Mathf.MoveTowards(_readyW, _ready ? 1f : 0f, dt / 0.25f);
            _loco.SetInputWeight(0, idleTot * (1f - _readyW));
            _loco.SetInputWeight(1, idleTot * _readyW * (_armed ? 1f : 0f));
            _loco.SetInputWeight(2, idleTot * _readyW * (_armed ? 0f : 1f));

            float actual = _actualSpeed >= 0f ? _actualSpeed : s * RunNaturalSpeed;
            BlendDirectional(_tierW, actual, dt);

            // ===== 动作起播记录（屏幕提示用）=====
            // 你要的是"每做出一个动作，屏幕上提示对应的动画"。判据只能是
            // **动作层的片段索引发生了变化**，而不是姿态枚举变了——同一个姿态
            // 可能换片段（左右转身），不同姿态也可能共用片段。
            // 写在这里而不是三个起播点里：起播点有三处（招式 / 休息动作 / 起身倒放），
            // 挂在调用侧迟早会漏一处，挂在状态跃迁上漏不了。
            if (_cur != _lastNotedAction)
            {
                _lastNotedAction = _cur;
                if (_cur >= 0 && _actionName != null && _cur < _actionName.Length)
                {
                    LastActionClip = _actionName[_cur];
                    LastActionLen = _playLen;
                    LastActionAt = Time.time;
                }
            }

            if (_cur >= 0)
            {
                _actionT += dt;
                float len = _playLen;
                // 淡入/淡出时长随招式长短收缩：0.3 秒的快拳若还用固定 0.07/0.12 的过渡，
                // 有近半程在与移动层混权，拳就"软"了。短招用短过渡，姿态立得住。
                // 下限 0.04（原 0.025）：过渡太短会让连段每一段都"啪"地硬切上来，
                // 硬切同样读作卡顿而不是快——快要靠动作本身，不能靠切换。
                float fadeInDur = _fadeDur > 0.001f
                    ? _fadeDur
                    : (_playHold ? 0.07f : Mathf.Clamp(len * 0.18f, 0.04f, 0.07f));
                float fadeIn = Mathf.Lerp(_fadeFrom, 1f, Mathf.Clamp01(_actionT / fadeInDur));
                if (_playHold)
                {
                    // 保持型（倒地/死亡/格挡）：播完停在最后一帧，等待外部切换姿态
                    _actionW = fadeIn;
                }
                else
                {
                    float fadeOut = Mathf.Clamp01(
                        (len - _actionT) / Mathf.Clamp(len * 0.28f, 0.04f, 0.12f));
                    _actionW = Mathf.Min(fadeIn, fadeOut);
                    if (_actionT >= len) { _actionW = 0f; _cur = -1; }
                }
            }
            else
            {
                _actionW = Mathf.MoveTowards(_actionW, 0f, dt / 0.12f);
            }
            _top.SetInputWeight(0, 1f - _actionW);
            _top.SetInputWeight(1, _actionW);

            if (_graph.IsValid()) _graph.Evaluate(dt);
        }

        public void Destroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
