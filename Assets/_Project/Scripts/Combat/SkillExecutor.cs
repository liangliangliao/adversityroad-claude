using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using AdversityRoad.Mobile;
using AdversityRoad.Player;
using AdversityRoad.Core;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 技能释放器：读取 SkillDefinition，处理消耗、冷却、伤害/恢复效果。
    ///
    /// 五大主题技能不再是"按一下出一个效果"，而是各自一整套连招演出
    /// （参考大型动作游戏的技能编排：多段动作 + 位移 + 多段判定 + 刀光/冲击环/时缓）：
    /// · 定「定心·四象归一」：收势凝神 → 三重内收气环（每环削韧+推离）→ 旋身归一爆发+心神恢复；
    /// · 收「收心·万流归元」：后旋踢起手 → 双旋清场（幻影全灭）→ 万流归元冲击波+专注回收；
    /// · 还「还域·界返三连」：撩斩挑飞 → 旋身反震（责任球全数打回/好人墙全破）→ 界域震地波+边界回补；
    /// · 火「燃火·三段突进斩」：点火解冻 → 火色双突进斩 → 上撩火浪终结+行动力点燃；
    /// · 盾「镜界·退身斩」：镜环展开护心 → 后空翻拉开身位 → 掷出镜界气刃（显形幻影）。
    /// 连招中被击倒会打断（与蓄力二连击同规则）；心理机制效果全部保留。
    /// </summary>
    public class SkillExecutor : MonoBehaviour
    {
        public List<Data.SkillDefinition> equippedSkills = new List<Data.SkillDefinition>();
        public Hitbox weaponHitbox;

        PlayerController _player;
        CombatStateMachine _fsm;
        CharacterController _cc;
        HumanoidAnimator _anim;
        PlayerCombatController _combat;
        Coroutine _comboRoutine;
        Coroutine _glideRoutine;
        readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _fsm = GetComponent<CombatStateMachine>();
            _cc = GetComponent<CharacterController>();
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
                var combat = Combat();
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
            _fsm.InCombat = true;

            // ---- 五大主题技能：整套连招演出 ----
            if (skill.isResponsibilityReturn) { StartCombo(ResponsibilityReturnCombo()); return true; }
            if (skill.isFiveMinuteSpark) { StartCombo(FiveMinuteSparkCombo()); return true; }
            if (skill.isMindShield) { StartCombo(MindShieldCombo()); return true; }
            if (skill.isAttentionRecall) { StartCombo(AttentionRecallCombo()); return true; }
            if (skill.isSteadyHeartGuard) { StartCombo(SteadyHeartCombo(skill.mentalRestore)); return true; }

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
                    var combat = Combat();
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

        // ===================== 连招编排基础件 =====================

        PlayerCombatController Combat()
        {
            if (_combat == null) _combat = GetComponent<PlayerCombatController>();
            return _combat;
        }

        void Pose(PoseState p)
        {
            if (_anim == null) _anim = GetComponent<HumanoidAnimator>();
            if (_anim != null) _anim.PlayAttackPose(p);
        }

        /// <summary>面向最近敌人（连招起手先咬住目标，无目标保持当前朝向）。</summary>
        void FaceTarget()
        {
            var combat = Combat();
            Transform aim = combat != null ? combat.AutoAimTarget() : null;
            if (aim == null) return;
            Vector3 dir = aim.position - transform.position; dir.y = 0;
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        /// <summary>短促滑行位移（同 PlayerCombatController.GlideMove：不瞬移防镜头抖）。</summary>
        void Glide(Vector3 offset, float duration)
        {
            if (_glideRoutine != null) StopCoroutine(_glideRoutine);
            _glideRoutine = StartCoroutine(GlideRoutine(offset, duration));
        }

        IEnumerator GlideRoutine(Vector3 offset, float duration)
        {
            float t = 0;
            while (t < duration)
            {
                float dt = Time.deltaTime;
                t += dt;
                if (_cc != null) _cc.Move(offset * Mathf.Min(dt / duration, 1f));
                yield return null;
            }
        }

        /// <summary>一段判定：按招式姿态定形开判定框（形状表与普攻共用）。</summary>
        void Strike(PoseState pose, float dmg, float posture, float knockback,
            float windup, float open, float scale = 1f, string tag = "player_skill")
        {
            if (weaponHitbox == null) return;
            PlayerCombatController.PoseHitShape(pose, out Vector3 size, out Vector3 center);
            if (!Mathf.Approximately(scale, 1f)) { size *= scale; center.z *= scale; }
            weaponHitbox.SetShape(size, center);
            StartCoroutine(StrikeWindow(windup, open, dmg, posture, knockback, tag));
        }

        IEnumerator StrikeWindow(float windup, float open, float dmg, float posture,
            float knockback, string tag)
        {
            yield return new WaitForSeconds(windup);
            weaponHitbox.onHit = h =>
            {
                CombatFeedback.HitStop(0.05f);
                Core.GameAudio.Play(Core.GameAudio.Sfx.Hit, 0.8f);
            };
            weaponHitbox.EnableHitbox(new DamageInfo
            {
                physicalDamage = dmg, postureDamage = posture,
                knockback = knockback, attackerId = tag
            });
            yield return new WaitForSeconds(open);
            weaponHitbox.DisableHitbox();
            weaponHitbox.onHit = null;
        }

        void StartCombo(IEnumerator combo)
        {
            if (_comboRoutine != null) StopCoroutine(_comboRoutine);
            _comboRoutine = StartCoroutine(combo);
        }

        /// <summary>连招阶段间隔：被击倒/硬直打断时返回 false，整套连招终止。</summary>
        bool ComboAlive() => _fsm.Current == CombatState.Finisher;

        // ===================== 定「定心·四象归一」 =====================

        /// <summary>收势凝神 → 三重内收气环（每环削韧+推离周围敌人）→ 旋身归一爆发，
        /// 心神大幅恢复。护体不是站桩：把周围搅扰整圈荡开。</summary>
        IEnumerator SteadyHeartCombo(float mentalRestore)
        {
            _fsm.RequestState(CombatState.Finisher, 1.5f);
            Core.GameEvents.RaiseSkillBanner("「定心·四象归一」");
            Pose(PoseState.Charge);
            CombatFeedback.ChargeGale(transform.position, 0.6f);
            yield return new WaitForSeconds(0.3f);

            // 三重内收气环：由外向内收束（外圈大→内圈小），每环削韧并推离敌人
            var ringColor = new Color(0.45f, 0.65f, 1f);
            for (int i = 0; i < 3 && ComboAlive(); i++)
            {
                CombatFeedback.ShockRing(transform.position, ringColor, 5.5f - i * 1.4f);
                CombatFeedback.HitSpark(transform.position + Vector3.up * 1.1f, ringColor, 4);
                Strike(PoseState.AttackSpin, 4f, 14f, 2.5f, 0.02f, 0.14f, 1.2f, "player_skill_dingxin");
                foreach (var e in FindObjectsOfType<AI.EnemyController>())
                    e.Repel(transform.position, 4.5f, 5f, 0.14f);
                Core.GameAudio.Play(Core.GameAudio.Sfx.Cast, 0.5f);
                yield return new WaitForSeconds(0.26f);
            }
            if (!ComboAlive()) yield break;

            // 归一：旋身收势爆发 + 心神恢复 + 短时缓
            Pose(PoseState.AttackSpin);
            CombatFeedback.EnergyBurst(transform.position + Vector3.up * 0.8f, ringColor, 1.1f);
            CombatFeedback.SlowMo(0.5f, 0.15f);
            _player.Stats.RestoreMental(mentalRestore);
            Core.GameEvents.RaiseSubtitle("四象归一——心神落定，心理属性恢复。");
            Core.GameAudio.Play(Core.GameAudio.Sfx.Parry, 0.8f);
        }

        // ===================== 收「收心·万流归元」 =====================

        /// <summary>后旋踢起手 → 双旋清场（幻影全灭、每旋一段判定）→
        /// 万流归元冲击波，专注回收、反刍下降。</summary>
        IEnumerator AttentionRecallCombo()
        {
            _fsm.RequestState(CombatState.Finisher, 1.3f);
            Core.GameEvents.RaiseSkillBanner("「收心·万流归元」");
            FaceTarget();
            var cyan = new Color(0.3f, 0.85f, 0.95f);

            // 双旋清场：两记后旋踢各带环身判定与刀光
            for (int i = 0; i < 2 && ComboAlive(); i++)
            {
                Pose(PoseState.SpinKick);
                CombatFeedback.SwingArc(transform, i == 1, cyan);
                Strike(PoseState.SpinKick, 8f, 16f, 3f, 0.08f, 0.18f, 1.25f, "player_skill_huishou");
                yield return new WaitForSeconds(0.34f);
            }
            if (!ComboAlive()) yield break;

            // 万流归元：幻影全灭 + 冲击波 + 专注回收
            int cleared = PhantomDecoy.ClearAll();
            Pose(PoseState.AttackSpin);
            CombatFeedback.ShockRing(transform.position, cyan, 6.5f);
            CombatFeedback.EnergyBurst(transform.position + Vector3.up * 0.9f, cyan, 0.95f);
            _player.Stats.RestoreAxis(Personalization.WeaknessAxis.NoiseSensitivity, 32f);
            _player.Stats.ReduceRumination(15f);
            Core.GameEvents.RaiseSubtitle(cleared > 0
                ? "万流归元——" + cleared + " 个幻影散去。不是所有声音都要回应。"
                : "万流归元——我把注意力拿回来，放回自己手上的事。");
            Core.GameAudio.Play(Core.GameAudio.Sfx.Parry, 0.7f);
        }

        // ===================== 还「还域·界返三连」 =====================

        /// <summary>撩斩挑飞 → 旋身反震（虚假责任球全数打回、好人墙整圈震破）→
        /// 界域震地波+边界回补。把不属于自己的，成套还回去。</summary>
        IEnumerator ResponsibilityReturnCombo()
        {
            _fsm.RequestState(CombatState.Finisher, 1.4f);
            Core.GameEvents.RaiseSkillBanner("「还域·界返三连」");
            FaceTarget();
            var green = new Color(0.4f, 0.85f, 0.6f);

            // 段1：撩斩挑飞（纵向高弧判定）
            Pose(PoseState.AttackUp);
            CombatFeedback.SwingArc(transform, true, green);
            Strike(PoseState.AttackUp, 10f, 20f, 4f, 0.1f, 0.16f, 1.15f, "player_skill_guihuan");
            yield return new WaitForSeconds(0.36f);
            if (!ComboAlive()) yield break;

            // 段2：旋身反震——清过度负责、责任球全数打回、好人墙整圈震破
            Pose(PoseState.AttackSpin);
            CombatFeedback.SwingArc(transform, true, green);
            Strike(PoseState.AttackSpin, 12f, 22f, 5f, 0.08f, 0.2f, 1.3f, "player_skill_guihuan");
            var debuff = GetComponent<OverResponsibilityDebuff>();
            if (debuff != null) Destroy(debuff);
            int returned = 0;
            foreach (var ball in FindObjectsOfType<ResponsibilityBall>())
                if (ball.isFalse) { ball.ForceReturn(); returned++; }
            int walls = CageWall.BreakAll();
            yield return new WaitForSeconds(0.4f);
            if (!ComboAlive()) yield break;

            // 段3：界域震地波 + 边界回补
            CombatFeedback.ShockRing(transform.position, green, 6f);
            CombatFeedback.Debris(transform.position + transform.forward * 0.8f, green, 6);
            CombatFeedback.SlowMo(0.5f, 0.12f);
            _player.Stats.RestoreAxis(Personalization.WeaknessAxis.BoundaryConflict, 18f);
            _player.Stats.ReduceRumination(12f);
            _player.Stats.ReduceRelationshipDrain(10f);
            Core.GameEvents.RaiseSubtitle(walls > 0
                ? "界返三连——好人牢笼被打破！我不是谁的替身人生。"
                : returned > 0
                    ? "界返三连——把不属于我的" + returned + "份责任，成套还了回去。"
                    : "界返三连——我只承担属于自己的那部分。");
            Core.GameAudio.Play(Core.GameAudio.Sfx.HeavyHit, 0.7f);
        }

        // ===================== 火「燃火·三段突进斩」 =====================

        /// <summary>点火解冻 → 火色双突进斩（每段突进 2 米+直线判定）→
        /// 上撩火浪终结。行动力点燃、意势+1——动力是被行动召回的。</summary>
        IEnumerator FiveMinuteSparkCombo()
        {
            _fsm.RequestState(CombatState.Finisher, 1.5f);
            Core.GameEvents.RaiseSkillBanner("「燃火·三段突进斩」");
            var fire = new Color(1f, 0.6f, 0.2f);

            // 点火：先解冻/解减速（先能动，才谈得上突进）
            _player.MoveSpeedMultiplier = 1f;
            var frozen = GetComponent<FrozenDebuff>();
            bool unfroze = frozen != null;
            if (frozen != null) Destroy(frozen);
            CombatFeedback.RecipeBurst(transform.position, fire);
            Core.GameAudio.Play(Core.GameAudio.Sfx.Cast, 0.6f);
            yield return new WaitForSeconds(0.2f);

            // 双突进斩：每段面向目标滑行突进 + 直线突刺判定 + 火色刀光
            for (int i = 0; i < 2 && ComboAlive(); i++)
            {
                FaceTarget();
                Pose(PoseState.SwordThrust);
                Glide(transform.forward * 2.2f, 0.14f);
                CombatFeedback.SwingArc(transform, i == 1, fire);
                CombatFeedback.HitSpark(transform.position + transform.forward * 1.2f, fire, 5);
                Strike(PoseState.SwordThrust, 14f, 16f, 2.5f, 0.08f, 0.18f, 1.2f, "player_skill_huozhong");
                yield return new WaitForSeconds(0.34f);
            }
            if (!ComboAlive()) yield break;

            // 上撩火浪终结 + 行动力点燃
            Pose(PoseState.AttackUp);
            CombatFeedback.SwingArc(transform, true, fire);
            Strike(PoseState.AttackUp, 16f, 24f, 5f, 0.1f, 0.18f, 1.3f, "player_skill_huozhong");
            CombatFeedback.ShockRing(transform.position + transform.forward * 1f, fire, 4.5f);
            _player.Stats.RestoreAxis(Personalization.WeaknessAxis.Procrastination, 45f);
            _player.Stats.ReduceRumination(8f);
            var combat = Combat();
            if (combat != null) combat.AddMomentum(1);
            Core.GameAudio.Play(Core.GameAudio.Sfx.Parry, 0.7f);
            Core.GameEvents.RaiseSubtitle(unfroze
                ? "燃火三段——行动打破冻结！先做五分钟，动起来再说。"
                : "燃火三段——不等动力，先开始；动力是被行动召回的。");
        }

        // ===================== 盾「镜界·退身斩」 =====================

        /// <summary>镜环展开护心（十秒内抵消下一次心理攻击）→ 后空翻拉开身位 →
        /// 掷出镜界气刃（顺带令幻影显形消散）。不硬接，先看清。</summary>
        IEnumerator MindShieldCombo()
        {
            _fsm.RequestState(CombatState.Finisher, 1.1f);
            Core.GameEvents.RaiseSkillBanner("「镜界·退身斩」");
            var blue = new Color(0.5f, 0.75f, 1f);

            // 镜环展开：护心 buff 上身
            var buff = GetComponent<MindShieldBuff>();
            if (buff == null) buff = gameObject.AddComponent<MindShieldBuff>();
            buff.Arm(10f);
            Pose(PoseState.Guard);
            CombatFeedback.RecipeBurst(transform.position, blue);
            CombatFeedback.ShockRing(transform.position, blue, 3f);
            yield return new WaitForSeconds(0.24f);
            if (!ComboAlive()) yield break;

            // 后空翻拉开身位（不硬接的身法）
            FaceTarget();
            Pose(PoseState.SpinKick);
            Glide(-transform.forward * 1.8f, 0.16f);
            CombatFeedback.SwingArc(transform, false, blue);
            yield return new WaitForSeconds(0.3f);
            if (!ComboAlive()) yield break;

            // 镜界气刃：短程投射，命中削韧（把"猜测"钉回原地）
            Pose(PoseState.Attack);
            Vector3 origin = transform.position + Vector3.up * 1.2f + transform.forward * 0.7f;
            Projectile.Launch(transform, origin, transform.forward, new DamageInfo
            {
                physicalDamage = 10f, postureDamage = 18f, knockback = 2f,
                attackerId = "player_skill_budu"
            }, 18f, blue, null, 1.1f);
            Core.GameAudio.Play(Core.GameAudio.Sfx.Cast, 0.6f);
            Core.GameEvents.RaiseSubtitle("镜界·退身斩——无法确认的事，我不把猜测当事实（抵消下一次心理攻击）。");
        }
    }
}
