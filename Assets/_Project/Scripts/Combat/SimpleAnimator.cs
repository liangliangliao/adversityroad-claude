using UnityEngine;

namespace AdversityRoad.Combat
{
    public enum PoseState
    {
        Idle, Attack, HeavyAttack, Dodge, Guard, Hit, Stagger, Knockdown, Cast, Death,
        // 言语攻击的微反应：只是"我听见了、身体动了一下"，不是踉跄更不是倒地。
        // 与 Stagger 分开是因为 Stagger 的首选片段是 Stunned——那是一段大幅度
        // 踉跄，玩家读作"倒地"，而且会把最好的攻防时机整个吃掉。
        Flinch,
        // 武术连段（参考黑神话悟空轻棍五段：横扫→上挑→蹬踢→旋身扫→跃劈）
        AttackUp,       // 上挑
        AttackKick,     // 蹬踢（腿法）
        AttackSpin,     // 旋身横扫
        AttackLeap,     // 跃劈（连段终结）
        Sweep,          // 蹲伏扫堂腿
        JumpAttack,     // 空中下劈
        Charge,         // 重击蓄力
        // KOF 式拳脚基本动作（朝正前方发力）
        PunchJab,       // 右直拳
        PunchCross,     // 左直拳（交替出拳）
        SideKick,       // 侧踢
        SpinKick,       // 后旋踢
        JumpKick,       // 飞踢
        SwordThrust,    // 剑式·突刺（弓步直刺）

        // ===== 移动层过渡（起步 / 急停 / 原地转身 / 后撤步）=====
        // 大作里"角色听话"很大一部分来自这几个过渡：推杆有起步、松杆有刹停、
        // 站着扭方向有原地转身，而不是站姿直接平移或整个人瞬间转过去。
        StartMove,      // 起步（静止→移动）
        StopMove,       // 急停（跑动→站定）
        TurnLeft,       // 原地左转 90°
        TurnRight,      // 原地右转 90°
        Turn180,        // 原地掉头 180°
        StepBack,       // 小步后撤（轻后闪，不消耗体力）

        // ===== 腾空（起跳 / 下落 / 着陆）=====
        JumpUp,         // 起跳
        FallLoop,       // 下落循环（保持）
        Land,           // 落地缓冲
        LandHard,       // 重着陆（高坠）

        // ===== 方向闪避（面向目标时的左右闪身）=====
        DodgeLeft,      // 左闪身
        DodgeRight,     // 右闪身

        // ===== 蹲伏 =====
        CrouchIdle,     // 蹲伏待机（保持）

        // ===== 战斗补位 =====
        DashAttack,     // 突进斩（技能位移段专用）
        ChargeLoop,     // 蓄力循环（可保持，蓄多久播多久）
        CastProjectile, // 掷出气刃 / 远程施法
        HitHeavy,       // 重受击（大伤害 / 大击退）
        GuardHit,       // 格挡受击（挡下时的抗力反馈）
        DrawWeapon,     // 拔刀
        SheathWeapon    // 收刀
    }

    /// <summary>
    /// 程序化动作动画：无动画资产阶段，用可视子物体的姿态变化表达
    /// 攻击前倾/翻滚/格挡/受击/硬直/倒地/施法/死亡。
    /// 玩家挂 CombatStateMachine 自动映射；敌人由 EnemyController 手动 SetPose。
    /// </summary>
    public class SimpleAnimator : MonoBehaviour
    {
        public Transform visual;
        public CombatStateMachine fsm;
        public Transform weaponPivot;    // 兵器枢轴：攻击时挥舞
        public TrailRenderer weaponTrail; // 刀光拖尾：挥击窗口内开启

        PoseState _pose = PoseState.Idle;
        CombatState _lastFsmState = CombatState.Idle;
        float _t;

        public void SetPose(PoseState p)
        {
            _pose = p;
            _t = 0;
        }

        void Update()
        {
            if (visual == null) return;
            if (fsm != null) MapFromFsm();
            _t += Time.deltaTime;
            Apply();
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

        void Apply()
        {
            Quaternion targetRot = Quaternion.identity;
            Vector3 targetPos = Vector3.zero;
            Vector3 targetScale = Vector3.one;
            float lerpSpeed = 14f;

            switch (_pose)
            {
                case PoseState.Attack:
                    targetRot = Quaternion.Euler(Mathf.Sin(Mathf.Clamp01(_t / 0.4f) * Mathf.PI) * 28f, 0, 0);
                    lerpSpeed = 22f;
                    break;
                case PoseState.HeavyAttack:
                    targetRot = Quaternion.Euler(Mathf.Sin(Mathf.Clamp01(_t / 0.7f) * Mathf.PI) * 45f, 0, 0);
                    lerpSpeed = 18f;
                    break;
                case PoseState.Dodge:
                    // 翻滚：绕 X 轴整周旋转
                    visual.localRotation = Quaternion.Euler(_t / 0.35f * 360f, 0, 0);
                    visual.localPosition = Vector3.zero;
                    return;
                case PoseState.Guard:
                    targetRot = Quaternion.Euler(-8f, 0, 0);
                    targetScale = new Vector3(1f, 0.94f, 1f);
                    break;
                case PoseState.Hit:
                    targetRot = Quaternion.Euler(-14f * Mathf.Max(0, 1f - _t * 3f), 0,
                        10f * Mathf.Max(0, 1f - _t * 3f));
                    break;
                case PoseState.Stagger:
                    targetRot = Quaternion.Euler(0, 0, Mathf.Sin(_t * 26f) * 13f * Mathf.Max(0, 1f - _t / 1.5f));
                    break;
                case PoseState.Knockdown:
                    targetRot = Quaternion.Euler(82f, 0, 0);
                    targetPos = new Vector3(0, -0.55f, -0.3f);
                    lerpSpeed = 10f;
                    break;
                case PoseState.Cast:
                    float pulse = 1f + Mathf.Sin(_t * 12f) * 0.08f;
                    targetScale = new Vector3(pulse, pulse, pulse);
                    break;
                case PoseState.Death:
                    targetRot = Quaternion.Euler(88f, 0, 20f);
                    targetPos = new Vector3(0, Mathf.Lerp(-0.55f, -1.4f, Mathf.Clamp01((_t - 1.2f) / 1.2f)), -0.3f);
                    lerpSpeed = 6f;
                    break;
            }

            float k = lerpSpeed * Time.deltaTime;
            visual.localRotation = Quaternion.Slerp(visual.localRotation, targetRot, k);
            visual.localPosition = Vector3.Lerp(visual.localPosition, targetPos, k);
            visual.localScale = Vector3.Lerp(visual.localScale, targetScale, k);

            ApplyWeaponSwing();
        }

        /// <summary>兵器挥舞：攻击姿态时枢轴从上举扫到斜下，同时开启刀光拖尾。</summary>
        void ApplyWeaponSwing()
        {
            if (weaponPivot == null) return;

            Quaternion rest = Quaternion.Euler(25f, 0, 0);
            bool swinging = false;

            if (_pose == PoseState.Attack)
            {
                float k = Mathf.Clamp01(_t / 0.3f);
                weaponPivot.localRotation = Quaternion.Euler(
                    Mathf.Lerp(-80f, 95f, k), 0, Mathf.Lerp(-15f, 15f, k));
                swinging = k < 1f;
            }
            else if (_pose == PoseState.HeavyAttack)
            {
                float k = Mathf.Clamp01(_t / 0.55f);
                weaponPivot.localRotation = Quaternion.Euler(
                    Mathf.Lerp(-110f, 110f, k), Mathf.Lerp(-25f, 25f, k), 0);
                swinging = k < 1f;
            }
            else
            {
                weaponPivot.localRotation = Quaternion.Slerp(
                    weaponPivot.localRotation, rest, 10f * Time.deltaTime);
            }

            if (weaponTrail != null && weaponTrail.emitting != swinging)
                weaponTrail.emitting = swinging;
        }
    }
}
