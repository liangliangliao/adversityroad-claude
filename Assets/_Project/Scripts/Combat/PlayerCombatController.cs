using UnityEngine;
using AdversityRoad.Player;
using AdversityRoad.Core;
using AdversityRoad.Mobile;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 玩家战斗控制：轻/重攻击三段连招、格挡（边界盾）、精准格挡（定心格挡）、
    /// 内功（意志燃烧）、受击、心理受击。
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

        [Header("格挡 / 精准格挡")]
        public float guardMentalReduction = 0.7f;   // 边界盾：心理伤害减免
        public float parryWindow = 0.2f;            // 定心格挡判定窗口
        public float parryFocusRestore = 25f;       // 精准格挡恢复专注

        [Header("内功：意志燃烧")]
        public float innerPowerWillCost = 40f;
        public float innerPowerDuration = 8f;
        public float innerPowerDamageBoost = 1.5f;

        PlayerController _player;
        CombatStateMachine _fsm;
        int _comboStep;
        float _comboTimer, _parryTimer, _innerPowerTimer;

        public bool IsGuarding { get; private set; }
        public bool InnerPowerActive => _innerPowerTimer > 0;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _fsm = GetComponent<CombatStateMachine>();
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

            if (_fsm.IsActionLocked) return;

            bool mouseOverUI = UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            bool lightIn = (Input.GetMouseButtonDown(0) && !mouseOverUI) || MobileInput.GetDown("Light");
            bool heavyIn = Input.GetMouseButtonDown(1) || MobileInput.GetDown("Heavy");
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

        void CloseHitbox() { if (weaponHitbox != null) weaponHitbox.DisableHitbox(); }

        void TryInnerPower()
        {
            if (!_player.Stats.SpendWill(innerPowerWillCost)) return;
            _innerPowerTimer = innerPowerDuration;
            _fsm.RequestState(CombatState.InnerPowerCast, 0.8f);
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
                if (!dmg.isMentalOnly && !_player.Stats.IsDead)
                    _fsm.RequestState(CombatState.HitReaction, 0.4f);
                if (_player.Stats.IsDead) _fsm.RequestState(CombatState.Death);
            }
        }
    }
}
