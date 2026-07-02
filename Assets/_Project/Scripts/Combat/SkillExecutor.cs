using UnityEngine;
using System.Collections.Generic;
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
        }

        public bool TryCast(Data.SkillDefinition skill)
        {
            if (skill == null || _fsm.IsActionLocked) return false;
            if (_cooldowns.TryGetValue(skill.skillId, out float cd) && cd > 0) return false;
            if (!_player.Stats.SpendStamina(skill.staminaCost)) return false;
            if (skill.willCost > 0 && !_player.Stats.SpendWill(skill.willCost)) return false;

            // 逆伤崩拳气质：高伤害但额外消耗自尊/意志的技能由 selfCostAxisDamage 表达
            if (skill.selfCostAxisDamage > 0)
                _player.Stats.TakeMentalDamage(skill.selfCostAxis, skill.selfCostAxisDamage);

            _cooldowns[skill.skillId] = skill.cooldown;
            _fsm.RequestState(CombatState.Finisher, skill.castLockTime);

            if (skill.mentalRestore > 0) _player.Stats.RestoreMental(skill.mentalRestore);

            if (skill.physicalDamage > 0 && weaponHitbox != null)
            {
                weaponHitbox.EnableHitbox(new DamageInfo
                {
                    physicalDamage = skill.physicalDamage,
                    postureDamage = skill.postureDamage,
                    knockback = skill.knockback,
                    attackerId = "player_skill_" + skill.skillId
                });
                Invoke(nameof(CloseHitbox), skill.hitboxOpenTime);
            }
            return true;
        }

        void CloseHitbox() { if (weaponHitbox != null) weaponHitbox.DisableHitbox(); }
    }
}
