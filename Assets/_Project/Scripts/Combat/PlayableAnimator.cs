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
        // speed=播放速度；hold=播完保持最后一帧（倒地/死亡等持续状态，直到切换姿态）。
        struct ActionDef
        {
            public PoseState pose;
            public string[] keys;
            public float speed;
            public bool hold;
        }

        static ActionDef A(PoseState p, float speed, bool hold, params string[] keys) =>
            new ActionDef { pose = p, keys = keys, speed = speed, hold = hold };

        // 播放速率整体上调：真实格斗的出手是"脆快"的，原速 Mixamo 片段偏演示节奏；
        // 提速后招式起手-接触-收招全程更利落，连招衔接紧凑（配合控制器帧数同步收紧）
        static readonly ActionDef[] ActionMap =
        {
            A(PoseState.Attack,      1.4f,  false, "great sword slash"),
            A(PoseState.HeavyAttack, 1.2f,  false, "great sword high spin attack", "great sword slash (1)"),
            A(PoseState.AttackUp,    1.4f,  false, "great sword slash (1)", "great sword high spin attack"),
            A(PoseState.SwordThrust, 1.45f, false, "stabbing", "stab"),
            A(PoseState.AttackLeap,  1.2f,  false, "great sword jump", "jump attack"),
            A(PoseState.JumpAttack,  1.3f,  false, "great sword jump", "jump attack"),
            A(PoseState.AttackSpin,  1.25f, false, "great sword high spin attack", "spin attack", "great sword slash (1)"),
            A(PoseState.PunchJab,    1.55f, false, "lead jab", "jab"),
            A(PoseState.PunchCross,  1.45f, false, "cross punch"),
            A(PoseState.AttackKick,  1.4f,  false, "kicking"),
            A(PoseState.SideKick,    1.4f,  false, "side kick"),
            A(PoseState.SpinKick,    1.35f, false, "spin flip kick", "spin kick"),
            A(PoseState.JumpKick,    1.35f, false, "flying kick"),
            A(PoseState.Sweep,       1.3f,  false, "spin flip kick"),
            A(PoseState.Hit,         1.25f, false, "hit reaction", "great sword impact", "hit"),
            A(PoseState.Knockdown,   1.0f,  true,  "knocked down", "sweep fall", "knockdown", "falling back"),
            A(PoseState.Death,       1.0f,  true,  "dying", "great sword death", "death"),
            A(PoseState.Cast,        1.0f,  false, "spell casting", "cast"),
            // 库里无专门格挡/踉跄/蓄力片段，用最贴切的片段替代：
            // 格挡=格斗架势收紧（保持到解除）；踉跄=受击慢放（晃神）；蓄力=聚气施法。
            A(PoseState.Guard,       1.0f,  true,  "great sword blocking", "blocking", "block", "fighting idle"),
            A(PoseState.Stagger,     0.55f, false, "stunned", "dizzy", "stagger", "hit reaction"),
            A(PoseState.Charge,      0.85f, false, "great sword casting", "warming up", "taunt", "charge", "spell casting"),
            // Dodge 无翻滚片段：由 HumanoidAnimator 在视根上做程序化翻滚
        };

        readonly Animator _animator;
        PlayableGraph _graph;
        AnimationMixerPlayable _top;      // 0=loco 1=action
        AnimationMixerPlayable _loco;     // 0=idle 1=combatIdle 2=walk 3=run
        AnimationClipPlayable _walkCp, _runCp;   // 步幅同步：播放速率随真实移速缩放
        AnimationMixerPlayable _actions;

        /// <summary>驱动中的 Animator（供脚踝校准等后处理访问骨骼）。</summary>
        public Animator Animator => _animator;

        // 步幅同步基准：该动作包在 2.3m 体型下走/跑动画的自然位移速度（m/s）。
        // 播放速率 = 真实速度 / 自然速度 → 步频与实际位移匹配，脚不打滑。
        const float WalkNaturalSpeed = 2.0f;
        const float RunNaturalSpeed = 4.8f;
        readonly Dictionary<PoseState, int> _actionIndex = new Dictionary<PoseState, int>();
        // 动作库全量索引（片段名→输入口）：未映射到招式的片段也接入，供预览试播
        readonly Dictionary<string, int> _clipIndex = new Dictionary<string, int>();
        float[] _actionLen;
        float[] _actionSpeed;
        bool[] _actionHold;
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

        public PlayableAnimator(Animator animator)
        {
            _animator = animator;
            Build();
        }

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
            foreach (var c in Resources.LoadAll<AnimationClip>("Characters/Anims"))
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
            var actionList = new List<(PoseState? pose, AnimationClip clip, float speed, bool hold)>();
            var connected = new HashSet<AnimationClip>();
            foreach (var m in ActionMap)
            {
                var clip = Pick(byName, m.keys);
                if (clip != null) { actionList.Add((m.pose, clip, m.speed, m.hold)); connected.Add(clip); }
            }
            foreach (var kv in byName)
                if (!connected.Contains(kv.Value))
                    actionList.Add(((PoseState?)null, kv.Value, 1f, false));

            _graph = PlayableGraph.Create("CharAnim_" + (_graphSerial++));
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);   // 手动推进，配合 timeScale/顿帧
            var output = AnimationPlayableOutput.Create(_graph, "out", _animator);

            _actionCount = actionList.Count;
            _actions = AnimationMixerPlayable.Create(_graph, Mathf.Max(1, _actionCount));
            _actionLen = new float[Mathf.Max(1, _actionCount)];
            _actionSpeed = new float[Mathf.Max(1, _actionCount)];
            _actionHold = new bool[Mathf.Max(1, _actionCount)];
            for (int i = 0; i < _actionCount; i++)
            {
                var (pose, clip, speed, hold) = actionList[i];
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
                _actionLen[i] = Mathf.Max(0.05f, clip.length / Mathf.Max(0.05f, speed));
                _actionSpeed[i] = speed;
                _actionHold[i] = hold;
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

        /// <summary>触发一次招式（有对应片段才生效，否则维持 locomotion）。</summary>
        public void PlayAction(PoseState p)
        {
            if (!Valid || !_actionIndex.TryGetValue(p, out int idx)) return;
            PlayIndex(idx);
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

        void PlayIndex(int idx)
        {
            for (int i = 0; i < _actionCount; i++) _actions.SetInputWeight(i, i == idx ? 1f : 0f);
            var cp = (AnimationClipPlayable)_actions.GetInput(idx);
            cp.SetSpeed(_actionSpeed[idx]);   // 起身反播可能改过速度，恢复默认
            cp.SetTime(0);
            cp.SetDone(false);
            _cur = idx;
            _actionT = 0f;
            _playLen = _actionLen[idx];
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
            float clipLen = _actionLen[idx] * _actionSpeed[idx];   // 原始片段时长
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
                float fadeIn = Mathf.Lerp(_fadeFrom, 1f, Mathf.Clamp01(_actionT / 0.07f));
                if (_playHold)
                {
                    // 保持型（倒地/死亡/格挡）：播完停在最后一帧，等待外部切换姿态
                    _actionW = fadeIn;
                }
                else
                {
                    float fadeOut = Mathf.Clamp01((len - _actionT) / 0.12f);
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
