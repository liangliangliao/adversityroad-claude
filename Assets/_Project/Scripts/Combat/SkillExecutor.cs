using UnityEngine;
using System.Collections.Generic;
using AdversityRoad.Mobile;
using AdversityRoad.Player;
using AdversityRoad.Core;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 技能释放器：读取 SkillDefinition，处理消耗、冷却、伤害/恢复效果。
    /// 原创技能命名（影步连踢/逆伤崩拳/破念指/流云步/定心护体/意志燃烧等）。
    /// </summary>
    public class SkillExecutor : MonoBehaviour
    {
        public List<Data.SkillDefinition> equippedSkills = new List<Data.SkillDefinition>();
        public Hitbox weaponHitbox;

        PlayerController _player;
        CombatStateMachine _fsm;
        readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _fsm = GetComponent<CombatStateMachine>();
        }

        void Update()
        {
            var keys = new List<string>(_cooldowns.Keys);
            foreach (var k in keys) _cooldowns[k] = Mathf.Max(0, _cooldowns[k] - Time.deltaTime);

            // 数字键 1-4 释放已装备技能
            for (int i = 0; i < Mathf.Min(4, equippedSkills.Count); i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) TryCast(equippedSkills[i]);

            // 触屏技能按钮：定（定心护体）/ 气（斩念气刃）
            if (MobileInput.GetDown("Skill1") && equippedSkills.Count > 0) TryCast(equippedSkills[0]);
            if (MobileInput.GetDown("Skill2") && equippedSkills.Count > 1) TryCast(equippedSkills[1]);
        }

        public bool TryCast(Data.SkillDefinition skill)
        {
            if (skill == null || _fsm.IsActionLocked) return false;
            if (_cooldowns.TryGetValue(skill.skillId, out float cd) && cd > 0)
            {
                Core.GameEvents.RaiseSubtitle("「" + skill.displayName + "」调息中……");
                return false;
            }
            // 能量门槛：大招需要消耗意势（能量积累才能释放）
            if (skill.momentumCost > 0)
            {
                var combat = GetComponent<PlayerCombatController>();
                if (combat == null || !combat.TrySpendMomentum(skill.momentumCost))
                {
                    Core.GameEvents.RaiseSubtitle("意势不足：「" + skill.displayName +
                        "」需要 " + skill.momentumCost + " 点意势（命中/完美闪避/蓄力积攒）");
                    return false;
                }
            }
            if (!_player.Stats.SpendStamina(skill.staminaCost)) return false;
            if (skill.willCost > 0 && !_player.Stats.SpendWill(skill.willCost)) return false;
            if (skill.momentumCost > 0) Core.GameEvents.RaiseSkillBanner("「" + skill.displayName + "」");

            // 逆伤崩拳气质：高伤害但额外消耗自尊/意志的技能由 selfCostAxisDamage 表达
            if (skill.selfCostAxisDamage > 0)
                _player.Stats.TakeMentalDamage(skill.selfCostAxis, skill.selfCostAxisDamage);

            _cooldowns[skill.skillId] = skill.cooldown;
            _fsm.RequestState(CombatState.Finisher, skill.castLockTime);

            if (skill.mentalRestore > 0)
            {
                _player.Stats.RestoreMental(skill.mentalRestore);
                Core.GameEvents.RaiseSubtitle("【" + skill.displayName + "】心神安定，心理属性恢复。");
            }

            if (skill.physicalDamage > 0)
            {
                var dmg = new DamageInfo
                {
                    physicalDamage = skill.physicalDamage,
                    postureDamage = skill.postureDamage,
                    knockback = skill.knockback,
                    attackerId = "player_skill_" + skill.skillId
                };

                if (skill.isRanged)
                {
                    // 远程：朝最近敌人（无则朝正前方）发射剑气
                    var combat = GetComponent<PlayerCombatController>();
                    Transform aim = combat != null ? combat.AutoAimTarget() : null;
                    if (aim != null)
                    {
                        Vector3 face = aim.position - transform.position;
                        face.y = 0;
                        if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face);
                    }
                    Vector3 origin = transform.position + Vector3.up * 1.2f + transform.forward * 0.8f;
                    Vector3 dir = aim != null
                        ? (aim.position + Vector3.up * 1.0f - origin)
                        : transform.forward;
                    Projectile.Launch(transform, origin, dir, dmg, skill.projectileSpeed,
                        new Color(0.5f, 0.85f, 1f), null, skill.projectileScale);
                    if (skill.momentumCost > 0)
                        CombatFeedback.RecipeBurst(transform.position, new Color(0.5f, 0.85f, 1f));
                    else CombatFeedback.Shake(0.3f);
                }
                else if (weaponHitbox != null)
                {
                    CombatFeedback.SwingArc(transform, true, new Color(1f, 0.7f, 0.3f));
                    weaponHitbox.EnableHitbox(dmg);
                    Invoke(nameof(CloseHitbox), skill.hitboxOpenTime);
                }
            }
            return true;
        }

        void CloseHitbox() { if (weaponHitbox != null) weaponHitbox.DisableHitbox(); }
    }
}
