using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 动捕动画驱动（基于 Playables，纯代码，无需 AnimatorController 手工连线）：
    /// 用只带 Avatar 的 Animator 播放从 Resources 载入的 Mixamo 人形动画片段。
    /// 契合本项目「无编辑器手工建场、CI 无头打包」的构建方式。
    ///
    /// 资源契约（详见仓库根目录 MIXAMO_SETUP.md）：
    ///   动画片段放在 Resources/Characters/Anims/ 下，命名与 PoseState 一致
    ///   （Attack / AttackUp / SwordThrust / HeavyAttack / PunchJab / AttackKick …），
    ///   外加运动片段 Idle / Walk / Run / CombatIdle（后者可选）。
    ///   缺 Idle/Walk/Run 任一则判定无效，上层回退到程序化骨骼。
    ///
    /// 结构：top[0]=locomotion 混合(idle/combatIdle/walk/run)、top[1]=action 层；
    /// 出招时对 action 交叉淡入淡出，播完自动回到 locomotion。
    /// </summary>
    public class PlayableAnimator
    {
        readonly Animator _animator;
        PlayableGraph _graph;
        AnimationMixerPlayable _top;      // 0=loco 1=action
        AnimationMixerPlayable _loco;     // 0=idle 1=combatIdle 2=walk 3=run
        AnimationMixerPlayable _actions;  // 每个招式一个输入
        readonly Dictionary<PoseState, int> _actionIndex = new Dictionary<PoseState, int>();
        float[] _actionLen;
        int _actionCount;

        int _cur = -1;          // 当前动作输入下标（-1=无）
        float _actionT, _actionW;
        float _speed01;
        bool _ready;

        public bool Valid { get; private set; }

        public PlayableAnimator(Animator animator)
        {
            _animator = animator;
            Build();
        }

        static AnimationClip Load(string n) => Resources.Load<AnimationClip>("Characters/Anims/" + n);
        static bool IsLoco(PoseState p) => p == PoseState.Idle;

        void Build()
        {
            var idle = Load("Idle"); var walk = Load("Walk"); var run = Load("Run");
            if (_animator == null || !_animator.isHuman || idle == null || walk == null || run == null)
            {
                Valid = false;
                return;
            }
            var combatIdle = Load("CombatIdle") ?? idle;

            _graph = PlayableGraph.Create("CharAnim_" + _animator.GetInstanceID());
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);   // 我们手动推进，配合 timeScale/顿帧
            var output = AnimationPlayableOutput.Create(_graph, "out", _animator);

            // 招式层：为每个有片段的 PoseState 建一个 ClipPlayable
            var poses = new List<PoseState>();
            foreach (PoseState p in System.Enum.GetValues(typeof(PoseState)))
            {
                if (IsLoco(p)) continue;
                if (Load(p.ToString()) != null) poses.Add(p);
            }
            _actionCount = poses.Count;
            _actions = AnimationMixerPlayable.Create(_graph, Mathf.Max(1, _actionCount));
            _actionLen = new float[Mathf.Max(1, _actionCount)];
            for (int i = 0; i < _actionCount; i++)
            {
                var clip = Load(poses[i].ToString());
                var cp = AnimationClipPlayable.Create(_graph, clip);
                cp.SetDuration(clip.length);
                cp.SetTime(clip.length);           // 起始为"已播完"（权重 0）
                _graph.Connect(cp, 0, _actions, i);
                _actions.SetInputWeight(i, 0f);
                _actionIndex[poses[i]] = i;
                _actionLen[i] = Mathf.Max(0.05f, clip.length);
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
        }

        public void Tick(float dt)
        {
            if (!Valid) return;

            // 运动混合：idle/combatIdle ↔ walk ↔ run
            float s = _speed01;
            float walkW, runW, idleTot;
            if (s < 0.5f) { walkW = s / 0.5f; runW = 0f; idleTot = 1f - walkW; }
            else { runW = (s - 0.5f) / 0.5f; walkW = 1f - runW; idleTot = 0f; }
            _loco.SetInputWeight(0, idleTot * (_ready ? 0f : 1f));
            _loco.SetInputWeight(1, idleTot * (_ready ? 1f : 0f));
            _loco.SetInputWeight(2, walkW);
            _loco.SetInputWeight(3, runW);

            // 招式交叉淡入淡出
            if (_cur >= 0)
            {
                _actionT += dt;
                float len = _actionLen[_cur];
                float fadeIn = Mathf.Clamp01(_actionT / 0.08f);
                float fadeOut = Mathf.Clamp01((len - _actionT) / 0.12f);
                _actionW = Mathf.Min(fadeIn, fadeOut);
                if (_actionT >= len) { _actionW = 0f; _cur = -1; }
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
