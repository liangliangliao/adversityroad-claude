using UnityEngine;
using UnityEngine.AI;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.UI;
using AdversityRoad.World;

namespace AdversityRoad.AI
{
    public enum EnemyState { Idle, Patrol, Chase, Attack, MentalAttack, Stagger, Dead }

    /// <summary>
    /// 敌人控制器：FSM（待机/巡逻/追击/物理攻击/心理攻击/硬直/死亡）。
    /// 心理攻击 = 数值伤害 + 实时恶意台词（气泡/字幕）+ 情绪状态展示。
    /// 受击有闪红/伤害数字/击退/削韧，死亡有倒地消散演出。
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyController : MonoBehaviour
    {
        public EnemyProfile profile = new EnemyProfile();
        public Hitbox attackHitbox;
        public Transform[] patrolPoints;

        [HideInInspector] public HumanoidAnimator poser;
        [HideInInspector] public EnemyStatusBar statusBar;
        [HideInInspector] public EnemyDialogue dialogue;

        public EnemyState State { get; private set; } = EnemyState.Idle;

        NavMeshAgent _agent;
        Animator _anim;
        Transform _player;
        float _hp, _posture;
        float _attackCd, _mentalCd, _rangedCd, _staggerTimer, _tauntTimer;
        int _patrolIndex;

        /// <summary>兵器/心念弹的主题色（生成时由外部注入）。</summary>
        [HideInInspector] public Color themeColor = new Color(0.7f, 0.4f, 0.9f);
        [HideInInspector] public Material baseMaterial;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _anim = GetComponentInChildren<Animator>();
        }

        // 属性初始化放在 Start：运行时动态生成敌人时，profile 在 AddComponent
        // 之后才注入，Awake 里读取会拿到默认值。
        void Start()
        {
            _hp = profile.maxHealth;
            _posture = profile.posture;
            _agent.speed = profile.moveSpeed;
            _tauntTimer = Random.Range(4f, 9f);
            var p = FindObjectOfType<Player.PlayerController>();
            if (p != null) _player = p.transform;
            if (statusBar != null)
            {
                statusBar.SetHealth(_hp, profile.maxHealth);
                statusBar.SetPosture(_posture, profile.posture);
            }
        }

        void Update()
        {
            if (State == EnemyState.Dead) return;
            float dt = Time.deltaTime;
            _attackCd -= dt; _mentalCd -= dt; _rangedCd -= dt;

            // 实时同步生命值/韧性到头顶状态条（不依赖事件，任何来源的变化都可见）
            if (statusBar != null)
            {
                statusBar.SetHealth(_hp, profile.maxHealth);
                statusBar.SetPosture(Mathf.Max(0, _posture), profile.posture);
            }

            // 把移动速度喂给人形动画（步行/奔跑步态）
            if (poser != null)
            {
                float v = AgentReady ? _agent.velocity.magnitude
                    : (State == EnemyState.Chase ? profile.moveSpeed : 0f);
                poser.SetLocomotion(v / Mathf.Max(0.5f, profile.moveSpeed) * 0.85f, false, true);
            }

            if (State == EnemyState.Stagger)
            {
                _staggerTimer -= dt;
                UpdateEmotion("慌乱");
                if (_staggerTimer <= 0) State = EnemyState.Chase;
                return;
            }

            if (_player == null) { PatrolTick(); return; }
            float dist = Vector3.Distance(transform.position, _player.position);

            // 追击/交战中周期性低语（语言层面的持续心理压迫）
            if (State == EnemyState.Chase || State == EnemyState.Attack)
            {
                _tauntTimer -= dt;
                if (_tauntTimer <= 0)
                {
                    _tauntTimer = Random.Range(6f, 12f);
                    if (dialogue != null)
                        dialogue.Taunt(profile.targetWeakness, ZoneBuilder.CurrentZoneId, false);
                }
            }

            switch (State)
            {
                case EnemyState.Idle:
                case EnemyState.Patrol:
                    PatrolTick();
                    UpdateEmotion("窥伺");
                    if (dist < profile.detectRange) State = EnemyState.Chase;
                    break;

                case EnemyState.Chase:
                    UpdateEmotion("紧逼");
                    MoveTowards(_player.position, dt);
                    if (dist <= profile.attackRange) State = EnemyState.Attack;
                    // 中距离远程：发射心念弹
                    else if (profile.rangedAttack && _rangedCd <= 0 &&
                             dist > profile.attackRange * 2f && dist < profile.detectRange)
                        DoRangedAttack();
                    // 中距离释放心理攻击（内心敌人/混合敌人的主要输出）
                    else if (dist < profile.detectRange * 0.7f && _mentalCd <= 0 && profile.mentalDamage > 0)
                        DoMentalAttack();
                    else if (dist > profile.detectRange * 1.5f) State = EnemyState.Patrol;
                    break;

                case EnemyState.Attack:
                    UpdateEmotion("狰狞");
                    StopMoving();
                    FaceTarget();
                    if (dist > profile.attackRange * 1.2f) { State = EnemyState.Chase; break; }
                    if (_attackCd <= 0) DoPhysicalAttack();
                    break;
            }
        }

        void UpdateEmotion(string emotion)
        {
            if (statusBar != null) statusBar.SetEmotion(emotion);
        }

        void PatrolTick()
        {
            if (patrolPoints == null || patrolPoints.Length == 0 || !AgentReady) { State = EnemyState.Idle; return; }
            State = EnemyState.Patrol;
            _agent.isStopped = false;
            if (!_agent.pathPending && _agent.remainingDistance < 0.5f)
            {
                _patrolIndex = (_patrolIndex + 1) % patrolPoints.Length;
                _agent.SetDestination(patrolPoints[_patrolIndex].position);
            }
        }

        /// <summary>Agent 是否可用：未落在 NavMesh 上时调用 isStopped/SetDestination 会抛异常。</summary>
        bool AgentReady => _agent != null && _agent.enabled && _agent.isOnNavMesh;

        void MoveTowards(Vector3 target, float dt)
        {
            if (AgentReady)
            {
                _agent.isStopped = false;
                _agent.SetDestination(target);
                return;
            }
            // NavMesh 不可用时的直线追击兜底
            Vector3 dir = target - transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude < 0.04f) return;
            transform.position += dir.normalized * profile.moveSpeed * dt;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir), 8f * dt);
        }

        void StopMoving()
        {
            if (AgentReady) _agent.isStopped = true;
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
            if (poser != null) poser.SetPose(PoseState.Attack);
            // 前摇 0.28 秒后才开判定框：给玩家读招与完美闪避留窗口
            Invoke(nameof(OpenAttackHitbox), 0.28f);
            Invoke(nameof(CloseHitbox), 0.6f);
        }

        void OpenAttackHitbox()
        {
            if (State == EnemyState.Dead || attackHitbox == null) return;
            attackHitbox.EnableHitbox(new DamageInfo
            {
                physicalDamage = profile.physicalDamage,
                mentalDamage = profile.mentalDamage * 0.3f,
                mentalAxis = profile.targetWeakness,
                knockback = 1.5f,
                attackerId = profile.enemyId
            });
        }

        void CloseHitbox() { if (attackHitbox != null) attackHitbox.DisableHitbox(); }

        /// <summary>远程攻击：朝玩家胸口发射心念弹。</summary>
        void DoRangedAttack()
        {
            _rangedCd = Mathf.Lerp(6f, 3f, profile.aggression);
            StopMoving();
            FaceTarget();
            if (poser != null) poser.SetPose(PoseState.Cast);
            UpdateEmotion("凝念");

            Vector3 origin = transform.position + Vector3.up * 1.3f + transform.forward * 0.8f;
            Vector3 targetPos = _player.position + Vector3.up * 1.0f;
            Projectile.Launch(transform, origin, targetPos - origin,
                new DamageInfo
                {
                    physicalDamage = profile.physicalDamage * 0.7f,
                    mentalDamage = profile.mentalDamage * 0.5f,
                    mentalAxis = profile.targetWeakness,
                    knockback = 1f,
                    attackerId = profile.enemyId
                }, 11f, themeColor, baseMaterial);
        }

        /// <summary>心理攻击：凝视/低语 + 实时恶意台词。可被定心格挡反制。</summary>
        void DoMentalAttack()
        {
            State = EnemyState.MentalAttack;
            _mentalCd = Mathf.Lerp(8f, 4f, profile.aggression);
            if (_anim != null) _anim.SetTrigger("MentalAttack");
            if (poser != null) poser.SetPose(PoseState.Cast);
            UpdateEmotion("讥讽");
            if (dialogue != null)
                dialogue.Taunt(profile.targetWeakness, ZoneBuilder.CurrentZoneId, true);

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

            // 受击反馈：闪红 / 伤害数字 / 碎块 / 顿帧 / 击退
            CombatFeedback.HitFlash(gameObject);
            CombatFeedback.DamageNumber(transform.position, Mathf.RoundToInt(final).ToString(),
                new Color(1f, 0.9f, 0.5f));
            CombatFeedback.Debris(transform.position, new Color(0.6f, 0.3f, 0.5f), 3);
            CombatFeedback.HitStop(0.035f);
            if (dmg.knockback > 0.1f)
            {
                Vector3 kb = DamageResolver.KnockbackDir(dmg.sourcePosition, transform.position)
                             * dmg.knockback * 0.35f;
                if (AgentReady) _agent.Move(kb);
                else transform.position += kb;
            }

            if (statusBar != null)
            {
                statusBar.SetHealth(_hp, profile.maxHealth);
                statusBar.SetPosture(Mathf.Max(0, _posture), profile.posture);
            }

            if (_anim != null) _anim.SetTrigger("Hit");
            if (poser != null && State != EnemyState.Stagger) poser.SetPose(PoseState.Hit);

            // 被打醒：立即进入追击
            if (State == EnemyState.Idle || State == EnemyState.Patrol) State = EnemyState.Chase;

            if (_hp <= 0) { Die(); return; }

            if (_posture <= 0)
            {
                _posture = profile.posture;
                State = EnemyState.Stagger;
                _staggerTimer = 2f;
                StopMoving();
                if (_anim != null) _anim.SetTrigger("Stagger");
                if (poser != null) poser.SetPose(PoseState.Stagger);
                if (statusBar != null) statusBar.SetPosture(_posture, profile.posture);
            }
        }

        void Die()
        {
            State = EnemyState.Dead;
            StopMoving();
            if (_agent != null) _agent.enabled = false;
            if (_anim != null) _anim.SetTrigger("Death");
            if (poser != null) poser.SetPose(PoseState.Death);
            if (statusBar != null) statusBar.Hide();
            if (dialogue != null) dialogue.Show("不……可能……", 2f);
            CombatFeedback.Debris(transform.position, new Color(0.4f, 0.2f, 0.45f), 8);
            CombatFeedback.Shake(0.5f);
            GameEvents.RaiseEnemyKilled(profile.enemyId);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Destroy(gameObject, 3f);
        }
    }
}
