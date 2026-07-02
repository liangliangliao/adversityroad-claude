using UnityEngine;
using UnityEngine.AI;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Personalization;

namespace AdversityRoad.AI
{
    public enum EnemyState { Idle, Patrol, Chase, Attack, MentalAttack, Stagger, Dead }

    /// <summary>
    /// 敌人控制器：FSM（待机/巡逻/追击/物理攻击/心理攻击/硬直/死亡）。
    /// 心理攻击走 MentalDamageSystem，命中玩家画像高分弱点时伤害放大。
    /// 需要场景已烘焙 NavMesh。
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyController : MonoBehaviour
    {
        public EnemyProfile profile = new EnemyProfile();
        public Hitbox attackHitbox;
        public Transform[] patrolPoints;

        public EnemyState State { get; private set; } = EnemyState.Idle;

        NavMeshAgent _agent;
        Animator _anim;
        Transform _player;
        float _hp, _posture;
        float _attackCd, _mentalCd, _staggerTimer;
        int _patrolIndex;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _anim = GetComponentInChildren<Animator>();
            _hp = profile.maxHealth;
            _posture = profile.posture;
            _agent.speed = profile.moveSpeed;
            var p = FindObjectOfType<Player.PlayerController>();
            if (p != null) _player = p.transform;
        }

        void Update()
        {
            if (State == EnemyState.Dead) return;
            float dt = Time.deltaTime;
            _attackCd -= dt; _mentalCd -= dt;

            if (State == EnemyState.Stagger)
            {
                _staggerTimer -= dt;
                if (_staggerTimer <= 0) State = EnemyState.Chase;
                return;
            }

            if (_player == null) { PatrolTick(); return; }
            float dist = Vector3.Distance(transform.position, _player.position);

            switch (State)
            {
                case EnemyState.Idle:
                case EnemyState.Patrol:
                    PatrolTick();
                    if (dist < profile.detectRange) State = EnemyState.Chase;
                    break;

                case EnemyState.Chase:
                    _agent.isStopped = false;
                    _agent.SetDestination(_player.position);
                    if (dist <= profile.attackRange) State = EnemyState.Attack;
                    // 中距离释放心理攻击（内心敌人/混合敌人的主要输出）
                    else if (dist < profile.detectRange * 0.7f && _mentalCd <= 0 && profile.mentalDamage > 0)
                        DoMentalAttack();
                    else if (dist > profile.detectRange * 1.5f) State = EnemyState.Patrol;
                    break;

                case EnemyState.Attack:
                    _agent.isStopped = true;
                    FaceTarget();
                    if (dist > profile.attackRange * 1.2f) { State = EnemyState.Chase; break; }
                    if (_attackCd <= 0) DoPhysicalAttack();
                    break;
            }
        }

        void PatrolTick()
        {
            if (patrolPoints == null || patrolPoints.Length == 0) { State = EnemyState.Idle; return; }
            State = EnemyState.Patrol;
            _agent.isStopped = false;
            if (!_agent.pathPending && _agent.remainingDistance < 0.5f)
            {
                _patrolIndex = (_patrolIndex + 1) % patrolPoints.Length;
                _agent.SetDestination(patrolPoints[_patrolIndex].position);
            }
        }

        void FaceTarget()
        {
            Vector3 dir = _player.position - transform.position; dir.y = 0;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir), 8f * Time.deltaTime);
        }

        void DoPhysicalAttack()
        {
            _attackCd = Mathf.Lerp(3.5f, 1.2f, profile.aggression);
            if (_anim != null) _anim.SetTrigger("Attack");
            if (attackHitbox != null)
            {
                attackHitbox.EnableHitbox(new DamageInfo
                {
                    physicalDamage = profile.physicalDamage,
                    mentalDamage = profile.mentalDamage * 0.3f,
                    mentalAxis = profile.targetWeakness,
                    knockback = 1.5f,
                    attackerId = profile.enemyId
                });
                Invoke(nameof(CloseHitbox), 0.4f);
            }
        }

        void CloseHitbox() { if (attackHitbox != null) attackHitbox.DisableHitbox(); }

        /// <summary>纯心理攻击：凝视/低语/黑雾。不接触，直接结算，可被定心格挡反制。</summary>
        void DoMentalAttack()
        {
            State = EnemyState.MentalAttack;
            _mentalCd = Mathf.Lerp(8f, 4f, profile.aggression);
            if (_anim != null) _anim.SetTrigger("MentalAttack");

            var pc = _player.GetComponent<PlayerCombatController>();
            if (pc != null)
            {
                var gm = GameManager.Instance;
                float dmg = MentalDamageSystem.Resolve(
                    profile.mentalDamage,
                    profile.targetWeakness,
                    gm != null ? gm.CurrentProfile : null,
                    gm != null ? gm.safety : null);
                pc.TakeHit(new DamageInfo
                {
                    mentalDamage = dmg,
                    mentalAxis = profile.targetWeakness,
                    isMentalOnly = true,
                    attackerId = profile.enemyId
                });
            }
            Invoke(nameof(BackToChase), 1.2f);
        }

        void BackToChase() { if (State != EnemyState.Dead) State = EnemyState.Chase; }

        public void TakeHit(DamageInfo dmg)
        {
            if (State == EnemyState.Dead) return;
            float final = DamageResolver.ResolvePhysical(dmg.physicalDamage, profile.defense);
            _hp -= final;
            _posture -= dmg.postureDamage;

            if (_anim != null) _anim.SetTrigger("Hit");

            if (_hp <= 0) { Die(); return; }

            if (_posture <= 0)
            {
                _posture = profile.posture;
                State = EnemyState.Stagger;
                _staggerTimer = 2f;
                _agent.isStopped = true;
                if (_anim != null) _anim.SetTrigger("Stagger");
            }
        }

        void Die()
        {
            State = EnemyState.Dead;
            _agent.isStopped = true;
            if (_anim != null) _anim.SetTrigger("Death");
            GameEvents.RaiseEnemyKilled(profile.enemyId);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Destroy(gameObject, 5f);
        }
    }
}
