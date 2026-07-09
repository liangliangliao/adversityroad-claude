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

        /// <summary>临战状态：为真时静立会摆出格斗架势（持械/抱拳、沉桩、踮步微动）。</summary>
        public void SetCombatReady(bool ready) => _ready = ready;

        /// <summary>切到动捕模式：成功接管返回 true；失败保持程序化骨骼。</summary>
        public bool TryEnableMecanim(Animator animator)
        {
            _mecanim = new PlayableAnimator(animator);
            if (!_mecanim.Valid) { _mecanim = null; return false; }
            return true;
        }

        void OnDestroy() { if (_mecanim != null) _mecanim.Destroy(); }

        public void SetPose(PoseState p)
        {
            _pose = p;
            _t = 0;
            _poseSerial++;   // 每次设招（含同名连招重触发）都递增，供动捕层重放动作

            // 战斗可读性：出招瞬间头顶弹出招式名（格斗游戏惯例），看清双方正在用什么招
            string mv = MoveNameOf(p);
            if (mv != null) CombatFeedback.MoveName(transform.position, mv, isEnemy);
        }

        static string MoveNameOf(PoseState p)
        {
            switch (p)
            {
                case PoseState.Attack: return "横斩";
                case PoseState.HeavyAttack: return "裂空重斩";
                case PoseState.AttackUp: return "撩天斩";
                case PoseState.SwordThrust: return "破空刺";
                case PoseState.AttackLeap: return "跃劈";
                case PoseState.JumpAttack: return "空袭斩";
                case PoseState.AttackSpin: return "回旋斩";
                case PoseState.PunchJab: return "疾风拳";
                case PoseState.PunchCross: return "贯心拳";
                case PoseState.AttackKick: return "正蹬";
                case PoseState.SideKick: return "侧踹";
                case PoseState.SpinKick: return "旋风腿";
                case PoseState.JumpKick: return "飞踢";
                case PoseState.Sweep: return "扫堂腿";
                case PoseState.Cast: return "心念术";
                default: return null;   // 受击/倒地/翻滚/格挡等不刷屏
            }
        }

        /// <summary>
        /// 连段专用：外部指定攻击姿态，并吞掉本次 FSM 状态变化，
        /// 避免 MapFromFsm 用默认攻击姿态覆盖连段姿态。
        /// </summary>
        public void PlayAttackPose(PoseState p)
        {
            if (fsm != null) _lastFsmState = fsm.Current;
            SetPose(p);
        }

        /// <summary>速度（0-1，相对奔跑速度）/ 是否蹲伏 / 是否着地。</summary>
        public void SetLocomotion(float speed01, bool crouch, bool grounded)
        {
            _speed01 = Mathf.Clamp01(speed01);
            _crouch = crouch;
            _grounded = grounded;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (fsm != null) MapFromFsm();

            // 动捕模式：用 Playables 播 Mixamo 片段，跳过下方程序化骨骼
            if (Mecanim)
            {
                _t += dt;
                _mecanim.SetLocomotion(_speed01);
                _mecanim.SetReady(_ready);
                if (_poseSerial != _lastMecanimSerial)
                {
                    _lastMecanimSerial = _poseSerial;
                    // 回到 Idle 也要显式结束保持型动作（倒地爬起/收格挡），否则最后一帧卡住
                    if (_pose == PoseState.Idle) _mecanim.StopAction();
                    else _mecanim.PlayAction(_pose);
                }

                // 动捕库无翻滚片段：闪避在视根上做程序化前滚翻（低身+整体翻转一周）
                if (visual != null)
                {
                    if (_pose == PoseState.Dodge && _t < 0.42f)
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
                _mecanim.Tick(dt);
                return;
            }

            if (rig == null || visual == null) return;
            _t += dt;
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

            if (weaponTrail != null && weaponTrail.emitting != swinging)
                weaponTrail.emitting = swinging;
        }

        /// <summary>攻击类姿态（用更高的关节跟随系数，保证爆发相位脆快有力）。</summary>
        static bool IsActionPose(PoseState p) =>
            p == PoseState.Attack || p == PoseState.AttackUp || p == PoseState.SwordThrust ||
            p == PoseState.HeavyAttack || p == PoseState.AttackSpin || p == PoseState.AttackLeap ||
            p == PoseState.JumpAttack || p == PoseState.Sweep ||
            p == PoseState.PunchJab || p == PoseState.PunchCross ||
            p == PoseState.AttackKick || p == PoseState.SideKick || p == PoseState.SpinKick ||
            p == PoseState.JumpKick || p == PoseState.Hit;

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

        void MapFromFsm()
        {
            if (fsm.Current == _lastFsmState) return;
            _lastFsmState = fsm.Current;
            switch (fsm.Current)
            {
                case CombatState.LightAttack: SetPose(PoseState.Attack); break;
                case CombatState.HeavyAttack:
                case CombatState.Finisher: SetPose(PoseState.HeavyAttack); break;
                case CombatState.Dodge: SetPose(PoseState.Dodge); break;
                case CombatState.HitReaction: SetPose(PoseState.Hit); break;
                case CombatState.MentalStagger: SetPose(PoseState.Stagger); break;
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
