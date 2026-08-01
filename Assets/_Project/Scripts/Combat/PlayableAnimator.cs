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
        }

        static ActionDef A(PoseState p, float speed, float start, float end, bool hold,
            params string[] keys) =>
            new ActionDef { pose = p, keys = keys, speed = speed, hold = hold, start = start, end = end };

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
            A(PoseState.Attack,      1.75f, 0.20f, 0.68f, false, "great sword slash"),
            A(PoseState.HeavyAttack, 1.5f,  0.12f, 0.78f, false, "great sword jump attack", "great sword jump", "great sword high spin attack"),
            A(PoseState.AttackUp,    1.75f, 0.20f, 0.68f, false, "great sword slash (1)", "great sword high spin attack"),
            A(PoseState.SwordThrust, 1.85f, 0.18f, 0.72f, false, "stabbing", "stab"),
            A(PoseState.AttackLeap,  1.55f, 0.12f, 0.80f, false, "great sword jump attack", "great sword jump", "jump attack"),
            A(PoseState.JumpAttack,  1.6f,  0.15f, 0.84f, false, "great sword jump attack", "great sword jump", "jump attack"),
            A(PoseState.AttackSpin,  1.6f,  0.15f, 0.76f, false, "great sword high spin attack", "spin attack", "great sword slash (1)"),
            A(PoseState.PunchJab,    1.95f, 0.15f, 0.70f, false, "lead jab", "jab"),
            A(PoseState.PunchCross,  1.85f, 0.15f, 0.70f, false, "cross punch"),
            A(PoseState.AttackKick,  1.8f,  0.18f, 0.76f, false, "kicking"),
            A(PoseState.SideKick,    1.8f,  0.18f, 0.76f, false, "side kick"),
            A(PoseState.SpinKick,    1.65f, 0.12f, 0.86f, false, "spin flip kick", "spin kick"),
            A(PoseState.JumpKick,    1.7f,  0.12f, 0.86f, false, "flying kick"),
            A(PoseState.Sweep,       1.6f,  0.12f, 0.84f, false, "leg sweep", "spin flip kick"),
            A(PoseState.Hit,         1.45f, 0.10f, 0.78f, false, "hit reaction", "great sword impact", "hit"),
            // 击倒提速：受了重击身体应当干脆地倒下去，而不是慢悠悠飘倒
            A(PoseState.Knockdown,   1.3f,  0.04f, 1f,    true,  "knocked down", "sweep fall", "knockdown", "falling back"),
            A(PoseState.Death,       1.0f,  0f,    1f,    true,  "dying", "great sword death", "death"),
            A(PoseState.Cast,        1.0f,  0f,    1f,    false, "spell casting", "cast"),
            // ===== 动作库覆盖面补位（下载对应片段放入 Anims/ 后自动生效）=====
            // 每项前面的候选是【专用片段】，末尾候选是没有专用片段时的替代：
            // 格挡=Great Sword Blocking（替代=格斗架势收紧）；
            // 踉跄=Stunned（替代=受击慢放）；蓄力=Great Sword Casting（替代=聚气施法）；
            // 翻滚=Stand To Roll / Forward Roll（无片段时由 HumanoidAnimator 程序化翻滚）；
            // 扫堂腿=Leg Sweep（替代=空翻踢低位）。
            A(PoseState.Guard,       1.0f,  0f,    1f,    true,  "great sword blocking", "blocking", "block", "fighting idle"),
            A(PoseState.Stagger,     0.55f, 0.10f, 1f,    false, "stunned", "dizzy", "stagger", "hit reaction"),
            A(PoseState.Charge,      0.85f, 0f,    1f,    true,  "great sword casting", "warming up", "taunt", "charge", "spell casting"),
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
        /// 2.2 倍下每帧推进 0.073 秒，动作仍然连得起来。
        ///
        /// 代价要说清楚：长片段（巨剑类 2 秒以上）因此无法在 0.35 秒内播完整个
        /// 发力窗，会被下一招切断。这是对的——动作游戏里连段本来就是"一刀砍到
        /// 一半接下一刀"，看得清比播得完重要。
        /// </summary>
        const float MaxDrivenSpeed = 2.2f;

        /// <summary>单次出招在画面上至少要占的时间（≈30fps 下 5-6 帧）。
        /// 技能连招里 0.14 秒一段的节拍若原样反推，动作只剩三四帧就换下一个，
        /// 同样会读成"抖了一下"而不是"打了一下"。低于此值的节拍按此值给动画，
        /// 动作因此会略微溢出节拍、被下一段切断——连续，但不空转。</summary>
        const float MinShownMotion = 0.18f;

        readonly Animator _animator;
        PlayableGraph _graph;
        AnimationMixerPlayable _top;      // 0=loco 1=action
        AnimationMixerPlayable _loco;     // 0=idle 1=combatIdle 2=walk 3=run
        AnimationClipPlayable _walkCp, _runCp;   // 步幅同步：播放速率随真实移速缩放
        AnimationMixerPlayable _actions;

        /// <summary>驱动中的 Animator（供脚踝校准等后处理访问骨骼）。</summary>
        public Animator Animator => _animator;

        // 步幅同步基准：走/跑动画在标准体型下的自然位移速度（m/s）随 TargetHeight
        // 等比缩放——模型被缩放后，动画里烘焙的位移也同比缩放，自然速度必须跟着变，
        // 否则改身高就会脚打滑。系数为原基准 3.6/8.6 相对 4.1m 的比值。
        // 播放速率 = 真实速度 / 自然速度 → 步频与实际位移匹配，脚不打滑。
        static float WalkNaturalSpeed => MecanimCharacter.TargetHeight * 0.878f;
        static float RunNaturalSpeed => MecanimCharacter.TargetHeight * 2.098f;
        readonly Dictionary<PoseState, int> _actionIndex = new Dictionary<PoseState, int>();
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

        int _cur = -1;
        float _actionT, _actionW, _fadeFrom;
        float _speed01;
        float _actualSpeed = -1f;   // 真实移速 m/s（<0 = 未提供，按 speed01 折算）
        bool _ready;
        float _readyW;   // 普通待机↔格斗架势的平滑过渡权重（瞬切会"弹一下"）

        static int _graphSerial;

        public bool Valid { get; private set; }

        readonly string _folder;   // 动作库目录（不同角色可有各自的动作库）

        public PlayableAnimator(Animator animator, string animsFolder = null)
        {
            _animator = animator;
            _folder = string.IsNullOrEmpty(animsFolder) ? "Characters/Anims" : animsFolder;
            Build();
        }

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

            var idle = Pick(byName, "idle", "breathing idle", "standing idle");
            var walk = Pick(byName, "walking", "great sword walk", "walk");
            var run = Pick(byName, "running", "great sword run", "run");
            if (idle == null || walk == null || run == null) { Valid = false; return; }
            var combatIdle = Pick(byName, "great sword idle", "fighting idle", "combat idle", "sword and shield idle") ?? idle;

            // 解析招式片段；目录中未被映射的片段也全部接入（动作库预览可逐个试播）
            var actionList =
                new List<(PoseState? pose, AnimationClip clip, float speed, bool hold, float start, float end)>();
            var connected = new HashSet<AnimationClip>();
            foreach (var m in ActionMap)
            {
                var clip = Pick(byName, m.keys);
                if (clip != null)
                {
                    actionList.Add((m.pose, clip, m.speed, m.hold, m.start, m.end));
                    connected.Add(clip);
                }
            }
            foreach (var kv in byName)
                if (!connected.Contains(kv.Value))
                    actionList.Add(((PoseState?)null, kv.Value, 1f, false, 0f, 1f));

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
                if (pose.HasValue) _actionIndex[pose.Value] = i;
                string ck = Norm(clip.name);
                if (!_clipIndex.ContainsKey(ck)) _clipIndex[ck] = i;
                _actionStart[i] = Mathf.Clamp01(start);
                _actionEnd[i] = Mathf.Clamp(end, _actionStart[i] + 0.1f, 1f);
                _actionLen[i] = Mathf.Max(0.05f,
                    clip.length * (_actionEnd[i] - _actionStart[i]) / Mathf.Max(0.05f, speed));
                _actionSpeed[i] = speed;
                _actionHold[i] = hold;
                _actionRawLen[i] = clip.length;
            }

            _loco = AnimationMixerPlayable.Create(_graph, 4);
            ConnectLoco(idle, 0); ConnectLoco(combatIdle, 1);
            _walkCp = ConnectLoco(walk, 2); _runCp = ConnectLoco(run, 3);
            _loco.SetInputWeight(0, 1f);

            _top = AnimationMixerPlayable.Create(_graph, 2);
            _graph.Connect(_loco, 0, _top, 0);
            _graph.Connect(_actions, 0, _top, 1);
            _top.SetInputWeight(0, 1f);
            _top.SetInputWeight(1, 0f);

            output.SetSourcePlayable(_top);
            Valid = true;
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

        /// <summary>speed01=相对满速的比例；actualSpeed=真实移速 m/s（供步幅同步）。</summary>
        public void SetLocomotion(float speed01, float actualSpeed = -1f)
        {
            _speed01 = Mathf.Clamp01(speed01);
            _actualSpeed = actualSpeed;
        }
        public void SetReady(bool ready) => _ready = ready;

        /// <summary>触发一次招式（有对应片段才生效，否则维持 locomotion）。
        /// targetDuration&gt;0 时按【招式帧数据】反推播放速度：这一招在游戏里占多久，
        /// 画面就在多久之内把发力窗打完——动作与判定从此对得上拍。</summary>
        public void PlayAction(PoseState p, float targetDuration = 0f)
        {
            if (!Valid || !_actionIndex.TryGetValue(p, out int idx)) return;
            PlayIndex(idx, targetDuration);
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
            _fadeFrom = _actionW;   // 连招接招：从当前权重继续淡入，不掉回 0（消除断档感）
        }

        /// <summary>起身过程：把倒地片段【倒放】——从躺地姿态连贯地撑起站立
        /// （腿脚先动、身体逐渐立起），播完自动淡回移动层。</summary>
        public void PlayGetUp()
        {
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
            _fadeFrom = Mathf.Max(_actionW, 0.9f);   // 从躺地姿态无缝续接，不闪回站立
        }

        /// <summary>结束保持型动作（倒地爬起/收架势），淡回移动层。</summary>
        public void StopAction()
        {
            _cur = -1;
        }

        public void Tick(float dt)
        {
            if (!Valid) return;

            float s = _speed01;
            float walkW, runW, idleTot;
            if (s < 0.5f) { walkW = s / 0.5f; runW = 0f; idleTot = 1f - walkW; }
            else { runW = (s - 0.5f) / 0.5f; walkW = 1f - runW; idleTot = 0f; }
            _readyW = Mathf.MoveTowards(_readyW, _ready ? 1f : 0f, dt / 0.25f);
            _loco.SetInputWeight(0, idleTot * (1f - _readyW));
            _loco.SetInputWeight(1, idleTot * _readyW);
            _loco.SetInputWeight(2, walkW);
            _loco.SetInputWeight(3, runW);

            // 步幅同步：走/跑播放速率 = 真实移速 / 动画自然速度——步频与位移匹配，
            // 脚落地不打滑（"脚的移动过程一目了然"的关键，参考电影/悟空的贴地感）
            float actual = _actualSpeed >= 0f ? _actualSpeed : s * RunNaturalSpeed;
            if (walkW > 0.001f && _walkCp.IsValid())
                _walkCp.SetSpeed(Mathf.Clamp(actual / WalkNaturalSpeed, 0.8f, 1.5f));
            if (runW > 0.001f && _runCp.IsValid())
                _runCp.SetSpeed(Mathf.Clamp(actual / RunNaturalSpeed, 0.8f, 1.35f));

            if (_cur >= 0)
            {
                _actionT += dt;
                float len = _playLen;
                // 淡入/淡出时长随招式长短收缩：0.3 秒的快拳若还用固定 0.07/0.12 的过渡，
                // 有近半程在与移动层混权，拳就"软"了。短招用短过渡，姿态立得住。
                // 下限 0.04（原 0.025）：过渡太短会让连段每一段都"啪"地硬切上来，
                // 硬切同样读作卡顿而不是快——快要靠动作本身，不能靠切换。
                float fadeInDur = _playHold ? 0.07f : Mathf.Clamp(len * 0.18f, 0.04f, 0.07f);
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
