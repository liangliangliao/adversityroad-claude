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
            float pelvisY = -0.05f;
            float torsoP = Mathf.Lerp(1f, 9f, _speed01), torsoY = 0, torsoR = 0;
            float headP = 0, headY = 0;
            float shLp, shRp, shLr = 4, shRr = -4;   // 肩 pitch / roll
            float elL = 14, elR = 14;                 // 肘（负值=小臂向前弯）
            float hipLp, hipRp, kneeLp, kneeRp;
            float footLp = 0, footRp = 0;

            // ---------- 步态循环 ----------
            float swing = moving ? Mathf.Sin(_phase) : 0;
            float legAmp = Mathf.Lerp(13f, 42f, _speed01);
            float armAmp = Mathf.Lerp(6f, 34f, _speed01);
            hipLp = swing * legAmp;
            hipRp = -swing * legAmp;
            kneeLp = Mathf.Max(0, Mathf.Sin(_phase - 1.9f)) * Mathf.Lerp(12f, 62f, _speed01);
            kneeRp = Mathf.Max(0, Mathf.Sin(_phase + 1.25f)) * Mathf.Lerp(12f, 62f, _speed01);
            shLp = -swing * armAmp;
            shRp = swing * armAmp;
            elL += _speed01 * 26f;
            elR += _speed01 * 26f;
            if (moving)
            {
                pelvisY += Mathf.Abs(Mathf.Cos(_phase)) * 0.045f * _speed01;
                footLp = -hipLp * 0.4f;
                footRp = -hipRp * 0.4f;
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
                case PoseState.Attack:
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
                    float d = Mathf.Max(0, 1f - _t * 3f);
                    torsoP -= 16f * d;
                    headP -= 12f * d;
                    shLr += 20f * d; shRr -= 20f * d;
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
            float k2 = Mathf.Clamp01((_pose == PoseState.Attack || _pose == PoseState.HeavyAttack
                ? 22f : 13f) * dt);
            rig.pelvis.localPosition = Vector3.Lerp(rig.pelvis.localPosition,
                new Vector3(0, pelvisY, 0), k2);
            J(rig.torso, torsoP, torsoY, torsoR, k2);
            J(rig.head, headP, headY, 0, k2);
            J(rig.shoulderL, shLp, 0, shLr, k2);
            J(rig.shoulderR, shRp, 0, shRr, k2);
            J(rig.elbowL, -elL, 0, 0, k2);
            J(rig.elbowR, -elR, 0, 0, k2);
            J(rig.hipL, hipLp, 0, 0, k2);
            J(rig.hipR, hipRp, 0, 0, k2);
            J(rig.kneeL, kneeLp, 0, 0, k2);
            J(rig.kneeR, kneeRp, 0, 0, k2);
            J(rig.footL, footLp, 0, 0, k2);
            J(rig.footR, footRp, 0, 0, k2);

            if (weaponTrail != null && weaponTrail.emitting != swinging)
                weaponTrail.emitting = swinging;
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
