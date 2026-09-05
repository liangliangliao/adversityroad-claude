using System;
using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 玩家属性：生命、体力 + 五维心理属性（意志/专注/自尊/边界/行动力）
    /// + 两条反向消耗条（反刍/关系消耗，越高越糟）。
    /// 心理属性归零不直接死亡，而是触发对应的"心理硬直"状态。
    /// </summary>
    [Serializable]
    public class PlayerStats
    {
        public float hp = 100, maxHp = 100;
        public float stamina = 100, maxStamina = 100;
        public float will = 100, maxWill = 100;           // 意志：抵抗低谷、放弃、旧我压迫
        public float focus = 100, maxFocus = 100;         // 专注：抵抗噪声、凝视
        public float selfWorth = 100, maxSelfWorth = 100; // 自尊：抵抗羞耻、否定
        public float boundary = 100, maxBoundary = 100;   // 边界：抵抗操控、责任转嫁
        public float actionPower = 100, maxActionPower = 100; // 行动力：抵抗拖延、行动瘫痪；低时移速下降

        // 反刍值：与其它属性相反——越高越糟。未被回应/被错误回应的言语攻击会累积反刍，
        // 满值触发旧事回声；战后复盘（归档）可清零。战斗外缓慢消退，避免软锁。
        public float rumination = 0, maxRumination = 100;

        // 关系消耗值：被索取、代付、情绪勒索、错误接下责任时上升——越高越糟。
        // 过高（≥70）时技能冷却变长（被消耗掉的注意力与精力）。边界圈/明确拒绝可回落。
        public float relationshipDrain = 0, maxRelationshipDrain = 100;

        // 公平刺痛值（方案「十七、核心数值系统」）：遇到赖账、不守承诺、不公平时上升——越高越糟。
        // 三档规则：0-30 记住事实、事实之刃更利；31-70 攻击上升但反刍风险增大；
        // 71-100 愤怒失控风险、判断下降（更易吃心理伤害）。复盘/正确言语回应可回落。
        public float fairnessPain = 0, maxFairnessPain = 100;

        public float staminaRegenPerSec = 22f;   // 连打可持续（大作轻攻击不该被体力卡死）
        public float mentalRegenPerSec = 2f;
        public float ruminationDecayPerSec = 1.5f;
        public float drainDecayPerSec = 1.2f;
        public float fairnessPainDecayPerSec = 1.0f;   // 战斗外缓慢平复（比反刍更慢消退）

        public bool IsDead => hp <= 0;

        /// <summary>单次物理伤害占最大生命的上限（0.25 = 一击最多掉四分之一）。</summary>
        public const float MaxSingleHitRatio = 0.25f;

        /// <summary>关系消耗过高：技能冷却 ×1.5（见 SkillExecutor）。</summary>
        public bool IsOverDrained => relationshipDrain >= 70f;

        // ===== 公平刺痛三档（方案规则） =====
        /// <summary>0-30：清明区——事实之刃/公平系技能增伤。</summary>
        public bool FairnessClear => fairnessPain <= 30f;
        /// <summary>31-70：激愤区——物理输出小增，但反刍累积风险上升。</summary>
        public bool FairnessAgitated => fairnessPain > 30f && fairnessPain <= 70f;
        /// <summary>71-100：失控区——判断下降，心理伤害吃得更重。</summary>
        public bool FairnessRaging => fairnessPain > 70f;

        /// <summary>公平三档物理输出（方案）：清明区 ×1.12（事实之刃更利）、
        /// 激愤区 ×1.08（攻击上升，代价是反刍风险）、失控区 ×0.9（被伤口困住、判断下降）。</summary>
        public float FairnessPhysicalOutMult =>
            FairnessClear ? 1.12f : FairnessAgitated ? 1.08f : 0.9f;
        /// <summary>失控区判断下降：心理伤害承受 ×1.25。</summary>
        public float FairnessMentalTakenMult => FairnessRaging ? 1.25f : 1f;

        public void TickRegen(float dt, bool inCombat)
        {
            float staminaBefore = stamina;
            stamina = Mathf.Min(maxStamina, stamina + staminaRegenPerSec * dt);
            // 濒临崩溃时恢复变慢（方案 12.3）：压力不只是数字，它改变你回气的速度
            var stress = Adversity.StressStateMachine.Instance;
            float regenMult = stress != null ? stress.RegenMultiplier() : 1f;
            if (regenMult < 1f)
                stamina = staminaBefore + (stamina - staminaBefore) * regenMult;
            if (!Mathf.Approximately(staminaBefore, stamina))
                GameEvents.RaiseStaminaChanged(stamina, maxStamina);
            // 战斗中被动回复压得很低：心理能量的涨落主要来自战斗行为本身
            // （受击/被围/站桩流失，命中/击杀/完美闪避回复——见 MentalDynamics），
            // 否则被动回复会把这些变化当场抹平，条上永远看不到起伏。
            float m = (inCombat ? 0.1f : 1f) * mentalRegenPerSec * dt * regenMult;
            will = Mathf.Min(maxWill, will + m);
            focus = Mathf.Min(maxFocus, focus + m);
            selfWorth = Mathf.Min(maxSelfWorth, selfWorth + m);
            boundary = Mathf.Min(maxBoundary, boundary + m);
            actionPower = Mathf.Min(maxActionPower, actionPower + m);
            if (!inCombat && rumination > 0)
                AddRumination(-ruminationDecayPerSec * dt);
            if (!inCombat && relationshipDrain > 0)
                AddRelationshipDrain(-drainDecayPerSec * dt);
            if (!inCombat && fairnessPain > 0)
                AddFairnessPain(-fairnessPainDecayPerSec * dt);
        }

        /// <summary>公平刺痛增减（可为负）。触发 UI 更新与失控区提示。</summary>
        public void AddFairnessPain(float amount)
        {
            bool wasRaging = FairnessRaging;
            fairnessPain = Mathf.Clamp(fairnessPain + amount, 0, maxFairnessPain);
            GameEvents.RaiseMentalStatChanged("fairnessPain", fairnessPain, maxFairnessPain);
            if (!wasRaging && FairnessRaging)
                GameEvents.RaiseSubtitle("公平刺痛失控——愤怒会让判断下降。记住事实，别被伤口困住。");
        }

        public void ReduceFairnessPain(float amount) => AddFairnessPain(-amount);

        /// <summary>生命垂危线（比例）与濒死守护冷却。</summary>
        public const float CriticalHpRatio = 0.15f;
        [NonSerialized] float _nextLastStand = -999f;

        public void TakePhysicalDamage(float dmg)
        {
            // ===== 单次伤害上限（可玩性规则）=====
            // Boss 的重招（基础 22 × 精英招 1.5 × 背刺 1.4 ≈ 46）能一口气打掉近半条命，
            // 玩家还没读懂这一招就已经死了一半——这不是难度，是**不可学习**。
            // 主流动作游戏普遍给单次伤害封顶（常见 20~30% 最大生命），理由很实际：
            //   ① 死亡至少需要四次失误，玩家有时间在同一场战斗里认出招式并改打法；
            //   ② 保证"看红光→闪避"这类规则有被验证的机会，学习才成立；
            //   ③ 秒杀会把战斗变成背板与运气，而不是技巧。
            // 上限只压【单次】，连续挨打照样会死——惩罚仍在，只是变得可读。
            dmg = Mathf.Min(dmg, maxHp * MaxSingleHitRatio);

            // 【第八章目标动作的长按保护】
            // 8-2 的三个目标动作要求站定按住 3~5 秒，方案写死的失败条件只有一条：
            // 中途松手。它考的是"被注视、被说话时还能不能把手按住"，
            // 不是"挨揍的时候还能不能把手按住"——8.7 也把本章 Physical 上限压到 15%。
            // 没有这一条，玩家站着按键的那几秒会被围殴致死，于是三个目标动作
            // 一个都做不完，整套规则在实机上等于不存在。
            // 章外恒为 1（Holding 只有第八章的目标物会置位），对前七章零影响。
            if (Shame.ObjectiveStation.Holding) dmg *= 0.25f;

            float before = hp;
            hp = Mathf.Max(0, hp - dmg);

            // 濒死守护：从高于 1 血被一击打穿至 0 时，拦下这次死亡、留 1 点生命，
            // 并强制弹出「生命垂危」决策——保证严重警告一定出现在倒下之前，
            // 而不是被单次高伤害直接跳过垂危线（事件驱动，不依赖每帧轮询）。
            // 90 秒冷却一次：不是无敌，是最后一次选择的机会。
            if (hp <= 0 && before > 1f && Time.unscaledTime >= _nextLastStand)
            {
                hp = 1f;
                _nextLastStand = Time.unscaledTime + 90f;
                GameEvents.RaisePlayerHpChanged(hp, maxHp);
                GameEvents.RaiseSubtitle("濒死守护——这一击没能击倒你，但下一击可以。");
                GameEvents.RaiseLifeThreatened();
                return;
            }

            GameEvents.RaisePlayerHpChanged(hp, maxHp);
            // 掉血穿越垂危线：立即抛事件强制弹窗（轮询在两帧之间会漏掉快速穿越）
            if (!IsDead && maxHp > 0 &&
                hp / maxHp < CriticalHpRatio && before / maxHp >= CriticalHpRatio)
                GameEvents.RaiseLifeThreatened();
            if (IsDead) GameEvents.RaisePlayerDied("physical");
        }

        /// <summary>心理伤害按弱点轴落到对应属性。返回是否触发心理硬直。</summary>
        /// <summary>
        /// 心理韧性 [0,1]：面对言语攻击"还剩多少不为所动"。
        ///
        /// 玩家要的是这么一条曲线：一个人从脆弱走到强大，外部与内部的言语攻击
        /// 对他的影响越来越小——先是能量掉得越来越少，最后连身体反应都没有了。
        /// 所以这个数由三样东西合成，正好对应他说的三条：
        ///   · 当下的心气（五项心理能量的平均充盈度）——"玩家的能量很强"
        ///   · 走了多远（复盘点，通关与斩杀 Boss 累积而来）——"通关的几率越大"
        ///   · 这一下有多重（相对满值的占比）——"敌人的攻击力非常弱"
        /// 三项加权而不是取其一：只看进度会让新号在低血时毫无还手之力，
        /// 只看当下心气则打完一场硬仗立刻被杂兵说两句就崩，都不是那个意思。
        ///
        /// 韧性只削**心理**能量与身体反应，永远不碰生命值——生命值本来就不由
        /// 言语攻击扣（见 TakeMentalDamage 的分支，一条都没有动 hp）。
        /// </summary>
        public float MentalResilience(float incoming)
        {
            float reserve = 0.2f * (
                Frac(will, maxWill) + Frac(focus, maxFocus) +
                Frac(selfWorth, maxSelfWorth) + Frac(boundary, maxBoundary) +
                Frac(actionPower, maxActionPower));
            // 复盘点是通关与斩杀累积来的，拿它当"走了多远"的代理量。
            // 200 点封顶：到那时进度这一项已经给满，再往后靠心气与强弱差继续拉开。
            float progress = Mathf.Clamp01(Core.GrowthSystem.Points / 200f);
            // 这一下相对满值越轻，越不该有反应。12 点是一次普通言语攻击的量级。
            float weak = 1f - Mathf.Clamp01(incoming / 12f);
            return Mathf.Clamp01(0.45f * reserve + 0.35f * progress + 0.20f * weak);
        }

        static float Frac(float v, float max) => max > 0.01f ? Mathf.Clamp01(v / max) : 0f;

        public bool TakeMentalDamage(Personalization.WeaknessAxis axis, float dmg)
        {
            // 休养生息答题结束后的短暂护体：心理攻击无效（方案 V3.0：返回战斗给 2 秒保护）
            if (Core.QuizSystem.IsMentalShielded) return false;
            dmg *= Core.GrowthSystem.MentalTakenMult(axis);
            // 公平刺痛失控区（>70）判断下降：一切心理伤害吃得更重（方案三档规则）
            dmg *= FairnessMentalTakenMult;
            // 第八章 Exposure 联动（方案 8.3）：暴露度 ≥60 时打自尊的伤害 ×1.5，≥85 时 ×2.0。
            // 【放在这里而不是各个发招处】被看见会放大的是**一切**心理攻击，
            // 不只是指认招式；逐个调用点各乘一次，迟早会漏掉一处、或者乘两次。
            // 章外恒为 1，所以对前七章零影响。
            if (axis == Personalization.WeaknessAxis.Shame ||
                axis == Personalization.WeaknessAxis.LowConfidence ||
                axis == Personalization.WeaknessAxis.SelfDoubt ||
                axis == Personalization.WeaknessAxis.FailureFear)
                dmg *= Shame.ExposureSystem.SelfWorthDamageMultiplier();
            // 每次被心理攻击命中都会积累反刍——除非被言语攻防正确化解（那条路径不走这里）。
            // 激愤区（31-70）反刍风险更大：反刍累积额外 +50%
            AddRumination(dmg * (FairnessAgitated ? 0.6f : 0.4f));
            switch (axis)
            {
                case Personalization.WeaknessAxis.Procrastination:
                    actionPower = Mathf.Max(0, actionPower - dmg);
                    GameEvents.RaiseMentalStatChanged("actionPower", actionPower, maxActionPower);
                    return actionPower <= 0;
                case Personalization.WeaknessAxis.NoiseSensitivity:
                    focus = Mathf.Max(0, focus - dmg);
                    GameEvents.RaiseMentalStatChanged("focus", focus, maxFocus);
                    return focus <= 0;
                case Personalization.WeaknessAxis.Shame:
                case Personalization.WeaknessAxis.LowConfidence:
                case Personalization.WeaknessAxis.SelfDoubt:
                    selfWorth = Mathf.Max(0, selfWorth - dmg);
                    GameEvents.RaiseMentalStatChanged("selfWorth", selfWorth, maxSelfWorth);
                    return selfWorth <= 0;
                case Personalization.WeaknessAxis.FairnessSensitivity:
                    // 公平/不公类攻击（赖账、毁约、小题大做）：主升【公平刺痛值】，
                    // 并顺带削一点边界（被伤口占据也会松动边界）
                    AddFairnessPain(dmg);
                    boundary = Mathf.Max(0, boundary - dmg * 0.4f);
                    GameEvents.RaiseMentalStatChanged("boundary", boundary, maxBoundary);
                    return boundary <= 0 || FairnessRaging;
                case Personalization.WeaknessAxis.BoundaryConflict:
                    boundary = Mathf.Max(0, boundary - dmg);
                    GameEvents.RaiseMentalStatChanged("boundary", boundary, maxBoundary);
                    // 被索取/被转嫁责任的攻击同时消耗关系：边界受损的一半转为关系消耗
                    AddRelationshipDrain(dmg * 0.5f);
                    return boundary <= 0;
                case Personalization.WeaknessAxis.FailureFear:
                    // 旧事回声类攻击：一半打自尊、一半直接转成反刍（越想越回放）
                    selfWorth = Mathf.Max(0, selfWorth - dmg * 0.6f);
                    GameEvents.RaiseMentalStatChanged("selfWorth", selfWorth, maxSelfWorth);
                    AddRumination(dmg * 0.3f);
                    return selfWorth <= 0;
                default: // WillpowerCollapse / JobAnxiety 等
                    will = Mathf.Max(0, will - dmg);
                    GameEvents.RaiseMentalStatChanged("will", will, maxWill);
                    return will <= 0;
            }
        }

        public bool SpendStamina(float cost)
        {
            if (stamina < cost) return false;
            stamina -= cost;
            GameEvents.RaiseStaminaChanged(stamina, maxStamina);
            return true;
        }

        public bool SpendWill(float cost)
        {
            if (will < cost) return false;
            will -= cost;
            GameEvents.RaiseMentalStatChanged("will", will, maxWill);
            return true;
        }

        /// <summary>目标板蓄力 / 自我确认等恢复技能调用。</summary>
        public void RestoreMental(float amount)
        {
            will = Mathf.Min(maxWill, will + amount);
            focus = Mathf.Min(maxFocus, focus + amount);
            selfWorth = Mathf.Min(maxSelfWorth, selfWorth + amount);
            actionPower = Mathf.Min(maxActionPower, actionPower + amount);
            GameEvents.RaiseMentalStatChanged("will", will, maxWill);
            GameEvents.RaiseMentalStatChanged("focus", focus, maxFocus);
            GameEvents.RaiseMentalStatChanged("selfWorth", selfWorth, maxSelfWorth);
            GameEvents.RaiseMentalStatChanged("actionPower", actionPower, maxActionPower);
        }

        /// <summary>反刍值增减（可为负）。触发 UI 更新。</summary>
        public void AddRumination(float amount)
        {
            if (amount > 0) amount *= Core.GrowthSystem.RuminationGainMult();
            rumination = Mathf.Clamp(rumination + amount, 0, maxRumination);
            GameEvents.RaiseMentalStatChanged("rumination", rumination, maxRumination);
        }

        public void ReduceRumination(float amount) => AddRumination(-amount);

        /// <summary>关系消耗增减（可为负）。触发 UI 更新。</summary>
        public void AddRelationshipDrain(float amount)
        {
            if (amount > 0) amount *= Core.GrowthSystem.DrainGainMult();
            bool wasOver = IsOverDrained;
            relationshipDrain = Mathf.Clamp(relationshipDrain + amount, 0, maxRelationshipDrain);
            GameEvents.RaiseMentalStatChanged("relationshipDrain", relationshipDrain, maxRelationshipDrain);
            if (!wasOver && IsOverDrained)
                GameEvents.RaiseSubtitle("关系消耗过高——注意力与精力被掏空，技能调息变慢了。");
        }

        public void ReduceRelationshipDrain(float amount) => AddRelationshipDrain(-amount);

        /// <summary>言语攻防正确回击：把伤害本应削减的那条弱点属性回补一部分。</summary>
        public void RestoreAxis(Personalization.WeaknessAxis axis, float amount)
        {
            switch (axis)
            {
                case Personalization.WeaknessAxis.Procrastination:
                    actionPower = Mathf.Min(maxActionPower, actionPower + amount);
                    GameEvents.RaiseMentalStatChanged("actionPower", actionPower, maxActionPower);
                    break;
                case Personalization.WeaknessAxis.NoiseSensitivity:
                    focus = Mathf.Min(maxFocus, focus + amount);
                    GameEvents.RaiseMentalStatChanged("focus", focus, maxFocus);
                    break;
                case Personalization.WeaknessAxis.Shame:
                case Personalization.WeaknessAxis.LowConfidence:
                case Personalization.WeaknessAxis.SelfDoubt:
                case Personalization.WeaknessAxis.FailureFear:
                    selfWorth = Mathf.Min(maxSelfWorth, selfWorth + amount);
                    GameEvents.RaiseMentalStatChanged("selfWorth", selfWorth, maxSelfWorth);
                    break;
                case Personalization.WeaknessAxis.BoundaryConflict:
                    boundary = Mathf.Min(maxBoundary, boundary + amount);
                    GameEvents.RaiseMentalStatChanged("boundary", boundary, maxBoundary);
                    // 守住边界的正确回应，同时回落关系消耗
                    ReduceRelationshipDrain(amount * 0.6f);
                    break;
                case Personalization.WeaknessAxis.FairnessSensitivity:
                    // 用事实回应不公：主降【公平刺痛值】（记住事实、不被伤口困住），
                    // 同时回补一点边界
                    ReduceFairnessPain(amount * 1.2f);
                    boundary = Mathf.Min(maxBoundary, boundary + amount * 0.5f);
                    GameEvents.RaiseMentalStatChanged("boundary", boundary, maxBoundary);
                    break;
                default:
                    will = Mathf.Min(maxWill, will + amount);
                    GameEvents.RaiseMentalStatChanged("will", will, maxWill);
                    break;
            }
        }
    }
}
