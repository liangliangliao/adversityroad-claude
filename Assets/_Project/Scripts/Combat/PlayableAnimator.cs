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
        // speed=播放速度；hold=播完保持最后一帧（倒地/死亡等持续状态，直到切换姿态）；
        // start=起手偏移（跳过片段开头这一比例）。
        struct ActionDef
        {
            public PoseState pose;
            public string[] keys;
            public float speed;
            public bool hold;
            public float start;
        }

        static ActionDef A(PoseState p, float speed, bool hold, float start, params string[] keys) =>
            new ActionDef { pose = p, keys = keys, speed = speed, hold = hold, start = start };

        // 手感两板斧（根治"连点有延迟/动作被压缩"）：
        // ① start 起手偏移——Mixamo 原始攻击片段前 15%~25% 是缓慢的摆架势预备，
        //    从偏移处起播，一按键立刻进入发力挥击相位，出手零迟滞；
        // ② speed 播放提速——真实格斗的出手是"脆快"的，挥击相位提速后
        //    在控制器的取消窗口(cancelAt)之前就能看完整记砍中，连点不糊招。
        static readonly ActionDef[] ActionMap =
        {
            A(PoseState.Attack,      1.5f,  false, 0.2f,  "great sword slash"),
            A(PoseState.HeavyAttack, 1.3f,  false, 0.12f, "great sword jump attack", "great sword jump", "great sword high spin attack"),
            A(PoseState.AttackUp,    1.5f,  false, 0.2f,  "great sword slash (1)", "great sword high spin attack"),
            A(PoseState.SwordThrust, 1.6f,  false, 0.18f, "stabbing", "stab"),
            A(PoseState.AttackLeap,  1.35f, false, 0.12f, "great sword jump attack", "great sword jump", "jump attack"),
            A(PoseState.JumpAttack,  1.4f,  false, 0.15f, "great sword jump attack", "great sword jump", "jump attack"),
            A(PoseState.AttackSpin,  1.4f,  false, 0.15f, "great sword high spin attack", "spin attack", "great sword slash (1)"),
            A(PoseState.PunchJab,    1.7f,  false, 0.15f, "lead jab", "jab"),
            A(PoseState.PunchCross,  1.6f,  false, 0.15f, "cross punch"),
            A(PoseState.AttackKick,  1.55f, false, 0.18f, "kicking"),
            A(PoseState.SideKick,    1.55f, false, 0.18f, "side kick"),
            A(PoseState.SpinKick,    1.45f, false, 0.12f, "spin flip kick", "spin kick"),
            A(PoseState.JumpKick,    1.5f,  false, 0.12f, "flying kick"),
            A(PoseState.Sweep,       1.4f,  false, 0.12f, "leg sweep", "spin flip kick"),
            A(PoseState.Hit,         1.45f, false, 0.1f,  "hit reaction", "great sword impact", "hit"),
            // 击倒提速：受了重击身体应当干脆地倒下去，而不是慢悠悠飘倒
            A(PoseState.Knockdown,   1.3f,  true,  0.04f, "knocked down", "sweep fall", "knockdown", "falling back"),
            A(PoseState.Death,       1.0f,  true,  0f,    "dying", "great sword death", "death"),
            A(PoseState.Cast,        1.0f,  false, 0f,    "spell casting", "cast"),
            // ===== 动作库覆盖面补位（下载对应片段放入 Anims/ 后自动生效）=====
            // 每项前面的候选是【专用片段】，末尾候选是没有专用片段时的替代：
            // 格挡=Great Sword Blocking（替代=格斗架势收紧）；
            // 踉跄=Stunned（替代=受击慢放）；蓄力=Great Sword Casting（替代=聚气施法）；
            // 翻滚=Stand To Roll / Forward Roll（无片段时由 HumanoidAnimator 程序化翻滚）；
            // 扫堂腿=Leg Sweep（替代=空翻踢低位）。
            A(PoseState.Guard,       1.0f,  true,  0f,    "great sword blocking", "blocking", "block", "fighting idle"),
            A(PoseState.Stagger,     0.55f, false, 0.1f,  "stunned", "dizzy", "stagger", "hit reaction"),
            A(PoseState.Charge,      0.85f, true,  0f,    "great sword casting", "warming up", "taunt", "charge", "spell casting"),
            // 翻滚：闪避时长会自动匹配片段长度（PlayerController），完整呈现整个滚翻
            A(PoseState.Dodge,       1.7f,  false, 0.1f,  "stand to roll", "forward roll", "sprinting forward roll", "dive roll"),
        };

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
            var actionList = new List<(PoseState? pose, AnimationClip clip, float speed, bool hold, float start)>();
            var connected = new HashSet<AnimationClip>();
            foreach (var m in ActionMap)
            {
                var clip = Pick(byName, m.keys);
                if (clip != null) { actionList.Add((m.pose, clip, m.speed, m.hold, m.start)); connected.Add(clip); }
            }
            foreach (var kv in byName)
                if (!connected.Contains(kv.Value))
                    actionList.Add(((PoseState?)null, kv.Value, 1f, false, 0f));

            _graph = PlayableGraph.Create("CharAnim_" + (_graphSerial++));
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);   // 手动推进，配合 timeScale/顿帧
            var output = AnimationPlayableOutput.Create(_graph, "out", _animator);

            _actionCount = actionList.Count;
            _actions = AnimationMixerPlayable.Create(_graph, Mathf.Max(1, _actionCount));
            _actionLen = new float[Mathf.Max(1, _actionCount)];
            _actionSpeed = new float[Mathf.Max(1, _actionCount)];
            _actionHold = new bool[Mathf.Max(1, _actionCount)];
            _actionStart = new float[Mathf.Max(1, _actionCount)];
            _actionRawLen = new float[Mathf.Max(1, _actionCount)];
            for (int i = 0; i < _actionCount; i++)
            {
                var (pose, clip, speed, hold, start) = actionList[i];
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
                _actionLen[i] = Mathf.Max(0.05f,
                    clip.length * (1f - Mathf.Clamp01(start)) / Mathf.Max(0.05f, speed));
                _actionSpeed[i] = speed;
                _actionHold[i] = hold;
                _actionStart[i] = Mathf.Clamp01(start);
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

        /// <summary>按关键词试播动作库中第一个匹配片段（如 "draw"/"sheath" 拔刀/收刀）。</summary>
        public bool PlayClipContaining(string key)
        {
            if (!Valid || string.IsNullOrEmpty(key)) return false;
            key = Norm(key);
            foreach (var kv in _clipIndex)
                if (kv.Key.Contains(key)) { PlayIndex(kv.Value); return true; }
            return false;
        }

        void PlayIndex(int idx)
        {
            for (int i = 0; i < _actionCount; i++) _actions.SetInputWeight(i, i == idx ? 1f : 0f);
            var cp = (AnimationClipPlayable)_actions.GetInput(idx);
            cp.SetSpeed(_actionSpeed[idx]);   // 起身反播可能改过速度，恢复默认
            // 从起手偏移处起播：跳过片段开头缓慢的摆架势，按键即入发力挥击相位
            cp.SetTime(_actionRawLen[idx] * _actionStart[idx]);
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
