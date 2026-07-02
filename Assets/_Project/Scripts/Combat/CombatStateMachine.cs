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
        public bool InCombat { get; set; }

        Animator _anim;
        float _stateTimer;

        /// <summary>处于攻击/受击/硬直等状态时锁定移动输入。</summary>
        public bool IsActionLocked =>
            Current == CombatState.LightAttack || Current == CombatState.HeavyAttack ||
            Current == CombatState.HitReaction || Current == CombatState.Knockdown ||
            Current == CombatState.MentalStagger || Current == CombatState.InnerPowerCast ||
            Current == CombatState.Finisher || Current == CombatState.Death;

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
            if (_anim != null && next != CombatState.Locomotion && next != CombatState.Idle)
                _anim.SetTrigger(next.ToString());
            return true;
        }

        /// <summary>心理硬直：镜头压迫 + 输入锁定 1.5 秒。</summary>
        public void TriggerMentalStagger() => RequestState(CombatState.MentalStagger, 1.5f);
    }
}
