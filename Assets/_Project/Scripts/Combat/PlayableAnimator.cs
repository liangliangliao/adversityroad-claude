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
    /// </summary>
    public class PlayableAnimator
    {
        // Mixamo 片段名（小写）→ 招式。前面的候选优先精确匹配，找不到再按包含匹配。
        static readonly (PoseState pose, string[] keys)[] ActionMap =
        {
            (PoseState.Attack,      new[]{ "great sword slash" }),
            (PoseState.HeavyAttack, new[]{ "great sword slash (1)", "great sword high spin attack" }),
            (PoseState.AttackUp,    new[]{ "great sword slash (1)", "great sword high spin attack" }),
            (PoseState.SwordThrust, new[]{ "stabbing", "stab" }),
            (PoseState.AttackLeap,  new[]{ "great sword jump", "jump attack" }),
            (PoseState.JumpAttack,  new[]{ "great sword jump", "jump attack" }),
            (PoseState.AttackSpin,  new[]{ "great sword high spin attack", "spin attack", "great sword slash (1)" }),
            (PoseState.PunchJab,    new[]{ "lead jab", "jab" }),
            (PoseState.PunchCross,  new[]{ "cross punch" }),
            (PoseState.AttackKick,  new[]{ "kicking" }),
            (PoseState.SideKick,    new[]{ "side kick" }),
            (PoseState.SpinKick,    new[]{ "spin flip kick", "spin kick" }),
            (PoseState.JumpKick,    new[]{ "flying kick" }),
            (PoseState.Sweep,       new[]{ "spin flip kick" }),
            (PoseState.Hit,         new[]{ "hit reaction", "hit" }),
            (PoseState.Knockdown,   new[]{ "knocked down", "knockdown" }),
            (PoseState.Death,       new[]{ "dying", "death" }),
            (PoseState.Cast,        new[]{ "spell casting", "cast" }),
            (PoseState.Guard,       new[]{ "blocking", "block" }),
            (PoseState.Dodge,       new[]{ "dodge", "roll" }),
        };

        readonly Animator _animator;
        PlayableGraph _graph;
        AnimationMixerPlayable _top;      // 0=loco 1=action
        AnimationMixerPlayable _loco;     // 0=idle 1=combatIdle 2=walk 3=run
        AnimationMixerPlayable _actions;
        readonly Dictionary<PoseState, int> _actionIndex = new Dictionary<PoseState, int>();
        float[] _actionLen;
        int _actionCount;

        int _cur = -1;
        float _actionT, _actionW;
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

            var idle = Pick(byName, "idle");
            var walk = Pick(byName, "walking", "walk");
            var run = Pick(byName, "running", "run");
            if (idle == null || walk == null || run == null) { Valid = false; return; }
            var combatIdle = Pick(byName, "fighting idle", "combat idle") ?? idle;

            // 解析招式片段（映射得到的才建输入）
            var actionList = new List<KeyValuePair<PoseState, AnimationClip>>();
            foreach (var m in ActionMap)
            {
                var clip = Pick(byName, m.keys);
                if (clip != null) actionList.Add(new KeyValuePair<PoseState, AnimationClip>(m.pose, clip));
            }

            _graph = PlayableGraph.Create("CharAnim_" + (_graphSerial++));
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);   // 手动推进，配合 timeScale/顿帧
            var output = AnimationPlayableOutput.Create(_graph, "out", _animator);

            _actionCount = actionList.Count;
            _actions = AnimationMixerPlayable.Create(_graph, Mathf.Max(1, _actionCount));
            _actionLen = new float[Mathf.Max(1, _actionCount)];
            for (int i = 0; i < _actionCount; i++)
            {
                var clip = actionList[i].Value;
                var cp = AnimationClipPlayable.Create(_graph, clip);
                cp.SetDuration(clip.length);
                cp.SetTime(clip.length);
                _graph.Connect(cp, 0, _actions, i);
                _actions.SetInputWeight(i, 0f);
                _actionIndex[actionList[i].Key] = i;
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
