using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 第三人称移动控制器：走/跑（摇杆模拟量）/慢走/蹲伏潜行/跳跃/
    /// 翻滚闪避（无敌帧）/快速灵活转身。
    /// 每帧把运动状态喂给 HumanoidAnimator 驱动类人步态动画。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("移动（速度按真实体感收敛，防晕）")]
        public float walkSpeed = 2.6f;
        public float runSpeed = 5.2f;
        // 起步/刹车响应（指数逼近速率，1/秒）：对齐 Unity 官方 ThirdPersonController
        // 的 SpeedChangeRate=10 思路，但按动作游戏上调——
        // 起步 k=20：0.05s 到 63%、0.15s 到 95%；刹车 k=26 更利落。
        // 防晕由镜头侧负责（软跟随 + 焦点死区 + 渐进回正），而不是靠拖慢角色本体。
        public float accelRate = 20f;              // 起步逼近速率
        public float decelRate = 26f;              // 停步逼近速率
        public float rotateSpeed = 14f;            // 转身更跟手（大作级的方向响应）
        public float quickTurnMultiplier = 2.1f;   // 大角度转身加速倍率：掉头近乎即时
        public float jumpForce = 7f;
        public float gravity = -20f;

        [Header("蹲伏")]
        public float crouchSpeedMult = 0.45f;

        [Header("闪避（翻跟头）")]
        public float dodgeSpeed = 10f;
        public float dodgeDuration = 0.35f;
        public float dodgeIFrames = 0.25f;
        public float dodgeStaminaCost = 20f;

        public PlayerStats Stats = new PlayerStats();
        public Transform cameraTransform;

        CharacterController _cc;
        CombatStateMachine _combat;
        HumanoidAnimator _anim;
        LockOnSystem _lockOn;
        PlayerAppearance _appearance;
        Combat.PlayerCombatController _pcc;
        float _vy;
        float _dodgeTimer, _iframeTimer;
        float _dodgeSpd = 10f;   // 本次翻滚的实际速度（时长匹配片段时反比缩放）
        Vector3 _dodgeDir;
        Vector3 _lastPos;
        Vector3 _hVel;   // 平滑后的水平速度

        /// <summary>拖延泥潭等减速效果的外部倍率（1 = 正常）。</summary>
        public float MoveSpeedMultiplier { get; set; } = 1f;

        // ===== 移动平台承载 =====
        // CharacterController 不会自动跟随脚下移动的物体：车在动、脚下的碰撞体在动，
        // 而角色的世界坐标是自己算出来的，于是"跳上车顶后车开走、人留在原地"。
        // Unity 从来没有内建这个——所有第三人称游戏都得自己补：每帧记住脚下那个
        // 物体的位移，原样加到角色身上。
        Transform _platform;
        Vector3 _platformLastPos;

        void CarryByPlatform()
        {
            if (_cc == null) return;
            Transform found = null;
            if (_cc.isGrounded)
            {
                // 从腰部往下扫一个略小于胶囊半径的球：命中脚下的实体碰撞体
                if (Physics.SphereCast(transform.position + Vector3.up * 0.7f,
                        Mathf.Max(0.1f, _cc.radius * 0.85f), Vector3.down,
                        out RaycastHit hit, 1.3f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var t = hit.collider != null ? hit.collider.transform : null;
                    if (t != null && !t.IsChildOf(transform)) found = t;
                }
            }

            if (found != null && found == _platform)
            {
                Vector3 delta = found.position - _platformLastPos;
                // 只跟随合理幅度的位移：平台被瞬移/重生时不要把角色一起甩飞
                if (delta.sqrMagnitude > 1e-8f && delta.sqrMagnitude < 25f) _cc.Move(delta);
            }
            _platform = found;
            if (found != null) _platformLastPos = found.position;
        }

        /// <summary>本帧摇杆的世界方向（相机相对，零向量=未推杆）。
        /// 技能连招据此判断玩家是否正在主动引导方向，从而让出自动吸附。</summary>
        public Vector3 StickWorldDir { get; private set; }

        // ---- 意图匹配：输入缓冲 + 土狼时间（大作通用）----
        const float DodgeBufferWindow = 0.3f;   // 闪避缓冲窗（硬直中按下也能在恢复帧兑现）
        const float JumpBufferWindow = 0.2f;    // 跳跃缓冲窗（落地前按下，落地即跳）
        const float CoyoteTime = 0.12f;         // 土狼时间（离开边缘后仍可起跳）
        readonly InputBuffer _inputBuf = new InputBuffer();

        /// <summary>供其它战斗组件共用的输入缓冲（技能在动作锁期间的排队兑现）。</summary>
        public InputBuffer Buffer => _inputBuf;
        float _coyoteT;
        float _attackSpeedFactor = 1f;   // 出招定步倍率（平滑过渡，防连打时速度震荡）

        public bool IsInvincible => _iframeTimer > 0;
        public bool IsDodging => _dodgeTimer > 0;
        public bool IsCrouched { get; private set; }
        /// <summary>手指/方向键此刻是否在推——**零平滑、零延迟**。
        /// 镜头的跟随起停以它为准（见 ThirdPersonCamera 的 active 判据）。</summary>
        public bool StickHeld { get; private set; }
        /// <summary>是否离地（跳跃/坠落中）。镜头据此切"腾空·拉远看落点"景别。</summary>
        public bool Airborne => _cc != null && !_cc.isGrounded;
        /// <summary>纵向速度（负=下坠）。镜头用它区分"起跳上升"与"真的在往下掉"。</summary>
        public float VerticalVelocity => _vy;

        /// <summary>倒地起身等外部授予的无敌帧。</summary>
        public void SetInvincible(float duration) => _iframeTimer = Mathf.Max(_iframeTimer, duration);

        /// <summary>空中下劈等强制下坠。</summary>
        public void ForceFall(float verticalVelocity) => _vy = Mathf.Min(_vy, verticalVelocity);

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _combat = GetComponent<CombatStateMachine>();
            _anim = GetComponent<HumanoidAnimator>();
            _lastPos = transform.position;
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void Update()
        {
            // 运行时组装顺序下 Awake 期间兄弟组件可能尚未挂载，惰性补齐
            if (_combat == null) _combat = GetComponent<CombatStateMachine>();
            if (_pcc == null) _pcc = GetComponent<Combat.PlayerCombatController>();

            float dt = Time.deltaTime;
            Stats.TickRegen(dt, _combat != null && _combat.InCombat);
            if (Stats.IsDead) return;

            if (_iframeTimer > 0) _iframeTimer -= dt;

            // 输入采集 → 缓冲（意图匹配）：必须在任何 early-return 之前，
            // 否则翻滚中/硬直中按下的键会被整帧跳过而丢失（连续翻滚就是这样失效的）。
            // 消费式输入（MobileInput.GetDown）每帧只在这里读一次，其余系统一律走缓冲。
            // 按下时角色是否正处在"做不了别的事"的状态——动作锁 或 翻滚中。
            // 这一位决定该输入是【排队意图】还是【即时意图】：排队的活到动作结束，
            // 即时的用短窗口。此前不分意图，闪避窗 0.30s 连翻滚自身(0.42~0.7s)都撑不过，
            // 连续翻滚与"技能收招接闪避"必然丢键。
            bool busyNow = (_combat != null && _combat.IsActionLocked) || _dodgeTimer > 0;
            if (Input.GetKeyDown(KeyCode.LeftShift) || MobileInput.GetDown("Dodge"))
                _inputBuf.Press("Dodge", busyNow);
            if (Input.GetKeyDown(KeyCode.Space) || MobileInput.GetDown("Jump"))
                _inputBuf.Press("Jump", busyNow);

            if (_dodgeTimer > 0)
            {
                _dodgeTimer -= dt;
                _cc.Move(_dodgeDir * _dodgeSpd * dt + Vector3.up * _vy * dt);
                if (_dodgeTimer <= 0 && _combat != null) _combat.RequestState(CombatState.Locomotion);
                return;   // 翻滚中按下的闪避已入缓冲，滚完立刻接下一次（连续翻滚）
            }

            bool dodgePressed = _inputBuf.Has("Dodge", DodgeBufferWindow);

            // 摇杆世界方向提前求出：硬锁动作（技能/绝招/重击）期间也要读它做「出招转向」，
            // 所以不能等到下面移动逻辑才算
            Vector2 stickInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            stickInput += MobileInput.Move;
            stickInput = Vector2.ClampMagnitude(stickInput, 1f);
            Vector3 stickDir = CameraRelative(stickInput);
            StickWorldDir = stickDir;   // 供技能连招判断"玩家是否正在主动引导方向"
            // 未经平滑的"手指/按键此刻在不在"——镜头用它作为跟随起停的零延迟判据。
            // StickWorldDir 来自平滑后的摇杆量，松手后还要几十毫秒才落到阈值以下，
            // 而镜头在那几十毫秒里多转的每一度都会被玩家看见。
            StickHeld = MobileInput.MoveHeld ||
                        Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f ||
                        Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f;

            // 硬锁定（重击/倒地/硬直等）才禁止移动；轻击连段可以边移动边出招。
            // 例外——收招闪避取消（大作手感）：技能/绝招打完主要段进入恢复相位后，
            // 按闪避可立刻打断收招，不必干等动作播完。
            if (_combat != null && _combat.IsHardLocked)
            {
                if (!(dodgePressed && _combat.CanCancelRecovery && Stats.stamina >= dodgeStaminaCost))
                {
                    // 出招转向影响（大作 attack steering / directional influence）：
                    // 技能、绝招、重击期间【仍可用摇杆缓慢调整朝向】——此前这里直接 return，
                    // 摇杆完全无响应、动作结束才突然弹到新朝向，正是"出招+转向不连贯"的主因。
                    // 受击/倒地/硬直不给转向（挨打就该失控）。
                    SteerDuringAction(stickDir, dt);
                    ApplyGravityOnly(dt);
                    return;
                }
                _combat.RequestState(CombatState.Locomotion);   // 解除收招锁，落入下方翻滚
            }
            bool attacking = _combat != null && _combat.Current == CombatState.LightAttack;

            // 蹲伏切换（潜行/低姿态）
            if (Input.GetKeyDown(KeyCode.C) || MobileInput.GetDown("Crouch")) ToggleCrouch();

            // 拔刀/收刀（带剑鞘武器，手动按钮触发）
            if (Input.GetKeyDown(KeyCode.T) || MobileInput.GetDown("Sheathe"))
            {
                if (_appearance == null) _appearance = GetComponent<PlayerAppearance>();
                if (_appearance != null) _appearance.ToggleWeaponDrawn();
            }

            float inputMag = stickInput.magnitude;            // 摇杆已在本帧开头统一读取
            Vector3 moveDir = stickDir;

            // 模拟量速度：摇杆半推=走路，全推=奔跑；桌面按住 Alt 慢走
            // 行动力过低时脚步沉重（拖延的具象体感）：35 以下开始线性减速，最低 ×0.65
            float apMult = Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(Stats.actionPower / 35f));
            float speed = runSpeed * MoveSpeedMultiplier * apMult * inputMag;
            if (!Application.isMobilePlatform && Input.GetKey(KeyCode.LeftAlt))
                speed = Mathf.Min(speed, walkSpeed * MoveSpeedMultiplier);
            if (IsCrouched) speed *= crouchSpeedMult;
            // 出招定步（平滑化）：攻击动画占据全身，照常位移会读作"脚不动人在滑"。
            // 但此前用【硬性 ×0.1】会造成速度震荡——推着摇杆连打时，每一段出招速度
            // 从全速骤降到 10%、收招again骤升回全速，配合已提速的加减速就是一顿一顿的
            // 抽搐感。改为对倍率本身做时间常数 ≈0.07s 的平滑过渡，并把下限抬到 0.3：
            // 既保留定步的分量感，又让"边推杆边连打"是一条连续的速度曲线。
            _attackSpeedFactor = Mathf.MoveTowards(_attackSpeedFactor,
                attacking ? 0.3f : 1f, dt / 0.07f);
            speed *= _attackSpeedFactor;

            // 土狼时间（coyote time）：离开地面边缘后仍有一小段时间可以起跳——
            // 玩家的意图是"我在跳台边缘按了跳"，而不是"我晚了两帧所以活该掉下去"
            if (_cc.isGrounded) { _vy = -1f; _coyoteT = CoyoteTime; }
            else { _vy += gravity * dt; _coyoteT -= dt; }

            // 跳跃：缓冲 + 土狼时间——落地前一瞬按下也能在落地帧兑现
            if (_coyoteT > 0f && _inputBuf.TryConsume("Jump", JumpBufferWindow))
            {
                if (IsCrouched) ToggleCrouch();
                _vy = jumpForce;
                _coyoteT = 0f;
                // 跳跃是连招元素，不是移动的附属品：起跳即入融合链，
                // 于是「跳→剑」「跳→重→剑」这些串法能被识别成招（踏空斩/踏空三叠）。
                if (_pcc != null) _pcc.Fusion.Push(Combat.MoveToken.Jump);
            }

            // 翻滚闪避（Shift / 闪）——走缓冲：硬直/翻滚中按下的闪避会在此兑现
            if (dodgePressed && Stats.SpendStamina(dodgeStaminaCost))
            {
                _inputBuf.Consume("Dodge");
                if (IsCrouched) ToggleCrouch();
                // 闪避取消：切断进行中的轻连段（清序列/收判定框），翻滚落地即可全新起手。
                // 注意——切断的是拳剑序列，**融合链不断**：闪避本身作为元素留在链上，
                // 所以「闪→剑」能接成闪身突刺，而不是把整段努力清零。
                var pcc = _pcc != null ? _pcc : GetComponent<Combat.PlayerCombatController>();
                if (pcc != null)
                {
                    pcc.CancelComboForDodge();
                    pcc.Fusion.Push(Combat.MoveToken.Dodge);
                }
                _dodgeDir = moveDir.sqrMagnitude > 0.01f ? moveDir : transform.forward;
                // 翻滚方向即刻转身
                transform.rotation = Quaternion.LookRotation(_dodgeDir);
                Core.GameAudio.Play(Core.GameAudio.Sfx.Dodge, 0.7f);
                // 有专用翻滚片段时：闪避时长匹配片段（完整呈现整个滚翻动作），
                // 总位移保持恒定（速度反比时长），无片段沿用默认参数
                float dur = dodgeDuration;
                _dodgeSpd = dodgeSpeed;
                if (_anim != null)
                {
                    float clipLen = _anim.ActionClipLength(PoseState.Dodge);
                    if (clipLen > 0.1f)
                    {
                        dur = Mathf.Clamp(clipLen * 0.85f, 0.42f, 0.7f);
                        _dodgeSpd = dodgeSpeed * dodgeDuration / dur;   // 位移总量不变
                    }
                }
                _dodgeTimer = dur;
                _iframeTimer = dodgeIFrames;
                if (_combat != null) _combat.RequestState(CombatState.Dodge);
                return;
            }

            // 加减速曲线：改用【指数逼近】而非线性匀加速——对齐 Unity 官方
            // ThirdPersonController 的做法（其注释原文：curved result rather than a
            // linear one giving a more organic speed change）。
            // 起步瞬间给足冲量（前几帧就到大半速度＝跟手），末段自然收敛（不突兀）。
            // 用 1-e^(-k·dt) 而非 Lerp(a,b,k·dt)：帧率无关，高低帧手感一致。
            Vector3 targetVel = moveDir * speed;
            float k = targetVel.sqrMagnitude > _hVel.sqrMagnitude ? accelRate : decelRate;
            _hVel = Vector3.Lerp(_hVel, targetVel, 1f - Mathf.Exp(-k * dt));
            CarryByPlatform();          // 站在会动的东西上（车顶等）要跟着它走
            _cc.Move(_hVel * dt + Vector3.up * _vy * dt);

            // 快速灵活转身：目标夹角越大转得越快
            if (moveDir.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(moveDir);
                if (attacking)
                {
                    // 轻击连段中的转向影响：用明确的角速度上限（度/秒）而非低倍率 Slerp——
                    // 旧值 2.2 的 Slerp 每帧只挪动 3.7%，推杆几乎转不动，读作"出招就僵住"。
                    // 150°/s 既能顺着摇杆把连招引向新方向，又不会一帧甩离当前目标。
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        target, AttackSteerDegPerSec * dt);
                }
                else
                {
                    float ang = Quaternion.Angle(transform.rotation, target);
                    float rs = rotateSpeed * (ang > 80f ? quickTurnMultiplier : 1f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, target, rs * dt);
                }
            }
        }

        // ---- 出招转向影响（大作 attack steering）速率表（度/秒）----
        const float AttackSteerDegPerSec = 150f;   // 轻击连段：顺杆引导连招方向
        const float SkillSteerDegPerSec = 110f;    // 技能/绝招：可调整但保持"已出招"的承诺感
        const float HeavySteerDegPerSec = 85f;     // 重击/蓄力：最重，转向最慢

        /// <summary>
        /// 硬锁动作期间的摇杆转向（技能/绝招/重击）：按状态给不同角速度上限，
        /// 让「出招 + 转向」是一个连贯动作，而不是无响应后突然弹向新朝向。
        /// 受击/倒地/心理硬直/死亡不给转向——挨打就该失控，这是打击反馈的一部分。
        /// </summary>
        void SteerDuringAction(Vector3 dir, float dt)
        {
            if (_combat == null || dir.sqrMagnitude < 0.01f) return;
            float rate;
            switch (_combat.Current)
            {
                case CombatState.Finisher:
                case CombatState.InnerPowerCast:
                    rate = SkillSteerDegPerSec; break;
                case CombatState.HeavyAttack:
                    rate = HeavySteerDegPerSec; break;
                default:
                    return;   // HitReaction / Knockdown / MentalStagger / Death：不可转向
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(dir), rate * dt);
        }

        void LateUpdate()
        {
            // 把实际位移换算成步态参数喂给人形动画
            if (_anim == null) _anim = GetComponent<HumanoidAnimator>();
            if (_anim == null) return;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 planar = transform.position - _lastPos;
            planar.y = 0;
            float actual = planar.magnitude / dt;
            float speed01 = Mathf.Clamp01(actual / Mathf.Max(0.1f, runSpeed));
            _anim.SetLocomotion(speed01, IsCrouched, _cc.isGrounded, actual);
            // 临战架势：只有敌人【逼近到近身范围(≈6m)】或正在交战时才摆格斗预备架势；
            // 敌人在远处/无敌人时用普通待机（不再一有敌人在场就一直端着架势）
            if (_lockOn == null) _lockOn = GetComponent<LockOnSystem>();
            bool enemyClose = _lockOn != null && _lockOn.CurrentTarget != null &&
                Vector3.Distance(transform.position, _lockOn.CurrentTarget.position) < 6f;
            bool ready = enemyClose || (_combat != null && _combat.InCombat);
            _anim.SetCombatReady(ready);
            // 拔刀/收刀改为手动按钮触发（见 PlayerAppearance.ToggleWeaponDrawn），此处不再自动驱动
            _lastPos = transform.position;
        }

        void ToggleCrouch()
        {
            IsCrouched = !IsCrouched;
            // 碰撞体随姿态变化，底部保持贴地
            if (IsCrouched)
            {
                _cc.height = 1.3f;
                _cc.center = new Vector3(0, -0.35f, 0);
            }
            else
            {
                _cc.height = 2f;
                _cc.center = Vector3.zero;
            }
        }

        /// <summary>
        /// 摇杆方向 → 世界方向（镜头相对）。**始终用镜头当前偏航**，
        /// 保证"推左＝画面左、推上＝画面深处"这条映射任何时候都成立。
        ///
        /// 曾试过把参考系锁存、只跟手动转镜，以此切断"镜头绕行↔角色转向"的代数环。
        /// 环确实断了，但代价是镜头绕到背后之后，摇杆与画面就对不上了
        /// （手指还按着左，画面里角色却在往前跑）——实测下来这个代价不可接受。
        ///
        /// 这三条性质数学上最多同时满足两条：
        ///   (a) 摇杆↔画面始终一致  (b) 镜头自动绕到背后  (c) 角色不画弧线
        /// 因为 moveDir = 镜头偏航 + 摇杆角，镜头一转 moveDir 就跟着转。
        /// 主流第三人称动作游戏一律取 (a)+(b)、放弃 (c)：持续推一个非正前方向时，
        /// 角色走一条弧线。缺陷从来不是"会画弧"，而是弧的松紧——
        /// 本作曾达 207~322°/s（半径仅 0.9~1.4m，读作原地转圈）；
        /// 现由镜头侧把持续绕行速率压到 30°/s（半径 9.9m），即大作那种缓弧。
        /// 见 ThirdPersonCamera 的 SustainedOrbitCap。
        /// </summary>
        Vector3 CameraRelative(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) { _recenterFrameInit = false; return Vector3.zero; }
            if (cameraTransform == null) return new Vector3(input.x, 0, input.y).normalized;

            // ===== 移动参考系永远是【当前镜头】，唯一例外是一键回正期间 =====
            //
            // 曾经试过"镜头自主掉头时钉住参考系"，让角色在镜头绕行期间走直线。
            // 代数上它确实成立，但实机截图给出了否决性的证据：
            // **摇杆推在左下、画面里角色却在往画面深处跑**——偏差最大可达 180°。
            // 上游此前也因同一现象撤回过（cd4bc4a）。两次独立验证，结论一致：
            // 「摇杆方向 ↔ 画面方向」的一致性是不可交易的，它比"轨迹是直线"更基本，
            // 因为玩家是照着画面推杆的，一旦对不上，每一次输入都在赌。
            //
            // 于是恒等式 H = C + θ 全时成立，而它的推论是：
            // **镜头在玩家按着摇杆时每转 1°，角色就被带转 1°。**
            // 所以镜头侧的答案只能是——按着摇杆时压根不自动转（见 ThirdPersonCamera
            // 的 _viewBlocked：开阔时旋转授权为 0）。这也正是《原神》《塞尔达》
            // 《魂系》等虚拟摇杆/手柄第三人称作品的通行做法：**镜头偏航归玩家**，
            // 自动运镜只用于锁定、显式回正与战斗兜底。
            //
            // 一键回正是唯一例外：那是玩家自己按的、有始有终的 0.6~0.9s 动作，
            // 期间冻结参考系可避免镜头把角色一起拖转，动作一结束立即恢复。
            if (_cam == null) _cam = cameraTransform.GetComponent<ThirdPersonCamera>();
            float frameYaw;
            if (_cam != null && _cam.RecenterActive)
            {
                if (!_recenterFrameInit)
                {
                    _recenterFrameYaw = cameraTransform.eulerAngles.y;
                    _recenterFrameInit = true;
                }
                frameYaw = _recenterFrameYaw;
            }
            else
            {
                _recenterFrameInit = false;
                // **实时映射，无任何偏置**：摇杆方向恒等于画面方向，一帧都不错开。
                // 代价由镜头侧承担——它只能以很慢的速率绕行（见 SustainedOrbitCap），
                // 慢到玩家的补杆是无意识的，于是角色路径几乎看不出弯。
                frameYaw = cameraTransform.eulerAngles.y;
            }

            Quaternion frame = Quaternion.Euler(0, frameYaw, 0);
            Vector3 fwd = frame * Vector3.forward;
            Vector3 right = frame * Vector3.right;
            return (fwd * input.y + right * input.x).normalized;
        }

        ThirdPersonCamera _cam;
        float _recenterFrameYaw;
        bool _recenterFrameInit;

        void ApplyGravityOnly(float dt)
        {
            // 硬锁状态(重击蓄力/聚气、施法、倒地等)：只落重力、水平零位移，且清空残余
            // 水平速度——聚气时玩家原地扎稳，不带着惯性前滑（"漂移"），锁定解除也不会
            // 突然窜出一段。
            _hVel = Vector3.zero;
            _vy = _cc.isGrounded ? -1f : _vy + gravity * dt;
            _cc.Move(Vector3.up * _vy * dt);
        }
    }
}
