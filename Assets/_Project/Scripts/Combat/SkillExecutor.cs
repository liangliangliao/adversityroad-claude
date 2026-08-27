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

        // 技能输入缓冲（意图匹配）：在出招锁定/硬直期间按下的技能不再石沉大海，
        // 而是排队等待——动作一结束立刻接上，读作"我按了它就会打出来"。
        //
        // 寿命必须覆盖"当前正锁着的那个动作"：技能只在 IsActionLocked 时才入缓冲，
        // 而锁本身可长达 2.05s（蓄力气场）/1.75s（超必杀）/1.45s（技能连招）。
        // 原来的 0.35s 固定窗比几乎每一个长动作都短——排队进去必然过期，
        // 与"排队等待"的注释完全相反：技能连招后想立刻接第二个技能，那一下永远丢。
        const float SkillBufferWindow = InputBuffer.QueuedLife;   // 2.5s，覆盖最长动作
        int _bufferedSkill = -1;
        float _bufferedSkillAt = -99f;
        float _lastCdHint = -99f, _lastResourceHint = -99f;   // 提示节流（连点不刷屏）

        void Update()
        {
            var keys = new List<string>(_cooldowns.Keys);
            foreach (var k in keys) _cooldowns[k] = Mathf.Max(0, _cooldowns[k] - Time.deltaTime);

            int n = Mathf.Min(6, equippedSkills.Count);
            int pressed = -1;
            // 桌面：数字键 1-6。触屏：技能没有独立按钮，由修饰键「术」+核心键路由产生
            //（术+拳=Skill1 … 术+挡=Skill6，见 MobileInput.SkillMap），此处读到的名字不变。
            for (int i = 0; i < n; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || MobileInput.GetDown("Skill" + (i + 1)))
                    pressed = i;

            if (pressed >= 0)
            {
                // 只有「正处于出招锁定」才排队——这类失败马上就会好转。
                // 冷却中/资源不足不入缓冲，否则会每帧重试并刷屏提示。
                bool lockedNow = _fsm.IsActionLocked;
                if (!TryCast(equippedSkills[pressed]) && lockedNow)
                {
                    _bufferedSkill = pressed;
                    _bufferedSkillAt = Time.unscaledTime;
                }
                return;
            }

            // 兑现排队中的技能：动作锁一解除立刻打出；超窗作废（防陈旧输入迟到触发）
            if (_bufferedSkill >= 0)
            {
                if (Time.unscaledTime - _bufferedSkillAt > SkillBufferWindow) _bufferedSkill = -1;
                else if (_bufferedSkill < equippedSkills.Count &&
                         TryCast(equippedSkills[_bufferedSkill])) _bufferedSkill = -1;
            }
        }

        public bool TryCast(Data.SkillDefinition skill)
        {
            if (skill == null) return false;
            // 收招取消：长动作打完主要判定进入恢复相位后，技能同样可以立刻接上——
            // 与攻击、闪避同一条规则。否则技能→技能之间恒定卡着一整个收招段，
            // 排队的第二个技能虽然不再丢，却要等一秒多才出来。
            //
            // 这里【只判定、不改状态】：下面还有冷却/意势/体力/意志四道门槛，
            // 任何一道没过都会 return false——若在此处先解锁，玩家会被白白踢出恢复相位
            // （技能没放出来，收招却被切了）。真正的状态切换交给下面的 RequestState，
            // 它本就无条件覆盖当前状态，并由 StartCombo 停掉上一套连招协程。
            if (_fsm.IsActionLocked && !_fsm.CanCancelRecovery) return false;
            if (_cooldowns.TryGetValue(skill.skillId, out float cd) && cd > 0)
            {
                // 连点冷却中的技能不刷屏（节流）
                if (Time.time - _lastCdHint > 1.2f)
                {
                    _lastCdHint = Time.time;
                    Core.GameEvents.RaiseSubtitle("「" + skill.displayName + "」调息中……");
                }
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
            // 资源不足要给明确反馈——静默失败会读作"我按了它却没反应"（节流防刷屏）
            if (!_player.Stats.SpendStamina(skill.staminaCost))
            {
                if (Time.time - _lastResourceHint > 1.5f)
                {
                    _lastResourceHint = Time.time;
                    Core.GameEvents.RaiseSubtitle("体力不足：「" + skill.displayName + "」施展不出来。");
                }
                return false;
            }
            if (skill.willCost > 0 && !_player.Stats.SpendWill(skill.willCost))
            {
                if (Time.time - _lastResourceHint > 1.5f)
                {
                    _lastResourceHint = Time.time;
                    Core.GameEvents.RaiseSubtitle("意志不足：「" + skill.displayName + "」施展不出来。");
                }
                return false;
            }
            if (skill.momentumCost > 0) Core.GameEvents.RaiseSkillBanner("「" + skill.displayName + "」");

            // 技能同样是连招元素：成功施展即入融合链，于是「术→剑」「跃→术→剑」
            // 这类跨系统串法能接成融招（术后追斩 / 踏云术斩）。
            // 技能不再是一段自成一体的独立演出，而是可被前后动作接住的一环。
            {
                var cf = Combat();
                if (cf != null) cf.Fusion.Push(MoveToken.Skill);
            }

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
                // 技能也吃融合加成：从连招串进来的技能比冷启动单放更狠——
                // 「技能是连招的一环」这句话必须在伤害上兑现，否则玩家没有理由去串。
                var cfd = Combat();
                float fusionMult = cfd != null ? cfd.Fusion.FusionMult : 1f;
                var dmg = new DamageInfo
                {
                    physicalDamage = skill.physicalDamage * fusionMult,
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

        /// <summary>连招每段的动作。dur = 这一段占的节拍——动画按它反推播放速度，
        /// 于是"一段 0.18 秒"的技能连招在画面上真的是 0.18 秒一刀，
        /// 而不是每段都慢吞吞地起手到一半就被下一段顶掉。</summary>
        void Pose(PoseState p, float dur = 0f)
        {
            if (_anim == null) _anim = GetComponent<HumanoidAnimator>();
            if (_anim != null) _anim.PlayAttackPose(p, dur);
        }

        /// <summary>
        /// 连招每段的朝向决策——玩家意图优先（大作 attack steering 的配套规则）：
        /// · 玩家正明显推杆（幅度足够）且方向与自动目标偏差 &gt;45° → 顺着摇杆出招，
        ///   连招跟着你走，不被自动吸附拽回去；
        /// · 否则咬住自动瞄准目标（无目标则保持当前朝向，由摇杆转向接管）。
        /// </summary>
        void FaceTarget()
        {
            Vector3 stick = _player != null ? _player.StickWorldDir : Vector3.zero;
            bool steering = stick.sqrMagnitude > 0.25f;   // 摇杆幅度 >0.5 视为主动引导

            var combat = Combat();
            Transform aim = combat != null ? combat.AutoAimTarget() : null;
            if (aim != null)
            {
                Vector3 dir = aim.position - transform.position; dir.y = 0;
                if (dir.sqrMagnitude > 0.01f)
                {
                    dir.Normalize();
                    // 玩家推杆明显偏离目标：尊重玩家方向，不硬拽回目标
                    if (steering && Vector3.Angle(stick, dir) > 45f)
                        transform.rotation = Quaternion.LookRotation(stick.normalized);
                    else
                        transform.rotation = Quaternion.LookRotation(dir);
                }
                return;
            }
            if (steering) transform.rotation = Quaternion.LookRotation(stick.normalized);
        }

        /// <summary>短促滑行位移（同 PlayerCombatController.GlideMove：不瞬移防镜头抖）。</summary>
        void Glide(Vector3 offset, float duration)
        {
            if (_glideRoutine != null) StopCoroutine(_glideRoutine);
            _glideRoutine = StartCoroutine(GlideRoutine(offset, duration));
        }

        /// <summary>
        /// 带动作的突进段。
        ///
        /// 此前突进 2.2 米期间播的是**原地**斩击片段——脚不动、人在飘，
        /// 这是"技能像开挂不像发力"的头号来源。
        ///
        /// 解法不是"先播冲刺再切斩击"：这一段的节拍只有 0.16~0.2 秒，
        /// 中途换片段会把两个动作都切成半截，判定窗还跨在切换点上。
        /// 正确做法是整段用一条**本身就带位移的攻击片段**（Great Sword Slide Attack：
        /// 滑步欺近同时挥出），一个动作把"移动"和"打击"一起演完——大作里的
        /// 突进斩就是这么做的。
        ///
        /// 只在真正"拉开距离再贴上去"的那几段用（preferDash）：全用同一条滑步斩，
        /// 三连突进就变成同一个动作播三遍，反而丢了连招的层次。
        /// </summary>
        void DashPose(PoseState strikePose, Vector3 offset, float duration, float strikeDur,
            bool preferDash)
        {
            Glide(offset, duration);
            bool dash = preferDash && offset.magnitude >= 0.8f && HasPose(PoseState.DashAttack);
            Pose(dash ? PoseState.DashAttack : strikePose, strikeDur);
        }

        /// <summary>动作库里有这个姿态吗（没有就退回原来的姿态，不留空）。</summary>
        bool HasPose(PoseState p)
        {
            if (_anim == null) _anim = GetComponent<HumanoidAnimator>();
            return _anim != null && _anim.HasPose(p);
        }

        Player.PlayerController _pcForMove;

        IEnumerator GlideRoutine(Vector3 offset, float duration)
        {
            float t = 0;
            while (t < duration)
            {
                float dt = Mathf.Min(Time.deltaTime, Player.PlayerController.MaxSimStep);
                t += dt;
                // 与出招磁吸同理：能走外部通道就走，让位移与重力在同一次 Move 里落地
                //（见 PlayerController._extMove）。技能执行器也挂在敌人身上，
                // 那边没有 PlayerController，退回自己分步位移。
                Vector3 step = offset * Mathf.Min(dt / duration, 1f);
                if (_pcForMove == null) _pcForMove = GetComponent<Player.PlayerController>();
                if (_pcForMove != null) _pcForMove.AddExternalMove(step, "技能突进");
                else if (_cc != null) CharacterMotion.StepMove(_cc, step);
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
            // 绝招连段的每一段同样吃融合加成（技能是连招的一环，不是独立的孤岛）
            var cf = Combat();
            if (cf != null) dmg *= cf.Fusion.FusionMult;
            StartCoroutine(StrikeWindow(windup, open, dmg, posture, knockback, tag));
        }

        IEnumerator StrikeWindow(float windup, float open, float dmg, float posture,
            float knockback, string tag)
        {
            yield return new WaitForSeconds(windup);
            weaponHitbox.onHit = h =>
            {
                CombatFeedback.HitStopByPower(Mathf.Clamp01(dmg / 60f));
                Core.GameAudio.Play(Core.GameAudio.Sfx.Hit, dmg >= 30f ? 1f : 0.8f);
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

        /// <summary>终结段判定窗时长（各技能终结段最长 windup 0.1 + open 0.2）：
        /// 收招取消窗必须开在它之后，否则玩家连打会把自己的终结段取消掉。
        /// 判定窗收紧后这里同步从 0.34 降到 0.3——收招少等一拍，接下一招更跟手。</summary>
        const float FinalStrikeWindow = 0.3f;

        void StartCombo(IEnumerator combo)
        {
            // 起势保护（大作惯例：技能起手无敌帧）：0.65s 内完全免伤——
            // 保证连招能顺利开出来，不被敌人抢先一下打断在起手式上；
            // 其后的连招段落由霸体接管（轻击不打断，见 PlayerCombatController.TakeHit）
            _player.SetInvincible(0.65f);
            if (_comboRoutine != null) StopCoroutine(_comboRoutine);
            _comboRoutine = StartCoroutine(combo);
        }

        /// <summary>连招阶段间隔：被击倒/硬直打断时返回 false，整套连招终止。</summary>
        bool ComboAlive() => _fsm.Current == CombatState.Finisher;

        // ===================== 定「定心·四象归一」 =====================

        /// <summary>收势凝神 → 三重内收气环（每环削韧+推离周围敌人）→ 旋身归一爆发
        /// → 终结段「镇岳跳劈」凌空砸地大范围震波。心神大幅恢复——
        /// 护体不是站桩：先把周围搅扰整圈荡开，再一击镇场。</summary>
        IEnumerator SteadyHeartCombo(float mentalRestore)
        {
            _fsm.RequestState(CombatState.Finisher, 1.25f);
            Core.GameEvents.RaiseSkillBanner("「定心·四象归一」");
            Pose(HasPose(PoseState.ChargeLoop) ? PoseState.ChargeLoop : PoseState.Charge);
            CombatFeedback.ChargeGale(transform.position, 0.6f);
            yield return new WaitForSeconds(0.14f);

            // 三重内收气环：由外向内收束（外圈大→内圈小），伤害递增、削韧并推离敌人
            var ringColor = new Color(0.45f, 0.65f, 1f);
            for (int i = 0; i < 3 && ComboAlive(); i++)
            {
                // 每一环都要有【动作】：此前这三段只放特效，角色自始至终保持
                // 「蓄力」这个 hold 姿态——动画停在最后一帧不动，三环 0.42 秒里
                // 人物完全静止，只有光环在扩散。这是"技能看起来不动"的实例之一。
                // 现在每环打一记旋身，三环＝三次旋转把搅扰荡开，动作与演出对得上。
                // 三环换着打：连着播同一个片段＝同一段动作反复从头来，读作"抖"而不是"连"。
                // 旋斩→撩斩→旋斩，三个不同轨迹串起来才像一套连招。
                var ringPose = i == 1 ? PoseState.AttackUp : PoseState.AttackSpin;
                Pose(ringPose, 0.2f);
                CombatFeedback.ShockRing(transform.position, ringColor, 5.5f - i * 1.4f);
                CombatFeedback.HitSpark(transform.position + Vector3.up * 1.1f, ringColor, 4);
                Strike(ringPose, 10f + i * 4f, 16f, 2.5f, 0.02f, 0.12f, 1.2f, "player_skill_dingxin");
                foreach (var e in AdversityRoad.Core.ActorRegistry.Enemies)
                    e.Repel(transform.position, 4.5f, 5f, 0.16f);
                Core.GameAudio.Play(Core.GameAudio.Sfx.Cast, 0.5f);
                yield return new WaitForSeconds(0.16f);
            }
            if (!ComboAlive()) yield break;

            // 归一：旋身收势爆发 + 心神恢复 + 短时缓
            Pose(PoseState.AttackSpin, 0.24f);
            Strike(PoseState.AttackSpin, 20f, 20f, 4f, 0.06f, 0.16f, 1.35f, "player_skill_dingxin");
            CombatFeedback.EnergyBurst(transform.position + Vector3.up * 0.8f, ringColor, 1.1f);
            CombatFeedback.SlowMo(0.5f, 0.15f);
            _player.Stats.RestoreMental(mentalRestore);
            Core.GameAudio.Play(Core.GameAudio.Sfx.Parry, 0.8f);
            yield return new WaitForSeconds(0.22f);
            if (!ComboAlive()) yield break;

            // 终结段「镇岳」：凌空跳劈砸地，大范围震波镇住整个战场
            FaceTarget();
            Pose(PoseState.AttackLeap, 0.34f);
            Glide(transform.forward * 1.2f, 0.14f);
            CombatFeedback.SwingArc(transform, true, ringColor);
            Strike(PoseState.AttackLeap, 38f, 34f, 6f, 0.1f, 0.2f, 1.35f, "player_skill_dingxin");
            CombatFeedback.ShockRing(transform.position + transform.forward * 1.2f, ringColor, 7f);
            CombatFeedback.Debris(transform.position + transform.forward * 1.2f, ringColor, 7);
            Core.GameAudio.Play(Core.GameAudio.Sfx.HeavyHit, 0.8f);
            Core.GameEvents.RaiseSubtitle("四象归一——心神落定，心理属性恢复。");
            // 等最后一击的判定窗完整走完再开放取消，否则连打会把终结段吃掉
            yield return new WaitForSeconds(FinalStrikeWindow);
            _fsm.CanCancelRecovery = true;   // 收招相位：闪避或攻击均可立刻打断，不必等动作播完
        }

        // ===================== 收「收心·万流归元」 =====================

        /// <summary>三连旋踢清场（伤害递增、每旋轻位移咬向目标）→ 幻影全灭+归元冲击波
        /// → 终结段「回身斩」时缓收势。专注回收、反刍下降。</summary>
        IEnumerator AttentionRecallCombo()
        {
            _fsm.RequestState(CombatState.Finisher, 1.1f);
            Core.GameEvents.RaiseSkillBanner("「收心·万流归元」");
            FaceTarget();
            var cyan = new Color(0.3f, 0.85f, 0.95f);

            // 三连旋踢清场：伤害递增，每旋向目标轻位移（旋进不飘）
            for (int i = 0; i < 3 && ComboAlive(); i++)
            {
                FaceTarget();
                // 旋踢→侧踹→旋踢：三段各有各的轨迹，清场读作"踢了三下"而不是"转了三圈"
                var kickPose = i == 1 ? PoseState.SideKick : PoseState.SpinKick;
                Glide(transform.forward * 0.7f, 0.12f);
                Pose(kickPose, 0.2f);
                CombatFeedback.SwingArc(transform, i >= 1, cyan);
                Strike(kickPose, 14f + i * 5f, 18f, 3f, 0.06f, 0.14f, 1.25f, "player_skill_huishou");
                yield return new WaitForSeconds(0.16f);
            }
            if (!ComboAlive()) yield break;

            // 万流归元：幻影全灭 + 冲击波 + 专注回收
            int cleared = PhantomDecoy.ClearAll();
            Pose(PoseState.AttackSpin, 0.22f);
            CombatFeedback.ShockRing(transform.position, cyan, 6.5f);
            CombatFeedback.EnergyBurst(transform.position + Vector3.up * 0.9f, cyan, 0.95f);
            _player.Stats.RestoreAxis(Personalization.WeaknessAxis.NoiseSensitivity, 32f);
            _player.Stats.ReduceRumination(15f);
            Core.GameAudio.Play(Core.GameAudio.Sfx.Parry, 0.7f);
            yield return new WaitForSeconds(0.16f);
            if (!ComboAlive()) yield break;

            // 终结段「回身斩」：环身大范围收势一斩 + 短时缓
            Pose(PoseState.AttackSpin, 0.3f);
            CombatFeedback.SwingArc(transform, true, cyan);
            Strike(PoseState.AttackSpin, 32f, 26f, 5f, 0.07f, 0.18f, 1.4f, "player_skill_huishou");
            CombatFeedback.SlowMo(0.5f, 0.14f);
            Core.GameEvents.RaiseSubtitle(cleared > 0
                ? "万流归元——" + cleared + " 个幻影散去。不是所有声音都要回应。"
                : "万流归元——我把注意力拿回来，放回自己手上的事。");
            // 等最后一击的判定窗完整走完再开放取消，否则连打会把终结段吃掉
            yield return new WaitForSeconds(FinalStrikeWindow);
            _fsm.CanCancelRecovery = true;   // 收招相位：闪避或攻击均可立刻打断，不必等动作播完
        }

        // ===================== 还「还域·界返三连」 =====================

        /// <summary>撩斩挑飞 → 横斩接力 → 旋身反震（虚假责任球全数打回、好人墙整圈震破）
        /// → 弓步突刺 → 界域震地波终结+边界回补。把不属于自己的，成套还回去。</summary>
        IEnumerator ResponsibilityReturnCombo()
        {
            _fsm.RequestState(CombatState.Finisher, 1.25f);
            Core.GameEvents.RaiseSkillBanner("「还域·界返三连」");
            FaceTarget();
            var green = new Color(0.4f, 0.85f, 0.6f);

            // 段1：撩斩挑飞（纵向高弧判定）
            Pose(PoseState.AttackUp, 0.2f);
            CombatFeedback.SwingArc(transform, true, green);
            Strike(PoseState.AttackUp, 20f, 22f, 4f, 0.07f, 0.14f, 1.15f, "player_skill_guihuan");
            yield return new WaitForSeconds(0.16f);
            if (!ComboAlive()) yield break;

            // 段2：横斩接力（承上启下的连贯挥击）
            FaceTarget();
            DashPose(PoseState.Attack, transform.forward * 0.8f, 0.1f, 0.2f, false);
            CombatFeedback.SwingArc(transform, false, green);
            Strike(PoseState.Attack, 22f, 18f, 3f, 0.06f, 0.14f, 1.2f, "player_skill_guihuan");
            yield return new WaitForSeconds(0.16f);
            if (!ComboAlive()) yield break;

            // 段3：旋身反震——清过度负责、责任球全数打回、好人墙整圈震破
            Pose(PoseState.AttackSpin, 0.22f);
            CombatFeedback.SwingArc(transform, true, green);
            Strike(PoseState.AttackSpin, 25f, 26f, 5f, 0.07f, 0.16f, 1.3f, "player_skill_guihuan");
            var debuff = GetComponent<OverResponsibilityDebuff>();
            if (debuff != null) Destroy(debuff);
            int returned = 0;
            foreach (var ball in FindObjectsOfType<ResponsibilityBall>())
                if (ball.isFalse) { ball.ForceReturn(); returned++; }
            int walls = CageWall.BreakAll();
            yield return new WaitForSeconds(0.17f);
            if (!ComboAlive()) yield break;

            // 段4：弓步突刺——把「不属于我的」钉还回去
            FaceTarget();
            DashPose(PoseState.SwordThrust, transform.forward * 1.6f, 0.12f, 0.2f, false);
            CombatFeedback.SwingArc(transform, false, green);
            Strike(PoseState.SwordThrust, 28f, 20f, 3f, 0.06f, 0.14f, 1.25f, "player_skill_guihuan");
            yield return new WaitForSeconds(0.16f);
            if (!ComboAlive()) yield break;

            // 段5：界域震地波终结 + 边界回补
            Pose(PoseState.AttackLeap, 0.32f);
            Strike(PoseState.AttackLeap, 34f, 30f, 6f, 0.09f, 0.18f, 1.3f, "player_skill_guihuan");
            CombatFeedback.ShockRing(transform.position, green, 6.5f);
            CombatFeedback.Debris(transform.position + transform.forward * 0.8f, green, 7);
            CombatFeedback.SlowMo(0.5f, 0.12f);
            _player.Stats.RestoreAxis(Personalization.WeaknessAxis.BoundaryConflict, 18f);
            _player.Stats.ReduceRumination(12f);
            _player.Stats.ReduceRelationshipDrain(10f);
            Core.GameEvents.RaiseSubtitle(walls > 0
                ? "界返三连——好人牢笼被打破！我不是谁的替身人生。"
                : returned > 0
                    ? "界返三连——把不属于我的" + returned + "份责任，成套还了回去。"
                    : "界返三连——我只承担属于自己的那部分。");
            // 等最后一击的判定窗完整走完再开放取消，否则连打会把终结段吃掉
            yield return new WaitForSeconds(FinalStrikeWindow);
            _fsm.CanCancelRecovery = true;   // 收招相位：闪避或攻击均可立刻打断，不必等动作播完
            Core.GameAudio.Play(Core.GameAudio.Sfx.HeavyHit, 0.7f);
        }

        // ===================== 火「燃火·三段突进斩」 =====================

        /// <summary>点火解冻 → 火色三连突进斩（伤害递增、每段突进 2 米）→ 上撩火浪
        /// → 终结段「烈焰跳劈」落地火环。行动力点燃、意势+1——动力是被行动召回的。</summary>
        IEnumerator FiveMinuteSparkCombo()
        {
            _fsm.RequestState(CombatState.Finisher, 1.3f);
            Core.GameEvents.RaiseSkillBanner("「燃火·五段燎原」");
            var fire = new Color(1f, 0.6f, 0.2f);

            // 点火：先解冻/解减速（先能动，才谈得上突进）
            _player.ClearAllSlow();   // 燃火·点火解冻：明确的全面解除
            var frozen = GetComponent<FrozenDebuff>();
            bool unfroze = frozen != null;
            if (frozen != null) Destroy(frozen);
            CombatFeedback.RecipeBurst(transform.position, fire);
            Core.GameAudio.Play(Core.GameAudio.Sfx.Cast, 0.6f);
            yield return new WaitForSeconds(0.1f);

            // 三连突进斩：伤害递增，每段面向目标滑行突进 + 直线突刺判定 + 火色刀光
            for (int i = 0; i < 3 && ComboAlive(); i++)
            {
                FaceTarget();
                // 突刺→横斩→突刺：突进途中夹一记横扫，三连不再是同一记戳刺重播
                var dashPose = i == 1 ? PoseState.Attack : PoseState.SwordThrust;
                DashPose(dashPose, transform.forward * 2.2f, 0.13f, 0.2f, i == 0);
                CombatFeedback.SwingArc(transform, i == 2, fire);
                CombatFeedback.HitSpark(transform.position + transform.forward * 1.2f, fire, 5);
                Strike(dashPose, 22f + i * 5f, 16f, 2.5f, 0.05f, 0.13f, 1.2f, "player_skill_huozhong");
                yield return new WaitForSeconds(0.16f);
            }
            if (!ComboAlive()) yield break;

            // 上撩火浪 + 行动力点燃
            Pose(PoseState.AttackUp, 0.24f);
            CombatFeedback.SwingArc(transform, true, fire);
            Strike(PoseState.AttackUp, 30f, 26f, 5f, 0.07f, 0.16f, 1.3f, "player_skill_huozhong");
            CombatFeedback.ShockRing(transform.position + transform.forward * 1f, fire, 4.5f);
            _player.Stats.RestoreAxis(Personalization.WeaknessAxis.Procrastination, 45f);
            _player.Stats.ReduceRumination(8f);
            var combat = Combat();
            if (combat != null) combat.AddMomentum(1);
            Core.GameAudio.Play(Core.GameAudio.Sfx.Parry, 0.7f);
            yield return new WaitForSeconds(0.19f);
            if (!ComboAlive()) yield break;

            // 终结段「烈焰跳劈」：凌空砸地，落地火环燎原 + 短时缓
            FaceTarget();
            Pose(PoseState.AttackLeap, 0.34f);
            Glide(transform.forward * 1.4f, 0.14f);
            CombatFeedback.SwingArc(transform, true, fire);
            Strike(PoseState.AttackLeap, 42f, 36f, 7f, 0.1f, 0.2f, 1.4f, "player_skill_huozhong");
            CombatFeedback.ShockRing(transform.position + transform.forward * 1.3f, fire, 7.5f);
            CombatFeedback.EnergyBurst(transform.position + transform.forward * 1.3f, fire, 1.1f);
            CombatFeedback.SlowMo(0.45f, 0.16f);
            Core.GameAudio.Play(Core.GameAudio.Sfx.HeavyHit, 0.85f);
            Core.GameEvents.RaiseSubtitle(unfroze
                ? "燃火燎原——行动打破冻结！先做五分钟，动起来再说。"
                : "燃火燎原——不等动力，先开始；动力是被行动召回的。");
            // 等最后一击的判定窗完整走完再开放取消，否则连打会把终结段吃掉
            yield return new WaitForSeconds(FinalStrikeWindow);
            _fsm.CanCancelRecovery = true;   // 收招相位：闪避或攻击均可立刻打断，不必等动作播完
        }

        // ===================== 盾「镜界·退身斩」 =====================

        /// <summary>镜环展开护心（十秒内抵消下一次心理攻击）→ 后空翻拉开身位 →
        /// 双镜界气刃连发 → 终结段「镜返突刺」闪回目标身前反击一击。
        /// 不硬接，先看清，再反打。</summary>
        IEnumerator MindShieldCombo()
        {
            _fsm.RequestState(CombatState.Finisher, 1.1f);
            Core.GameEvents.RaiseSkillBanner("「镜界·退身反击」");
            var blue = new Color(0.5f, 0.75f, 1f);

            // 镜环展开：护心 buff 上身
            var buff = GetComponent<MindShieldBuff>();
            if (buff == null) buff = gameObject.AddComponent<MindShieldBuff>();
            buff.Arm(10f);
            Pose(PoseState.Guard);
            CombatFeedback.RecipeBurst(transform.position, blue);
            CombatFeedback.ShockRing(transform.position, blue, 3f);
            yield return new WaitForSeconds(0.12f);
            if (!ComboAlive()) yield break;

            // 后空翻拉开身位（不硬接的身法）
            FaceTarget();
            Pose(PoseState.SpinKick, 0.2f);
            Glide(-transform.forward * 1.8f, 0.16f);
            CombatFeedback.SwingArc(transform, false, blue);
            yield return new WaitForSeconds(0.16f);
            if (!ComboAlive()) yield break;

            // 双镜界气刃连发：命中削韧（把"猜测"逐一钉回原地）
            for (int i = 0; i < 2 && ComboAlive(); i++)
            {
                // 两记气刃一横斩一撩斩，掷出的姿势不重复
                Pose(HasPose(PoseState.CastProjectile)
                    ? PoseState.CastProjectile
                    : (i == 0 ? PoseState.Attack : PoseState.AttackUp), 0.18f);
                Vector3 origin = transform.position + Vector3.up * 1.2f + transform.forward * 0.7f;
                Projectile.Launch(transform, origin, transform.forward, new DamageInfo
                {
                    physicalDamage = 12f, postureDamage = 20f, knockback = 2f,
                    attackerId = "player_skill_budu"
                }, 18f, blue, null, 1.1f);
                Core.GameAudio.Play(Core.GameAudio.Sfx.Cast, 0.6f);
                yield return new WaitForSeconds(0.16f);
            }
            if (!ComboAlive()) yield break;

            // 终结段「镜返突刺」：闪身欺近，反手一记弓步突刺 + 短时缓
            FaceTarget();
            DashPose(PoseState.SwordThrust, transform.forward * 2.4f, 0.13f, 0.3f, true);
            CombatFeedback.SwingArc(transform, true, blue);
            Strike(PoseState.SwordThrust, 34f, 26f, 4f, 0.07f, 0.15f, 1.3f, "player_skill_budu");
            CombatFeedback.SlowMo(0.5f, 0.12f);
            Core.GameEvents.RaiseSubtitle("镜界反击——无法确认的事，我不把猜测当事实（抵消下一次心理攻击）。");
            // 等最后一击的判定窗完整走完再开放取消，否则连打会把终结段吃掉
            yield return new WaitForSeconds(FinalStrikeWindow);
            _fsm.CanCancelRecovery = true;   // 收招相位：闪避或攻击均可立刻打断，不必等动作播完
        }
    }
}
