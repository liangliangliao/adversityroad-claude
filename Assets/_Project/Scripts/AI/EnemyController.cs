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
        float _flinchCd;          // 受击霸体冷却：期间轻击不再打断（防无限硬直）
        int _patrolIndex;
        TextMesh _alertMark;      // 前摇警示「！」
        GameObject _dangerRing;   // 前摇地面红圈
        bool _telegraphing;       // 是否处于前摇（脉冲放大红圈/警示，让读招更醒目）
        Vector3 _dangerRingBaseScale;
        float _telegraphT;

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
            BuildTelegraph();
        }

        /// <summary>前摇警示物：头顶红色「！」+ 脚下红圈，出手前亮起（读招窗口）。</summary>
        void BuildTelegraph()
        {
            var markGo = new GameObject("AlertMark");
            markGo.transform.SetParent(transform, false);
            markGo.transform.localPosition = new Vector3(0, 3.3f, 0);
            _alertMark = markGo.AddComponent<TextMesh>();
            _alertMark.text = "";
            _alertMark.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _alertMark.fontSize = 110;
            _alertMark.characterSize = 0.05f;
            _alertMark.anchor = TextAnchor.MiddleCenter;
            _alertMark.color = new Color(1f, 0.2f, 0.15f);
            var mr = markGo.GetComponent<MeshRenderer>();
            if (_alertMark.font != null) mr.material = _alertMark.font.material;
            markGo.AddComponent<World.FaceCamera>();

            _dangerRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_dangerRing.GetComponent<Collider>());
            _dangerRing.transform.SetParent(transform, false);
            _dangerRing.transform.localPosition = new Vector3(0, -0.95f, 0);
            _dangerRing.transform.localScale = new Vector3(2.6f, 0.03f, 2.6f);
            _dangerRingBaseScale = _dangerRing.transform.localScale;
            var rr = _dangerRing.GetComponent<MeshRenderer>();
            Material m = baseMaterial != null ? new Material(baseMaterial)
                : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            m.color = new Color(0.9f, 0.15f, 0.1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color);
            rr.sharedMaterial = m;
            _dangerRing.SetActive(false);
        }

        void ShowTelegraph(bool on)
        {
            _telegraphing = on;
            _telegraphT = 0f;
            if (_alertMark != null) _alertMark.text = on ? "！" : "";
            if (_dangerRing != null)
            {
                _dangerRing.SetActive(on);
                if (on) _dangerRing.transform.localScale = _dangerRingBaseScale;
            }
        }

        /// <summary>前摇脉冲：红圈由小放大到出手、警示「！」跳动，读招窗口一目了然。</summary>
        void TickTelegraph(float dt)
        {
            if (!_telegraphing) return;
            _telegraphT += dt;
            if (_dangerRing != null)
            {
                // 从 0.6 倍胀到 1.15 倍循环，越临近出手视觉张力越强
                float pulse = 0.6f + Mathf.PingPong(_telegraphT * 2.4f, 0.55f);
                _dangerRing.transform.localScale = new Vector3(
                    _dangerRingBaseScale.x * pulse, _dangerRingBaseScale.y,
                    _dangerRingBaseScale.z * pulse);
            }
            if (_alertMark != null)
                _alertMark.characterSize = 0.05f * (1f + 0.25f * Mathf.Sin(_telegraphT * 18f));
        }

        void Update()
        {
            if (State == EnemyState.Dead) return;
            float dt = Time.deltaTime;
            _attackCd -= dt; _mentalCd -= dt; _rangedCd -= dt; _flinchCd -= dt;
            TickTelegraph(dt);

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
                // 交战中静立时摆出格斗预备架势（而非松垮站立）
                poser.SetCombatReady(State == EnemyState.Chase || State == EnemyState.Attack
                    || State == EnemyState.MentalAttack);
            }

            if (State == EnemyState.Stagger)
            {
                _staggerTimer -= dt;
                UpdateEmotion("慌乱");
                if (_staggerTimer <= 0)
                {
                    State = EnemyState.Chase;
                    if (poser != null) poser.SetPose(PoseState.Idle); // 从倒地/踉跄姿态爬起
                }
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
            _attackCd = Mathf.Lerp(3.8f, 1.5f, profile.aggression);
            if (_anim != null) _anim.SetTrigger("Attack");
            // 前摇 0.55 秒：头顶「！」跳动 + 脚下红圈脉冲放大 + 警示音 = 明确读招/闪避窗口，
            // 蓄势姿态先行，判定框随后才开——绝不让玩家"莫名其妙就掉血"。
            ShowTelegraph(true);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.5f);
            if (poser != null) poser.SetPose(PoseState.Charge);
            Invoke(nameof(OpenAttackHitbox), 0.55f);
            Invoke(nameof(CloseHitbox), 0.85f);
        }

        void OpenAttackHitbox()
        {
            ShowTelegraph(false);
            if (State == EnemyState.Dead || attackHitbox == null) return;
            GameAudio.Play(GameAudio.Sfx.Swing, 0.55f);
            if (poser != null) poser.SetPose(PoseState.Attack);
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

        /// <summary>远程攻击：前摇警示后朝玩家胸口发射心念弹。</summary>
        void DoRangedAttack()
        {
            _rangedCd = Mathf.Lerp(6f, 3f, profile.aggression);
            StopMoving();
            FaceTarget();
            if (poser != null) poser.SetPose(PoseState.Cast);
            UpdateEmotion("凝念");
            ShowTelegraph(true);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.4f);
            Invoke(nameof(FireProjectile), 0.5f);
        }

        void FireProjectile()
        {
            ShowTelegraph(false);
            if (State == EnemyState.Dead || _player == null) return;
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

            // 取出这句恶意台词（气泡+字幕都用同一句，便于言语攻防面板复述）
            string line = DialogueLibrary.GetTaunt(profile.targetWeakness, ZoneBuilder.CurrentZoneId);
            if (dialogue != null)
            {
                dialogue.Show(line, 3.5f);
                GameEvents.RaiseSubtitle("『" + dialogue.displayName + "』：" + line);
            }

            var pc = _player.GetComponent<PlayerCombatController>();
            if (pc != null)
            {
                var gm = GameManager.Instance;
                float dmg = MentalDamageSystem.Resolve(
                    profile.mentalDamage,
                    profile.targetWeakness,
                    gm != null ? gm.CurrentProfile : null,
                    gm != null ? gm.safety : null);
                var mentalHit = new DamageInfo
                {
                    mentalDamage = dmg,
                    mentalAxis = profile.targetWeakness,
                    isMentalOnly = true,
                    attackerId = profile.enemyId
                };

                // 言语攻防：优先交给玩家三选一回应；接管失败（冷却/恢复模式/已在进行）时照常落伤害。
                bool challenged = UI.VerbalDefenseController.Instance != null &&
                    UI.VerbalDefenseController.Instance.Begin(this, profile.targetWeakness,
                        dialogue != null ? dialogue.displayName : profile.displayName, line, mentalHit);
                if (!challenged) pc.TakeHit(mentalHit);
            }
            Invoke(nameof(BackToChase), 1.2f);
        }

        void BackToChase() { if (State != EnemyState.Dead) State = EnemyState.Chase; }

        /// <summary>被玩家正确回击（言语攻防）：语塞、削韧、短暂破绽——奖励用言语克制言语。</summary>
        public void OnVerbalCountered()
        {
            if (State == EnemyState.Dead) return;
            CancelInvoke(nameof(OpenAttackHitbox));
            CancelInvoke(nameof(FireProjectile));
            ShowTelegraph(false);
            if (attackHitbox != null) attackHitbox.DisableHitbox();

            _posture -= profile.posture * 0.5f;
            CombatFeedback.HitSpark(transform.position + Vector3.up * 1.4f,
                new Color(0.5f, 0.85f, 1f));
            if (dialogue != null) dialogue.Show(ResponseLibrary.GetBrokenLine(), 2.2f);

            State = EnemyState.Stagger;
            StopMoving();
            if (poser != null) poser.SetPose(PoseState.Stagger);

            if (_posture <= 0)
            {
                // 语塞击破韧性=大破绽
                _posture = profile.posture;
                _staggerTimer = 2.4f;
                if (statusBar != null) statusBar.SetEmotion("破绽！！猛攻！");
                if (dialogue != null) dialogue.Show("【破绽】", 2.2f);
                CombatFeedback.SlowMo(0.5f, 0.15f);
            }
            else
            {
                _staggerTimer = 1.2f;
                if (statusBar != null) statusBar.SetEmotion("语塞");
            }
            if (statusBar != null) statusBar.SetPosture(Mathf.Max(0, _posture), profile.posture);
        }

        public void TakeHit(DamageInfo dmg)
        {
            if (State == EnemyState.Dead) return;
            float final = DamageResolver.ResolvePhysical(dmg.physicalDamage, profile.defense);
            // 破绽期（韧性击破硬直）吃 1.6 倍伤害：奖励削韧打法
            if (State == EnemyState.Stagger) final *= 1.6f;
            _hp -= final;
            _posture -= dmg.postureDamage;

            // 受击反馈：火花 / 闪红 / 伤害数字（随伤害变大）/ 碎块 / 击退
            CombatFeedback.HitSpark(transform.position,
                State == EnemyState.Stagger ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.75f, 0.45f));
            CombatFeedback.HitFlash(gameObject);
            CombatFeedback.DamageNumber(transform.position, Mathf.RoundToInt(final).ToString(),
                State == EnemyState.Stagger ? new Color(1f, 0.85f, 0.25f) : new Color(1f, 0.9f, 0.5f),
                final >= 35f ? 1.6f : 1f);
            CombatFeedback.Debris(transform.position, new Color(0.6f, 0.3f, 0.5f), 3);
            if (dmg.knockback > 0.1f)
            {
                Vector3 kb = DamageResolver.KnockbackDir(dmg.sourcePosition, transform.position)
                             * dmg.knockback * 0.5f;
                if (AgentReady) _agent.Move(kb);
                else transform.position += kb;
            }

            if (statusBar != null)
            {
                statusBar.SetHealth(_hp, profile.maxHealth);
                statusBar.SetPosture(Mathf.Max(0, _posture), profile.posture);
            }

            if (_anim != null) _anim.SetTrigger("Hit");

            // 被打醒：立即进入追击
            if (State == EnemyState.Idle || State == EnemyState.Patrol) State = EnemyState.Chase;

            if (_hp <= 0) { Die(); return; }

            // 受击反应（去掉"铁桩感"的关键）：
            // 轻击=踉跄小硬直并打断正在进行的攻击；重击=直接击倒趴地；
            // 受击霸体冷却防止无限连打硬直，Boss 霸体更长（可打出但不能锁死）
            bool heavyHit = dmg.postureDamage >= 22f || final >= 28f;
            if (_posture > 0 && State != EnemyState.Stagger && (_flinchCd <= 0f || heavyHit))
            {
                _flinchCd = profile.category == EnemyCategory.Boss ? 2.4f : 1.1f;
                CancelInvoke(nameof(OpenAttackHitbox));
                CancelInvoke(nameof(FireProjectile));
                ShowTelegraph(false);
                if (attackHitbox != null) attackHitbox.DisableHitbox();
                State = EnemyState.Stagger;
                _staggerTimer = heavyHit ? 1.0f : 0.42f;
                StopMoving();
                if (poser != null)
                    poser.SetPose(heavyHit ? PoseState.Knockdown : PoseState.Hit);
                if (heavyHit) CombatFeedback.Shake(0.5f);
            }

            if (_posture <= 0)
            {
                // 韧性击破=破绽：明确提示 + 破绽期吃 1.6 倍伤害
                _posture = profile.posture;
                State = EnemyState.Stagger;
                _staggerTimer = 2.4f;
                StopMoving();
                CancelInvoke(nameof(OpenAttackHitbox));
                ShowTelegraph(false);
                if (_anim != null) _anim.SetTrigger("Stagger");
                if (poser != null) poser.SetPose(PoseState.Stagger);
                if (statusBar != null)
                {
                    statusBar.SetPosture(_posture, profile.posture);
                    statusBar.SetEmotion("破绽！！猛攻！");
                }
                if (dialogue != null) dialogue.Show("【破绽】", 2.2f);
                CombatFeedback.SlowMo(0.5f, 0.15f);
            }
        }

        void Die()
        {
            State = EnemyState.Dead;
            CancelInvoke();
            ShowTelegraph(false);
            StopMoving();
            if (_agent != null) _agent.enabled = false;
            if (_anim != null) _anim.SetTrigger("Death");
            if (poser != null) poser.SetPose(PoseState.Death);
            if (statusBar != null) statusBar.Hide();
            if (dialogue != null) dialogue.Show("不……可能……", 2f);
            CombatFeedback.Debris(transform.position, new Color(0.4f, 0.2f, 0.45f), 8);
            CombatFeedback.Shake(0.5f);
            GameAudio.Play(GameAudio.Sfx.Death, 0.9f);
            GameEvents.RaiseEnemyKilled(profile.enemyId);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Destroy(gameObject, 3f);
        }
    }
}
