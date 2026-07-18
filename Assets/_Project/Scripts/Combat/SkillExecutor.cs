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

            // 数字键 1-6 释放已装备技能
            for (int i = 0; i < Mathf.Min(6, equippedSkills.Count); i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) TryCast(equippedSkills[i]);

            // 触屏技能按钮：定（定心护体）/ 气（斩念气刃）/ 还（责任归还）/
            // 火（五分钟火种）/ 盾（不读心盾）/ 收（注意力回收）
            for (int i = 0; i < Mathf.Min(6, equippedSkills.Count); i++)
                if (MobileInput.GetDown("Skill" + (i + 1))) TryCast(equippedSkills[i]);
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

            // 冷却：成长节点/套装缩减 × 关系消耗过高时被拉长（被掏空的注意力与精力）
            float cdTime = skill.cooldown * Core.GrowthSystem.CooldownMult(skill);
            if (_player.Stats.IsOverDrained) cdTime *= 1.5f;
            _cooldowns[skill.skillId] = cdTime;
            _fsm.RequestState(CombatState.Finisher, skill.castLockTime);

            if (skill.isResponsibilityReturn)
            {
                DoResponsibilityReturn();
                return true;
            }
            if (skill.isFiveMinuteSpark)
            {
                DoFiveMinuteSpark(skill);
                return true;
            }
            if (skill.isMindShield)
            {
                var buff = GetComponent<MindShieldBuff>();
                if (buff == null) buff = gameObject.AddComponent<MindShieldBuff>();
                buff.Arm(10f);
                CombatFeedback.RecipeBurst(transform.position, new Color(0.5f, 0.75f, 1f));
                Core.GameEvents.RaiseSkillBanner("「不读心盾」");
                Core.GameEvents.RaiseSubtitle("不读心盾——无法确认的事，我不把猜测当事实（抵消下一次心理攻击）。");
                return true;
            }
            if (skill.isAttentionRecall)
            {
                DoAttentionRecall();
                return true;
            }

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
                    // 技能级近战判定：范围大于普通连段（技能越高范围越大的总原则）
                    weaponHitbox.SetShape(new Vector3(2.6f, 1.8f, 2.6f), new Vector3(0, 0.1f, 1.0f));
                    weaponHitbox.EnableHitbox(dmg);
                    Invoke(nameof(CloseHitbox), skill.hitboxOpenTime);
                }
            }
            return true;
        }

        void CloseHitbox() { if (weaponHitbox != null) weaponHitbox.DisableHitbox(); }

        /// <summary>
        /// 五分钟火种：不等状态完美，先动五分钟——恢复行动力、清除减速与身份冻结、意势+1。
        /// 拖延沼泽与旧我 Boss 冻结阶段的核心解法。
        /// </summary>
        void DoFiveMinuteSpark(Data.SkillDefinition skill)
        {
            _player.Stats.RestoreAxis(Personalization.WeaknessAxis.Procrastination, 45f);
            _player.Stats.ReduceRumination(8f);
            _player.MoveSpeedMultiplier = 1f;

            var frozen = GetComponent<FrozenDebuff>();
            bool unfroze = frozen != null;
            if (frozen != null) Destroy(frozen);

            var combat = GetComponent<PlayerCombatController>();
            if (combat != null) combat.AddMomentum(1);

            CombatFeedback.RecipeBurst(transform.position, new Color(1f, 0.6f, 0.2f));
            GameAudio.Play(GameAudio.Sfx.Parry, 0.7f);
            Core.GameEvents.RaiseSkillBanner("「五分钟火种」");
            Core.GameEvents.RaiseSubtitle(unfroze
                ? "五分钟火种——行动打破冻结！先做五分钟，动起来再说。"
                : "五分钟火种——不等动力，先开始；动力是被行动召回的。");
        }

        /// <summary>
        /// 注意力回收：清除全部幻影假目标、恢复专注、降低反刍——把注意力从猜测里拿回来。
        /// 刺激放大器 Boss 战的核心解法。
        /// </summary>
        void DoAttentionRecall()
        {
            int cleared = PhantomDecoy.ClearAll();
            _player.Stats.RestoreAxis(Personalization.WeaknessAxis.NoiseSensitivity, 32f);
            _player.Stats.ReduceRumination(15f);
            CombatFeedback.RecipeBurst(transform.position, new Color(0.3f, 0.85f, 0.95f));
            Core.GameEvents.RaiseSkillBanner("「注意力回收」");
            Core.GameEvents.RaiseSubtitle(cleared > 0
                ? "注意力回收——" + cleared + " 个幻影散去。不是所有声音都要回应。"
                : "注意力回收——我把注意力拿回来，放回自己手上的事。");
        }

        /// <summary>
        /// 责任归还：清除「过度负责」减速，把仍在飞来的虚假责任球全部打回法官（每个削韧），
        /// 并回补边界。象征"把不属于我的部分，准确地还回去"。
        /// </summary>
        void DoResponsibilityReturn()
        {
            var debuff = GetComponent<OverResponsibilityDebuff>();
            if (debuff != null) Destroy(debuff);

            int returned = 0;
            foreach (var ball in FindObjectsOfType<ResponsibilityBall>())
                if (ball.isFalse) { ball.ForceReturn(); returned++; }

            // 好人牢笼：责任归还把好人卡之墙整圈打破
            int walls = CageWall.BreakAll();

            _player.Stats.RestoreAxis(Personalization.WeaknessAxis.BoundaryConflict, 18f);
            _player.Stats.ReduceRumination(12f);
            _player.Stats.ReduceRelationshipDrain(10f);
            CombatFeedback.RecipeBurst(transform.position, new Color(0.4f, 0.85f, 0.6f));
            Core.GameEvents.RaiseSkillBanner("「责任归还」");
            Core.GameEvents.RaiseSubtitle(walls > 0
                ? "责任归还——好人牢笼被打破！我不是谁的替身人生。"
                : returned > 0
                    ? "责任归还——把不属于我的" + returned + "份责任，准确地还了回去。"
                    : "责任归还——我只承担属于自己的那部分。");
        }
    }
}
