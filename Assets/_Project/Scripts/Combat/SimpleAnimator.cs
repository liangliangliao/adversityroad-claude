using UnityEngine;

namespace AdversityRoad.Combat
{
    public enum PoseState
    {
        Idle, Attack, HeavyAttack, Dodge, Guard, Hit, Stagger, Knockdown, Cast, Death
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
        }
    }
}
