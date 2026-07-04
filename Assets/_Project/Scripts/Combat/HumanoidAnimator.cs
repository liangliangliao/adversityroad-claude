using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 人形程序动画：驱动 HumanoidRig 全部关节做类人动作——
    /// 步行/奔跑循环（腿摆+臂摆反相+躯干起伏）、慢走、蹲伏潜行、跳跃收腿、
    /// 翻滚（团身空翻）、挥臂劈砍（肩肘联动+转腰）、双臂格挡、施法、
    /// 受击后仰、心理硬直摇晃、击倒仰倒、死亡倒地。全部关节平滑过渡，无机械感。
    /// 玩家由 CombatStateMachine 自动映射；敌人手动 SetPose + SetLocomotion。
    /// </summary>
    public class HumanoidAnimator : MonoBehaviour
    {
        public Transform visual;      // 整体可视根（翻滚/倒地时旋转）
        public HumanoidRig rig;
        public CombatStateMachine fsm;
        public Transform weaponPivot; // 兼容保留（兵器随手部关节运动）
        public TrailRenderer weaponTrail;

        PoseState _pose = PoseState.Idle;
        CombatState _lastFsmState = CombatState.Idle;
        float _t;

        // 运动参数（每帧由控制器喂入）
        float _speed01;
        bool _crouch;
        bool _grounded = true;
        float _phase;

        public void SetPose(PoseState p)
        {
            _pose = p;
            _t = 0;
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
            if (rig == null || visual == null) return;
            float dt = Time.deltaTime;
            if (fsm != null) MapFromFsm();
            _t += dt;

            bool moving = _speed01 > 0.03f && _grounded;
            if (moving) _phase += dt * Mathf.Lerp(3.5f, 11.5f, _speed01);

            // ---------- 基础目标角 ----------
            float pelvisY = -0.05f, pelvisX = 0f;
            float torsoP = Mathf.Lerp(1f, 9f, _speed01), torsoY = 0, torsoR = 0;
            float headP = 0, headY = 0;
            float shLp, shRp, shLr = 4, shRr = -4;   // 肩 pitch / roll
            float elL = 14, elR = 14;                 // 肘（负值=小臂向前弯）
            float hipLp, hipRp, kneeLp, kneeRp;
            float footLp = 0, footRp = 0;

            // ---------- 步态循环 ----------
            // 步态：走路小步摆，奔跑大步幅+摆臂屈肘+前倾冲刺感
            float swing = moving ? Mathf.Sin(_phase) : 0;
            float legAmp = Mathf.Lerp(13f, 56f, _speed01);
            float armAmp = Mathf.Lerp(6f, 48f, _speed01);
            hipLp = swing * legAmp;
            hipRp = -swing * legAmp;
            kneeLp = Mathf.Max(0, Mathf.Sin(_phase - 1.9f)) * Mathf.Lerp(12f, 82f, _speed01);
            kneeRp = Mathf.Max(0, Mathf.Sin(_phase + 1.25f)) * Mathf.Lerp(12f, 82f, _speed01);
            shLp = -swing * armAmp;
            shRp = swing * armAmp;
            elL += _speed01 * 55f;   // 奔跑屈肘摆臂
            elR += _speed01 * 55f;
            torsoP += _speed01 * 7f; // 冲刺前倾
            if (moving)
            {
                pelvisY += Mathf.Abs(Mathf.Cos(_phase)) * 0.055f * _speed01;
                footLp = -hipLp * 0.45f;
                footRp = -hipRp * 0.45f;

                // ---- 次要动作（消除机械感，参考真人步态运动学）----
                // 步频的两倍是"每一步"的节拍（左右各一次），用于侧向重心转移。
                float step = Mathf.Sin(_phase);          // 前后摆（与腿同相）
                float bob2 = Mathf.Cos(_phase * 2f);     // 每步一次的上下/侧向节拍
                // 脊柱反向扭转：肩带与骨盆做对侧旋转（走路时上身与髋反向拧），
                // 这是"看起来像人"最关键的一笔——僵直的关键缺失就是它。
                torsoY += -step * Mathf.Lerp(5f, 13f, _speed01);
                headY += step * Mathf.Lerp(2f, 6f, _speed01);       // 头略朝行进侧引导视线
                // 每一步的重心左右转移：骨盆侧摆 + 躯干反向侧倾（钟摆式配重）
                pelvisX = bob2 * Mathf.Lerp(0.015f, 0.05f, _speed01);
                torsoR += -bob2 * Mathf.Lerp(2f, 6f, _speed01);
                headP += -Mathf.Abs(Mathf.Sin(_phase)) * 2.5f * _speed01;  // 落步轻微点头
                // 手臂不只前后摆：叠加一点外摆与屈肘变化，摆动更松弛自然
                shLr += step * 4f * _speed01;
                shRr += step * 4f * _speed01;
                elL += Mathf.Max(0f, step) * 18f * _speed01;
                elR += Mathf.Max(0f, -step) * 18f * _speed01;
            }
            else
            {
                // 待机呼吸
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

            // ---------- 全身姿态（翻滚/倒地/死亡） ----------
            Quaternion bodyRot = Quaternion.identity;
            Vector3 bodyPos = Vector3.zero;
            float bodyLerp = 10f;
            bool directBody = false;
            bool swinging = false;

            switch (_pose)
            {
                case PoseState.Attack: // 横斩
                {
                    float k = Ease(Mathf.Clamp01(_t / 0.32f));
                    shRp = Mathf.Lerp(168f, 22f, k);
                    shRr = Mathf.Lerp(-30f, 8f, k);
                    elR = Mathf.Lerp(55f, 8f, k);
                    shLp = Mathf.Lerp(-25f, 12f, k);
                    torsoY = Mathf.Lerp(-18f, 16f, k);
                    torsoP += 6f;
                    swinging = _t < 0.34f;
                    break;
                }
                case PoseState.AttackUp: // 上挑：由低向高反手撩击，重心后送
                {
                    float k = Ease(Mathf.Clamp01(_t / 0.3f));
                    shRp = Mathf.Lerp(15f, 172f, k);
                    shRr = Mathf.Lerp(20f, -18f, k);
                    elR = Mathf.Lerp(10f, 40f, k);
                    shLp = Mathf.Lerp(20f, -30f, k);
                    torsoP = Mathf.Lerp(14f, -10f, k);
                    torsoY = Mathf.Lerp(14f, -12f, k);
                    swinging = _t < 0.32f;
                    break;
                }
                case PoseState.AttackKick: // 蹬踢：右腿蹬出，躯干后仰，双臂平衡
                {
                    float k = Ease(Mathf.Clamp01(_t / 0.34f));
                    hipRp = Mathf.Lerp(15f, 105f, k);
                    kneeRp = Mathf.Lerp(85f, 4f, k);
                    footRp = -20f;
                    hipLp = -8f; kneeLp = 12f;   // 支撑腿微屈
                    torsoP = Mathf.Lerp(8f, -14f, k);
                    shLp = 45f; shRp = -30f;
                    shLr = 30f; shRr = -35f;
                    elL = elR = 60f;
                    swinging = _t < 0.36f;
                    break;
                }
                case PoseState.SwordThrust: // 剑式·突刺：弓步前倾，持剑臂直线刺出
                {
                    float k = Ease(Mathf.Clamp01(_t / 0.26f));
                    shRp = 92f;
                    shRr = -4f;
                    elR = Mathf.Lerp(100f, 2f, k);               // 肘由屈到全伸=刺出
                    shLp = Mathf.Lerp(30f, -35f, k);             // 后手向后展开配重
                    shLr = 30f;
                    torsoP = Mathf.Lerp(4f, 22f, k);             // 弓步前倾
                    torsoY = Mathf.Lerp(-18f, 8f, k);
                    hipRp = 35f; kneeRp = 45f;                   // 前弓
                    hipLp = -22f; kneeLp = 8f;                   // 后箭
                    pelvisY -= 0.1f * k;
                    swinging = _t < 0.3f;
                    break;
                }
                case PoseState.PunchJab: // 右直拳：手臂朝正前方快速伸出（拳打在前方）
                {
                    float k = Ease(Mathf.Clamp01(_t / 0.24f));
                    shRp = 88f;                                  // 大臂抬平指向前
                    shRr = -6f;
                    elR = Mathf.Lerp(95f, 4f, k);                // 肘由屈到伸=出拳
                    shLp = 55f; elL = 105f; shLr = 14f;          // 左手护面
                    torsoY = Mathf.Lerp(-14f, 12f, k);           // 转腰送肩
                    torsoP += 4f;
                    swinging = _t < 0.26f;
                    break;
                }
                case PoseState.PunchCross: // 左直拳：交替出拳，反向转腰
                {
                    float k = Ease(Mathf.Clamp01(_t / 0.24f));
                    shLp = 88f;
                    shLr = 6f;
                    elL = Mathf.Lerp(95f, 4f, k);
                    shRp = 55f; elR = 105f; shRr = -14f;         // 右手护面
                    torsoY = Mathf.Lerp(14f, -12f, k);
                    torsoP += 4f;
                    swinging = _t < 0.26f;
                    break;
                }
                case PoseState.SideKick: // 侧踢：右腿侧向蹬出，躯干侧倾平衡
                {
                    float k = Ease(Mathf.Clamp01(_t / 0.32f));
                    hipRp = Mathf.Lerp(20f, 92f, k);
                    kneeRp = Mathf.Lerp(90f, 5f, k);
                    footRp = -18f;
                    hipLp = -6f; kneeLp = 14f;
                    torsoP = -6f;
                    torsoR = Mathf.Lerp(0f, -16f, k);
                    shLp = 30f; shRp = -25f; shLr = 45f; shRr = -45f;
                    elL = elR = 45f;
                    swinging = _t < 0.34f;
                    break;
                }
                case PoseState.SpinKick: // 后旋踢：整身旋转，腿平扫
                {
                    float k = Mathf.Clamp01(_t / 0.44f);
                    visual.localRotation = Quaternion.Euler(0, k * 360f, 0);
                    visual.localPosition = Vector3.zero;
                    hipRp = 85f; kneeRp = 8f; footRp = -12f;
                    hipLp = 18f; kneeLp = 28f;
                    torsoP = 12f; torsoR = -10f;
                    shLp = 40f; shLr = 50f; shRp = 30f; shRr = -50f;
                    elL = elR = 30f;
                    directBody = true;
                    swinging = _t < 0.46f;
                    break;
                }
                case PoseState.JumpKick: // 飞踢：腾空正蹬，后腿收紧
                {
                    float k = Ease(Mathf.Clamp01(_t / 0.3f));
                    hipRp = Mathf.Lerp(30f, 100f, k);
                    kneeRp = Mathf.Lerp(90f, 4f, k);
                    footRp = -22f;
                    hipLp = 60f; kneeLp = 110f;                  // 收腿
                    torsoP = -18f;
                    shLp = 40f; shRp = -35f; shLr = 40f; shRr = -40f;
                    elL = elR = 55f;
                    swinging = _t < 0.4f;
                    break;
                }
                case PoseState.AttackSpin: // 旋身横扫：整身转一周，兵器水平外展
                {
                    float k = Mathf.Clamp01(_t / 0.42f);
                    visual.localRotation = Quaternion.Euler(0, k * 360f, 0);
                    visual.localPosition = Vector3.zero;
                    shRp = 88f; shRr = -78f;
                    elR = 8f;
                    shLp = 60f; shLr = 55f;
                    torsoP = 10f;
                    hipLp = 22f; hipRp = 22f; kneeLp = 30f; kneeRp = 30f; // 沉桩
                    directBody = true;
                    swinging = _t < 0.44f;
                    break;
                }
                case PoseState.AttackLeap: // 跃劈：小跳跃起双手过顶劈落
                {
                    float k = Ease(Mathf.Clamp01(_t / 0.5f));
                    bodyPos = new Vector3(0, Mathf.Sin(Mathf.Clamp01(_t / 0.5f) * Mathf.PI) * 0.45f, 0.1f);
                    bodyLerp = 16f;
                    shRp = Mathf.Lerp(178f, 18f, k);
                    shLp = Mathf.Lerp(178f, 18f, k);
                    elL = elR = Mathf.Lerp(35f, 6f, k);
                    torsoP = Mathf.Lerp(-14f, 30f, k);
                    hipLp = Mathf.Lerp(40f, 12f, k); hipRp = Mathf.Lerp(46f, 16f, k);
                    kneeLp = Mathf.Lerp(70f, 20f, k); kneeRp = Mathf.Lerp(76f, 20f, k);
                    swinging = _t < 0.52f;
                    break;
                }
                case PoseState.Sweep: // 扫堂腿：深蹲整身速旋，右腿贴地扫出
                {
                    float k = Mathf.Clamp01(_t / 0.4f);
                    visual.localRotation = Quaternion.Euler(0, k * 360f, 0);
                    visual.localPosition = new Vector3(0, -0.42f, 0);
                    hipRp = 88f; kneeRp = 6f; footRp = -15f;     // 扫出腿伸直
                    hipLp = 95f; kneeLp = 120f;                  // 支撑腿深蹲
                    torsoP = 24f;
                    shLp = 40f; shRp = 20f; shLr = 40f; shRr = -25f;
                    directBody = true;
                    swinging = _t < 0.42f;
                    break;
                }
                case PoseState.JumpAttack: // 空中下劈：收腿、双手过顶下砸
                {
                    float k = Ease(Mathf.Clamp01(_t / 0.4f));
                    shRp = Mathf.Lerp(180f, 24f, k);
                    shLp = Mathf.Lerp(160f, 30f, k);
                    elL = elR = Mathf.Lerp(30f, 8f, k);
                    torsoP = Mathf.Lerp(-8f, 34f, k);
                    hipLp = 55f; hipRp = 62f;
                    kneeLp = 85f; kneeRp = 92f;
                    swinging = _t < 0.45f;
                    break;
                }
                case PoseState.Charge: // 蓄力：兵器高举于后，沉腰蓄势微颤
                {
                    shRp = 152f; shRr = -22f; elR = 45f;
                    shLp = 35f; shLr = 25f; elL = 55f;
                    torsoP = 12f + Mathf.Sin(_t * 26f) * 1.6f;
                    torsoY = -14f;
                    hipLp += 30f; hipRp += 30f;
                    kneeLp += 42f; kneeRp += 42f;
                    pelvisY -= 0.14f;
                    break;
                }
                case PoseState.HeavyAttack:
                {
                    float k = Ease(Mathf.Clamp01(_t / 0.58f));
                    shRp = Mathf.Lerp(176f, 14f, k);
                    shLp = Mathf.Lerp(176f, 14f, k);
                    shLr = 12f; shRr = -12f;
                    elL = elR = Mathf.Lerp(40f, 6f, k);
                    torsoP = Mathf.Lerp(-10f, 26f, k);
                    swinging = _t < 0.6f;
                    break;
                }
                case PoseState.Guard:
                    shLp = 62f; shRp = 62f;
                    shLr = 22f; shRr = -22f;
                    elL = elR = 96f;
                    torsoP += 4f;
                    break;
                case PoseState.Cast:
                    shLp = 92f; shRp = 92f;
                    elL = elR = 12f;
                    torsoP -= 4f;
                    break;
                case PoseState.Hit:
                {
                    // 明显的受击踉跄：上身猛向后仰、头甩、双臂扬起、重心后坐
                    float d = Mathf.Max(0, 1f - _t * 2.4f);
                    torsoP -= 32f * d;
                    torsoR += 8f * d;
                    headP -= 22f * d;
                    shLp -= 30f * d; shRp -= 30f * d;
                    shLr += 40f * d; shRr -= 40f * d;
                    hipLp += 14f * d; hipRp += 14f * d;
                    kneeLp += 18f * d; kneeRp += 18f * d;
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
                    // 翻跟头：团身空翻一周
                    float k = Mathf.Clamp01(_t / 0.35f);
                    visual.localRotation = Quaternion.Euler(k * 360f, 0, 0);
                    visual.localPosition = new Vector3(0, Mathf.Sin(k * Mathf.PI) * 0.15f - 0.25f, 0);
                    hipLp = hipRp = 95f;
                    kneeLp = kneeRp = 110f;
                    shLp = shRp = 70f;
                    elL = elR = 100f;
                    headP = 25f;
                    directBody = true;
                    break;
                }
                case PoseState.Knockdown:
                    bodyRot = Quaternion.Euler(-78f, 0, 6f);
                    bodyPos = new Vector3(0, -0.5f, 0.25f);
                    shLr = 70f; shRr = -70f;
                    hipLp = 15f; hipRp = 28f;
                    kneeLp = 20f; kneeRp = 12f;
                    bodyLerp = 9f;
                    break;
                case PoseState.Death:
                    bodyRot = Quaternion.Euler(-84f, 0, 14f);
                    bodyPos = new Vector3(0,
                        Mathf.Lerp(-0.5f, -1.5f, Mathf.Clamp01((_t - 1.4f) / 1.4f)), 0.25f);
                    shLr = 80f; shRr = -65f;
                    shLp = -20f; shRp = 30f;
                    hipLp = 10f; hipRp = 24f;
                    torsoR = 8f;
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
            bool attackPose = _pose == PoseState.Attack || _pose == PoseState.HeavyAttack ||
                _pose == PoseState.PunchJab || _pose == PoseState.PunchCross ||
                _pose == PoseState.AttackUp || _pose == PoseState.AttackKick ||
                _pose == PoseState.SideKick || _pose == PoseState.AttackLeap ||
                _pose == PoseState.JumpAttack || _pose == PoseState.JumpKick ||
                _pose == PoseState.Hit;
            float k2 = Mathf.Clamp01((attackPose ? 30f : 13f) * dt);
            rig.pelvis.localPosition = Vector3.Lerp(rig.pelvis.localPosition,
                new Vector3(pelvisX, pelvisY, 0), k2);
            J(rig.torso, torsoP, torsoY, torsoR, k2);
            J(rig.head, headP, headY, 0, k2);
            // 肩/髋俯仰在应用时取负：肢体几何向下延伸，绕 X 正角会转向身后，
            // 取负后本文件中所有姿态角语义统一为「正值=向前方出击」——
            // 出拳打向正前方、踢腿踢向正前方（修复拳脚方向反了的问题）
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

        /// <summary>
        /// 兵器耍花（武术表演式）：枢轴层旋转叠加在手臂动作之上，
        /// 每个剑式姿态有独立的大幅刀刃轨迹，配合加宽刀光形成明显剑花。
        /// 之前兵器只是刚性跟随手部——这是「死死握剑」的根因修复。
        /// </summary>
        void ApplyWeaponFlourish()
        {
            if (weaponPivot == null) return;
            Quaternion rest = Quaternion.Euler(-30f, 0, 8f);
            float k;

            switch (_pose)
            {
                case PoseState.Attack: // 横斩：刃自举位横扫过体前
                    k = Ease(Mathf.Clamp01(_t / 0.3f));
                    weaponPivot.localRotation = Quaternion.Euler(
                        Mathf.Lerp(-100f, 110f, k), Mathf.Lerp(-40f, 40f, k), 0);
                    break;
                case PoseState.AttackUp: // 上撩：刃自下向上反撩画弧
                    k = Ease(Mathf.Clamp01(_t / 0.3f));
                    weaponPivot.localRotation = Quaternion.Euler(
                        Mathf.Lerp(115f, -125f, k), Mathf.Lerp(30f, -30f, k), 0);
                    break;
                case PoseState.SwordThrust: // 突刺：起手绕腕剑花后刃指正前
                    k = Ease(Mathf.Clamp01(_t / 0.3f));
                    weaponPivot.localRotation = Quaternion.Euler(
                        Mathf.Lerp(40f, -8f, k), 0, Mathf.Lerp(-170f, 0f, k));
                    break;
                case PoseState.AttackLeap:
                case PoseState.HeavyAttack: // 跃劈/重劈：过顶大轮劈
                    k = Ease(Mathf.Clamp01(_t / 0.55f));
                    weaponPivot.localRotation = Quaternion.Euler(
                        Mathf.Lerp(-150f, 115f, k), Mathf.Lerp(-25f, 25f, k), 0);
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
                case PoseState.JumpAttack: // 空中下劈
                    k = Ease(Mathf.Clamp01(_t / 0.4f));
                    weaponPivot.localRotation = Quaternion.Euler(
                        Mathf.Lerp(-160f, 100f, k), 0, 0);
                    break;
                case PoseState.Cast: // 施法/能量斩：绕腕立剑花一周
                    weaponPivot.localRotation = Quaternion.Euler(0, 0, _t / 0.4f * 360f);
                    break;
                default: // 静息：刃斜立体侧，随呼吸微摆
                    weaponPivot.localRotation = Quaternion.Slerp(
                        weaponPivot.localRotation,
                        rest * Quaternion.Euler(Mathf.Sin(Time.time * 1.6f) * 3f, 0, 0),
                        8f * Time.deltaTime);
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

        static float Ease(float k) => k * k * (3f - 2f * k);

        static void J(Transform t, float x, float y, float z, float k)
        {
            if (t == null) return;
            t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.Euler(x, y, z), k);
        }
    }
}
