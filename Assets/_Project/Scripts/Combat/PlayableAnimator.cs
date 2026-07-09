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

        static readonly ActionDef[] ActionMap =
        {
            A(PoseState.Attack,      1.15f, false, "great sword slash"),
            A(PoseState.HeavyAttack, 1.0f,  false, "great sword slash (1)", "great sword high spin attack"),
            A(PoseState.AttackUp,    1.15f, false, "great sword slash (1)", "great sword high spin attack"),
            A(PoseState.SwordThrust, 1.2f,  false, "stabbing", "stab"),
            A(PoseState.AttackLeap,  1.0f,  false, "great sword jump", "jump attack"),
            A(PoseState.JumpAttack,  1.1f,  false, "great sword jump", "jump attack"),
            A(PoseState.AttackSpin,  1.0f,  false, "great sword high spin attack", "spin attack", "great sword slash (1)"),
            A(PoseState.PunchJab,    1.25f, false, "lead jab", "jab"),
            A(PoseState.PunchCross,  1.2f,  false, "cross punch"),
            A(PoseState.AttackKick,  1.15f, false, "kicking"),
            A(PoseState.SideKick,    1.15f, false, "side kick"),
            A(PoseState.SpinKick,    1.1f,  false, "spin flip kick", "spin kick"),
            A(PoseState.JumpKick,    1.1f,  false, "flying kick"),
            A(PoseState.Sweep,       1.1f,  false, "spin flip kick"),
            A(PoseState.Hit,         1.1f,  false, "hit reaction", "great sword impact", "hit"),
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
        AnimationMixerPlayable _actions;
        readonly Dictionary<PoseState, int> _actionIndex = new Dictionary<PoseState, int>();
        float[] _actionLen;
        bool[] _actionHold;
        int _actionCount;

        int _cur = -1;
        float _actionT, _actionW, _fadeFrom;
        float _speed01;
        bool _ready;

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
            if (_animator == null || !_animator.isHuman) { Valid = false; return; }

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

            // 解析招式片段（映射得到的才建输入）
            var actionList = new List<(PoseState pose, AnimationClip clip, float speed, bool hold)>();
            foreach (var m in ActionMap)
            {
                var clip = Pick(byName, m.keys);
                if (clip != null) actionList.Add((m.pose, clip, m.speed, m.hold));
            }

            _graph = PlayableGraph.Create("CharAnim_" + (_graphSerial++));
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);   // 手动推进，配合 timeScale/顿帧
            var output = AnimationPlayableOutput.Create(_graph, "out", _animator);

            _actionCount = actionList.Count;
            _actions = AnimationMixerPlayable.Create(_graph, Mathf.Max(1, _actionCount));
            _actionLen = new float[Mathf.Max(1, _actionCount)];
            _actionHold = new bool[Mathf.Max(1, _actionCount)];
            for (int i = 0; i < _actionCount; i++)
            {
                var (pose, clip, speed, hold) = actionList[i];
                var cp = AnimationClipPlayable.Create(_graph, clip);
                cp.SetDuration(clip.length);
                cp.SetTime(clip.length);
                cp.SetSpeed(speed);
                _graph.Connect(cp, 0, _actions, i);
                _actions.SetInputWeight(i, 0f);
                _actionIndex[pose] = i;
                _actionLen[i] = Mathf.Max(0.05f, clip.length / Mathf.Max(0.05f, speed));
                _actionHold[i] = hold;
            }

            _loco = AnimationMixerPlayable.Create(_graph, 4);
            ConnectLoco(idle, 0); ConnectLoco(combatIdle, 1); ConnectLoco(walk, 2); ConnectLoco(run, 3);
            _loco.SetInputWeight(0, 1f);

            _top = AnimationMixerPlayable.Create(_graph, 2);
            _graph.Connect(_loco, 0, _top, 0);
            _graph.Connect(_actions, 0, _top, 1);
            _top.SetInputWeight(0, 1f);
            _top.SetInputWeight(1, 0f);

            output.SetSourcePlayable(_top);
            Valid = true;
        }

        void ConnectLoco(AnimationClip clip, int idx)
        {
            var cp = AnimationClipPlayable.Create(_graph, clip);
            cp.SetApplyFootIK(true);
            _graph.Connect(cp, 0, _loco, idx);
        }

        public void SetLocomotion(float speed01) => _speed01 = Mathf.Clamp01(speed01);
        public void SetReady(bool ready) => _ready = ready;

        /// <summary>触发一次招式（有对应片段才生效，否则维持 locomotion）。</summary>
        public void PlayAction(PoseState p)
        {
            if (!Valid || !_actionIndex.TryGetValue(p, out int idx)) return;
            for (int i = 0; i < _actionCount; i++) _actions.SetInputWeight(i, i == idx ? 1f : 0f);
            var cp = (AnimationClipPlayable)_actions.GetInput(idx);
            cp.SetTime(0);
            cp.SetDone(false);
            _cur = idx;
            _actionT = 0f;
            _fadeFrom = _actionW;   // 连招接招：从当前权重继续淡入，不掉回 0（消除断档感）
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
            _loco.SetInputWeight(0, idleTot * (_ready ? 0f : 1f));
            _loco.SetInputWeight(1, idleTot * (_ready ? 1f : 0f));
            _loco.SetInputWeight(2, walkW);
            _loco.SetInputWeight(3, runW);

            if (_cur >= 0)
            {
                _actionT += dt;
                float len = _actionLen[_cur];
                float fadeIn = Mathf.Lerp(_fadeFrom, 1f, Mathf.Clamp01(_actionT / 0.07f));
                if (_actionHold[_cur])
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
