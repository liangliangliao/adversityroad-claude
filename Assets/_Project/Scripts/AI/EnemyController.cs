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
        float _defendCd;          // 防御冷却：闪避/格挡后短时间内不再防（防无敌化）
        PoseState _attackPose = PoseState.Attack;   // 本次出手选中的招式（多样化）
        int _comboLeft;           // 精英/首领的连击追加段数
        float _strafeDir = 1f, _strafeFlipT;        // 交战游走（像人一样找角度）
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
            _attackCd -= dt; _mentalCd -= dt; _rangedCd -= dt; _flinchCd -= dt; _defendCd -= dt;
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
                    if (_attackCd <= 0) { DoPhysicalAttack(); break; }
                    // 出手间隙像人一样左右游走找角度（而非钉在原地干等）
                    if (_attackCd > 0.45f && !_telegraphing && AgentReady)
                    {
                        _strafeFlipT -= dt;
                        if (_strafeFlipT <= 0)
                        {
                            _strafeFlipT = Random.Range(1.2f, 2.6f);
                            _strafeDir = Random.value < 0.5f ? -1f : 1f;
                        }
                        Vector3 toP = _player.position - transform.position; toP.y = 0;
                        Vector3 side = Vector3.Cross(Vector3.up, toP.normalized) * _strafeDir;
                        _agent.Move(side * profile.moveSpeed * 0.32f * dt);
                    }
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

        // 出手招式池：普通敌人用基础拳脚剑技，精英/首领追加重斩/旋风/腿法大招
        static readonly PoseState[] BasicMoves =
            { PoseState.Attack, PoseState.AttackUp, PoseState.SwordThrust,
              PoseState.PunchCross, PoseState.AttackKick };
        static readonly PoseState[] EliteMoves =
            { PoseState.HeavyAttack, PoseState.AttackSpin, PoseState.SpinKick,
              PoseState.SideKick, PoseState.JumpKick };

        /// <summary>按招式给出伤害/击退权重：重招伤害高击退大，快招频率高。</summary>
        static void MoveStats(PoseState p, out float dmgMul, out float knock)
        {
            switch (p)
            {
                case PoseState.HeavyAttack: dmgMul = 1.5f; knock = 4.5f; break;
                case PoseState.AttackSpin:  dmgMul = 1.3f; knock = 3.5f; break;
                case PoseState.SpinKick:    dmgMul = 1.25f; knock = 4f; break;
                case PoseState.JumpKick:    dmgMul = 1.2f; knock = 4f; break;
                case PoseState.SideKick:    dmgMul = 1.0f; knock = 3.5f; break;
                case PoseState.SwordThrust: dmgMul = 1.15f; knock = 1.5f; break;
                case PoseState.PunchCross:  dmgMul = 0.9f; knock = 1.2f; break;
                case PoseState.AttackKick:  dmgMul = 0.95f; knock = 2.5f; break;
                default:                    dmgMul = 1f; knock = 1.5f; break;
            }
        }

        void DoPhysicalAttack()
        {
            _attackCd = Mathf.Lerp(3.0f, 1.1f, profile.aggression);   // 更主动地找时机出手
            TriggerAnim("Attack");

            // 招式多样化：像人一样换招——精英/首领概率掏出重斩/旋风/腿法，
            // 且有概率追加 1-2 段连击（高手连招压制）
            bool elite = profile.category == EnemyCategory.Boss || profile.aggression >= 0.6f;
            bool useElite = elite && Random.value < (profile.category == EnemyCategory.Boss ? 0.45f : 0.25f);
            var pool = useElite ? EliteMoves : BasicMoves;
            _attackPose = pool[Random.Range(0, pool.Length)];
            _comboLeft = profile.category == EnemyCategory.Boss ? Random.Range(1, 3)
                       : elite && Random.value < 0.4f ? 1 : 0;

            // 前摇（等级越高越短，越难反应）：头顶「！」跳动 + 脚下红圈脉冲 + 警示音
            // = 明确读招/闪避窗口，蓄势姿态先行，判定框随后才开。
            float windup = Mathf.Lerp(0.7f, 0.42f, profile.aggression);
            ShowTelegraph(true);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.5f);
            if (poser != null) poser.SetPose(PoseState.Charge);
            Invoke(nameof(OpenAttackHitbox), windup);
            Invoke(nameof(CloseHitbox), windup + 0.3f);
        }

        void OpenAttackHitbox()
        {
            ShowTelegraph(false);
            if (State == EnemyState.Dead || attackHitbox == null) return;
            GameAudio.Play(GameAudio.Sfx.Swing, 0.55f);
            if (poser != null) poser.SetPose(_attackPose);
            MoveStats(_attackPose, out float dmgMul, out float knock);
            attackHitbox.EnableHitbox(new DamageInfo
            {
                physicalDamage = profile.physicalDamage * dmgMul,
                mentalDamage = profile.mentalDamage * 0.3f,
                mentalAxis = profile.targetWeakness,
                knockback = knock,
                attackerId = profile.enemyId
            });
        }

        void CloseHitbox()
        {
            if (attackHitbox != null) attackHitbox.DisableHitbox();
            // 连击追加段：紧凑衔接下一招（间隔短，读作一套连招）
            if (_comboLeft > 0 && State == EnemyState.Attack && _player != null &&
                Vector3.Distance(transform.position, _player.position) < profile.attackRange * 1.6f)
            {
                _comboLeft--;
                var pool = Random.value < 0.5f ? BasicMoves : EliteMoves;
                _attackPose = pool[Random.Range(0, pool.Length)];
                FaceTarget();
                Invoke(nameof(OpenAttackHitbox), 0.32f);
                Invoke(nameof(CloseHitbox), 0.62f);
            }
        }

        /// <summary>Animator 触发器兜底：动捕路径下 Animator 无控制器，SetTrigger 会刷警告。</summary>
        void TriggerAnim(string name)
        {
            if (_anim != null && _anim.runtimeAnimatorController != null) _anim.SetTrigger(name);
        }

        /// <summary>格挡后收架势（保持型格挡姿态由此解除，回到移动/预备）。</summary>
        void GuardRecover()
        {
            if (State != EnemyState.Dead && State != EnemyState.Stagger && poser != null)
                poser.SetPose(PoseState.Idle);
        }

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
            TriggerAnim("MentalAttack");
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

            // ---- 偷袭：敌人未察觉（待机/巡逻）时被打 = 趁其不备，1.8 倍伤害且无法防御 ----
            bool unaware = State == EnemyState.Idle || State == EnemyState.Patrol;
            float sneakMult = 1f;
            if (unaware)
            {
                sneakMult = 1.8f;
                CombatFeedback.DamageNumber(transform.position, "偷袭！",
                    new Color(1f, 0.85f, 0.3f), 1.35f);
            }
            // ---- 防御：交战中的敌人有概率闪避（完全躲开）或格挡（大幅减伤），
            //      概率随敌人级别/攻击性上升（Boss 更像高手），带冷却防无敌化 ----
            else if (_defendCd <= 0f && State != EnemyState.Stagger)
            {
                float chance = (profile.category == EnemyCategory.Boss ? 0.34f : 0.1f)
                             + 0.18f * profile.aggression;
                if (Random.value < chance)
                {
                    _defendCd = 1.7f;
                    if (Random.value < 0.5f && AgentReady)
                    {
                        if (poser != null) poser.SetPose(PoseState.Dodge);
                        // 平滑侧滑而非一帧瞬移：瞬移会被战斗镜头焦点复制成画面跳动
                        StartCoroutine(DodgeSlide(
                            transform.right * (Random.value < 0.5f ? 1.7f : -1.7f)));
                        CombatFeedback.DamageNumber(transform.position, "闪避",
                            new Color(0.55f, 0.8f, 1f), 1.1f);
                        _attackCd = Mathf.Min(_attackCd, 0.55f);   // 闪开即寻机反击
                        return;   // 侧闪成功：完全不受伤
                    }
                    if (poser != null) poser.SetPose(PoseState.Guard);
                    CombatFeedback.DamageNumber(transform.position, "格挡",
                        new Color(0.5f, 0.9f, 0.6f), 1.1f);
                    GameAudio.Play(GameAudio.Sfx.Block, 0.6f);
                    dmg.physicalDamage *= 0.25f;
                    dmg.postureDamage *= 0.55f;
                    // 格挡成功立即反击（挡+还手=像人一样的攻防转换）；架势片刻后收起
                    _attackCd = Mathf.Min(_attackCd, 0.35f);
                    CancelInvoke(nameof(GuardRecover));
                    Invoke(nameof(GuardRecover), 0.6f);
                }
            }

            // ---- 对攻：敌人正处于出招前摇时被打 = 双方硬碰硬，都掉血；
            //      攻击力高的一方受伤更小、给对方造成更大伤害 ----
            if (_telegraphing && _player != null)
            {
                var pc = _player.GetComponent<PlayerCombatController>();
                if (pc != null)
                {
                    bool playerStronger = dmg.physicalDamage >= profile.physicalDamage;
                    pc.TakeHit(new DamageInfo
                    {
                        physicalDamage = profile.physicalDamage * (playerStronger ? 0.35f : 0.75f),
                        sourcePosition = transform.position
                    });
                    Vector3 mid = (transform.position + _player.position) * 0.5f + Vector3.up * 1.3f;
                    CombatFeedback.DamageNumber(mid, "对攻！", new Color(1f, 0.6f, 0.2f), 1.5f);
                    CombatFeedback.HitImpact(mid, new Color(1f, 0.7f, 0.3f), true, false);
                }
            }

            float final = DamageResolver.ResolvePhysical(dmg.physicalDamage, profile.defense) * sneakMult;
            // 破绽期（韧性击破硬直）吃 1.6 倍伤害：奖励削韧打法
            if (State == EnemyState.Stagger) final *= 1.6f;
            // 调试模式：敌人耐揍，大幅削减实际伤害（方便测试，不被秒杀）
            if (Core.GameDebug.TankyEnemies) final *= Core.GameDebug.TankyDamageScale;
            _hp -= final;
            _posture -= dmg.postureDamage;

            // 受击反馈：命中点冲击（火花+白闪盘+顿帧+震屏，重击拉近特写）/ 闪红 / 伤害数字 / 碎块 / 击退
            Color sparkCol = State == EnemyState.Stagger
                ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.75f, 0.45f);
            Vector3 toAtk = dmg.sourcePosition - transform.position; toAtk.y = 0;
            Vector3 dirA = toAtk.sqrMagnitude > 0.01f ? toAtk.normalized : transform.forward;
            // 命中点：受击者朝攻击者一侧、胸口高度（一眼看清"击中了哪里"）
            Vector3 contact = transform.position + dirA * 0.55f + Vector3.up * 1.25f;
            // 重击判定用原始招式数值（不受调试减伤影响），保证打击手感稳定
            bool fbHeavy = dmg.postureDamage >= 22f || dmg.physicalDamage >= 28f;
            CombatFeedback.HitImpact(contact, sparkCol, fbHeavy);
            CombatFeedback.HitFlash(gameObject);
            CombatFeedback.DamageNumber(transform.position, Mathf.RoundToInt(final).ToString(),
                State == EnemyState.Stagger ? new Color(1f, 0.85f, 0.25f) : new Color(1f, 0.9f, 0.5f),
                final >= 35f ? 1.6f : 1f);
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

            TriggerAnim("Hit");

            // 被打醒：立即进入追击
            if (State == EnemyState.Idle || State == EnemyState.Patrol) State = EnemyState.Chase;

            if (_hp <= 0) { Die(); return; }

            // 受击反应（去掉"铁桩感"的关键）：
            // 轻击=踉跄小硬直并打断正在进行的攻击；重击=直接击倒趴地；
            // 受击霸体冷却防止无限连打硬直，Boss 霸体更长（可打出但不能锁死）
            bool heavyHit = dmg.postureDamage >= 22f || dmg.physicalDamage >= 28f;
            if (_posture > 0 && State != EnemyState.Stagger && (_flinchCd <= 0f || heavyHit))
            {
                _flinchCd = profile.category == EnemyCategory.Boss ? 2.4f : 1.1f;
                CancelInvoke(nameof(OpenAttackHitbox));
                CancelInvoke(nameof(FireProjectile));
                ShowTelegraph(false);
                if (attackHitbox != null) attackHitbox.DisableHitbox();
                State = EnemyState.Stagger;
                _staggerTimer = heavyHit ? 1.5f : 0.42f;
                StopMoving();
                if (poser != null)
                    poser.SetPose(heavyHit ? PoseState.Knockdown : PoseState.Hit);
                // 重击=被撞飞一段距离重重倒地（受击状态可视化），起身后立刻重新投入战斗
                if (heavyHit)
                    StartCoroutine(KnockFly(
                        DamageResolver.KnockbackDir(dmg.sourcePosition, transform.position)));
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
                TriggerAnim("Stagger");
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

        /// <summary>侧闪滑步：0.18 秒滑到位（快出慢收），镜头软跟随不产生跳动。</summary>
        System.Collections.IEnumerator DodgeSlide(Vector3 offset)
        {
            float t = 0, dur = 0.18f;
            while (t < dur && State != EnemyState.Dead)
            {
                float dt = Time.deltaTime;
                t += dt;
                if (AgentReady) _agent.Move(offset * Mathf.Min(dt / dur, 1f));
                yield return null;
            }
        }

        /// <summary>重击击飞：0.35 秒内向后飞退 ~2.5m（快出慢收），配合倒地动画读作"被打飞"。</summary>
        System.Collections.IEnumerator KnockFly(Vector3 dir)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 0.01f) yield break;
            dir = dir.normalized;
            float t = 0;
            while (t < 0.35f && State == EnemyState.Stagger)
            {
                t += Time.deltaTime;
                float sp = Mathf.Lerp(12f, 0f, t / 0.35f);
                if (AgentReady) _agent.Move(dir * sp * Time.deltaTime);
                yield return null;
            }
        }

        void Die()
        {
            State = EnemyState.Dead;
            CancelInvoke();
            ShowTelegraph(false);
            StopMoving();
            if (_agent != null) _agent.enabled = false;
            TriggerAnim("Death");
            if (poser != null) poser.SetPose(PoseState.Death);
            if (statusBar != null) statusBar.Hide();
            if (dialogue != null) dialogue.Show("不……可能……", 2f);
            CombatFeedback.Debris(transform.position, new Color(0.4f, 0.2f, 0.45f), 6);
            // 击杀落幕（电影语言）：短促时缓 + 镜头缓推特写，看清敌人倒下的瞬间
            CombatFeedback.SlowMo(0.45f, 0.28f);
            CombatFeedback.UltimateShot(1.1f);
            GameAudio.Play(GameAudio.Sfx.Death, 0.9f);
            GameEvents.RaiseEnemyKilled(profile.enemyId);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Destroy(gameObject, 3f);
        }
    }
}
