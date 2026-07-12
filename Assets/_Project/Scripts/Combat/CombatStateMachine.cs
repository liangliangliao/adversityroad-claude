using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 战斗状态机：管理动作状态切换与动作锁。
    /// Animator 参数约定：Trigger 与状态名同名（LightAttack/HeavyAttack/Dodge/...）。
    /// </summary>
    public class CombatStateMachine : MonoBehaviour
    {
        public CombatState Current { get; private set; } = CombatState.Idle;

        /// <summary>临战标志：出招时置真并刷新计时，最后一次战斗动作 4 秒后自动回落为假。
        /// 之前各出招处只置真、从不复位，一旦出过手就永久临战——无敌人时也一直端着
        /// 格斗架势、不回待机。改为带超时的自动衰减：脱战一段时间即松架，配合
        /// PlayerController 的「敌人逼近才临战」判定，无战斗时角色自然回到待机。</summary>
        public bool InCombat
        {
            get => Time.time - _lastCombatTime < CombatHoldTime;
            set => _lastCombatTime = value ? Time.time : -999f;
        }
        const float CombatHoldTime = 4f;
        float _lastCombatTime = -999f;

        Animator _anim;
        float _stateTimer;

        /// <summary>处于攻击/受击/硬直等状态时锁定新动作输入。</summary>
        public bool IsActionLocked =>
            Current == CombatState.LightAttack || Current == CombatState.HeavyAttack ||
            Current == CombatState.HitReaction || Current == CombatState.Knockdown ||
            Current == CombatState.MentalStagger || Current == CombatState.InnerPowerCast ||
            Current == CombatState.Finisher || Current == CombatState.Death;

        /// <summary>
        /// 硬性锁定：完全禁止移动的状态（重击/倒地/硬直/死亡等）。
        /// 轻击连段不在其中——真实格斗可以边移动边出拳踢腿。
        /// </summary>
        public bool IsHardLocked =>
            Current == CombatState.HeavyAttack || Current == CombatState.HitReaction ||
            Current == CombatState.Knockdown || Current == CombatState.MentalStagger ||
            Current == CombatState.InnerPowerCast || Current == CombatState.Finisher ||
            Current == CombatState.Death;

        void Awake() => _anim = GetComponentInChildren<Animator>();

        void Update()
        {
            if (_stateTimer > 0)
            {
                _stateTimer -= Time.deltaTime;
                if (_stateTimer <= 0 && Current != CombatState.Death)
                    RequestState(CombatState.Locomotion);
            }
        }

        public bool RequestState(CombatState next, float autoExitAfter = 0)
        {
            if (Current == CombatState.Death) return false;
            Current = next;
            _stateTimer = autoExitAfter;
            if (_anim != null && _anim.runtimeAnimatorController != null &&
                next != CombatState.Locomotion && next != CombatState.Idle)
                _anim.SetTrigger(next.ToString());
            return true;
        }

        /// <summary>心理硬直：镜头压迫 + 输入锁定 1.5 秒。</summary>
        public void TriggerMentalStagger() => RequestState(CombatState.MentalStagger, 1.5f);
    }
}
