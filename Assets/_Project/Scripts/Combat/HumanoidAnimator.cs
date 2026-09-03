using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 人形程序动画（武术级重制）：驱动 HumanoidRig 全部关节做类人动作。
    ///
    /// 关键：招式不再是「一条 lerp 从起手滑到收招」（那是机械感的根源），而是用
    /// 关键帧曲线 Kf() 把每一击拆成武术动作的相位——
    ///   ① 预备/蓄势（反身拧腰、沉重心、收肘/提膝蓄力）
    ///   ② 爆发/出击（髋先动→带躯干→送肩→出刃/出拳；腿弹出）
    ///   ③ 触点（极短的顿挫，力道到位）
    ///   ④ 随势/收势（惯性带过→回到防守架势）
    /// 并加入全身协调：转腰送肩的动力链时序、弓步/沉桩、重心左右转移、
    /// 腿击的「提膝→弹踢→收腿→落步」。刀光只在真正挥砍的相位出现。
    ///
    /// 玩家由 CombatStateMachine 自动映射；敌人手动 SetPose + SetLocomotion。
    /// </summary>
    public class HumanoidAnimator : MonoBehaviour
    {
        public Transform visual;      // 整体可视根（翻滚/倒地/旋身时旋转）
        public HumanoidRig rig;
        public CombatStateMachine fsm;
        public Transform weaponPivot; // 兵器枢轴（叠加在手部之上做刀刃轨迹）
        public TrailRenderer weaponTrail;
        public bool isEnemy;          // 招式名浮字颜色区分（玩家金/敌人红）

        PoseState _pose = PoseState.Idle;
        CombatState _lastFsmState = CombatState.Idle;
        float _t;

        /// <summary>当前姿态（外部做保持型姿态的兜底收招判断用）。</summary>
        public PoseState CurrentPose => _pose;

        // 运动参数（每帧由控制器喂入）
        float _speed01;
        bool _crouch;
        bool _grounded = true;
        bool _ready;   // 临战：站立时进入格斗预备架势（而非松垮的垂手待机）
        float _phase;

        // 动捕模式（Playables 驱动 Mixamo 人形）：有资源时接管，无则走下方程序化骨骼
        PlayableAnimator _mecanim;
        int _poseSerial, _lastMecanimSerial = -1;
        bool Mecanim => _mecanim != null && _mecanim.Valid;

        /// <summary>诊断：动画层实际用的方向混合角与步频（无动捕时为 0）。</summary>
        public float DbgBlendAngle => Mecanim ? _mecanim.DbgBlendAngle : 0f;
        public float DbgPhaseRate => Mecanim ? _mecanim.DbgPhaseRate : 0f;
        /// <summary>腿此刻是否真的在演走路（判据见 PlayableAnimator.LegsWalking）。</summary>
        public bool LegsWalking => Mecanim && _mecanim.LegsWalking;
        /// <summary>诊断：本帧两条方向片段共混的程度（次高权重）。见 PlayableAnimator.DbgBlendMix。</summary>
        public float DbgBlendMix => Mecanim ? _mecanim.DbgBlendMix : 0f;
        /// <summary>调试叠层：此刻画面上真正在播的动作层片段与移动层片段。</summary>
        public string DbgNowPlaying => Mecanim ? _mecanim.DbgNowPlaying() : "（方块骨骼）";
        /// <summary>诊断：此刻招式是否只写上半身（腿归移动层）。</summary>
        public bool ActionUpperBodyOnly => Mecanim && _mecanim.ActionUpperBodyOnly;
        /// <summary>调试叠层：当前姿态枚举名（"该播什么"，与上面的"实际在播什么"对照）。</summary>
        public string DbgPose => _pose.ToString();
        /// <summary>最近一次起播的动作片段名（屏幕提示用）。</summary>
        public string LastActionClip => Mecanim ? _mecanim.LastActionClip : "";
        /// <summary>那一段计划播多久（秒）。</summary>
        public float LastActionLen => Mecanim ? _mecanim.LastActionLen : 0f;
        /// <summary>起播时刻（Time.time）。</summary>
        public float LastActionAt => Mecanim ? _mecanim.LastActionAt : -999f;

        /// <summary>该行进夹角上最快的方向片段的自然速度（m/s）；0=这个方向没片段。
        /// 横移封顶按它给，不再手写一张与素材无关的表。</summary>
        public float NaturalSpeedAt(float angle) => Mecanim ? _mecanim.NaturalSpeedAt(angle) : 0f;

        /// <summary>调试叠层：这一帧每步跨了多远、跟片段自带步幅差多少、有没有打滑。</summary>
        public string DbgStride(float actual) => Mecanim ? _mecanim.DbgStride(actual) : "步幅 —";

        // ===== 结构化诊断（调试日志用；与上面的 HUD 字符串同源）=====
        public string DbgActionName => Mecanim ? _mecanim.DbgActionName : "";
        public float DbgActionW => Mecanim ? _mecanim.DbgActionW : 0f;
        public string DbgSlipClip => Mecanim ? _mecanim.DbgSlipClip : "";
        public float DbgSlipWant => Mecanim ? _mecanim.DbgSlipWant : 0f;
        public float DbgSlipGot => Mecanim ? _mecanim.DbgSlipGot : 0f;
        public float DbgStrideActual(float actual) =>
            Mecanim ? _mecanim.DbgStrideActual(actual) : 0f;
        public float DbgStrideWant() => Mecanim ? _mecanim.DbgStrideWant() : 0f;

        /// <summary>某一档正前方片段的实测自然速度（m/s）；没有 Mecanim 时返回 0。
        /// 移速锚在它上面，见 PlayableAnimator.TierNaturalSpeed。</summary>
        public float TierNaturalSpeed(int tier) =>
            Mecanim ? _mecanim.TierNaturalSpeed(tier) : 0f;
        public void DbgTopDirs(out string n1, out float w1, out string n2, out float w2)
        {
            if (Mecanim) { _mecanim.DbgTopDirs(out n1, out w1, out n2, out w2); return; }
            n1 = ""; w1 = 0f; n2 = ""; w2 = 0f;
        }

        /// <summary>诊断用：这个角色走的是不是【动捕】通路。
        /// 动捕角色各自持有一张 PlayableGraph（片段数 × 一个 ClipPlayable），
        /// 而方块骨骼角色只是每帧算几十个关节角——两者开销差一个量级，
        /// 只报"角色 131"分不出贵在哪儿。</summary>
        public bool IsMocap => Mecanim;

        // ---- 髋骨 XZ 锚定（Generic 原样播放的原地化）----
        // 本动作包的走/跑等片段自带水平位移（实测 Walking +1.8m），Generic 播放
        // 会把模型带离胶囊体。每帧动画求值后把髋骨水平位置钉回绑定位（在模型
        // 根局部空间做，轴向安全），纵向起伏/腾空原样保留；世界位移由控制器负责。
        /// <summary>倾身取真实倾角的几成（1.0 = 完全按 atan(a/g) 倾，像摩托压弯）。</summary>
        const float LeanFraction = 0.6f;
        /// <summary>倾角封顶（度）。</summary>
        const float MaxLeanDeg = 12f;
        /// <summary>倾角变化速率上限（度/秒）：转弯起手不允许"啪"地倒过去。</summary>
        const float LeanSlewDegPerSec = 90f;
        float _lean, _leanPrevYaw;
        bool _leanInit;

        Transform _mocapModel, _hips;
        Vector3 _hipsBindLP;
        bool _hipsPin;
        bool _pendingGetUp;
        // 双脚贴地校准：不同体型腿长≠动作数据骨架腿长（踮脚/悬空/陷地的根因）
        Transform _footL, _footR;         // 脚尖（toe）
        Transform _ankleL, _ankleR;       // 脚踝（foot）——脚掌放平修正用
        float _groundLocalY = -1f;
        float _modelBaseY;
        float _feetOffset, _feetTarget;

        // ---- 休息姿态（坐 / 躺）----
        // 坐下与躺下的片段带着**动作骨架自己的座面高度**（Mixamo 那把椅子约 45cm、
        // 那张床约 67cm）。家里的沙发、餐椅、吧椅、床、躺椅、瑜伽垫高度各不相同，
        // 直接播片段必然出现"悬空坐"或"陷进床里"。所以休息期间改一条锚定规则：
        // 不再把【脚底】对到地面，而是把【骨盆】对到这件家具的座面/床面高度。
        // 权重 _restW 让这段修正随动作渐入渐出——坐下的过程里身体本来就该从
        // 站姿一路沉到座面，一上来就锚死会把人按进地板。
        bool _rest;
        float _restPelvisY, _restW, _restBase;

        /// <summary>临战状态：为真时静立会摆出格斗架势（持械/抱拳、沉桩、踮步微动）。</summary>
        public void SetCombatReady(bool ready) => _ready = ready;

        /// <summary>兵器在不在手上：收刀后临战架势、蹲伏、踢击、死亡都换空手版本。</summary>
        public void SetArmed(bool armed)
        {
            _armed = armed;
            if (Mecanim) _mecanim.SetArmed(armed);
        }
        bool _armed = true;

        /// <summary>切到动捕模式：成功接管返回 true；失败保持程序化骨骼。
        /// animsFolder 可指定角色专属动作库目录（如 Characters/Anims2），
        /// 该目录无效时自动回退默认动作库（Mixamo 标准骨架通用）。</summary>
        public bool TryEnableMecanim(Animator animator, string animsFolder = null)
        {
            _mecanim = new PlayableAnimator(animator, animsFolder);
            if (!_mecanim.Valid && !string.IsNullOrEmpty(animsFolder))
            {
                _mecanim.Destroy();
                _mecanim = new PlayableAnimator(animator);   // 回退默认动作库
            }
            if (!_mecanim.Valid) { _mecanim.Destroy(); _mecanim = null; return false; }
            return true;
        }

        void OnDestroy() { if (_mecanim != null) _mecanim.Destroy(); }

        /// <summary>装配时注入模型根与髋骨（绑定姿态下记录髋骨水平锚点）；
        /// footL/footR 与 groundLocalY 供双脚贴地校准。</summary>
        public void SetMocapRoot(Transform model, Transform hips,
            Transform footL = null, Transform footR = null, float groundLocalY = -1f,
            Transform ankleL = null, Transform ankleR = null)
        {
            _mocapModel = model;
            _hips = hips;
            _hipsPin = model != null && hips != null;
            if (_hipsPin)
            {
                _hipsBindLP = model.InverseTransformPoint(hips.position);
                // ===== 这一行是"漂移"的根：锚点抓的是**当时那一帧的姿势**，不是绑定姿势 =====
                //
                // 钉髋的职责是"身体的水平位置跟着胶囊走"，锚点必须是绑定姿势下
                // 髋骨的水平位置——对正常骨架就是模型原点附近（几厘米内）。
                // 可是这里抓的是 SetMocapRoot 被调用那一瞬 hips.position 的**实际**值。
                // 本动作库的走跑片段不是原地的（位移全在髋骨上），如果那一刻图已经
                // 求值过一帧，抓到的就是片段把髋骨甩出去之后的位置。
                //
                // 实机日志（21695 帧）把后果量得清清楚楚：
                //     bodyLX（髋骨在角色自身坐标系里的横向位置）从第 2 帧起
                //     恒等于 1.338 米，中位数 1.111，只有 1 帧小于 0.2 米；
                //     bodyLZ 恒为 0；hipRaw 恒等于 1.337。
                // 也就是说：**渲染出来的身体常年站在自己碰撞胶囊右侧 1.34 米处**，
                // 而且钉髋不是没拦住它，是钉髋自己每帧把它按在那里。
                // 它和转向倾身无关（相关系数 0.04）、和速度无关（0.01）、
                // 和步态无关（−0.27）——就是一个恒定偏置。
                //
                // 这同时解释了我一直复现不了的那条："360 度转圈推杆还是会穿墙"。
                // 我查了好几轮胶囊嵌墙，一次都没查到（这份日志里 deep 也全是 0）——
                // 因为穿墙的根本不是胶囊，是那个偏出去 1.34 米的身体：原地转一圈，
                // 它就绕着胶囊扫出一个半径 1.34 米的圆，扫过旁边的墙。
                //
                // 修法不是去猜"什么时候抓才对"，而是让它抓不坏：绑定姿势下髋骨
                // 横向偏移不可能有这么大，超过阈值就判定抓到的是动画帧，退回原点。
                // 正常骨架那几厘米的真实偏移仍然保留。
                float sc = Mathf.Abs(model.lossyScale.x) > 1e-4f
                         ? Mathf.Abs(model.lossyScale.x) : 1f;
                float offM = new Vector2(_hipsBindLP.x, _hipsBindLP.z).magnitude * sc;
                if (offM > MaxHipBindOffset)
                {
                    Debug.LogWarning("[HumanoidAnimator] 髋骨锚点偏移 " + offM.ToString("F2") +
                        "m，明显不是绑定姿势（多半抓到了动画帧），已退回模型原点。");
                    _hipsBindLP.x = 0f;
                    _hipsBindLP.z = 0f;
                }
                DbgHipBind = new Vector2(_hipsBindLP.x, _hipsBindLP.z) * sc;
            }
            else DbgHipBind = Vector2.zero;
            _footL = footL;
            _footR = footR;
            _ankleL = ankleL;
            _ankleR = ankleR;
            _groundLocalY = groundLocalY;
            _modelBaseY = model != null ? model.localPosition.y : 0f;
            _feetOffset = _feetTarget = 0f;
            _spine = FindBoneLoose(model, "spine1", "spine", "chest");
            _shoulderL = FindBoneLoose(model, "leftarm", "lupperarm", "leftshoulder");
            _shoulderR = FindBoneLoose(model, "rightarm", "rupperarm", "rightshoulder");
            _upLegR = FindBoneLoose(model, "rightupleg", "rightthigh");
        }

        // ===== 出手前摇的形体征兆（读招的第一手依据）=====
        //
        // UI 记号与地面指示器是**辅助**；玩家最终要学会的是看**身体**：
        // 兵器高举＝要劈、后拉＝要刺、整个人压低＝要扫腿。所有格斗类作品的
        // 读招教学都建立在这一层上——它不依赖任何界面元素，关了 UI 照样成立。
        //
        // 实现是在动画之上叠加一层"预备姿态"：不替换动作（动作还在演蓄力片段），
        // 而是把脊柱/肩/髋按招式族拧到位，于是每一族的剪影都不一样、且始终一致。
        int _windupKind = -1;
        int _windupShape = -1;
        float _windup01;

        /// <summary>设置前摇形体征兆。kind 见 TelegraphKind（-1=没有前摇）；
        /// t01=前摇进度（0 起手→1 即将出手），姿态随进度加深。</summary>
        public void SetWindup(int kind, float t01)
        {
            _windupKind = kind;
            if (kind >= 0) _windupShape = kind;   // 收招时还要用它把姿态平滑退回去
            _windup01 = Mathf.Clamp01(t01);
        }

        Transform _shoulderL, _shoulderR, _upLegR;

        /// <summary>把前摇姿态叠加到当前动画之上（在 LateUpdate，动画已求值完）。</summary>
        void ApplyWindup()
        {
            // 没有前摇时姿态平滑归零，避免出手瞬间"弹"一下
            float w = _windupKind < 0 ? 0f : Mathf.SmoothStep(0.25f, 1f, _windup01);
            _windupW = Mathf.MoveTowards(_windupW, w, Time.deltaTime / 0.12f);
            if (_windupW < 0.01f || _windupShape < 0) return;

            Transform spine = _spine != null ? _spine : (rig != null ? rig.torso : null);
            Transform hips = _hips != null ? _hips : (rig != null ? rig.pelvis : null);
            Transform shL = _shoulderL != null ? _shoulderL : (rig != null ? rig.shoulderL : null);
            Transform shR = _shoulderR != null ? _shoulderR : (rig != null ? rig.shoulderR : null);
            Transform legR = _upLegR != null ? _upLegR : (rig != null ? rig.hipR : null);
            float k = _windupW;

            switch ((TelegraphKind)_windupShape)
            {
                case TelegraphKind.Overhead:   // 高举过顶、上身后仰：最大的剪影变化
                    Pitch(spine, -26f * k);
                    Pitch(shL, -105f * k); Pitch(shR, -112f * k);
                    break;
                case TelegraphKind.Horizontal: // 拧腰、兵器拉到右体侧
                    Yaw(spine, 42f * k);
                    Yaw(shR, 40f * k); Pitch(shR, -20f * k);
                    break;
                case TelegraphKind.Thrust:     // 正面对齐、兵器收到腰际、重心下沉前压
                    Pitch(spine, 14f * k);
                    Pitch(shR, 34f * k); Yaw(shR, -18f * k);
                    Sink(hips, 0.04f * k);
                    break;
                case TelegraphKind.LowSweep:   // 整个人压低——最容易一眼认出的那一族
                    // 压低主要靠上身前俯，下沉量只给很小的一点：
                    // 骨盆是整条腿的父节点，把它往下挪多少，脚就往地里陷多少
                    //（这套骨骼没有 IK 去把脚留在原地）。8cm 大致藏在鞋和地面的
                    // 接触里，再多就会看见脚脖子插进地板。
                    Pitch(spine, 38f * k);
                    Sink(hips, 0.08f * k);
                    Pitch(shL, 26f * k); Pitch(shR, 26f * k);
                    break;
                case TelegraphKind.Kick:       // 提膝
                    Pitch(legR, -46f * k);
                    Pitch(spine, -10f * k);
                    break;
                case TelegraphKind.Spin:       // 反向拧身蓄力（转之前先往回卷）
                    Yaw(spine, -52f * k);
                    Yaw(shL, -34f * k); Yaw(shR, -34f * k);
                    break;
            }
        }

        float _windupW;

        // 绕【角色自身的世界轴】旋转，而不是绕骨骼的局部轴：
        // 动捕骨架的局部轴朝向各家各样（Mixamo 的骨骼 Y 沿骨长），
        // 按局部轴拧出来的姿态在不同模型上完全不同，形体征兆就不可能"每次都一样"。
        void Pitch(Transform t, float deg)
        {
            if (t == null || Mathf.Abs(deg) < 0.05f) return;
            t.rotation = Quaternion.AngleAxis(deg, transform.right) * t.rotation;
        }

        void Yaw(Transform t, float deg)
        {
            if (t == null || Mathf.Abs(deg) < 0.05f) return;
            t.rotation = Quaternion.AngleAxis(deg, Vector3.up) * t.rotation;
        }

        static void Sink(Transform t, float meters)
        {
            if (t == null || Mathf.Abs(meters) < 0.001f) return;
            t.position -= Vector3.up * meters;
        }

        /// <summary>宽松骨骼查找（上下半身分离用）：按关键词优先级，同词取名字最短的。</summary>
        static Transform FindBoneLoose(Transform root, params string[] keys)
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var k in keys)
            {
                Transform best = null; int bestLen = int.MaxValue;
                foreach (var t in all)
                {
                    if (t == null) continue;
                    var sb = new System.Text.StringBuilder(t.name.Length);
                    foreach (char c in t.name)
                        if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                    string n = sb.ToString();
                    if (!n.Contains(k) || n.Length >= bestLen) continue;
                    bestLen = n.Length; best = t;
                }
                if (best != null) return best;
            }
            return null;
        }

        /// <summary>
        /// 当前允不允许做上下半身分离：只有"身体归移动层管"的时候可以。
        /// 出招/翻滚/倒地/死亡/腾空时朝向由动作本身负责，再拧骨盆只会变形。
        ///
        /// 【为什么不能看 _pose】
        /// _pose 记的是"最后一次设的招"，**招播完了它不会自己变回 Idle**。
        /// 敌人由 EnemyController 手动 SetPose，出完一次手之后 _pose 就永久停在
        /// PoseState.Attack 上——用它做判据的话，敌人这辈子只有第一次交手之前
        /// 会做横移分离，之后绕圈又变回原地倒腾脚（也就是"漂移"复发）。
        /// 改看动画层**此刻在不在播招式**：招一播完，身体立刻交还给移动层。
        /// </summary>
        bool CanStrafe
        {
            get
            {
                if (!_grounded || _rest) return false;
                // 有成套方向片段时不做分离：方向已经由片段本身表达了
                if (Mecanim && _mecanim.HasDirectionalSet) return false;
                // 这几种姿态即使没有片段在播，身体朝向也不该被拧
                if (_pose == PoseState.Knockdown || _pose == PoseState.Death ||
                    _pose == PoseState.Dodge || _pose == PoseState.DodgeLeft ||
                    _pose == PoseState.DodgeRight || _pose == PoseState.Stagger ||
                    _pose == PoseState.Charge || _pose == PoseState.ChargeLoop ||
                    _pose == PoseState.CrouchIdle || _pose == PoseState.FallLoop) return false;
                if (Mecanim) return !_mecanim.ActionPlaying;
                // 程序化骨骼没有播放机：按招式曲线的时间轴判断这一招演完没有
                return !IsActionPose(_pose) || _t * Mathf.Max(1f, _poseTimeScale) > ProcPoseNominal;
            }
        }

        /// <summary>把移动夹角折算成下半身偏航：
        /// 前半球直接用夹角（限幅），后半球用"180° 的补角"——因为片段已经倒放，
        /// 倒放的走路本身就是朝后走，腿只需要再偏一点点。</summary>
        float TargetStrafeYaw()
        {
            if (_speed01 <= 0.05f) return 0f;
            float a = Mathf.DeltaAngle(0f, _moveAngle);
            // 后半球：片段已经倒放（脚朝身体背面走），腿只需再补上与"正后方"的偏差。
            if (Mathf.Abs(a) > 125f) a = Mathf.DeltaAngle(180f, _moveAngle);
            return Mathf.Clamp(a, -MaxLowerBodyYaw, MaxLowerBodyYaw);
        }

        /// <summary>上下半身分离：骨盆转向实际行进方向，脊柱回拧同样的角度，
        /// 于是【腿朝着走的方向、脸仍然对着目标】。找不到脊柱就只转下半身
        /// （仍然比"整个人转过去"接近正确）。</summary>
        void ApplyStrafeSplit(Transform hips, Transform spine, float dt)
        {
            _strafeYaw = Mathf.MoveTowards(_strafeYaw, TargetStrafeYaw(), 360f * dt);
            if (Mathf.Abs(_strafeYaw) < 0.5f || hips == null) return;
            hips.rotation = Quaternion.AngleAxis(_strafeYaw, Vector3.up) * hips.rotation;
            if (spine != null)
                spine.rotation = Quaternion.AngleAxis(-_strafeYaw, Vector3.up) * spine.rotation;
        }

        /// <summary>脚掌放平修正：异源骨骼脚踝 rest 朝向不同，Mixamo 脚踝旋转数据套
        /// 上去会让脚尖【持续下垂(踮脚)或持续上翘(鞋尖翘起)】。把脚尖俯仰角钳制到
        /// 大致贴地的窄带 [-35°下垂, +3°上翘] 内——两端异常都拉回接近水平贴地。
        /// 上翘上限收到近乎水平(角色贰的鞋在原模型里本就平贴地面)，
        /// 正常步态的自然绷脚仍在下垂带内不受影响。</summary>
        static void LevelAnkle(Transform ankle, Transform toe)
        {
            if (ankle == null || toe == null || ankle == toe) return;
            Vector3 v = toe.position - ankle.position;
            float len = v.magnitude;
            if (len < 1e-4f) return;
            float sinPitch = v.y / len;                     // >0 脚尖上翘，<0 下垂
            const float maxDropSin = -0.57f;                // 下垂上限 ≈35°
            const float maxRiseSin = 0f;                    // 上翘上限 0°（不许高过水平，彻底修鞋尖翘起）
            float clamped = Mathf.Clamp(sinPitch, maxDropSin, maxRiseSin);
            if (Mathf.Abs(clamped - sinPitch) < 1e-3f) return;
            Vector3 flat = new Vector3(v.x, 0, v.z);
            if (flat.sqrMagnitude < 1e-8f) return;
            float cosP = Mathf.Sqrt(Mathf.Max(0f, 1f - clamped * clamped));
            Vector3 target = (flat.normalized * cosP + Vector3.up * clamped) * len;
            ankle.rotation = Quaternion.FromToRotation(v, target) * ankle.rotation;
        }


        // ==================== 支撑脚锁定（foot lock IK）====================
        //
        // 【为什么必须有它——这一条是算出来的，不是感觉】
        // 转弯时角色绕**自身竖轴**旋转，而脚是根节点的子物体，于是踩住的那只脚
        // 会跟着一起扫过去。按实测参数（3.8m/s、0.61g、半径 2.41m、每步 1.39m）：
        //     一步之内身体转过 33°
        //     支撑脚离根 0.3m ⇒ 被拖 0.17m ；0.5m ⇒ 0.29m ；0.7m ⇒ 0.40m
        // 一步才 1.39m，横向被拖走 0.17~0.40m —— 这就是"转圈会滑动"。
        // 它跟播放速率无关（实测步幅比 0.92~1.00 是对的），也不是混合的问题：
        // 直线跑时步幅同步就足以让脚站住，一旦路径带曲率就必然拖。
        //
        // 动作库里没有跑动转弯片段，所以只剩下成熟战斗游戏的通行解：
        // **踩住的那一刻把脚钉在世界坐标上，让腿去迁就它。**
        //
        // 解析二骨 IK（先按余弦定理改膝角，再整条链绕髋对准目标）。
        // 数学已离线验证：目标在可达范围内时踝误差恒为 0，超出时误差正好等于
        // 超出量，且大腿/小腿长度一毫不变（绝不拉伸骨骼）。
        static void TwoBoneIK(Transform hip, Transform knee, Transform ankle, Vector3 target)
        {
            if (hip == null || knee == null || ankle == null) return;
            Vector3 a = hip.position, b = knee.position, c = ankle.position;
            float lab = (b - a).magnitude, lcb = (c - b).magnitude;
            if (lab < 1e-4f || lcb < 1e-4f) return;
            float lat = Mathf.Clamp((target - a).magnitude, 1e-4f, lab + lcb - 1e-4f);

            // ---- ① 弯曲：轴取【肢体自身平面的法线】。
            // 用外部 hint 当轴是错的——hint 不在 A-B-C 平面里时，转完 B 会离开平面，
            // 三角形就散了（离线验证时这个写法最坏残差 0.11m，换成平面法线后归零）。
            Vector3 axis = Vector3.Cross(c - a, b - a);
            if (axis.sqrMagnitude < 1e-10f) return;
            axis.Normalize();
            float ac_ab_0 = Mathf.Acos(Mathf.Clamp(
                Vector3.Dot((c - a).normalized, (b - a).normalized), -1f, 1f));
            float ba_bc_0 = Mathf.Acos(Mathf.Clamp(
                Vector3.Dot((a - b).normalized, (c - b).normalized), -1f, 1f));
            float ac_ab_1 = Mathf.Acos(Mathf.Clamp(
                (lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float ba_bc_1 = Mathf.Acos(Mathf.Clamp(
                (lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));
            hip.rotation = Quaternion.AngleAxis(
                (ac_ab_1 - ac_ab_0) * Mathf.Rad2Deg, axis) * hip.rotation;
            knee.rotation = Quaternion.AngleAxis(
                (ba_bc_1 - ba_bc_0) * Mathf.Rad2Deg, axis) * knee.rotation;

            // ---- ② 对准：整条链绕髋转，让踝指向目标。
            Vector3 c2 = ankle.position;
            Vector3 axis2 = Vector3.Cross(c2 - a, target - a);
            if (axis2.sqrMagnitude < 1e-10f) return;
            float aim = Mathf.Acos(Mathf.Clamp(
                Vector3.Dot((c2 - a).normalized, (target - a).normalized), -1f, 1f));
            hip.rotation = Quaternion.AngleAxis(aim * Mathf.Rad2Deg, axis2.normalized) * hip.rotation;
        }

        /// <summary>一条腿的锁定状态：踩住的世界坐标、权重、以及自校准的最低点。</summary>
        struct FootLock
        {
            public Vector3 pos;      // 踩住那一刻的世界坐标
            public float w;          // 0~1，进出都做渐变，避免"啪"地钉住
            public float minH;       // 该脚离地高度的历史最低（自校准踩地阈值）
            public bool init;
        }
        FootLock _lockL, _lockR;

        /// <summary>锁定修正量的封顶（米）。超过它就说明步幅同步本身有问题，
        /// 这时硬钉只会把腿拉成一字马——宁可让它滑，也不能扭断。</summary>
        const float FootLockMaxFix = 0.28f;
        /// <summary>支撑脚锁定总开关。**默认关。**
        ///
        /// 上一轮我一次性上了两个全新机制（支撑脚锁定、转向倾身）又把转向速率砍掉
        /// 六成——三件事一起改，实机一说"更烂了"就根本分不清是哪一个。这是方法错误。
        /// 现在全部挂到设置面板里，最新最险的这一个默认关着：先确认基线，
        /// 再一个一个打开。IK 每帧直接改写腿骨旋转，是这几条里唯一能把画面弄难看的。</summary>
        public static bool FootLockOn;
        /// <summary>转向倾身总开关（默认开：它只是绕正前轴滚一个角度，风险低）。</summary>
        public static bool TurnLeanOn = true;
        /// <summary>诊断：这一帧两只脚的锁定修正量（米）与权重。</summary>
        public float DbgFootFix { get; private set; }

        /// <summary>踩住的脚钉在世界坐标上；腿由二骨 IK 迁就它。</summary>
        void ApplyFootLock(Transform ankle, ref FootLock st, float dt)
        {
            if (ankle == null || visual == null) return;
            var knee = ankle.parent;
            var hip = knee != null ? knee.parent : null;
            if (knee == null || hip == null) return;

            float h = visual.InverseTransformPoint(ankle.position).y - _groundLocalY;
            if (!st.init) { st.init = true; st.minH = h; }
            // 历史最低点缓慢上浮（0.4m/s）：既能自校准出"踩地高度"，
            // 又不会被一次异常的低点永久带偏。
            st.minH = Mathf.Min(st.minH + 0.4f * dt, h);
            bool planted = h < st.minH + 0.05f;

            if (planted && st.w <= 0.001f) st.pos = ankle.position;   // 踩下的那一刻记位置
            st.w = Mathf.MoveTowards(st.w, planted ? 1f : 0f, dt / (planted ? 0.08f : 0.10f));
            if (st.w <= 0.001f) return;

            // 只锁水平：纵向留给动画自己（起伏、屈膝都该保留）
            Vector3 cur = ankle.position;
            Vector3 fix = new Vector3(st.pos.x - cur.x, 0f, st.pos.z - cur.z);
            float m = fix.magnitude;
            if (m > FootLockMaxFix) fix *= FootLockMaxFix / m;        // 封顶，绝不拉伸成一字马
            fix *= st.w;
            DbgFootFix = Mathf.Max(DbgFootFix, fix.magnitude);
            if (fix.sqrMagnitude < 1e-6f) return;
            TwoBoneIK(hip, knee, ankle, cur + fix);
        }

        /// <summary>
        /// 【二分定位开关】关掉动画图之后的**全部骨骼后处理**：
        /// 钉髋、转向倾身、上下半身拧腰、前摇叠加、双脚贴地校准、锁脚 IK。
        /// 关掉之后画面上就是 PlayableGraph 的原始输出，一根骨头都没被改过。
        ///
        /// 为什么要有它：找"漂移"找了八轮，每一轮都在图的**上游**（速度、权重、
        /// 相位、混合）找到一个真实机制、修掉、症状照旧。而图**下游**这六层
        /// 我一次都没验证过。与其再猜第九个机制，不如让这一刀切下去——
        /// 关掉后不漂，原因就在这六层里；照样漂，就与骨骼后处理无关，
        /// 我该去查角色位置与镜头的关系，而不是继续在动画里翻。
        ///
        /// 注意：钉髋一起关掉了，所以模型会带着片段自带的根位移往前"爬"，
        /// 与碰撞体分家。那是**这个测试模式的正常现象**，不是新 bug；
        /// 只用来看十秒钟内腿有没有停。
        /// </summary>
        public static bool BonePostFxOn = true;

        // ===== 诊断：画面上的身体到底跑到哪去了 =====
        //
        // 玩家报的"漂移"一直定位不到，是因为之前所有的度量都长在**胶囊**上：
        // 位移、速度、步幅、相位……而实机日志已经证明胶囊本身是干净的
        // （逐帧全覆盖，除入座滑行与切场传送外，单帧位移从没超过 0.13m）。
        // 那么玩家看见的漂移只可能发生在**渲染出来的身体相对胶囊**这一段上——
        // 而这一段，我一次都没有量过。
        //
        // 走跑片段不是原地的（位移在髋骨上），钉髋负责把它抵消掉。抵消一旦
        // 不完全或不连续，身体就会相对胶囊往前爬、再在循环点弹回去——那正是
        // "从 a 漂移到 b，中间没有脚步"和"不断重复回到原来位置"。
        //
        //   hipLeak = 髋骨在模型局部空间里偏离绑定位的水平距离
        //             ——钉髋漏掉了多少片段自带位移（应恒为 0）
        //   visStep = 渲染身体这一帧在世界里走了多远
        //             ——玩家眼睛看到的位移。它和胶囊的 stepLen 一旦对不上，
        //               对不上的那部分就是漂移本体。
        public float DbgHipLeak { get; private set; }
        public float DbgVisStep { get; private set; }
        /// <summary>本帧采样到的身体世界坐标（髋骨）。给漂移自检算矢量差用——
        /// 标量的 visStep 减 stepLen 会把方向不同的两段位移当成同一件事。</summary>
        public Vector3 DbgVisPos { get; private set; }
        public bool DbgVisValid { get; private set; }
        /// <summary>钉髋**之前**髋骨偏离绑定位多少（米）——片段这一帧想要的根位移。</summary>
        public float DbgHipRaw { get; private set; }
        /// <summary>本帧到底钉没钉髋。没钉时 DbgHipLeak 也是 0，光看它分不出来。</summary>
        public bool DbgPinOn { get; private set; }
        /// <summary>髋骨在**角色自身坐标系**里的水平位置。
        /// 身体相对胶囊到底有没有滑、往哪个方向滑，只有它说了算：
        /// 世界坐标里的位移混着胶囊平移和转身，分不开。</summary>
        public Vector2 DbgBodyLocal { get; private set; }
        /// <summary>钉髋锚点在模型空间里的水平偏移（米）。正常应接近 0；
        /// 它一大，身体就被钉在离胶囊那么远的地方。</summary>
        public Vector2 DbgHipBind { get; private set; }

        /// <summary>锚点横向偏移的合理上限（米）。超过就不可能是绑定姿势。
        /// 人的髋骨在绑定姿势下本来就该在模型中轴上，留 0.25m 已经很宽松。</summary>
        const float MaxHipBindOffset = 0.25f;
        Vector3 _visPrev; bool _visHas;

        /// <summary>在整条后处理跑完之后采样：这时的骨骼就是这一帧渲染出去的骨骼。</summary>
        void SampleVisualDrift()
        {
            // 量身体的**髋骨**而不是模型根：模型根的水平位置就是胶囊的位置，
            // 片段自带位移全在髋骨上，量根节点等于什么都没量。
            Transform m = _hips != null ? _hips
                        : _mocapModel != null ? _mocapModel : visual;
            if (m == null)
            {
                _visHas = false; DbgVisValid = false;
                DbgVisStep = 0f; DbgHipLeak = 0f; return;
            }
            Vector3 w = m.position;
            DbgVisStep = _visHas
                ? new Vector2(w.x - _visPrev.x, w.z - _visPrev.z).magnitude : 0f;
            _visPrev = w; _visHas = true;
            DbgVisPos = w; DbgVisValid = true;
            Vector3 bodyLp = transform.InverseTransformPoint(w);
            DbgBodyLocal = new Vector2(bodyLp.x, bodyLp.z);
            if (_hipsPin && _hips != null && _mocapModel != null)
            {
                Vector3 lp = _mocapModel.InverseTransformPoint(_hips.position);
                DbgHipLeak = new Vector2(lp.x - _hipsBindLP.x, lp.z - _hipsBindLP.z).magnitude
                             * Mathf.Abs(_mocapModel.lossyScale.x);
            }
            else DbgHipLeak = 0f;
        }

        // LateUpdate 里有好几处提前 return（降频、非 Mecanim、没钉髋），采样必须
        // 跑在所有分支之后，否则恰恰是"后处理没跑完"的那些帧量不到——而那正是
        // 最可疑的一批帧。所以本体挪进 LateUpdateBody，采样统一在这里收口。
        void LateUpdate()
        {
            LateUpdateBody();
            SampleVisualDrift();
        }

        void LateUpdateBody()
        {
            if (!BonePostFxOn) return;
            // ===== 钉髋绝不能跟着降频一起跳过 =====
            //
            // 原来这里第一行就是 `if (!_lodDue) return;`，理由写的是"骨骼没动，
            // 钉髋也无事可做"。那句话是错的：降频跳过的只是 **Tick**（不推进片段
            // 时间），而 **PlayableGraph 照样每帧求值并把姿态写进骨骼**——写进去的
            // 髋骨 XZ 带着片段自身的前进位移（本动作库的走跑片段不是原地的）。
            // 于是降频角色每一帧都在两个位置之间来回跳：
            //     跳过的帧 → 髋骨停在片段自带位移处（一个步幅可达 1.7m）
            //     更新的帧 → 被钉回绑定位
            // 1/2 降频就是每隔一帧摆一次，幅度接近一个身位。这就是"某些角色
            // 突然漂移到别处又回来"。玩家因为 _lodExempt 不受影响，所以只在
            // 别的角色身上看得到——但它同样是"动作看起来不同步"的一份来源。
            //
            // 钉髋本身只有一次 InverseTransformPoint + TransformPoint，
            // 比起被它防住的画面撕裂，这点开销可以忽略。降频要省的是下面那些
            // 三角函数与四元数（分离拧腰、前摇、双脚贴地校准），不是这两行。
            DbgPinOn = Mecanim && _hipsPin && _hips != null && _mocapModel != null &&
                       _pose != PoseState.Death && _pose != PoseState.Knockdown;
            if (DbgPinOn)
            {
                Vector3 pin = _mocapModel.InverseTransformPoint(_hips.position);
                // 钉之前先记一笔：这才是片段这一帧**想要**的根位移。
                // 钉完再量必然是 0（钉髋干的就是把它清零），拿钉后的值当"漏没漏"
                // 的证据等于什么都没证明——上一版的 hipLeak 就栽在这里，
                // 而且 _hipsPin 为 false 时它同样是 0，"钉得好"和"根本没钉"
                // 读数一模一样。strideRatio 我犯过一次同样的错，这次记在代码里。
                DbgHipRaw = new Vector2(pin.x - _hipsBindLP.x, pin.z - _hipsBindLP.z).magnitude
                            * Mathf.Abs(_mocapModel.lossyScale.x);
                pin.x = _hipsBindLP.x;
                pin.z = _hipsBindLP.z;
                _hips.position = _mocapModel.TransformPoint(pin);
            }
            else DbgHipRaw = 0f;
            if (!_lodDue) return;
            if (!Mecanim)
            {
                // 程序化方块骨骼：同一套上下半身分离（骨盆转、躯干回拧）
                if (rig != null && CanStrafe)
                    ApplyStrafeSplit(rig.pelvis, rig.torso, Time.deltaTime);
                else _strafeYaw = Mathf.MoveTowards(_strafeYaw, 0f, 360f * Time.deltaTime);
                ApplyWindup();
                return;
            }
            if (!_hipsPin || _hips == null || _mocapModel == null) return;

            // （钉髋已在本方法开头无条件做过——它不能跟着降频跳过，理由见那里。
            //   倒地/死亡两个姿态放行：那两段动作本来就是靠髋骨水平移动完成的，
            //   钉住的话人永远倒不下去，会停在下蹲到一半的姿势上。）

            // ===== 转向倾身：跑动转弯的向心力靠身体向内倾提供 =====
            //
            // 录屏里 13 秒每一帧「夹角 0°、移动 Jog Forward + Running」——
            // 身体以近 200°/s 在转，腿上却始终是一条直线跑循环，**没有任何
            // 迹象表明这个人正在转弯**。这就是"不是一步一个脚印转的"。
            // 动作库里没有跑动转弯片段（对齐表里方向环只有 前/后/左/右/斜），
            // 而拿侧移片段去凑是错的：转弯时速度相对身体仍然是正前方，
            // 播侧移会变成"横着挪"，那是另一种失真。
            //
            // 真人的做法只有一个自由度：**向内倾**，倾角就是 atan(a_lat / g)。
            // 这是几何恒等式，不是手感参数——0.6g 的转弯对应 31°。
            // 取六成、封顶 12°：够看得出在转弯，又不至于像摩托压弯。
            // 直线跑时 a_lat=0 ⇒ 倾角自然归零，不需要任何额外的收手逻辑。
            if (_mocapModel != null)
            {
                float dtl = Mathf.Max(Time.deltaTime, 1e-4f);
                float yawNow = transform.eulerAngles.y;
                if (!_leanInit) { _leanInit = true; _leanPrevYaw = yawNow; }
                float yawRate = Mathf.DeltaAngle(_leanPrevYaw, yawNow) / dtl;
                _leanPrevYaw = yawNow;
                bool leanOk = TurnLeanOn && _grounded && !_rest &&
                              _pose != PoseState.Knockdown && _pose != PoseState.Death;
                float aLat = yawRate * Mathf.Deg2Rad * Mathf.Max(0f, _actualSpeed);
                float want = leanOk
                    ? Mathf.Clamp(Mathf.Atan2(aLat, 9.81f) * Mathf.Rad2Deg * LeanFraction,
                                  -MaxLeanDeg, MaxLeanDeg)
                    : 0f;
                _lean = Mathf.MoveTowards(_lean, want, LeanSlewDegPerSec * dtl);
                // 绕模型本地正前轴滚转：+Z 正角把头顶推向 −X（左），
                // 所以右转（yawRate>0 ⇒ _lean>0）要用负角才是向右倾。
                _mocapModel.localRotation = Mathf.Abs(_lean) > 0.05f
                    ? Quaternion.AngleAxis(-_lean, Vector3.forward)
                    : Quaternion.identity;
            }

            // 横移/后撤的上下半身分离（只在贴地常规移动姿态下做——
            // 出招/翻滚/倒地时身体的朝向由动作自己负责，再拧一下只会变形）
            if (CanStrafe) ApplyStrafeSplit(_hips, _spine, Time.deltaTime);
            else _strafeYaw = Mathf.MoveTowards(_strafeYaw, 0f, 360f * Time.deltaTime);
            ApplyWindup();   // 前摇形体征兆叠加在动画之上（读招的第一手依据）

            // 休息姿态（坐/躺）：把骨盆锚到座面高度，而不是把脚底锚到地面。
            // 坐着的人脚是悬着/前伸的，躺着的人脚离地更远——沿用站立那套校准
            // 会把整个人往上顶（因为它总想把最低的那只脚放到地面上）。
            if (_rest && _hips != null && visual != null)
            {
                float nowY = visual.InverseTransformPoint(_hips.position).y;
                float wantY = visual.InverseTransformPoint(
                    new Vector3(_hips.position.x, _restPelvisY, _hips.position.z)).y;
                // full = 让骨盆正好落在座面上所需的模型偏移（与当前偏移无关的定值：
                // nowY 会随偏移一起动，两者相加抵消）。权重 0 时沿用站立校准值，
                // 1 时完全按座面锚定——于是坐下的过程没有任何跳变。
                float full = _feetOffset + (wantY - nowY);
                _feetTarget = Mathf.Clamp(Mathf.Lerp(_restBase, full, _restW), -2.5f, 2.5f);
                _feetOffset = Mathf.Lerp(_feetOffset, _feetTarget, 6f * Time.deltaTime);
                if (_mocapModel != null)
                {
                    var rp = _mocapModel.localPosition;
                    rp.y = _modelBaseY + _feetOffset;
                    _mocapModel.localPosition = rp;
                }
                return;
            }

            // 双脚贴地校准：动作数据把髋骨抬到【动作骨架】的高度，体型腿长不同的
            // 角色会踮脚悬空/陷地。持续量测最低脚的局部高度，平滑修正模型整体 Y。
            // 只在贴地常规姿态下更新目标（翻滚/击倒/腾空沿用上次校准值）。
            if ((_footL != null || _footR != null) && visual != null)
            {
                // 脚掌放平：站/走/跑/格挡等直立姿态都放平——不依赖 CharacterController
                // 的 isGrounded(静止时常误报 false 导致放平时断时续、鞋尖又翘起)，
                // 只要不是翻滚/击倒/腾空的动作姿态就恒定放平，脚不再翘尖。
                // 放平适用范围从「只有待机/格挡」扩到【一切贴地的常规姿态】。
                // 鞋尖上翘的成因是异源骨骼的脚踝 rest 朝向不同，Mixamo 的脚踝旋转
                // 数据套上去会让脚尖持续上翘——那是**每一帧**都在发生的，不只在待机。
                // 原来只在 Idle/Guard 放平，于是走一步、出一拳、被打一下脚尖就翘回去，
                // 停下来才平——读作"角色贰的鞋老是翘着"。只排除翻滚/击倒/死亡/腾空
                // （那些姿态里脚本来就不该贴地）。
                bool upright = _grounded &&
                    _pose != PoseState.Dodge && _pose != PoseState.Knockdown &&
                    _pose != PoseState.Death && _pose != PoseState.JumpAttack &&
                    _pose != PoseState.JumpKick && _pose != PoseState.AttackLeap;
                if (upright)
                {
                    LevelAnkle(_ankleL, _footL);
                    LevelAnkle(_ankleR, _footR);
                }
                // 脚底高度校准仍只在【静立姿态】更新目标：出招/受击时脚离地是正常的，
                // 拿那些帧去量最低脚会把整个模型上下拽（腾空/翻滚沿用上次值）
                //
                // 【本轮修正：真正判"站定"，而不是判"没在出招"】
                // 原判据只有 _pose == Idle/Guard。但 _pose 记的是**招式**姿态，
                // 走路跑步根本不经过 SetPose——移动全程 _pose 恒等于 Idle。
                // 于是这段"只在静立时校准"的代码，实际上**每一步都在跑**：
                //   _feetTarget = _feetOffset + (_groundLocalY - minY)
                // 是个反馈积分器，它每帧都想把"最低那只脚"按回地面。而跑动片段里
                // 最低脚的高度本来就随步态起伏（冲刺还有双脚离地的腾空相），
                // 于是整个模型被按着步频上下拽——读作**"人不是自己在跑，是被拉着腾空"**。
                // 搓杆换向时方向片段一混合，minY 更是直接跳变，模型跟着被猛拽一下。
                //
                // 又一次栽在同一条上（铁律一）：判据写在了"恰好相关的量"(_pose)上，
                // 而意图是"站定"。意图就在手边——_speed01。
                //
                // 【录屏证明：这段校准在实机上一次都没跑过】
                // 上面那段注释已经把道理写对了——判"站定"要用 _speed01，不要用 _pose——
                // 我却把 _pose 那一条**留着当额外的 &&**。而录屏里 13 秒每一帧都是
                // 「姿态 TurnRight」（_pose 卡死，见下面 Update 里的回落修复），
                // 于是 calibrate 恒为 false：**双脚贴地校准从来没有执行过**，
                // 模型的 Y 偏移一直停在初始值。脚不在地上，人自然读作
                // "不是自己在跑，是被拉着腾空"。
                // 现在只留意图本身：站定 + 贴地 + 身体确实是直立的。
                bool calibrate = _grounded && _speed01 < 0.05f &&
                    _pose != PoseState.Knockdown && _pose != PoseState.Death &&
                    _pose != PoseState.Dodge && !_rest;
                if (calibrate)
                {
                    float minY = float.MaxValue;
                    if (_footL != null)
                        minY = Mathf.Min(minY, visual.InverseTransformPoint(_footL.position).y);
                    if (_footR != null)
                        minY = Mathf.Min(minY, visual.InverseTransformPoint(_footR.position).y);
                    if (minY < float.MaxValue * 0.5f)
                        _feetTarget = Mathf.Clamp(_feetOffset + (_groundLocalY - minY), -0.9f, 0.9f);
                }
                _feetOffset = Mathf.Lerp(_feetOffset, _feetTarget, 5f * Time.deltaTime);
                var mp = _mocapModel.localPosition;
                mp.y = _modelBaseY + _feetOffset;
                _mocapModel.localPosition = mp;
            }

            // ===== 支撑脚锁定：必须放在整条 LateUpdate 的最后 =====
            // 它读的是踝骨的**最终世界坐标**，所以钉髋、倾身、贴地校准全部落定
            // 之后才能算——任何一个还在后面动，锁定就会追着一个还会变的目标。
            // 只在贴地的常规移动里做：出招/翻滚/倒地/休息时身体的支撑关系由动作
            // 自己负责，再去钉脚只会把腿拧坏。
            DbgFootFix = 0f;
            if (FootLockOn && _grounded && !_rest &&
                _pose != PoseState.Knockdown && _pose != PoseState.Death &&
                _pose != PoseState.Dodge && !_mecanim.ActionPlaying)
            {
                float dtl = Mathf.Max(Time.deltaTime, 1e-4f);
                ApplyFootLock(_ankleL, ref _lockL, dtl);
                ApplyFootLock(_ankleR, ref _lockR, dtl);
            }
            else { _lockL.w = 0f; _lockR.w = 0f; }
        }

        /// <summary>某招式对应动捕片段的有效时长（无片段返回 0；翻滚时长匹配用）。</summary>
        public float ActionClipLength(PoseState p) =>
            Mecanim ? _mecanim.ActionLength(p) : 0f;

        /// <summary>这个姿态有没有专用片段（没有就别切过去，留在原姿态更好看）。</summary>
        public bool HasPose(PoseState p) => Mecanim && _mecanim.HasAction(p);

        /// <summary>
        /// 按这一击的分量挑受击姿态：轻击抖一下，重击整个人被打退。
        ///
        /// 这是打击感里最便宜也最有效的一条：伤害数字必须能从**挨打的人**身上读出来。
        /// 只有一条受击片段时，10 点和 42 点看起来完全一样，玩家就无法从画面上
        /// 判断自己这一下打得重不重——连招的爽感有一大半来源于此。
        /// </summary>
        public static PoseState HitPoseFor(float damage, float knockback) =>
            damage >= 26f || knockback >= 4.5f ? PoseState.HitHeavy : PoseState.Hit;

        /// <summary>按这一击的分量设受击姿态（没有重受击片段时自动退回普通受击）。</summary>
        public void SetHitPose(float damage, float knockback, float duration = 0f)
        {
            var p = ResolveHitPose(damage, knockback);
            HitPose = p;                      // 走 FSM 映射的那一路（玩家）也用这一条
            SetPose(p, duration);
        }

        /// <summary>只挑姿态、不立刻切——供"先设好再请求 HitReaction 状态"的调用方用。</summary>
        public PoseState ResolveHitPose(float damage, float knockback)
        {
            // 空手只有一条受击片段——重受击那两条是持剑版本，套在拳脚型身上
            // 会变成"空着手却像握着剑被打退"。宁可不分档，也不串错动作集。
            if (!_armed) return PoseState.Hit;
            var p = HitPoseFor(damage, knockback);
            if (p == PoseState.HitHeavy && !HasPose(PoseState.HitHeavy)) p = PoseState.Hit;
            return p;
        }

        float _tumbleT = -1f, _tumbleDur = 0.55f;

        /// <summary>击飞后翻滚：被打飞很远时播放腾空后翻（配合控制器的飞行位移），
        /// 落地后进入倒地。dur 与飞行时长匹配。</summary>
        public void PlayTumble(float dur)
        {
            _tumbleDur = Mathf.Max(0.3f, dur);
            _tumbleT = 0f;
            SetPose(PoseState.Knockdown);
        }

        /// <summary>从倒地爬起：倒地片段倒放呈现"腿脚先动、身体逐渐立起"的起身过程。</summary>
        public void PlayGetUp()
        {
            _pendingGetUp = true;
            SetPose(PoseState.Idle);
        }

        /// <summary>动作库预览：按片段名直接试播（测试面板逐个动作验证效果）。</summary>
        public bool PlayClipPreview(string clipName) =>
            Mecanim && _mecanim.PlayClip(clipName);

        /// <summary>按关键词试播动作库片段（拔刀/收刀等按 "draw"/"sheath" 触发）。</summary>
        public bool PlayClipContaining(string key) =>
            Mecanim && _mecanim.PlayClipContaining(key);

        /// <summary>按关键词返回匹配片段时长（拔刀/收刀过渡与动画同步用）；无则 0。</summary>
        public float ClipLengthContaining(string key) =>
            Mecanim ? _mecanim.ClipLengthContaining(key) : 0f;

        // ================= 休息姿态（坐下 / 躺下 / 睡觉 / 起身） =================

        /// <summary>动作库里有这个片段吗（没有就让上层退回到不带动画的兜底做法）。</summary>
        public bool HasClip(string key) => Mecanim && _mecanim.HasClip(key);

        /// <summary>休息动作片段的原始时长（秒）；无此片段返回 0。</summary>
        public float RestClipLength(string key) => Mecanim ? _mecanim.RawClipLength(key) : 0f;

        /// <summary>
        /// 播一段休息动作（坐下/躺下/睡觉/起身）。reverse=倒放（从椅子上站起来
        /// 就是"坐下"反过来放），hold=播完停在末帧（坐着、躺着都是持续状态），
        /// start01/end01 只取片段中的一段（坐稳之后循环末段，人才有呼吸感）。
        /// 返回本次时长（秒）；0 = 没有这个片段。
        /// </summary>
        public float PlayRestClip(string key, bool reverse = false, bool hold = true,
            float speed = 1f, float fade = 0.35f, float start01 = 0f, float end01 = 1f) =>
            Mecanim ? _mecanim.PlayNamed(key, reverse, hold, speed, fade, start01, end01) : 0f;

        /// <summary>进入休息姿态：把骨盆锚到 pelvisWorldY（座面/床面高度 + 一点）。</summary>
        public void BeginRest(float pelvisWorldY)
        {
            _rest = true;
            _restPelvisY = pelvisWorldY;
            _restW = 0f;
            _restBase = _feetOffset;   // 从站立时的贴地校准值起算，入座那一瞬不跳
        }

        /// <summary>休息期间随时修正骨盆目标高度（换姿势：坐→躺，床面比座面高）。</summary>
        public void SetRestPelvisY(float pelvisWorldY) => _restPelvisY = pelvisWorldY;

        /// <summary>锚定权重 0→1：坐下/躺下的过程里渐入，起身的过程里渐出。</summary>
        public void SetRestWeight(float w01) => _restW = Mathf.Clamp01(w01);

        /// <summary>退出休息姿态，动作层交还给移动层。</summary>
        public void EndRest()
        {
            _rest = false;
            _restW = 0f;
            if (Mecanim) _mecanim.StopAction();
        }

        /// <summary>当前是否处在坐/躺的休息姿态里。</summary>
        public bool Resting => _rest;

        /// <summary>
        /// 按一串候选名播第一个找得到的片段（拔剑/收剑/放下兵器这类一次性动作）。
        /// 前面的候选优先——不同角色各自偏好的片段放在前面，后面的是通用兜底。
        /// 返回时长（秒）；0 = 一个都没有。
        /// </summary>
        public float PlayFirstClip(float speed, float fade, params string[] keys)
        {
            if (!Mecanim || keys == null) return 0f;
            foreach (var k in keys)
            {
                float len = _mecanim.PlayNamed(k, false, false, speed, fade);
                if (len > 0f) return len;
            }
            return 0f;
        }

        /// <summary>这一串候选里有没有片段（界面据此决定要不要显示对应按钮）。</summary>
        public bool HasAnyClip(params string[] keys)
        {
            if (!Mecanim || keys == null) return false;
            foreach (var k in keys) if (_mecanim.HasClip(k)) return true;
            return false;
        }

        string _lastMoveName;
        float _lastMoveNameAt;

        /// <summary>下一次翻滚姿态的目标时长（由 PlayerController 按实际翻滚时长写入；0=按片段原速）。</summary>
        public float DodgeDuration { get; set; }

        /// <summary>
        /// 下一次闪避该用哪个身法。默认前滚翻；锁定目标时按摇杆方向换成
        /// 左闪身 / 右闪身 / 后撤步——这是锁定战里"闪避有方向"的标准做法
        /// （面向目标时向左推杆按闪避，人应该往左侧闪，而不是转身朝左滚出去）。
        /// 由 PlayerController 在请求 Dodge 状态之前写入。
        /// </summary>
        public PoseState DodgePose { get; set; } = PoseState.Dodge;

        /// <summary>下一次受击该用哪条受击片段（轻/重）。由伤害方在请求
        /// HitReaction 之前按这一击的分量写入，见 HitPoseFor。</summary>
        public PoseState HitPose { get; set; } = PoseState.Hit;

        public void SetPose(PoseState p) => SetPose(p, 0f);

        /// <summary>设招。duration&gt;0 = 这一招在战斗逻辑里占用的时长——
        /// 动捕层据此反推播放速度、程序化骨骼层据此压缩关键帧时间轴，
        /// 让「画面上的动作」与「招式表的帧数据」严格同拍（出招不再拖泥带水）。</summary>
        public void SetPose(PoseState p, float duration)
        {
            _pose = p;
            _t = 0;
            _poseDur = duration;
            // 程序化骨骼的招式曲线按 ≈0.5s 一招编写；招式更短就等比压缩时间轴
            _poseTimeScale = duration > 0.02f && IsActionPose(p)
                ? Mathf.Clamp(ProcPoseNominal / duration, 1f, 2.4f) : 1f;
            _poseSerial++;   // 每次设招（含同名连招重触发）都递增，供动捕层重放动作

            // 战斗可读性：出招瞬间头顶弹出招式名（格斗游戏惯例），看清双方正在用什么招。
            // 节流：同名招 0.9s 内不重复弹——快速连点时浮字不再叠成一摞刷屏
            string mv = MoveNameOf(p);
            if (mv != null && (mv != _lastMoveName || Time.time - _lastMoveNameAt > 0.9f))
            {
                _lastMoveName = mv;
                _lastMoveNameAt = Time.time;
                CombatFeedback.MoveName(transform.position, mv, isEnemy);
            }
        }

        static string MoveNameOf(PoseState p)
        {
            // 与动作库片段一一对应的招式名（玩家在测试面板/招式表看到的同名动作）
            switch (p)
            {
                case PoseState.Attack: return "巨剑横斩";
                case PoseState.HeavyAttack: return "巨剑跳劈";
                case PoseState.AttackUp: return "巨剑撩斩";
                case PoseState.SwordThrust: return "突刺";
                case PoseState.AttackLeap: return "裂地跳劈";
                case PoseState.JumpAttack: return "空袭跳劈";
                case PoseState.AttackSpin: return "巨剑旋风斩";
                case PoseState.PunchJab: return "前手直拳";
                case PoseState.PunchCross: return "交叉重拳";
                case PoseState.AttackKick: return "正踢";
                case PoseState.SideKick: return "侧踹";
                case PoseState.SpinKick: return "旋身空翻踢";
                case PoseState.JumpKick: return "飞踢";
                case PoseState.Sweep: return "扫堂腿";
                case PoseState.Cast: return "心念术";
                default: return null;   // 受击/倒地/翻滚/格挡等不刷屏
            }
        }

        /// <summary>
        /// 连段专用：外部指定攻击姿态，并吞掉本次 FSM 状态变化，
        /// 避免 MapFromFsm 用默认攻击姿态覆盖连段姿态。
        /// duration = 该招在战斗逻辑里的时长（0=沿用片段默认速度）。
        /// </summary>
        public void PlayAttackPose(PoseState p, float duration = 0f)
        {
            if (fsm != null) _lastFsmState = fsm.Current;
            SetPose(p, duration);
        }

        // 招式时长（战斗逻辑给的帧数据）与程序化骨骼的时间轴缩放
        float _poseDur;
        float _poseTimeScale = 1f;
        const float ProcPoseNominal = 0.5f;   // 程序化招式曲线的编写长度

        float _actualSpeed = -1f;

        /// <summary>速度（0-1，相对奔跑速度）/ 是否蹲伏 / 是否着地 /
        /// 真实移速 m/s（供步幅同步，&lt;0=未提供）/
        /// moveAngleDeg = 移动方向相对角色**正面**的夹角（0=正前、±90=横移、180=后退）。</summary>
        /// <summary>跑动中出招是否只写上半身（腿继续走移动混合）。关掉＝回到旧行为。</summary>
        public static bool UpperBodyAttacksOn = true;
        /// <summary>
        /// 超过这个地面速度（m/s）才算"在移动"，才需要把腿还给移动层。
        ///
        /// 【1.2 是错的，它把所有真正发生的情况都挡在门外】
        /// 出招期间速度被 _attackSpeedFactor 压到 0.3 倍，实机就是 1.2~1.56 m/s——
        /// 正好压在这个门槛上下。于是遮罩在**最需要它的那一刻从不生效**：
        /// 日志里 `Great Sword Slash 5 权重1.00 速度1.2` 连续 5.63 秒，
        /// 2.4 秒里人挪了 3.81 米，全程没有一帧走路动画。
        /// 玩家的原话是"直接从 a 漂移到 b，这段过程没有脚步移动动画"。
        ///
        /// 门槛该问的是"看得出来在移动吗"，不是"跑起来了吗"。0.25 m/s 下
        /// 一秒挪 0.25 米，已经是肉眼可见的位移，腿就该跟着走。
        /// </summary>
        const float UpperBodySpeed = 0.25f;

        /// <summary>这一招是不是【上半身发力】——只有这些才适合在跑动中只写上半身。
        /// 公开是为了让 PlayerController 判断"这一招能不能只动上半身"：
        /// 不能的（腿法/旋身/位移型）必须靠**定步**让腿与地面一致，见那边的 attackFloor。</summary>
        public static bool IsUpperBodyAction(PoseState p) =>
            p == PoseState.Attack || p == PoseState.HeavyAttack || p == PoseState.AttackUp ||
            p == PoseState.SwordThrust || p == PoseState.PunchJab || p == PoseState.PunchCross ||
            p == PoseState.Cast || p == PoseState.CastProjectile ||
            p == PoseState.Charge || p == PoseState.ChargeLoop || p == PoseState.Guard ||
            // 言语攻击的微反应只写上半身：走着走着被说一句，是身子一颤，
            // 不是停下脚步。这样它既看得见，又一步都不耽误。
            p == PoseState.Flinch;

        public void SetLocomotion(float speed01, bool crouch, bool grounded, float actualSpeed = -1f,
            float moveAngleDeg = 0f, bool strafing = false)
        {
            _speed01 = Mathf.Clamp01(speed01);
            _crouch = crouch;
            _grounded = grounded;
            _actualSpeed = actualSpeed;
            _moveAngle = moveAngleDeg;
            _strafing = strafing;
        }
        bool _strafing;

        // ===== 横移 / 后退（面向目标不转身）=====
        //
        // 现在动作库里有成套的方向片段（后退、左右横移、斜向），移动层直接按方向
        // 混合真片段（见 PlayableAnimator 的方向混合）——这是大作的标准做法，
        // 上下半身的姿态都是对的。
        //
        // 下面这套【上下半身分离】只在**没有方向片段可用时**才启用（程序化方块骨骼、
        // 或某个角色用的动作库里只有向前走）：腿拧向实际行进方向、上身回拧对着目标。
        // 它是兜底，不是主路——真片段在场时必须让开，否则等于把已经正确的腿再拧一次。
        float _moveAngle;          // 目标夹角（度）
        float _strafeYaw;          // 平滑后的下半身偏航（度，正=向右）
        Transform _spine;          // 上半身回拧用（找不到就只转下半身）

        /// <summary>下半身偏航的上限：超过这个角度就改用【倒放】表达后退，
        /// 否则骨盆要拧出人体做不到的角度。55° 是常见取值（左右横跨自然，后撤走倒放）。</summary>
        const float MaxLowerBodyYaw = 55f;

        // ===================== 距离分级（远处的人不必每帧算） =====================
        //
        // 这个 Update 是四百多行、每帧几十次三角函数与四元数运算，而实机上同时
        // 存在**一百三十来个**角色（市民、路人、敌人）。此前没有任何距离或可见性
        // 剔除：站在城市另一头、屏幕上只有几个像素的路人，和贴脸的敌人跑一样多的
        // 计算。动捕角色更贵——每个各持一张 PlayableGraph，跳过一帧就是省下整张图
        // 的求值。
        //
        // 做法是**降频**而不是关掉：远处按 1/2、1/4、1/8 的频率更新，跳过的时间
        // 累加起来在真正更新的那一帧一次性推进（_lodDt），所以步态相位不会走慢——
        // 远处的人依旧在正常速度地走路，只是动画的时间分辨率低一些。
        // 各实例按 InstanceID 错开，避免所有人挤在同一帧更新形成周期性尖刺。
        const float LodFullDist = 30f;    // 以内每帧
        const float LodHalfDist = 55f;    // 1/2
        const float LodQuarterDist = 85f; // 1/4，再远 1/8

        static Transform _lodCam;
        bool _lodExempt;                  // 玩家永不降频
        static int _lodSeq;               // 自发的错开序号（见 OnEnable）
        int _lodJitter;
        int _lodStride = 1;
        float _lodDt, _nextLodEval;
        bool _lodDue = true;              // 本帧是否真的更新（LateUpdate 跟随同一决定）

        // 本组件原本没有 Awake/Start，分级参数要有地方初始化。
        // 用 OnEnable 而不是 Awake：对象池复用时也会重新跑到。
        void OnEnable()
        {
            // 错开量自己发号：Unity 6000.5 起 GetInstanceID() 被标记为
            // obsolete-as-error（CS0619）——本仓库 ShameLineEnemies 里早有同样的
            // 注记，我这次径直踩了回去。而这里要的只是"让各实例落在不同帧"，
            // 一个自增序号足矣，根本不需要引擎的实例 id。
            _lodJitter = (_lodSeq++) & 7;
            _lodExempt = GetComponent<Player.PlayerController>() != null;
            _lodStride = 1;
            _lodDue = true;
            _nextLodEval = 0f;
        }

        bool LodDueThisFrame()
        {
            if (_lodExempt) return true;
            if (Time.unscaledTime >= _nextLodEval)
            {
                // 半秒重估一次距离档位就够，且 Camera.main 会按 tag 查找，不能每帧调
                _nextLodEval = Time.unscaledTime + 0.5f;
                if (_lodCam == null && Camera.main != null) _lodCam = Camera.main.transform;
                if (_lodCam == null) _lodStride = 1;
                else
                {
                    float d = Vector3.Distance(_lodCam.position, transform.position);
                    _lodStride = d < LodFullDist ? 1
                               : d < LodHalfDist ? 2
                               : d < LodQuarterDist ? 4 : 8;
                }
            }
            if (_lodStride <= 1) return true;
            return ((Time.frameCount + _lodJitter) % _lodStride) == 0;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            // 状态映射照常每帧跑：它决定"该摆什么姿态"，属于玩法，不是画面开销
            if (fsm != null) MapFromFsm();

            // 降频：跳过的那几帧把时间攒着，轮到自己时一次性推进
            _lodDt += dt;
            _lodDue = LodDueThisFrame();
            if (!_lodDue) return;
            dt = _lodDt;
            _lodDt = 0f;

            // 动捕模式：用 Playables 播 Mixamo 片段，跳过下方程序化骨骼
            if (Mecanim)
            {
                _t += dt;
                _mecanim.SetLocomotion(_speed01, _actualSpeed, _moveAngle, _crouch, _strafing);
                // ===== 跑动中出招：招式只写上半身，腿继续走移动混合 =====
                // 日志里最顽固的一类"多个动画在打架"就是这个：动作层是**替换**
                // 移动层的，所以只要出招时人还在位移，腿就在演招式、身体却在平移。
                // 140 秒的实机日志里这样的片段有 33 段。
                //
                // 判据两条，缺一不可：
                //   · 招式本身是【上半身发力】的（挥砍/突刺/直拳/施法/格挡）。
                //     腿法（踢/扫堂腿/旋身）与位移型招式（跃劈/飞踢/突进斩）不在此列
                //     ——那些招的主体就是腿，遮掉腿等于把招砍没了。
                //   · 人确实在移动。站着打就该整个身体一起使劲，遮上半身反而软。
                _mecanim.SetActionUpperBodyOnly(
                    UpperBodyAttacksOn && _actualSpeed > UpperBodySpeed && IsUpperBodyAction(_pose));
                _mecanim.SetReady(_ready);
                _mecanim.SetArmed(_armed);
                if (_poseSerial != _lastMecanimSerial)
                {
                    _lastMecanimSerial = _poseSerial;
                    // 休息中（坐着/躺着）：战斗状态机把状态收回 Idle 是常态（人没在动），
                    // 但那会走到下面的 StopAction 把坐姿片段收掉——人会突然站起来。
                    // 休息姿态由 SitController 全权控制，这里一律不插手。
                    if (_rest) { }
                    else if (_pose == PoseState.Idle)
                    {
                        // 回到 Idle：从倒地恢复时先倒放起身，否则直接收招回移动层
                        if (_pendingGetUp) { _pendingGetUp = false; _mecanim.PlayGetUp(); }
                        else _mecanim.StopAction();
                    }
                    else
                    {
                        _pendingGetUp = false;
                        _mecanim.PlayAction(_pose, _poseDur);
                    }
                }

                // ===== 一次性招式播完之后，_pose 必须回落到 Idle =====
                //
                // 【录屏证明】13 秒里每一帧都是「姿态 TurnRight ｜ 动作 —」：
                // 动作层权重早就是 0（片段播完了），_pose 却永远停在 TurnRight。
                // 因为设招只有两个入口，两个都只在**变化时**触发：
                //   · MapFromFsm 只在战斗状态机换状态时跑，而移动全程状态不变；
                //   · PlayerController.UpdateMoveStatePose 只在转场那一帧设招。
                // 没有任何人负责"招式结束了，回到普通移动"。
                //
                // 这不是一个显示问题——**_pose 是被别处当条件读的**：
                // 贴地校准要求站定姿态、CanStrafe 有一张姿态排除表、
                // 动作层的接管判断也看它。锁死一个招式姿态等于让这些逻辑
                // 集体停在错误的分支上（上面的双脚校准就是这么被锁掉的）。
                //
                // 保持型姿态（格挡/蓄力/倒地/死亡/蹲伏待机/下落循环）不回落——
                // 它们本来就该停在那儿等外部收招。
                if (!_rest && _pose != PoseState.Idle &&
                    !_mecanim.ActionPlaying && !IsHoldPose(_pose))
                    _pose = PoseState.Idle;

                // 击飞翻滚：被打飞很远时在视根上做后翻滚（腾空后仰翻转 + 落地），
                // 让"飞出去"是一段真实的空翻而非僵直漂移
                bool clipRoll = _mecanim.HasAction(PoseState.Dodge);
                if (_tumbleT >= 0f && visual != null)
                {
                    _tumbleT += dt;
                    float k = Mathf.Clamp01(_tumbleT / _tumbleDur);
                    // 后翻两周（绕视根本地 X 负向=向后翻），腾空弧线上抛下落
                    visual.localRotation = Quaternion.Euler(-720f * k, 0, 0);
                    visual.localPosition = new Vector3(0, Mathf.Sin(k * Mathf.PI) * 0.5f, 0);
                    if (k >= 1f)
                    {
                        _tumbleT = -1f;
                        visual.localRotation = Quaternion.identity;
                        visual.localPosition = Vector3.zero;
                    }
                }
                // 翻滚：有专用翻滚片段（Stand To Roll 等）就播片段；
                // 没有则在视根上做程序化前滚翻兜底（低身+整体翻转一周）
                else if (visual != null)
                {
                    if (!clipRoll && _pose == PoseState.Dodge && _t < 0.42f)
                    {
                        float k = Mathf.Clamp01(_t / 0.4f);
                        visual.localRotation = Quaternion.Euler(k * 360f, 0, 0);
                        visual.localPosition = new Vector3(0, -0.35f * Mathf.Sin(k * Mathf.PI), 0);
                    }
                    else if (visual.localPosition != Vector3.zero ||
                             visual.localRotation != Quaternion.identity)
                    {
                        visual.localRotation = Quaternion.Slerp(visual.localRotation,
                            Quaternion.identity, 16f * dt);
                        visual.localPosition = Vector3.Lerp(visual.localPosition,
                            Vector3.zero, 16f * dt);
                    }
                }
                // 刀光拖尾：此前只在下方【程序化骨骼】分支里开合，动捕模式走到这里
                // 就 return 了——于是真正在跑的动捕角色从来没有过刀光。按招式的发力窗
                // 开合：出招即开，招式时长走完即关。
                if (weaponTrail != null)
                {
                    float swingLen = _poseDur > 0.02f ? _poseDur : _mecanim.ActionLength(_pose);
                    if (swingLen <= 0.01f) swingLen = 0.45f;
                    UpdateWeaponTrail(IsActionPose(_pose) && _pose != PoseState.Hit &&
                                      _pose != PoseState.HitHeavy && _t < swingLen);
                }
                _mecanim.Tick(dt);
                return;
            }

            if (rig == null || visual == null) return;
            // 程序化骨骼：招式曲线的时间轴按招式实际时长压缩（出招同样"脆快"）
            _t += dt * _poseTimeScale;
            float T = _t;

            bool moving = _speed01 > 0.03f && _grounded;
            if (moving) _phase += dt * Mathf.Lerp(3.5f, 11.5f, _speed01);

            // ---------- 基础目标角 ----------
            float pelvisY = -0.05f, pelvisX = 0f;
            float torsoP = Mathf.Lerp(1f, 9f, _speed01), torsoY = 0, torsoR = 0;
            float headP = 0, headY = 0;
            float shLp, shRp, shLr = 4, shRr = -4;   // 肩 pitch / roll
            float elL = 14, elR = 14;                 // 肘（正值=前臂前弯）
            float hipLp, hipRp, kneeLp, kneeRp;
            float footLp = 0, footRp = 0;

            // ---------- 步态循环 ----------
            float swing = moving ? Mathf.Sin(_phase) : 0;
            float legAmp = Mathf.Lerp(18f, 58f, _speed01);
            float armAmp = Mathf.Lerp(16f, 54f, _speed01);
            hipLp = swing * legAmp;
            hipRp = -swing * legAmp;
            kneeLp = Mathf.Max(0, Mathf.Sin(_phase - 1.9f)) * Mathf.Lerp(20f, 92f, _speed01);
            kneeRp = Mathf.Max(0, Mathf.Sin(_phase + 1.25f)) * Mathf.Lerp(20f, 92f, _speed01);
            shLp = -swing * armAmp;
            shRp = swing * armAmp;
            if (moving) { elL += Mathf.Lerp(16f, 78f, _speed01); elR += Mathf.Lerp(16f, 78f, _speed01); }
            torsoP += _speed01 * 13f;
            if (moving)
            {
                pelvisY += Mathf.Abs(Mathf.Cos(_phase)) * 0.055f * _speed01;
                footLp = -hipLp * 0.45f;
                footRp = -hipRp * 0.45f;

                float step = Mathf.Sin(_phase);
                float bob2 = Mathf.Cos(_phase * 2f);
                torsoY += -step * Mathf.Lerp(5f, 13f, _speed01);
                headY += step * Mathf.Lerp(2f, 6f, _speed01);
                pelvisX = bob2 * Mathf.Lerp(0.015f, 0.05f, _speed01);
                torsoR += -bob2 * Mathf.Lerp(2f, 6f, _speed01);
                headP += -Mathf.Abs(Mathf.Sin(_phase)) * 2.5f * _speed01;
                shLr += step * 4f * _speed01;
                shRr += step * 4f * _speed01;
                elL += Mathf.Max(0f, step) * 18f * _speed01;
                elR += Mathf.Max(0f, -step) * 18f * _speed01;
            }
            else if (_ready && _pose == PoseState.Idle)
            {
                // 格斗预备架势：半侧身对敌、双臂抬起护中、屈膝沉桩、踮步左右微晃。
                // 高手临战绝不垂手站着——这一处最能把"松垮路人"变成"临战高手"。
                float bob = Mathf.Sin(Time.time * 3.4f);
                float sway = Mathf.Sin(Time.time * 1.7f);
                torsoP += 9f; torsoY = 15f;
                shLp = 48f; shRp = 42f;
                shLr = 20f + sway * 2f; shRr = -22f - sway * 2f;
                elL = 95f; elR = 86f;                     // 屈肘持械/抱拳于胸前
                hipLp = 12f; hipRp = -8f;                 // 左前右后小弓步
                kneeLp = 28f + bob * 3f; kneeRp = 32f + bob * 3f;  // 屈膝沉桩+踮步
                footRp = -10f;
                pelvisY += -0.07f + bob * 0.02f;
                pelvisX = sway * 0.02f;
                headP = -2f;
            }
            else
            {
                torsoP += Mathf.Sin(Time.time * 1.8f) * 1.4f;
                shLr += Mathf.Sin(Time.time * 1.8f) * 1.5f;
                shRr -= Mathf.Sin(Time.time * 1.8f) * 1.5f;
            }

            // ---------- 蹲伏 / 空中 ----------
            if (_crouch && _grounded)
            {
                pelvisY -= 0.3f;
                hipLp += 52f; hipRp += 52f;
                kneeLp += 68f; kneeRp += 68f;
                footLp -= 18f; footRp -= 18f;
                torsoP += 18f;
                headP -= 12f;
            }
            if (!_grounded)
            {
                hipLp += 24f; hipRp += 30f;
                kneeLp += 46f; kneeRp += 52f;
                shLr += 38f; shRr -= 38f;
            }

            // ---------- 招式姿态 ----------
            Quaternion bodyRot = Quaternion.identity;
            Vector3 bodyPos = Vector3.zero;
            float bodyLerp = 10f;
            bool directBody = false;
            bool swinging = false;

            switch (_pose)
            {
                // ===================== 剑法 =====================
                case PoseState.Attack: // 横斩：拧腰蓄势 → 转腰送肩横扫 → 随势 → 归架
                {
                    torsoY = Kf(T, 0f,-22f, 0.09f,-26f, 0.22f,20f, 0.32f,28f, 0.5f,6f);   // 髋腰先动
                    shRp   = Kf(T, 0f,150f, 0.11f,158f, 0.25f,26f, 0.34f,14f, 0.5f,48f);  // 肩滞后=动力链
                    shRr   = Kf(T, 0f,-34f, 0.12f,-30f, 0.26f,10f, 0.5f,4f);
                    elR    = Kf(T, 0f,58f, 0.12f,60f, 0.24f,10f, 0.34f,8f, 0.5f,30f);
                    shLp   = Kf(T, 0f,-20f, 0.12f,-28f, 0.26f,14f, 0.5f,40f);
                    elL = 70f;
                    torsoP = Kf(T, 0f,3f, 0.22f,12f, 0.5f,4f);
                    torsoR = Kf(T, 0f,-6f, 0.22f,9f, 0.5f,0f);
                    Stance(ref hipLp, ref kneeLp, ref hipRp, ref kneeRp, ref footRp, T, 0.22f); // 弓步沉身
                    pelvisX = Kf(T, 0f,-0.03f, 0.24f,0.05f, 0.5f,0f);                    // 重心右→左
                    swinging = T > 0.1f && T < 0.32f;
                    break;
                }
                case PoseState.AttackUp: // 撩剑：沉身蓄力 → 由下往上斜撩 → 展身
                {
                    shRp   = Kf(T, 0f,22f, 0.1f,14f, 0.26f,170f, 0.34f,176f, 0.5f,60f);
                    shRr   = Kf(T, 0f,22f, 0.26f,-18f, 0.5f,-2f);
                    elR    = Kf(T, 0f,22f, 0.12f,16f, 0.26f,40f, 0.5f,30f);
                    shLp   = Kf(T, 0f,16f, 0.26f,-30f, 0.5f,12f);
                    elL = 66f;
                    torsoP = Kf(T, 0f,14f, 0.1f,18f, 0.28f,-12f, 0.5f,2f);               // 先沉后展身
                    torsoY = Kf(T, 0f,14f, 0.28f,-12f, 0.5f,0f);
                    hipLp  = Kf(T, 0f,20f, 0.1f,28f, 0.28f,8f, 0.5f,14f);
                    kneeLp = Kf(T, 0f,30f, 0.1f,44f, 0.28f,16f, 0.5f,24f);
                    hipRp  = -10f; kneeRp = Kf(T, 0f,26f, 0.28f,10f, 0.5f,16f); footRp = -12f;
                    pelvisY += Kf(T, 0f,-0.08f, 0.1f,-0.14f, 0.3f,0.04f, 0.5f,0f);        // 沉→蹬起
                    swinging = T > 0.1f && T < 0.32f;
                    break;
                }
                case PoseState.SwordThrust: // 突刺：收剑于腰蓄势 → 弓步爆发直刺 → 收
                {
                    shRp   = 90f; shRr = -4f;
                    elR    = Kf(T, 0f,110f, 0.1f,116f, 0.24f,2f, 0.34f,4f, 0.5f,70f);     // 收肘→直刺→收
                    shLp   = Kf(T, 0f,20f, 0.24f,-38f, 0.5f,10f); shLr = 30f; elL = 60f;
                    torsoP = Kf(T, 0f,2f, 0.1f,-6f, 0.24f,24f, 0.34f,20f, 0.5f,6f);       // 后坐→前扑
                    torsoY = Kf(T, 0f,-20f, 0.24f,10f, 0.5f,0f);
                    hipLp  = Kf(T, 0f,10f, 0.1f,4f, 0.24f,36f, 0.5f,18f);                 // 前腿弓深屈
                    kneeLp = Kf(T, 0f,20f, 0.24f,56f, 0.5f,28f);
                    hipRp  = Kf(T, 0f,-16f, 0.24f,-32f, 0.5f,-12f);                       // 后腿蹬直
                    kneeRp = Kf(T, 0f,20f, 0.24f,6f, 0.5f,16f); footRp = -14f;
                    pelvisY -= 0.06f;
                    swinging = T > 0.1f && T < 0.36f;
                    break;
                }
                case PoseState.HeavyAttack: // 重劈：举械过顶后仰蓄势 → 全身下劈 → 触地顿 → 收
                {
                    shRp   = Kf(T, 0f,120f, 0.16f,178f, 0.2f,180f, 0.34f,20f, 0.42f,14f, 0.6f,40f);
                    shLp   = Kf(T, 0f,110f, 0.16f,176f, 0.34f,22f, 0.6f,42f);
                    elL = elR = Kf(T, 0f,50f, 0.18f,40f, 0.34f,6f, 0.6f,30f);
                    shLr = 12f; shRr = -12f;
                    torsoP = Kf(T, 0f,-8f, 0.18f,-16f, 0.34f,30f, 0.42f,26f, 0.6f,6f);    // 后仰→前折
                    hipLp  = Kf(T, 0f,10f, 0.18f,4f, 0.34f,32f, 0.6f,16f);
                    kneeLp = Kf(T, 0f,20f, 0.34f,52f, 0.6f,28f);
                    hipRp  = Kf(T, 0f,-8f, 0.34f,-18f, 0.6f,-8f);
                    kneeRp = Kf(T, 0f,18f, 0.6f,16f); footRp = -12f;
                    pelvisY += Kf(T, 0f,0.02f, 0.16f,0.06f, 0.34f,-0.12f, 0.6f,-0.02f);   // 起身→沉劈
                    swinging = T > 0.18f && T < 0.5f;
                    break;
                }
                case PoseState.AttackSpin: // 旋身横扫：稍蓄后整身转一周，刃水平外展
                {
                    float sp = Kf(T, 0f,0f, 0.1f,0f, 0.44f,360f, 0.5f,360f);
                    visual.localRotation = Quaternion.Euler(0, sp, 0);
                    visual.localPosition = Vector3.zero;
                    shRp = Kf(T, 0f,120f, 0.12f,95f, 0.44f,90f, 0.5f,60f);
                    shRr = Kf(T, 0f,-40f, 0.12f,-78f, 0.5f,-40f); elR = 8f;
                    shLp = 60f; shLr = 55f; elL = 30f;
                    torsoP = 10f;
                    hipLp = hipRp = Kf(T, 0f,10f, 0.12f,24f, 0.5f,14f);
                    kneeLp = kneeRp = Kf(T, 0f,20f, 0.12f,34f, 0.5f,24f);
                    directBody = true;
                    swinging = T > 0.1f && T < 0.46f;
                    break;
                }
                case PoseState.AttackLeap: // 跃劈：屈膝起跳过顶 → 空中下劈 → 落地缓冲
                {
                    bodyPos = new Vector3(0,
                        Kf(T, 0f,-0.05f, 0.12f,0.12f, 0.32f,0.5f, 0.46f,0.08f, 0.6f,0f),
                        Kf(T, 0f,0f, 0.32f,0.32f, 0.6f,0f));
                    bodyLerp = 18f;
                    shRp = Kf(T, 0f,120f, 0.2f,178f, 0.42f,18f, 0.6f,40f);
                    shLp = Kf(T, 0f,120f, 0.2f,176f, 0.42f,20f, 0.6f,40f);
                    elL = elR = Kf(T, 0f,40f, 0.2f,35f, 0.42f,6f, 0.6f,28f);
                    torsoP = Kf(T, 0f,-8f, 0.2f,-16f, 0.44f,32f, 0.6f,8f);
                    hipLp = Kf(T, 0f,30f, 0.2f,52f, 0.46f,16f, 0.6f,12f);
                    hipRp = Kf(T, 0f,34f, 0.2f,56f, 0.46f,20f, 0.6f,16f);
                    kneeLp = Kf(T, 0f,50f, 0.2f,84f, 0.46f,42f, 0.6f,22f);
                    kneeRp = Kf(T, 0f,54f, 0.2f,88f, 0.46f,46f, 0.6f,24f);
                    swinging = T > 0.2f && T < 0.52f;
                    break;
                }
                case PoseState.JumpAttack: // 空中下劈：收腿、双手过顶下砸
                {
                    shRp = Kf(T, 0f,150f, 0.14f,180f, 0.34f,24f, 0.5f,40f);
                    shLp = Kf(T, 0f,140f, 0.14f,160f, 0.34f,30f, 0.5f,44f);
                    elL = elR = Kf(T, 0f,34f, 0.34f,8f, 0.5f,28f);
                    torsoP = Kf(T, 0f,-8f, 0.14f,-14f, 0.36f,34f, 0.5f,10f);
                    hipLp = 55f; hipRp = 62f; kneeLp = 85f; kneeRp = 92f;
                    swinging = T > 0.14f && T < 0.44f;
                    break;
                }
                case PoseState.Sweep: // 扫堂腿：深蹲整身速旋，一腿贴地扫出
                {
                    float sp = Kf(T, 0f,0f, 0.08f,0f, 0.4f,360f, 0.5f,360f);
                    visual.localRotation = Quaternion.Euler(0, sp, 0);
                    visual.localPosition = new Vector3(0, -0.42f, 0);
                    hipRp = Kf(T, 0f,40f, 0.12f,88f, 0.4f,84f, 0.5f,30f);
                    kneeRp = Kf(T, 0f,30f, 0.12f,6f, 0.5f,20f); footRp = -15f;
                    hipLp = 95f; kneeLp = 120f;
                    torsoP = 24f;
                    shLp = 40f; shRp = 20f; shLr = 40f; shRr = -25f;
                    directBody = true;
                    break;
                }

                // ===================== 拳法（拳击体系：出拳即回防的脆弹） =====================
                case PoseState.PunchJab: // 前手直拳：短蓄→爆发直伸→脆弹收回护面→架势
                {
                    shRp = Kf(T, 0f,70f, 0.06f,88f, 0.16f,90f, 0.28f,72f, 0.5f,58f);
                    elR  = Kf(T, 0f,100f, 0.05f,95f, 0.13f,6f, 0.2f,8f, 0.3f,96f, 0.5f,104f); // 弹出→猛收
                    shRr = -6f;
                    shLp = 58f; elL = 112f; shLr = 16f;                                  // 左手护面
                    torsoY = Kf(T, 0f,-8f, 0.13f,14f, 0.28f,4f, 0.5f,0f);                // 转腰送肩
                    torsoP = 6f;
                    hipLp = 6f; kneeLp = 18f; hipRp = -6f; kneeRp = 20f; footRp = -8f;   // 原地拳架
                    pelvisX = Kf(T, 0f,0f, 0.13f,0.025f, 0.5f,0f);
                    break;
                }
                case PoseState.PunchCross: // 后手重拳：拧腰碾步大幅转体→爆发→脆弹收回
                {
                    shLp = Kf(T, 0f,60f, 0.07f,88f, 0.18f,90f, 0.3f,70f, 0.5f,56f);
                    elL  = Kf(T, 0f,100f, 0.06f,95f, 0.16f,4f, 0.24f,8f, 0.34f,96f, 0.5f,104f);
                    shLr = 6f;
                    shRp = 58f; elR = 112f; shRr = -16f;                                 // 右手护面
                    torsoY = Kf(T, 0f,12f, 0.16f,-18f, 0.3f,-6f, 0.5f,0f);               // 大幅拧腰
                    torsoP = 6f;
                    hipLp = -8f; kneeLp = 18f; hipRp = 8f; kneeRp = 22f; footLp = -10f;  // 后脚碾转
                    pelvisX = Kf(T, 0f,0f, 0.16f,-0.03f, 0.5f,0f);
                    break;
                }

                // ===================== 腿法（提膝→弹踢→收腿→落，泰拳/散打式发力） =====================
                case PoseState.AttackKick: // 正蹬：提膝蓄力 → 弹踢直出 → 收膝 → 落步
                {
                    hipRp  = Kf(T, 0f,10f, 0.12f,95f, 0.24f,100f, 0.34f,70f, 0.5f,10f);
                    kneeRp = Kf(T, 0f,20f, 0.12f,96f, 0.2f,4f, 0.28f,10f, 0.36f,82f, 0.5f,20f); // 提膝→弹→收
                    footRp = Kf(T, 0f,0f, 0.2f,-26f, 0.5f,0f);                           // 勾脚背蹬出
                    hipLp  = -6f; kneeLp = Kf(T, 0f,10f, 0.18f,20f, 0.5f,12f);           // 支撑腿稳桩
                    torsoP = Kf(T, 0f,4f, 0.2f,-16f, 0.34f,-8f, 0.5f,2f);                // 踢出后仰配重
                    torsoR = Kf(T, 0f,0f, 0.2f,6f, 0.5f,0f);
                    shLp = Kf(T, 0f,20f, 0.2f,46f, 0.5f,20f); shRp = Kf(T, 0f,-10f, 0.2f,-32f, 0.5f,-10f);
                    shLr = 35f; shRr = -38f; elL = elR = 55f;                            // 双臂张开平衡
                    pelvisY += Kf(T, 0f,0f, 0.12f,0.05f, 0.5f,0f);
                    break;
                }
                case PoseState.SideKick: // 侧踢：提膝 → 侧身蹬出（身体侧倾表现）→ 收膝落步
                {
                    hipRp  = Kf(T, 0f,15f, 0.12f,80f, 0.24f,88f, 0.34f,55f, 0.5f,15f);
                    kneeRp = Kf(T, 0f,30f, 0.12f,92f, 0.2f,6f, 0.28f,12f, 0.36f,80f, 0.5f,24f);
                    footRp = Kf(T, 0f,0f, 0.2f,-20f, 0.5f,0f);
                    hipLp  = -6f; kneeLp = Kf(T, 0f,12f, 0.2f,22f, 0.5f,14f);
                    torsoP = -6f;
                    torsoR = Kf(T, 0f,0f, 0.2f,-18f, 0.34f,-14f, 0.5f,0f);               // 侧倾配重
                    shLp = Kf(T, 0f,20f, 0.2f,36f, 0.5f,20f); shRp = -26f;
                    shLr = 46f; shRr = -46f; elL = elR = 45f;
                    break;
                }
                case PoseState.SpinKick: // 后旋踢：转身蓄力 → 转体带腿横扫（鞭腿）→ 收
                {
                    float sp = Kf(T, 0f,0f, 0.12f,0f, 0.44f,360f, 0.5f,360f);
                    visual.localRotation = Quaternion.Euler(0, sp, 0);
                    visual.localPosition = Vector3.zero;
                    hipRp  = Kf(T, 0f,10f, 0.16f,80f, 0.32f,88f, 0.44f,30f, 0.5f,20f);
                    kneeRp = Kf(T, 0f,30f, 0.16f,74f, 0.32f,6f, 0.44f,44f, 0.5f,24f);    // 转体中鞭出
                    footRp = -12f;
                    hipLp = 18f; kneeLp = Kf(T, 0f,24f, 0.16f,34f, 0.5f,26f);
                    torsoP = 12f; torsoR = Kf(T, 0f,-4f, 0.32f,-12f, 0.5f,-6f);
                    shLp = 40f; shLr = 50f; shRp = 30f; shRr = -50f; elL = elR = 30f;
                    directBody = true;
                    break;
                }
                case PoseState.JumpKick: // 飞踢：腾空提膝 → 空中弹踢 → 收腿
                {
                    hipRp  = Kf(T, 0f,30f, 0.1f,92f, 0.24f,100f, 0.34f,60f, 0.5f,30f);
                    kneeRp = Kf(T, 0f,60f, 0.1f,92f, 0.2f,4f, 0.3f,44f, 0.5f,62f);
                    footRp = Kf(T, 0f,0f, 0.2f,-24f, 0.5f,0f);
                    hipLp = 60f; kneeLp = 110f;                                          // 后腿收紧
                    torsoP = Kf(T, 0f,-10f, 0.2f,-18f, 0.5f,-6f);
                    shLp = 42f; shRp = -34f; shLr = 40f; shRr = -40f; elL = elR = 55f;
                    break;
                }

                // ===================== 蓄力 / 防守 / 施法 =====================
                case PoseState.Charge: // 蓄力：举械后引、沉腰扣桩、蓄势微颤
                {
                    shRp = Kf(T, 0f,110f, 0.3f,152f); shRr = -22f; elR = Kf(T, 0f,70f, 0.3f,45f);
                    shLp = 35f; shLr = 25f; elL = 55f;
                    torsoP = 12f + Mathf.Sin(_t * 26f) * 1.4f;                            // 蓄势微颤
                    torsoY = Kf(T, 0f,-4f, 0.3f,-16f);
                    hipLp += 30f; hipRp += 30f; kneeLp += 42f; kneeRp += 42f;
                    pelvisY -= Kf(T, 0f,0.04f, 0.3f,0.14f);
                    break;
                }
                case PoseState.Guard: // 防守架势：双臂抬起格挡于身前
                    shLp = 62f; shRp = 62f;
                    shLr = 22f; shRr = -22f;
                    elL = elR = 100f;
                    torsoP += 5f; torsoY = 8f;                                            // 半侧身减少受击面
                    hipLp = 6f; hipRp = 6f; kneeLp = 22f; kneeRp = 22f;
                    break;
                case PoseState.Cast:
                    shLp = 92f; shRp = 92f;
                    elL = elR = 12f;
                    torsoP -= 4f;
                    break;

                // ===================== 受击 / 硬直 / 翻滚 / 倒地 =====================
                case PoseState.Hit:
                {
                    float d = Mathf.Max(0, 1f - _t * 2.4f);
                    torsoP -= 32f * d; torsoR += 8f * d; headP -= 22f * d;
                    shLp -= 30f * d; shRp -= 30f * d; shLr += 40f * d; shRr -= 40f * d;
                    hipLp += 14f * d; hipRp += 14f * d; kneeLp += 18f * d; kneeRp += 18f * d;
                    pelvisY -= 0.08f * d;
                    break;
                }
                case PoseState.Stagger:
                {
                    float d = Mathf.Max(0, 1f - _t / 1.5f);
                    torsoR = Mathf.Sin(_t * 22f) * 12f * d;
                    headY = Mathf.Sin(_t * 17f) * 14f * d;
                    shLp = -12f; shRp = -12f;
                    break;
                }
                case PoseState.Dodge:
                {
                    float k = Mathf.Clamp01(_t / 0.35f);
                    visual.localRotation = Quaternion.Euler(k * 360f, 0, 0);
                    visual.localPosition = new Vector3(0, Mathf.Sin(k * Mathf.PI) * 0.15f - 0.25f, 0);
                    hipLp = hipRp = 95f; kneeLp = kneeRp = 110f;
                    shLp = shRp = 70f; elL = elR = 100f; headP = 25f;
                    directBody = true;
                    break;
                }
                case PoseState.Knockdown:
                    bodyRot = Quaternion.Euler(-78f, 0, 6f);
                    bodyPos = new Vector3(0, -0.5f, 0.25f);
                    shLr = 70f; shRr = -70f;
                    hipLp = 15f; hipRp = 28f; kneeLp = 20f; kneeRp = 12f;
                    bodyLerp = 9f;
                    break;
                case PoseState.Death:
                    bodyRot = Quaternion.Euler(-84f, 0, 14f);
                    bodyPos = new Vector3(0,
                        Mathf.Lerp(-0.5f, -1.5f, Mathf.Clamp01((_t - 1.4f) / 1.4f)), 0.25f);
                    shLr = 80f; shRr = -65f; shLp = -20f; shRp = 30f;
                    hipLp = 10f; hipRp = 24f; torsoR = 8f;
                    bodyLerp = 5f;
                    break;
            }

            if (!directBody)
            {
                float bk = bodyLerp * dt;
                visual.localRotation = Quaternion.Slerp(visual.localRotation, bodyRot, bk);
                visual.localPosition = Vector3.Lerp(visual.localPosition, bodyPos, bk);
            }

            // ---------- 应用关节 ----------
            bool attackPose = IsActionPose(_pose);
            // 出招用更高的跟随系数，保证爆发相位的脆快与力道（不被过度平滑吞掉）
            float k2 = Mathf.Clamp01((attackPose ? 34f : 13f) * dt);
            rig.pelvis.localPosition = Vector3.Lerp(rig.pelvis.localPosition,
                new Vector3(pelvisX, pelvisY, 0), k2);
            J(rig.torso, torsoP, torsoY, torsoR, k2);
            J(rig.head, headP, headY, 0, k2);
            // 肩/髋俯仰应用时取负：几何向下延伸，取负后语义统一为「正值=向前方出击」
            J(rig.shoulderL, -shLp, 0, shLr, k2);
            J(rig.shoulderR, -shRp, 0, shRr, k2);
            J(rig.elbowL, -elL, 0, 0, k2);
            J(rig.elbowR, -elR, 0, 0, k2);
            J(rig.hipL, -hipLp, 0, 0, k2);
            J(rig.hipR, -hipRp, 0, 0, k2);
            J(rig.kneeL, kneeLp, 0, 0, k2);
            J(rig.kneeR, kneeRp, 0, 0, k2);
            J(rig.footL, footLp, 0, 0, k2);
            J(rig.footR, footRp, 0, 0, k2);

            ApplyWeaponFlourish();

            UpdateWeaponTrail(swinging);
        }

        /// <summary>
        /// 刀光的开关与清点。
        ///
        /// 【为什么单独抽出来】这段逻辑此前只写在 Mecanim 那条路径上，
        /// 程序化骨骼这条只有一句 `weaponTrail.emitting = swinging`——**没有 Clear()**。
        /// 于是角色贰（走程序化骨骼那条）收招之后，已经吐出去的拖尾点还挂在身上：
        /// TrailRenderer 按**缩放时间**老化，而言语攻防面板、暂停、顿帧都会把
        /// timeScale 打到 0，那一刻拖尾就永远不再消失——玩家截图里挂在人身上的
        /// 那两片白色带子就是它。
        ///
        /// 两条路径共用同一份逻辑，以后再加第三种骨骼也不会漏。
        /// </summary>
        void UpdateWeaponTrail(bool swinging)
        {
            if (weaponTrail == null) return;
            if (weaponTrail.emitting != swinging)
            {
                weaponTrail.emitting = swinging;
                // 收招就把已有的刀光抹掉，不指望它自己淡出（见上面 timeScale 的坑）
                if (!swinging) weaponTrail.Clear();
            }
            // 时间停住时也不留残迹：面板/顿帧那一帧可能正好停在挥砍中间
            if (!swinging && Time.timeScale < 0.01f && weaponTrail.positionCount > 0)
                weaponTrail.Clear();
        }

        /// <summary>攻击类姿态（用更高的关节跟随系数，保证爆发相位脆快有力）。</summary>
        static bool IsActionPose(PoseState p) =>
            p == PoseState.Attack || p == PoseState.AttackUp || p == PoseState.SwordThrust ||
            p == PoseState.HeavyAttack || p == PoseState.AttackSpin || p == PoseState.AttackLeap ||
            p == PoseState.JumpAttack || p == PoseState.Sweep ||
            p == PoseState.PunchJab || p == PoseState.PunchCross ||
            p == PoseState.AttackKick || p == PoseState.SideKick || p == PoseState.SpinKick ||
            p == PoseState.JumpKick || p == PoseState.Hit || p == PoseState.HitHeavy ||
            p == PoseState.DashAttack;

        /// <summary>通用弓步：前腿(左)出招时踏前屈膝，后腿(右)蹬撑，重心随出招前压。</summary>
        static void Stance(ref float hipLp, ref float kneeLp, ref float hipRp, ref float kneeRp,
            ref float footRp, float t, float strikeAt)
        {
            hipLp  = Kf(t, 0f,8f, strikeAt,26f, 0.5f,14f);
            kneeLp = Kf(t, 0f,18f, strikeAt,44f, 0.5f,26f);
            hipRp  = Kf(t, 0f,-16f, strikeAt,-22f, 0.5f,-10f);
            kneeRp = Kf(t, 0f,20f, 0.5f,16f);
            footRp = -12f;
        }

        /// <summary>
        /// 兵器耍花：刀刃轨迹与出招相位同步——预备相位刃在后蓄，爆发相位刃快速划过，
        /// 收势归位。之前是全程一条 lerp（"死死握剑感"的残留），现在与身法一致。
        /// </summary>
        void ApplyWeaponFlourish()
        {
            if (weaponPivot == null) return;
            Quaternion rest = Quaternion.Euler(-30f, 0, 8f);
            float T = _t, sw;

            switch (_pose)
            {
                case PoseState.Attack: // 横斩：预备刃在右后，爆发时横扫过体前
                    sw = Kf(T, 0f,0f, 0.1f,0f, 0.26f,1f, 0.5f,1f);
                    weaponPivot.localRotation = Quaternion.Euler(
                        Mathf.Lerp(-110f, 118f, sw), Mathf.Lerp(-45f, 45f, sw), 0);
                    break;
                case PoseState.AttackUp: // 撩剑：自下往上反撩画弧
                    sw = Kf(T, 0f,0f, 0.1f,0f, 0.28f,1f, 0.5f,1f);
                    weaponPivot.localRotation = Quaternion.Euler(
                        Mathf.Lerp(120f, -128f, sw), Mathf.Lerp(30f, -30f, sw), 0);
                    break;
                case PoseState.SwordThrust: // 突刺：起手绕腕小剑花后刃指正前
                    sw = Kf(T, 0f,0f, 0.1f,0f, 0.26f,1f, 0.5f,1f);
                    weaponPivot.localRotation = Quaternion.Euler(
                        Mathf.Lerp(46f, -8f, sw), 0, Mathf.Lerp(-170f, 0f, sw));
                    break;
                case PoseState.AttackLeap:
                case PoseState.HeavyAttack: // 过顶大轮劈
                    sw = Kf(T, 0f,0f, 0.18f,0f, 0.4f,1f, 0.6f,1f);
                    weaponPivot.localRotation = Quaternion.Euler(
                        Mathf.Lerp(-155f, 120f, sw), Mathf.Lerp(-25f, 25f, sw), 0);
                    break;
                case PoseState.AttackSpin: // 旋斩：刃持平随身旋转
                    weaponPivot.localRotation = Quaternion.Slerp(weaponPivot.localRotation,
                        Quaternion.Euler(95f, 0, -20f), 18f * Time.deltaTime);
                    break;
                case PoseState.Charge: // 蓄力：刃举于脑后蓄势微颤
                    weaponPivot.localRotation = Quaternion.Slerp(weaponPivot.localRotation,
                        Quaternion.Euler(-135f + Mathf.Sin(_t * 24f) * 4f, 0, -15f),
                        12f * Time.deltaTime);
                    break;
                case PoseState.JumpAttack:
                    sw = Kf(T, 0f,0f, 0.14f,0f, 0.34f,1f, 0.5f,1f);
                    weaponPivot.localRotation = Quaternion.Euler(Mathf.Lerp(-160f, 100f, sw), 0, 0);
                    break;
                case PoseState.Cast: // 施法：绕腕立剑花一周
                    weaponPivot.localRotation = Quaternion.Euler(0, 0, _t / 0.4f * 360f);
                    break;
                default:
                    // 临战：刃举于身前中位预备（斜指前上，随踮步微动）；否则静息斜立体侧
                    Quaternion hold = _ready
                        ? Quaternion.Euler(-70f + Mathf.Sin(Time.time * 3.4f) * 4f, 10f, -6f)
                        : rest * Quaternion.Euler(Mathf.Sin(Time.time * 1.6f) * 3f, 0, 0);
                    weaponPivot.localRotation = Quaternion.Slerp(
                        weaponPivot.localRotation, hold, 8f * Time.deltaTime);
                    break;
            }
        }

        /// <summary>保持型姿态：播到末尾就停住等外部收招，不该自动回落到 Idle。
        /// 与 PlayableAnimator 的 ActionMap 里 hold=true 的那几条对应。</summary>
        static bool IsHoldPose(PoseState p) =>
            p == PoseState.Guard || p == PoseState.Charge || p == PoseState.ChargeLoop ||
            p == PoseState.Knockdown || p == PoseState.Death ||
            p == PoseState.CrouchIdle || p == PoseState.FallLoop;

        void MapFromFsm()
        {
            if (fsm.Current == _lastFsmState) return;
            var prev = _lastFsmState;
            _lastFsmState = fsm.Current;
            // 倒地→恢复行动：先播起身过程（倒地片段倒放），不许原地瞬间站直
            if (prev == CombatState.Knockdown &&
                (fsm.Current == CombatState.Locomotion || fsm.Current == CombatState.Idle))
            {
                PlayGetUp();
                return;
            }
            switch (fsm.Current)
            {
                case CombatState.LightAttack: SetPose(PoseState.Attack); break;
                case CombatState.HeavyAttack:
                case CombatState.Finisher: SetPose(PoseState.HeavyAttack); break;
                // 翻滚走【时长驱动】：PlayerController 会把本次翻滚的实际时长写进
                // DodgeDuration，片段按这个时长加速播完。不这么做的话，
                // 逻辑上翻滚 0.35 秒就结束了，动画却还在按片段原速演 0.8 秒，
                // 于是"人已经能动了，画面还在滚"——读作闪避迟钝。
                case CombatState.Dodge: SetPose(DodgePose, DodgeDuration); break;
                case CombatState.HitReaction: SetPose(HitPose); break;
                // 【心理失守用"稳住自己"，不用踉跄】
                // 高反刍会把专注抽干，专注归零即触发短暂失守，而它原来落到
                // PoseState.Stagger——首选片段是 Stunned，那是一段大幅度踉跄，
                // 玩家读作"倒地"，而且"太明显"。
                // 心理上的失守不是身体被打倒，是**撑住**：换成防御姿态，
                // 人架起来稳一下，既看得出"这一下受了影响"，又不是被打趴。
                case CombatState.MentalStagger: SetPose(PoseState.Guard); break;
                case CombatState.Knockdown: SetPose(PoseState.Knockdown); break;
                case CombatState.InnerPowerCast: SetPose(PoseState.Cast); break;
                case CombatState.Death: SetPose(PoseState.Death); break;
                default: SetPose(PoseState.Idle); break;
            }
        }

        /// <summary>分段关键帧插值：kv = t0,v0,t1,v1,...（t 递增），段间用 smoothstep 缓入缓出。</summary>
        static float Kf(float t, params float[] kv)
        {
            int n = kv.Length / 2;
            if (n == 0) return 0f;
            if (t <= kv[0]) return kv[1];
            if (t >= kv[(n - 1) * 2]) return kv[(n - 1) * 2 + 1];
            for (int i = 0; i < n - 1; i++)
            {
                float ta = kv[i * 2], tb = kv[(i + 1) * 2];
                if (t <= tb)
                {
                    float u = (tb - ta) > 1e-5f ? (t - ta) / (tb - ta) : 1f;
                    return Mathf.Lerp(kv[i * 2 + 1], kv[(i + 1) * 2 + 1], Mathf.SmoothStep(0f, 1f, u));
                }
            }
            return kv[(n - 1) * 2 + 1];
        }

        static void J(Transform t, float x, float y, float z, float k)
        {
            if (t == null) return;
            t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.Euler(x, y, z), k);
        }
    }
}
