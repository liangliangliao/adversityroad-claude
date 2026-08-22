using System.Collections.Generic;
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
        // 普通敌人的攻击距离硬上限（Boss 机制关的专属弹幕不走这里）：
        // 远程心念弹 ≤8m、心理攻击 ≤7m——超距只能追近，禁止超远距离隔空输出
        const float MaxRangedReach = 8f;
        const float MaxMentalReach = 7f;

        float _attackCd, _mentalCd, _rangedCd, _staggerTimer, _tauntTimer;
        float _flinchCd;          // 受击霸体冷却：期间轻击不再打断（防无限硬直）
        float _defendCd;          // 防御冷却：闪避/格挡后短时间内不再防（防无敌化）
        PoseState _attackPose = PoseState.Attack;   // 本次出手选中的招式（多样化）
        int _comboLeft;           // 精英/首领的连击追加段数
        float _strafeDir = 1f, _strafeFlipT;        // 交战游走（像人一样找角度）
        Vector3 _lastSelfPos;     // 位移驱动动画：任何来源的移动都要迈脚，不许滑行
        float _measuredSpeed;
        bool _posInit;
        float _lastHurtT = -99f;  // 受击眩晕：刚被打中短时间内没有反击能力
        float _legHurtUntil;      // 腿部被打中：一段时间内移动变慢（打腿＝打机动力）
        float _armHurtUntil;      // 手臂被打中：一段时间内出手变慢、伤害下降（打手＝打攻势）
        float _swingUntil;        // 挥击动作进行中（此间不游走、不被小受击打断画面）
        bool _downed;             // 被击倒趴地中（恢复时要播起身过程）
        int _patrolIndex;
        TextMesh _alertMark;      // 前摇警示「！」
        GameObject _dangerRing;   // 前摇地面红圈
        Material _dangerRingMat;  // 红圈材质（危险攻击时染更亮的橙红）
        bool _perilous;           // 本次攻击为「危险攻击」：不可格挡，只能闪避（大作红光警示）

        /// <summary>起手前摇下限（秒）：对玩家的可反应性承诺，任何敌人都不得低于此值。</summary>
        public const float MinWindup = 0.5f;

        /// <summary>连击追加段的前摇（秒）：比起手短，但绝不为零。</summary>
        public const float ComboWindup = 0.34f;

        /// <summary>是否正处于攻击前摇（威胁指示器据此在屏幕边缘标出看不见的敌人）。</summary>
        public bool Telegraphing => _telegraphing;

        /// <summary>本次前摇是否为不可格挡的危险攻击。</summary>
        public bool PerilousTelegraph => _telegraphing && _perilous;

        // 征兆的颜色语言统一由 TelegraphTable 定义（颜色即应对答案），
        // 这里只保留一个建材色供红圈材质初始化。
        static readonly Color TeleNormal = new Color(0.9f, 0.15f, 0.1f);
        bool _telegraphing;       // 是否处于前摇（脉冲放大红圈/警示，让读招更醒目）
        Vector3 _dangerRingBaseScale;
        float _telegraphT;

        /// <summary>敌方武术类型（方案 10.5）：决定它的动作语言与它考验玩家的那一项能力。</summary>
        [HideInInspector] public MartialArchetype archetype = MartialArchetype.Fist;

        /// <summary>兵器/心念弹的主题色（生成时由外部注入）。</summary>
        [HideInInspector] public Color themeColor = new Color(0.7f, 0.4f, 0.9f);
        [HideInInspector] public Material baseMaterial;

        /// <summary>外部伤害倍率（Boss 机制：明天之王泥壳护体、旧我整合阶段等）。</summary>
        [HideInInspector] public float externalDamageMult = 1f;

        /// <summary>安抚状态（旧我整合阶段）：停止一切攻击与追击，站在原地等待整合。</summary>
        [HideInInspector] public bool pacified;

        /// <summary>
        /// 候场状态（群战人数上限）：这一个还没轮到上场——不追击、不出手、不喊话，
        /// 在自己那一带待着。但它**照样能被打到**，而且挨一下就立刻入场（见 TakeHit）。
        ///
        /// 与 pacified 的区别：pacified 是剧情态（连伤害都免疫），
        /// 这个只是排队。玩家反馈"四个以上敌人加大 Boss 一起围上来，不公平"——
        /// 攻击令牌只挡住了同时**出手**的人数，挡不住同时**围过来喊话**的人数，
        /// 场面上仍然是六个打一个。真正要限的是同时参战的人头。
        /// </summary>
        [HideInInspector] public bool holdPosition;

        /// <summary>玩家主动打过它：从此不再被排进候场队列（打了就得认真打完）。</summary>
        [HideInInspector] public bool provoked;

        /// <summary>血线保护（0-1）：血量不会被打到该比例以下（旧我必须走整合结局而非击杀）。</summary>
        [HideInInspector] public float minHpFloor = 0f;

        /// <summary>当前血量比例（Boss 阶段机 0-1）。</summary>
        public float HpRatio => profile.maxHealth > 0 ? Mathf.Clamp01(_hp / profile.maxHealth) : 0f;

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
            // 【必须用 Unlit】：红圈是给玩家看的警示 UI，不是场景里的一块地板。
            // 之前跟着敌人的 Lit 材质走，夜间/暗巷关卡里环境光一低，红圈就跟着变成
            // 一圈几乎看不见的深褐色——玩家反馈的"看不清敌人的状况、突然就掉血"
            // 有一半是这么来的：警示确实亮了，只是在那种光照下看不见。
            // Unlit 不受光照影响，白天黑夜一样醒目；再关掉投影与接收阴影，避免它自己发黑。
            Material m = new Material(
                Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            m.color = TeleNormal;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color);
            rr.sharedMaterial = m;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rr.receiveShadows = false;
            _dangerRingMat = m;
            _dangerRing.SetActive(false);
        }

        /// <summary>
        /// 亮起/收起前摇征兆。征兆规格全部来自 <see cref="TelegraphTable"/>：
        /// 形状按招式轨迹、颜色按应对答案、时长按招式族恒定——三层信息各说一件事，
        /// 玩家因此可以真正"学会"某个敌人的出手规律，而不是只知道"它要打了"。
        /// </summary>
        void ShowTelegraph(bool on, bool perilous = false)
        {
            _telegraphing = on;
            _telegraphT = 0f;
            if (!on)
            {
                _perilous = false;
                if (_alertMark != null) _alertMark.text = "";
                if (_dangerRing != null) _dangerRing.SetActive(false);
                if (poser != null) poser.SetWindup(-1, 0f);   // 形体征兆平滑退回
                return;
            }

            _spec = TelegraphTable.Get(_attackPose, perilous);

            if (_alertMark != null)
            {
                _alertMark.text = _spec.mark;
                _alertMark.color = _spec.color;
            }
            if (_dangerRingMat != null)
            {
                _dangerRingMat.color = _spec.color;
                if (_dangerRingMat.HasProperty("_BaseColor"))
                    _dangerRingMat.SetColor("_BaseColor", _spec.color);
            }
            if (_dangerRing != null)
            {
                _dangerRing.SetActive(true);
                // 指示器按招式轨迹取形：横斩是横向宽弧、突刺是细长直线、扫腿是贴地宽环。
                // 「往哪躲」于是有画面依据，而不是全场统一一个圆圈。
                _dangerRingBaseScale = new Vector3(_spec.ring.x, 0.03f, _spec.ring.y);
                _dangerRing.transform.localScale = _dangerRingBaseScale;
                // 指示器中心随纵深前移：突刺的危险区在身前四米，不在脚下
                _dangerRing.transform.localPosition =
                    new Vector3(0, -0.95f + _spec.ringHeight, _spec.ring.y * 0.28f);
            }
            // 教学提示：只在玩家还没见过这一族时说一次（说多了就成了噪声）
            if (NoteTelegraphSeen(_spec.kind, perilous))
                GameEvents.RaiseSubtitle("【" + _spec.name + "】" + _spec.answer);
        }

        TelegraphSpec _spec;

        /// <summary>某一族征兆是否第一次出现（全局记一次，教学提示只讲一遍）。</summary>
        static readonly HashSet<int> _seenTelegraphs = new HashSet<int>();
        static bool NoteTelegraphSeen(TelegraphKind k, bool perilous)
        {
            int key = (int)k * 2 + (perilous ? 1 : 0);
            return _seenTelegraphs.Add(key);
        }

        /// <summary>前摇脉冲：红圈由小放大到出手、警示「！」跳动，读招窗口一目了然。</summary>
        void TickTelegraph(float dt)
        {
            if (!_telegraphing)
            {
                if (poser != null && _windupTotal > 0f) { poser.SetWindup(-1, 0f); _windupTotal = 0f; }
                return;
            }
            _telegraphT += dt;
            // 前摇进度：指示器不再是无意义的来回脉动，而是**从小涨到满**——
            // 涨满 = 出手。这条"进度条"是玩家判断出手时机的直接依据（读招的第三层信息）。
            float p01 = _windupTotal > 0.01f ? Mathf.Clamp01(_telegraphT / _windupTotal) : 0f;
            if (_dangerRing != null)
            {
                float grow = Mathf.Lerp(0.45f, 1.12f, p01);
                _dangerRing.transform.localScale = new Vector3(
                    _dangerRingBaseScale.x * grow, _dangerRingBaseScale.y,
                    _dangerRingBaseScale.z * grow);
            }
            if (_alertMark != null)
                _alertMark.characterSize = 0.05f * (1f + 0.35f * p01);
            // 形体征兆：随进度加深的预备姿态（高举/后拉/压低……）——
            // 这是关了 UI 也读得出来的那一层
            if (poser != null) poser.SetWindup((int)_spec.kind, p01);
        }

        float _windupTotal;   // 本次前摇的总时长（进度条与形体征兆按它归一化）
        float _lastCastShot = -99f;   // 上次绝招特写的时刻（节流：特写贵在稀有）

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

            // 部位伤情：腿伤减速、手伤削弱攻势（每帧同步到寻路速度上）
            if (_agent != null)
                _agent.speed = profile.moveSpeed * (Time.time < _legHurtUntil ? 0.62f : 1f);

            // 把移动速度喂给人形动画（步行/奔跑步态）。
            // 用【真实每帧位移】而非寻路速度：追击/游走/击退/侧闪，无论位移来自
            // 哪个系统，脚都会迈动——消灭"人在滑、脚不动"的漂移感。
            if (poser != null)
            {
                if (!_posInit) { _lastSelfPos = transform.position; _posInit = true; }
                Vector3 planar = transform.position - _lastSelfPos;
                planar.y = 0;
                _lastSelfPos = transform.position;
                float inst = dt > 0.0001f ? planar.magnitude / dt : 0f;
                _measuredSpeed = Mathf.Lerp(_measuredSpeed, inst, 12f * dt);
                float refSpeed = Mathf.Max(2.5f, profile.moveSpeed);
                // 移动方向相对身体正面的夹角：敌人交战时脸始终对着玩家，
                // 而脚下在绕圈/后撤/横挪——不把这个角度喂给动画层，播的就永远是
                // "向前走"，于是横着挪动时脚在原地倒腾＝**漂移**。
                float moveAngle = 0f;
                if (planar.sqrMagnitude > 1e-6f)
                    moveAngle = Vector3.SignedAngle(transform.forward, planar.normalized, Vector3.up);
                // 敌人交战中脸始终对着玩家、脚下在绕圈/后撤，夹角恒为有意为之
                poser.SetLocomotion(Mathf.Clamp01(_measuredSpeed / refSpeed) * 0.9f,
                    false, true, _measuredSpeed, moveAngle, true);
                // 交战中静立时摆出格斗预备架势（而非松垮站立）
                // 十类武术里只有刀术/棍术/重武器是持械的，其余七类是拳脚。
                // 持剑动作集（临战架势、蹲伏、踢击、受击、倒下）双手都是握着东西的，
                // 空手型敌人套上去会像凭空攥着一把剑。
                poser.SetArmed(archetype == MartialArchetype.Blade ||
                               archetype == MartialArchetype.Staff ||
                               archetype == MartialArchetype.Heavy);
                poser.SetCombatReady(State == EnemyState.Chase || State == EnemyState.Attack
                    || State == EnemyState.MentalAttack);
            }

            // ===== 出招立足（本轮修的"敌人边跑边打时在漂移"）=====
            //
            // 漂移的成因不是动画，是**位移与动作各走各的**：
            // NavMeshAgent 带着速度与一条未作废的路径继续把人往前送，
            // 而身上正在播一段原地的挥击动作——脚不动、人在飘，就是漂移。
            // 之前只在 DoPhysicalAttack 的那一瞬调了一次 StopMoving：
            // 那一帧之后 Chase 分支照样会 SetDestination 把 isStopped 重新打开，
            // 于是"跑动中发起的攻击"整段都在滑行。
            //
            // 规则改成硬性的一条：**从亮起前摇到收招结束，脚钉在地上**。
            // 攻击是一次承诺——出手前站定、出手后收势，这既是所有格斗游戏的通例，
            // 也是玩家能够读招、绕背、抓破绽的前提（会移动的攻击等于没有破绽）。
            // 需要"移动着打"的招式走 AttackStep 的可控前踏，而不是让寻路顺带把人推过去。
            bool committed = _telegraphing || Time.time < _swingUntil;
            if (committed && AgentReady)
            {
                if (!_agent.isStopped) _agent.isStopped = true;
                _agent.velocity = Vector3.zero;
                if (_agent.hasPath) _agent.ResetPath();
            }

            // 安抚状态（旧我整合阶段）：收势站定，不再攻击、不再追击
            if (pacified)
            {
                if (State != EnemyState.Idle)
                {
                    State = EnemyState.Idle;
                    CancelInvoke();
                    ShowTelegraph(false);
                    if (attackHitbox != null) attackHitbox.DisableHitbox();
                    StopMoving();
                    if (poser != null) poser.SetPose(PoseState.Idle);
                }
                UpdateEmotion("平静");
                return;
            }

            // 候场：还没轮到上场的那几个，站在自己那一带，不追不打不喊话。
            // 硬直中不打断——被打飞的那一下要播完，否则会看到人被打中却瞬间站直。
            if (holdPosition && State != EnemyState.Stagger)
            {
                if (State == EnemyState.Chase || State == EnemyState.Attack ||
                    State == EnemyState.MentalAttack)
                {
                    Combat.CombatDirector.Release(this);
                    ShowTelegraph(false);
                    if (attackHitbox != null) attackHitbox.DisableHitbox();
                    StopMoving();
                    State = EnemyState.Idle;
                }
                PatrolTick();
                UpdateEmotion("旁观");
                return;
            }

            if (State == EnemyState.Stagger)
            {
                _staggerTimer -= dt;
                UpdateEmotion("慌乱");
                if (_staggerTimer <= 0)
                {
                    State = EnemyState.Chase;
                    if (poser != null)
                    {
                        // 被击倒的要先播"起身过程"（倒地片段倒放：腿脚先动、身体渐立），
                        // 绝不原地瞬间站直；普通踉跄直接回架势
                        if (_downed) { _downed = false; poser.PlayGetUp(); }
                        else poser.SetPose(PoseState.Idle);
                    }
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
                {
                    UpdateEmotion("紧逼");
                    if (committed) break;   // 出招承诺期间不追不挪（见上方"出招立足"）
                    bool isBoss = profile.category == EnemyCategory.Boss;
                    // 围攻礼让（大作群战规则）：远处先逼近到「待战环」；只有抢到攻击令牌的
                    // 敌人才继续挤进近身发动攻击，其余在待战环外绕圈施压，不堆挤玩家身体。
                    float standoff = profile.attackRange + 1.8f;
                    if (dist > standoff)
                    {
                        MoveTowards(_player.position, dt);   // 尚在环外：拉近到待战环，无需令牌
                    }
                    else if (isBoss || Combat.CombatDirector.TryAcquire(this, isBoss))
                    {
                        MoveTowards(_player.position, dt);   // 抢到攻击位：贴身
                        if (dist <= profile.attackRange) State = EnemyState.Attack;
                    }
                    else
                    {
                        MaintainStandoff(standoff, dt);      // 没轮到：环上绕圈候场
                    }
                    // 中距离言语攻击（内心/混合敌人的主要远程手段——话语弹幕，非物理弹丸）
                    if (dist < Mathf.Min(profile.detectRange * 0.7f, MaxMentalReach) &&
                        _mentalCd <= 0 && profile.mentalDamage > 0)
                        DoMentalAttack();
                    else if (dist > profile.detectRange * 1.5f)
                    {
                        Combat.CombatDirector.Release(this);
                        State = EnemyState.Patrol;
                    }
                    break;
                }

                case EnemyState.Attack:
                    UpdateEmotion("狰狞");
                    StopMoving();
                    FaceTarget();
                    if (dist > profile.attackRange * 1.2f)
                    {
                        Combat.CombatDirector.Release(this);   // 脱离攻击态：归还攻击令牌
                        State = EnemyState.Chase; break;
                    }
                    // 受击眩晕：刚被打中 0.55s 内头脑发懵，没有能力立即反击——
                    // 攻势停止后才逐步恢复出手（被打了不能若无其事地还手）；
                    // 攻击令牌（围攻礼让）：取到令牌才真正出手，否则只在下方走位伺机
                    if (_attackCd <= 0 && Time.time - _lastHurtT > 0.55f &&
                        Combat.CombatDirector.TryAcquire(this, profile.category == EnemyCategory.Boss))
                    { DoPhysicalAttack(); break; }
                    // 出手间隙像人一样左右游走找角度（而非钉在原地干等）；
                    // 挥击动作进行中绝不游走——脚下滑动会毁掉出招画面（漂移感）
                    if (_attackCd > 0.45f && !_telegraphing && Time.time > _swingUntil && AgentReady)
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

        /// <summary>
        /// 待战环走位（围攻礼让）：没抢到攻击令牌的敌人保持在玩家周围一圈候场——
        /// 太近则后撤、太远则贴到环上、在环上则绕圈游走，绝不堆挤进玩家身体。
        /// 让群战像大作那样"一两个进攻、其余围而不攻"，而非一拥而上。
        /// </summary>
        void MaintainStandoff(float standoff, float dt)
        {
            FaceTarget();
            if (_player == null) return;
            Vector3 toP = _player.position - transform.position; toP.y = 0;
            float d = toP.magnitude;
            if (d < 0.01f) return;
            Vector3 dir;
            if (d < standoff - 0.4f) dir = -toP.normalized;          // 太近：后撤
            else if (d > standoff + 0.9f) dir = toP.normalized;      // 太远：贴回环上
            else
            {
                _strafeFlipT -= dt;
                if (_strafeFlipT <= 0)
                {
                    _strafeFlipT = Random.Range(1.5f, 3f);
                    _strafeDir = Random.value < 0.5f ? -1f : 1f;
                }
                dir = Vector3.Cross(Vector3.up, toP.normalized) * _strafeDir;   // 环上绕圈
            }
            if (AgentReady) _agent.Move(dir * profile.moveSpeed * 0.5f * dt);
            else transform.position += dir * profile.moveSpeed * 0.5f * dt;
        }

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
            if (!AgentReady) return;
            _agent.isStopped = true;
            // 清残余速度并作废当前路径：施法/聚气/出招前摇时，NavMeshAgent 的惯性会
            // 让敌人继续前滑一小段（"漂移"）。硬停(速度归零+作废路径)后基本原地不动。
            _agent.velocity = Vector3.zero;
            if (_agent.hasPath) _agent.ResetPath();
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

        /// <summary>
        /// 敌人招式的伤害/击退：统一从 EnemyMoveTable 取，不再散落魔数。
        /// 与玩家表结构相同（轨迹/范围/伤害/削韧/击退），调值各自独立——
        /// 玩家的大招付蓄力与意势的代价，敌人不付，照抄玩家系数会强出一截。
        /// </summary>
        static void MoveStats(PoseState p, out float dmgMul, out float knock)
        {
            var m = Combat.EnemyMoveTable.Get(p);
            dmgMul = m.damageMult;
            knock = m.knockback;
        }

        /// <summary>各招式从起手到真正接触目标的时间：判定框在动画"打到"的那一帧
        /// 才开启——刀/脚碰到身体的瞬间与伤害同步，打击所见即所得。
        /// （动画层加了起手偏移+提速，接触帧整体提前）</summary>
        static float ContactDelay(PoseState p)
        {
            switch (p)
            {
                case PoseState.HeavyAttack:
                case PoseState.AttackSpin:  return 0.2f;
                case PoseState.SpinKick:
                case PoseState.JumpKick:
                case PoseState.SideKick:    return 0.16f;
                case PoseState.AttackKick:  return 0.13f;
                case PoseState.PunchJab:    return 0.08f;
                case PoseState.PunchCross:
                case PoseState.SwordThrust: return 0.12f;
                default:                    return 0.14f;   // 横斩/撩斩等剑技
            }
        }

        void DoPhysicalAttack()
        {
            _attackCd = Mathf.Lerp(3.0f, 1.1f, profile.aggression);   // 更主动地找时机出手
            StopMoving();   // 蓄势前摇(Charge/聚气)即刻硬停：前摇期间原地不动，不前滑漂移
            TriggerAnim("Attack");

            // 招式多样化：像人一样换招——精英/首领概率掏出重斩/旋风/腿法，
            // 且有概率追加 1-2 段连击（高手连招压制）
            bool elite = profile.category == EnemyCategory.Boss || profile.aggression >= 0.6f;
            bool useElite = elite && Random.value < (profile.category == EnemyCategory.Boss ? 0.45f : 0.25f);
            var pool = useElite ? EliteMoves : BasicMoves;
            _attackPose = pool[Random.Range(0, pool.Length)];
            _comboLeft = profile.category == EnemyCategory.Boss ? Random.Range(1, 3)
                       : elite && Random.value < 0.4f ? 1 : 0;

            // 危险攻击（大作红光警示）：精英重招/Boss 有概率使出【不可格挡】的危险一击——
            // 头顶亮「危」、红圈更大更亮，只能闪避不能格挡，教玩家读招而非无脑格挡
            _perilous = useElite && Random.value < (profile.category == EnemyCategory.Boss ? 0.5f : 0.35f);

            // ===== 前摇：可观测、且**同族恒定**（这是"可学习"的前提）=====
            //
            // 上一版的前摇时长是 Lerp(0.75, 0.5, 攻击性)——同一个横斩，攻击性 0.3 的
            // 杂兵给 0.68 秒、攻击性 0.9 的精英给 0.53 秒。玩家永远学不会这种东西：
            // 规律必须能被记住，而"每个敌人各有各的节拍、还随机换招"是记不住的。
            // 现在时长由**招式族**决定（TelegraphTable），全场统一：
            // 见到"高举过顶"就知道还有 0.78 秒、见到"收刀到腰"就知道是 0.62 秒。
            // 敌人的强弱改由出手频率/连段长度/招式选择体现，而不是偷玩家的反应时间。
            float windup = Mathf.Max(MinWindup, TelegraphTable.Get(_attackPose, _perilous).windup);
            _windupTotal = windup;
            ShowTelegraph(true, _perilous);
            GameAudio.Play(GameAudio.Sfx.Alert, _perilous ? 0.75f : 0.55f, _spec.pitch);
            // 【绝招特写】不可格挡的大招值得一个镜头：把施展者框进画面、报出招名与应对，
            // 玩家看得见"它在做什么"，才有可能防住（见 ThirdPersonCamera.FocusOn）。
            // 不夺走操作权——特写期间玩家照常可以闪避/格挡/走位。
            //
            // 两道克制：只有【首领】的不可格挡技才给特写，且同一个敌人 9 秒内最多一次。
            // 特写贵在稀有——杂兵每记红光都推一次镜头，镜头就成了噪声，
            // 玩家反而更看不清战场（这是"知道何时不特写"的那一半）。
            if (_perilous && profile.category == EnemyCategory.Boss &&
                Time.time - _lastCastShot > 9f)
            {
                _lastCastShot = Time.time;
                CombatFeedback.EnemyCastShot(transform, Mathf.Min(windup, 1.1f),
                    profile.displayName + " · " + _spec.name, _spec.answer);
            }
            if (poser != null) poser.SetPose(PoseState.Charge);
            Invoke(nameof(OpenAttackHitbox), windup);
        }

        /// <summary>起手：播挥击动作。判定框延迟到动画的接触帧才开启（FireHitbox），
        /// 刀/脚真正碰到对方身体的那一刻伤害与特效同步出现。</summary>
        void OpenAttackHitbox()
        {
            ShowTelegraph(false);
            if (State == EnemyState.Dead || attackHitbox == null) return;
            GameAudio.Play(GameAudio.Sfx.Swing, 0.55f);
            if (poser != null) poser.SetPose(_attackPose);
            float contact = ContactDelay(_attackPose);
            _swingUntil = Time.time + contact + 0.45f;
            StartCoroutine(AttackStep(contact));   // 踏前一步接上距离（替代滑行）
            Invoke(nameof(FireHitbox), contact);
            Invoke(nameof(CloseHitbox), contact + 0.25f);
        }

        /// <summary>
        /// 出招前踏（大作里的 attack lunge）：把"够不着就滑过去"换成一次**可控的踏步**。
        ///
        /// 差别不在位移量，在**归属**：寻路推过来的位移与动作无关，所以看着像漂移；
        /// 这一步是攻击自己的一部分——只在挥击的接触帧之前推进、总量封顶、
        /// 够得着就完全不推。玩家看到的是"它踏前一步一刀劈过来"，
        /// 而不是"它一边滑行一边挥了一下"。
        /// </summary>
        System.Collections.IEnumerator AttackStep(float dur)
        {
            if (_player == null) yield break;
            Vector3 to = _player.position - transform.position; to.y = 0;
            float gap = to.magnitude - profile.attackRange * 0.75f;
            if (gap <= 0.05f || dur <= 0.01f) yield break;
            Vector3 dir = to.normalized;
            float total = Mathf.Min(gap, 0.9f);   // 封顶 0.9m：踏一步，不是冲刺
            float moved = 0f, t = 0f;
            while (t < dur && State != EnemyState.Dead)
            {
                float dt = Time.deltaTime;
                t += dt;
                // 前快后慢：起步就把身位吃掉大半，收尾自然停住（不是匀速滑行）
                float want = total * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                float step = want - moved;
                moved = want;
                if (step > 0f)
                {
                    if (AgentReady) _agent.Move(dir * step);
                    else transform.position += dir * step;
                }
                yield return null;
            }
        }

        void FireHitbox()
        {
            if (State == EnemyState.Dead || attackHitbox == null) return;
            MoveStats(_attackPose, out float dmgMul, out float knock);
            // 判定框按招式轨迹取形：突刺细长（侧移可躲开）、横斩横宽、回旋斩环身 360°、
            // 重砸罩住一片。此前敌人所有招共用一个固定方盒，玩家读了招也无从"往哪躲"。
            var spec = Combat.EnemyMoveTable.Get(_attackPose);
            attackHitbox.SetShape(spec.Size, spec.center);
            // 危险攻击：不可格挡（须闪避）+ 伤害/击退加成，兑现红光警示的威胁
            // 手臂伤情：出手明显变弱（打手臂的收益在这里兑现，玩家看得到）
            float armWeak = Time.time < _armHurtUntil ? 0.7f : 1f;
            attackHitbox.EnableHitbox(new DamageInfo
            {
                physicalDamage = profile.physicalDamage * dmgMul * armWeak * (_perilous ? 1.5f : 1f),
                mentalDamage = profile.mentalDamage * 0.3f,
                mentalAxis = profile.targetWeakness,
                knockback = knock + (_perilous ? 3f : 0f),
                unblockable = _perilous,
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
                // 【连击段同样要有前摇】——此前这里直接排 OpenAttackHitbox，
                // 也就是说敌人一套连招里只有第一下亮「！」，第二、三下是**零征兆**打到脸上。
                // 玩家反馈的"毫无理由、毫无征兆就掉血"，绝大多数就是这两下。
                // 连击段的前摇比起手短（保留连招的压迫感），但绝不为零：
                // 本作对玩家的承诺是「任何一次会造成伤害的攻击，出手前必定有可见警示」。
                _perilous = false;   // 连击段不做不可格挡（不可格挡只出现在有完整前摇的起手）
                _windupTotal = ComboWindup;
                ShowTelegraph(true, false);
                GameAudio.Play(GameAudio.Sfx.Alert, 0.45f, _spec.pitch + 0.1f);
                if (poser != null) poser.SetPose(PoseState.Charge);
                Invoke(nameof(OpenAttackHitbox), ComboWindup);
            }
            // 一套连招收尾：归还攻击令牌（让别的敌人有机会进攻——围攻礼让）
            else
            {
                Combat.CombatDirector.Release(this);
                // 攻防步法：收尾后有概率快速撤步拉开身位（进退有据的剑斗节奏）
                if (State == EnemyState.Attack && Random.value < 0.4f && AgentReady)
                    StartCoroutine(DodgeSlide(-transform.forward * 1.3f));
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
            // 凝念能量门禁：每发耗能，打空须停火回气（不能无限制零成本连发）
            if (!EnemyRangedEnergy.TryFire(this, _player.position, 20f, MaxRangedReach + 2f))
                return;
            Vector3 origin = transform.position + Vector3.up * 1.3f + transform.forward * 0.8f;
            Vector3 targetPos = _player.position + Vector3.up * 1.0f;
            Projectile.Launch(transform, origin, targetPos - origin,
                new DamageInfo
                {
                    physicalDamage = 0f,   // 言语弹幕：只压心神，不做远程物理削血
                    mentalDamage = profile.mentalDamage * 0.6f,
                    mentalAxis = profile.targetWeakness,
                    knockback = 0f,
                    attackerId = profile.enemyId
                }, 11f, themeColor, baseMaterial);
        }

        /// <summary>心理攻击：凝视/低语 + 实时恶意台词。可被定心格挡反制。</summary>
        void DoMentalAttack()
        {
            State = EnemyState.MentalAttack;
            _mentalCd = Mathf.Lerp(8f, 4f, profile.aggression);
            StopMoving();   // 施法凝念时停步：施法动画不许边滑边播（漂移感）
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
                CancelInvoke(nameof(FireHitbox));
            CancelInvoke(nameof(FireProjectile));
            ShowTelegraph(false);
            if (attackHitbox != null) attackHitbox.DisableHitbox();

            _posture -= profile.posture * 0.5f;
            CombatFeedback.HitSpark(transform.position + Vector3.up * 1.4f,
                new Color(0.5f, 0.85f, 1f));
            if (dialogue != null) dialogue.Show(ResponseLibrary.GetBrokenLine(), 2.2f);

            State = EnemyState.Stagger;
            Combat.CombatDirector.Release(this);   // 进入硬直/破绽：立即让出攻击令牌
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

            // 安抚状态（旧我整合阶段）：不再需要战斗，攻击无效——请走向整合圆环
            if (pacified)
            {
                CombatFeedback.DamageNumber(transform.position, "无需再战",
                    new Color(0.7f, 0.85f, 1f), 1.15f);
                return;
            }
            // 候场中被打：立刻入场。排队是为了不围殴玩家，不是给玩家一个
            // 站着挨打不还手的木桩——主动上去打它，它当然该还手。
            holdPosition = false;
            provoked = true;

            // Boss 护体（明天之王泥壳等）：伤害大幅削减时给出机制提示
            if (externalDamageMult <= 0.35f && Time.time - _lastHurtT > 3f)
                CombatFeedback.DamageNumber(transform.position + Vector3.up * 0.4f, "护体",
                    new Color(0.7f, 0.7f, 0.5f), 1.05f);

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
            bool guardedHit = false;
            if (!unaware && _defendCd <= 0f && State != EnemyState.Stagger && !dmg.unblockable)
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
                    // 兵器相撞：玩家的刀砍在敌人举起的兵器上——接触点金白火花四溅
                    Vector3 toSrcW = dmg.sourcePosition - transform.position; toSrcW.y = 0;
                    Vector3 guardDir = toSrcW.sqrMagnitude > 0.01f ? toSrcW.normalized : transform.forward;
                    CombatFeedback.WeaponClash(transform.position + guardDir * 0.7f + Vector3.up * 1.25f);
                    dmg.physicalDamage *= 0.25f;
                    dmg.postureDamage *= 0.55f;
                    guardedHit = true;   // 砍在兵器上：不出血花
                    // 格挡成功立即反击（挡+还手=像人一样的攻防转换）；架势片刻后收起
                    _attackCd = Mathf.Min(_attackCd, 0.35f);
                    CancelInvoke(nameof(GuardRecover));
                    Invoke(nameof(GuardRecover), 0.6f);
                }
            }

            // ---- 打断前摇：敌人在出招前摇里被打中，出招被打断 ----
            //
            // 【这里原来是本作最严重的一处设计错误】
            // 旧逻辑是"对攻"：只要在敌人前摇期间打中它，就【无条件反弹一份伤害给玩家】。
            // 它同时踩了三个大忌，玩家的"防不胜防、做什么防御都没用"基本全出自这里：
            //   ① 这份伤害【躲不掉】。它由玩家自己的攻击触发，而玩家正在出招——
            //      既不可能同时按住格挡，也不在翻滚无敌帧里。跳、闪、挡一个都用不上。
            //   ② 这份伤害【看不出来源】。玩家的招式判定框长达 2~4 米，站在三米外
            //      挥一刀照样触发，于是屏幕上就是"离得老远突然掉血"。
            //   ③ 它【惩罚了游戏自己教的东西】。全套读招系统（前摇警示、危险攻击、
            //      威胁指示器）都在教玩家"看见前摇就抢攻"，抢攻却要挨一下无法规避的伤害。
            //
            // 成熟动作游戏在这一格的通行规则是相反的：**打中前摇＝打断它**，这就是读招的奖励；
            // 精英/首领可以有霸体（轻击打不断），但那时它的攻击【依然带着完整前摇打出来】，
            // 玩家仍然能闪能挡——风险始终是可规避的，而不是凭空扣血。照此重写：
            // 破绽期要在【打断之前】就判定完：下面的打断会把敌人推进 Stagger，
            // 如果之后再看 State，等于"每一次成功打断都算处决"——处决横幅与慢镜会满屏刷，
            // 且把削韧破防这条正经循环的收益冲淡。处决只认真正靠削韧打出来的破绽。
            bool wasStaggered = State == EnemyState.Stagger;

            if (_telegraphing && !dmg.unblockable)
            {
                // 霸体：精英/首领对轻击不吃打断（但重击/绝招仍打得断）
                bool superArmor = (profile.category == EnemyCategory.Boss || profile.aggression >= 0.6f)
                                  && !DamageResolver.IsHeavy(dmg);
                Vector3 mid = _player != null
                    ? (transform.position + _player.position) * 0.5f + Vector3.up * 1.3f
                    : transform.position + Vector3.up * 1.3f;
                if (superArmor)
                {
                    // 打不断：明确告诉玩家"这一下没能打断，它的招还会出来"——
                    // 招还带着前摇，所以仍然躲得掉，玩家知道该准备闪了
                    CombatFeedback.DamageNumber(mid, "霸体·未打断", new Color(0.8f, 0.8f, 0.85f), 1.1f);
                }
                else
                {
                    ForceBreak(0.9f);   // 出招被打断 + 短硬直：读招抢攻的正收益
                    CombatFeedback.DamageNumber(mid, "打断！", new Color(1f, 0.85f, 0.35f), 1.4f);
                    CombatFeedback.WeaponClash(mid);
                }
            }

            // ---- 部位精准伤害（大作惯例：打得准有实际收益）----
            // 部位由**挂在骨骼上的受击框**落款（见 BodyPartHurtboxes），不再靠命中点
            // 高度去猜——下蹲、倒地、腾空时同一个高度对应的部位完全不同。
            // 系数集中在 BodyPartTable：头会心、四肢伤害低但削韧高（扫腿打失衡）。
            var part = dmg.bodyPart;
            if (part == BodyPart.None && dmg.hasContact)
                part = BodyPartTable.FromHeight(dmg.contactPoint.y - transform.position.y);
            var pp = BodyPartTable.Get(part, false);
            float partDmgMult = pp.damage, partPostureMult = pp.posture;
            bool headshot = pp.critical;
            // 打腿＝打机动力，打手＝打攻势：四肢命中除了削韧，还落到**它该影响的能力**上。
            // 这是"打哪儿有什么用"能被玩家学会的关键——只改数字学不会，行为变化才学得会。
            if (BodyPartTable.IsLeg(part)) _legHurtUntil = Time.time + 2.2f;
            else if (BodyPartTable.IsArm(part)) _armHurtUntil = Time.time + 2.2f;

            float final = DamageResolver.ResolvePhysical(dmg.physicalDamage, profile.defense)
                * sneakMult * partDmgMult;
            // 破绽期（韧性击破硬直）吃 1.6 倍伤害：奖励削韧打法。
            // 处决（大作破韧终结）：破绽期用重击/大招命中 = 巨额增伤 + 横幅 + 强顿帧慢镜，
            // 把「削韧破防→抓破绽猛攻」的循环做成有仪式感的收益。
            bool execHeavy = DamageResolver.IsHeavy(dmg);
            bool execution = false;
            if (wasStaggered)
            {
                if (execHeavy)
                {
                    final *= 2.8f;
                    execution = true;
                }
                else final *= 1.6f;
            }
            // 调试模式：敌人耐揍，大幅削减实际伤害（方便测试，不被秒杀）
            if (Core.GameDebug.TankyEnemies) final *= Core.GameDebug.TankyDamageScale;
            // Boss 护体倍率：血量与韧性同步受保护（破防要走机制，不能硬磨）
            final *= externalDamageMult;
            _hp -= final;
            if (minHpFloor > 0f) _hp = Mathf.Max(_hp, profile.maxHealth * minHpFloor);
            _posture -= dmg.postureDamage * partPostureMult * externalDamageMult;

            // 受击反馈：命中点冲击（火花+白闪盘+顿帧）/ 闪红 / 伤害数字 / 血花 / 击退
            Color sparkCol = State == EnemyState.Stagger
                ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.78f, 0.4f);
            Vector3 toAtk = dmg.sourcePosition - transform.position; toAtk.y = 0;
            Vector3 dirA = toAtk.sqrMagnitude > 0.01f ? toAtk.normalized : transform.forward;
            // 命中点：优先用判定框算出的【真实接触身体点】，退回估算（朝攻击者一侧胸口）
            Vector3 contact = dmg.hasContact ? dmg.contactPoint
                : transform.position + dirA * 0.55f + Vector3.up * 1.25f;
            // 重击判定用原始招式数值（不受调试减伤影响），保证打击手感稳定；
            // 力度 0-1 连续分级：火花密度、闪核大小、顿帧时长、受击冲量都按它给，
            // 一记直拳与一记裂地跳劈的反馈差距一眼可辨
            bool fbHeavy = DamageResolver.IsHeavy(dmg);
            float power = DamageResolver.Power01(dmg);
            CombatFeedback.HitImpact(contact, sparkCol, fbHeavy, true, power);
            // 血花：兵器/拳脚实打实击中血肉（格挡住的不出血）——从接触点顺打击方向外喷
            if (!guardedHit && dmg.physicalDamage > 0.5f)
                CombatFeedback.BloodSpray(contact, -dirA);
            // 部位受击反应：命中头就甩头、命中腿就屈弯，接触点弹部位标签；
            // 连续两次打中同一部位就有两次可见反应（冲量叠加，打几下动几下）
            if (!guardedHit)
                HitReactionOverlay.Trigger(transform, contact, -dirA, fbHeavy, 0.85f + power * 0.75f);
            CombatFeedback.HitFlash(gameObject);
            _lastHurtT = Time.time;   // 受击眩晕计时：攻势未停就没能力还手
            // 处决命中：仪式感反馈——横幅「处决」+ 强顿帧 + 短慢镜 + 能量爆发
            if (execution)
            {
                CombatFeedback.HitStop(0.13f);
                CombatFeedback.SlowMo(0.4f, 0.16f);
                CombatFeedback.EnergyBurst(contact, new Color(1f, 0.8f, 0.3f), 1.2f);
                CombatFeedback.CloseUp(1.0f, 0.75f);   // 破韧终结＝高光时刻，值得推近
                GameEvents.RaiseSkillBanner("处决");
            }
            // 伤害数字按部位分色分号：头部会心最大最亮，四肢偏冷色且带削韧提示。
            // （部位名由 HitReactionOverlay 在接触点弹出，这里不重复文字，只统一颜色语言）
            CombatFeedback.DamageNumber(transform.position, Mathf.RoundToInt(final).ToString(),
                execution ? new Color(1f, 0.6f, 0.15f)
                : headshot ? new Color(1f, 0.55f, 0.25f)
                : State == EnemyState.Stagger ? new Color(1f, 0.85f, 0.25f)
                : part != BodyPart.Chest && part != BodyPart.None ? BodyPartTable.TintOf(part)
                : new Color(1f, 0.9f, 0.5f),
                execution ? 1.9f : headshot ? 1.45f : final >= 35f ? 1.6f : 1f);
            if (dmg.knockback > 0.1f && !fbHeavy)
            {
                // 击退平滑化：瞬移是"打地鼠式漂移"的最大来源——改为 0.22s 快滑退，
                // 位移驱动的步态会同步迈脚，读作"被打得连退几步"。
                // 重击不走这里：位移完全交给 KnockFly 与倒地动画同步（否则双重
                // 位移=先漂移一段再倒下，不真实）
                // knockback 是【力度记号】(1~16)，不是米数——这里必须换算。
                // 上一版直接 ×0.85 当米用：巨剑横斩 knockback 4.5 → 一记轻击把人推开
                // 3.8 米，连招直接脱靶。动作游戏的普通连段推开量在 0.2~0.8 米，
                // 目的是「打得动」而不是「打飞」——推远了反而接不上下一段。
                Vector3 kb = DamageResolver.KnockbackDir(dmg.sourcePosition, transform.position)
                             * Mathf.Min(dmg.knockback * 0.09f, 0.8f);
                StartCoroutine(KnockSlide(kb));
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
            bool heavyHit = fbHeavy;
            if (_posture > 0 && State != EnemyState.Stagger && (_flinchCd <= 0f || heavyHit))
            {
                // 受击霸体冷却 1.1→0.7s（Boss 2.4→1.9s）：原值下杂兵在一整套连段里
                // 只踉跄一次，剩下四五下全程站着不动——这是"打上去没反应/攻击力弱"
                // 最刺眼的一处。缩短后普通敌人几乎每两下就吃一次硬直，但仍保留
                // 霸体窗口，不至于被彻底连到死。
                _flinchCd = profile.category == EnemyCategory.Boss ? 1.9f : 0.7f;
                CancelInvoke(nameof(OpenAttackHitbox));
                CancelInvoke(nameof(FireHitbox));
                CancelInvoke(nameof(FireProjectile));
                ShowTelegraph(false);
                if (attackHitbox != null) attackHitbox.DisableHitbox();
                State = EnemyState.Stagger;
            Combat.CombatDirector.Release(this);   // 进入硬直/破绽：立即让出攻击令牌
                _staggerTimer = heavyHit ? 1.5f : 0.42f;
                StopMoving();
                // 重击=被撞飞重重倒地（受击状态可视化），恢复时播起身过程。
                // 击飞很远（大击退）时播【腾空后翻滚】——飞出去是真实空翻而非僵直漂移
                if (heavyHit)
                {
                    _downed = true;
                    // 「击飞」是少数招式的特权，不是重击的默认表现。
                    // 门槛从 knockback≥4 抬到 ≥8：4 这条线连巨剑横斩(4.5)都算数，
                    // 于是普通连段每隔几下就把人抛出去一次——既接不上连招，
                    // 也不是大作的做法（大作里只有专门的吹飞技/终结技才击飞）。
                    // ≥8 之后只剩旋身空翻踢(9)与成招终结(×1.8)能触发。
                    bool bigLaunch = dmg.knockback >= 8f || dmg.physicalDamage >= 60f;
                    Vector3 flyDir = DamageResolver.KnockbackDir(dmg.sourcePosition, transform.position);
                    if (bigLaunch)
                    {
                        float flyDur = 0.55f;
                        _staggerTimer = Mathf.Max(_staggerTimer, flyDur + 0.6f);
                        if (poser != null) poser.PlayTumble(flyDur);
                        // 距离同样要换算并封顶：原式 5.5+knockback*0.6 在 knockback=16
                        // （成招终结的旋身空翻踢）时算出 15 米，人直接飞出视野。
                        StartCoroutine(KnockFly(flyDir,
                            Mathf.Clamp(1.6f + dmg.knockback * 0.22f, 1.6f, 4.2f), flyDur));
                    }
                    else
                    {
                        if (poser != null) poser.SetPose(PoseState.Knockdown);
                        StartCoroutine(KnockFly(flyDir, 1.4f, 0.15f));
                    }
                }
                else if (poser != null)
                    poser.SetHitPose(dmg.physicalDamage, dmg.knockback);
            }
            // 霸体冷却期间也要【看得出挨了打】：不打断攻防逻辑，但受击动作必播
            //（此前霸体期间连受击动画都不播，就是"被踢了一脚却站着没反应"的原因）。
            // 仅在自己不处于挥击相位时播，避免把正在出的招从画面上抹掉。
            else if (!guardedHit && State != EnemyState.Stagger &&
                     Time.time > _swingUntil && poser != null)
            {
                poser.SetHitPose(dmg.physicalDamage, dmg.knockback);
            }

            if (_posture <= 0)
            {
                // 韧性击破=破绽：明确提示 + 破绽期吃 1.6 倍伤害
                _posture = profile.posture;
                State = EnemyState.Stagger;
            Combat.CombatDirector.Release(this);   // 进入硬直/破绽：立即让出攻击令牌
                _staggerTimer = 2.4f;
                StopMoving();
                CancelInvoke(nameof(OpenAttackHitbox));
                CancelInvoke(nameof(FireHitbox));
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

        /// <summary>Boss 机制回血（两元赖账王耍赖回血等）：按最大生命比例恢复。</summary>
        public void HealFraction(float frac)
        {
            if (State == EnemyState.Dead) return;
            _hp = Mathf.Min(profile.maxHealth, _hp + profile.maxHealth * Mathf.Clamp01(frac));
            if (statusBar != null) statusBar.SetHealth(_hp, profile.maxHealth);
            CombatFeedback.DamageNumber(transform.position, "耍赖回血",
                new Color(0.6f, 0.9f, 0.6f), 1.1f);
        }

        /// <summary>机制性强制破绽（火种齐燃/整合触发等 Boss 事件）：进入长硬直吃增伤。</summary>
        public void ForceBreak(float duration)
        {
            if (State == EnemyState.Dead) return;
            CancelInvoke(nameof(OpenAttackHitbox));
            CancelInvoke(nameof(FireHitbox));
            CancelInvoke(nameof(FireProjectile));
            ShowTelegraph(false);
            if (attackHitbox != null) attackHitbox.DisableHitbox();
            _posture = profile.posture;
            State = EnemyState.Stagger;
            Combat.CombatDirector.Release(this);   // 进入硬直/破绽：立即让出攻击令牌
            _staggerTimer = duration;
            StopMoving();
            if (poser != null) poser.SetPose(PoseState.Stagger);
            if (statusBar != null)
            {
                statusBar.SetPosture(_posture, profile.posture);
                statusBar.SetEmotion("破绽！！猛攻！");
            }
            if (dialogue != null) dialogue.Show("【破绽】", 2.2f);
            CombatFeedback.SlowMo(0.5f, 0.15f);
        }

        /// <summary>蓄力气场外推：玩家蓄力风场把靠近的敌人持续推出半径外（无法近身攻击）。
        /// 越靠近中心推力越强；同时打断正在进行的出手前摇。</summary>
        public void Repel(Vector3 center, float radius, float strength, float dt)
        {
            if (State == EnemyState.Dead) return;
            Vector3 away = transform.position - center; away.y = 0;
            float d = away.magnitude;
            if (d > radius || d < 0.01f) return;
            float k = 1f - d / radius;
            Vector3 step = away.normalized * (strength * (0.35f + k)) * dt;
            if (AgentReady) _agent.Move(step);
            else transform.position += step;
        }

        /// <summary>受击退步：0.22 秒滑完击退量（快出慢收），不瞬移。</summary>
        System.Collections.IEnumerator KnockSlide(Vector3 offset)
        {
            offset.y = 0;
            float t = 0, dur = 0.22f;
            while (t < dur && State != EnemyState.Dead)
            {
                float dt = Time.deltaTime;
                t += dt;
                Vector3 step = offset * Mathf.Min(dt / dur, 1f);
                if (AgentReady) _agent.Move(step);
                else transform.position += step;
                yield return null;
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

        /// <summary>重击击飞：受击位移（二次强减速）。distance=总飞行距离、dur=时长。
        /// 小击退=极短栽倒（≈1.4m/0.15s，当场倒地）；大击退=飞很远（配合腾空后翻滚，
        /// 5m+/0.55s），位移与空翻同步，不再是僵直漂移。</summary>
        System.Collections.IEnumerator KnockFly(Vector3 dir, float distance, float dur)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 0.01f) yield break;
            dir = dir.normalized;
            // 二次减速位移积分 ∫3k²=1 → 峰值速度系数使总位移=distance
            float peak = distance * 3f / dur;
            float t = 0;
            while (t < dur && State == EnemyState.Stagger)
            {
                t += Time.deltaTime;
                float k = 1f - t / dur;
                float sp = peak * k * k;
                if (AgentReady) _agent.Move(dir * sp * Time.deltaTime);
                else transform.position += dir * sp * Time.deltaTime;
                yield return null;
            }
        }

        void Die()
        {
            State = EnemyState.Dead;
            Combat.CombatDirector.Release(this);   // 死亡：归还攻击令牌
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
            // 击杀是【轻推】而非大招级满推，且交由镜头节流/群战抑制裁决——
            // 群战里每杀一个就贴脸一次，会让镜头长期焊死在特写位
            CombatFeedback.CloseUp(0.9f, 0.55f);
            GameAudio.Play(GameAudio.Sfx.Death, 0.9f);
            GameEvents.RaiseEnemyKilled(profile.enemyId);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Destroy(gameObject, 3f);
        }
    }
}
