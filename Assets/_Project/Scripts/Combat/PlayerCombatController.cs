using UnityEngine;
using AdversityRoad.Player;
using AdversityRoad.Core;
using AdversityRoad.Mobile;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 玩家战斗控制：轻/重攻击三段连招（自动瞄准+突进+剑气）、格挡（边界盾）、
    /// 精准格挡（定心格挡）、内功（意志燃烧）、受击、重击倒地、心理受击。
    /// </summary>
    [RequireComponent(typeof(CombatStateMachine))]
    public class PlayerCombatController : MonoBehaviour
    {
        [Header("攻击")]
        public Hitbox weaponHitbox;
        public float lightDamage = 12f;
        public float heavyDamage = 28f;
        public float lightStamina = 10f;
        public float heavyStamina = 25f;
        public float comboWindow = 0.6f;
        public float autoAimRange = 5f;      // 出手自动转向最近敌人
        public float lungeDistance = 0.7f;   // 攻击小突进

        [Header("格挡 / 精准格挡")]
        public float guardMentalReduction = 0.7f;
        public float parryWindow = 0.2f;
        public float parryFocusRestore = 25f;

        [Header("内功：意志燃烧")]
        public float innerPowerWillCost = 40f;
        public float innerPowerDuration = 8f;
        public float innerPowerDamageBoost = 1.5f;

        [Header("倒地")]
        public float knockdownThreshold = 20f;  // 单次物理伤害超过该值触发倒地

        [Header("状态可视化（运行时注入）")]
        public GameObject guardShield;
        public GameObject innerAura;

        PlayerController _player;
        CombatStateMachine _fsm;
        CharacterController _cc;
        int _comboStep;
        float _comboTimer, _parryTimer, _innerPowerTimer;

        public bool IsGuarding { get; private set; }
        public bool InnerPowerActive => _innerPowerTimer > 0;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _fsm = GetComponent<CombatStateMachine>();
            _cc = GetComponent<CharacterController>();
        }

        void Update()
        {
            if (_player.Stats.IsDead) return;
            float dt = Time.deltaTime;
            if (_comboTimer > 0) _comboTimer -= dt; else _comboStep = 0;
            if (_parryTimer > 0) _parryTimer -= dt;
            if (_innerPowerTimer > 0) _innerPowerTimer -= dt;

            IsGuarding = Input.GetKey(KeyCode.LeftControl) || MobileInput.GetHeld("Guard");
            if (Input.GetKeyDown(KeyCode.LeftControl) || MobileInput.GetDown("Guard")) _parryTimer = parryWindow;

            if (guardShield != null && guardShield.activeSelf != (IsGuarding && !_fsm.IsActionLocked))
                guardShield.SetActive(IsGuarding && !_fsm.IsActionLocked);
            if (innerAura != null && innerAura.activeSelf != InnerPowerActive)
                innerAura.SetActive(InnerPowerActive);

            if (_fsm.IsActionLocked) return;

            bool mouseOverUI = UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            // 真机上第一根手指会被旧输入系统映射成鼠标键，只走触屏按钮通道
            bool desktop = !Application.isMobilePlatform;
            bool lightIn = (desktop && Input.GetMouseButtonDown(0) && !mouseOverUI) || MobileInput.GetDown("Light");
            bool heavyIn = (desktop && Input.GetMouseButtonDown(1)) || MobileInput.GetDown("Heavy");
            bool innerIn = Input.GetKeyDown(KeyCode.R) || MobileInput.GetDown("Inner");
            if (lightIn) TryAttack(false);
            else if (heavyIn) TryAttack(true);
            else if (innerIn) TryInnerPower();
        }

        void TryAttack(bool heavy)
        {
            float cost = heavy ? heavyStamina : lightStamina;
            if (!_player.Stats.SpendStamina(cost)) return;

            _comboStep = (_comboStep % 3) + 1;
            _comboTimer = comboWindow;
            _fsm.RequestState(heavy ? CombatState.HeavyAttack : CombatState.LightAttack,
                              heavy ? 0.8f : 0.45f);
            _fsm.InCombat = true;

            // 自动瞄准：朝最近敌人转身，触屏无锁定时也能打中
            var target = AutoAimTarget();
            if (target != null)
            {
                Vector3 dir = target.position - transform.position;
                dir.y = 0;
                if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir);
            }

            // 小突进 + 剑气
            float lunge = heavy ? lungeDistance * 1.4f : lungeDistance;
            if (target == null || Vector3.Distance(transform.position, target.position) > 1.2f)
                _cc.Move(transform.forward * lunge);
            CombatFeedback.SwingArc(transform, heavy, new Color(0.45f, 0.75f, 1f));

            float dmg = (heavy ? heavyDamage : lightDamage) * (1f + 0.15f * (_comboStep - 1));
            if (InnerPowerActive) dmg *= innerPowerDamageBoost;

            if (weaponHitbox != null)
            {
                // 无动画资产时直接开框 0.3 秒；接入动画后改由 Animation Event 调用
                weaponHitbox.EnableHitbox(new DamageInfo
                {
                    physicalDamage = dmg,
                    postureDamage = heavy ? 20f : 8f,
                    knockback = heavy ? 3f : 1f,
                    attackerId = "player"
                });
                Invoke(nameof(CloseHitbox), 0.3f);
            }
        }

        /// <summary>最近的存活敌人（供普攻转向与远程技能瞄准）。</summary>
        public Transform AutoAimTarget()
        {
            var enemies = FindObjectsOfType<AI.EnemyController>();
            Transform best = null;
            float bestDist = Mathf.Max(autoAimRange, 14f);
            foreach (var e in enemies)
            {
                if (e.State == AI.EnemyState.Dead) continue;
                float d = Vector3.Distance(transform.position, e.transform.position);
                if (d < bestDist) { bestDist = d; best = e.transform; }
            }
            return best;
        }

        void CloseHitbox() { if (weaponHitbox != null) weaponHitbox.DisableHitbox(); }

        void TryInnerPower()
        {
            if (!_player.Stats.SpendWill(innerPowerWillCost)) return;
            _innerPowerTimer = innerPowerDuration;
            _fsm.RequestState(CombatState.InnerPowerCast, 0.8f);
            CombatFeedback.Shake(0.4f);
        }

        public void TakeHit(DamageInfo dmg)
        {
            if (_player.IsInvincible) return;

            // 心理伤害
            if (dmg.mentalDamage > 0)
            {
                float mult = GameManager.Instance != null && GameManager.Instance.safety != null
                    ? GameManager.Instance.safety.MentalDamageMultiplier() : 1f;
                float mental = dmg.mentalDamage * mult;

                if (_parryTimer > 0)
                {
                    // 定心格挡成功：不掉值，反而恢复专注
                    _player.Stats.focus = Mathf.Min(_player.Stats.maxFocus,
                        _player.Stats.focus + parryFocusRestore);
                    GameEvents.RaiseMentalStatChanged("focus", _player.Stats.focus, _player.Stats.maxFocus);
                    GameEvents.RaiseSubtitle("定心格挡！心理攻击被化解，专注恢复。");
                }
                else
                {
                    if (IsGuarding) mental *= (1f - guardMentalReduction);
                    bool staggered = _player.Stats.TakeMentalDamage(dmg.mentalAxis, mental);
                    if (staggered) _fsm.TriggerMentalStagger();
                }
            }

            // 物理伤害
            if (dmg.physicalDamage > 0)
            {
                float phys = dmg.physicalDamage;
                if (IsGuarding && _player.Stats.SpendStamina(phys * 0.5f)) phys *= 0.2f;
                _player.Stats.TakePhysicalDamage(phys);

                CombatFeedback.HitFlash(gameObject);
                CombatFeedback.DamageNumber(transform.position, Mathf.RoundToInt(phys).ToString(),
                    new Color(1f, 0.35f, 0.3f));
                CombatFeedback.Shake(phys >= knockdownThreshold ? 1.2f : 0.5f);

                if (_player.Stats.IsDead)
                {
                    _fsm.RequestState(CombatState.Death);
                }
                else if (!dmg.isMentalOnly)
                {
                    // 重击倒地：短暂倒地 + 起身无敌帧
                    if (phys >= knockdownThreshold)
                    {
                        _fsm.RequestState(CombatState.Knockdown, 1.4f);
                        _player.SetInvincible(1.8f);
                        CombatFeedback.HitStop(0.06f);
                    }
                    else
                    {
                        _fsm.RequestState(CombatState.HitReaction, 0.4f);
                    }
                }
            }
        }
    }
}
