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

        public float staminaRegenPerSec = 15f;
        public float mentalRegenPerSec = 2f;
        public float ruminationDecayPerSec = 1.5f;
        public float drainDecayPerSec = 1.2f;

        public bool IsDead => hp <= 0;

        /// <summary>关系消耗过高：技能冷却 ×1.5（见 SkillExecutor）。</summary>
        public bool IsOverDrained => relationshipDrain >= 70f;

        public void TickRegen(float dt, bool inCombat)
        {
            stamina = Mathf.Min(maxStamina, stamina + staminaRegenPerSec * dt);
            float m = (inCombat ? 0.3f : 1f) * mentalRegenPerSec * dt;
            will = Mathf.Min(maxWill, will + m);
            focus = Mathf.Min(maxFocus, focus + m);
            selfWorth = Mathf.Min(maxSelfWorth, selfWorth + m);
            boundary = Mathf.Min(maxBoundary, boundary + m);
            actionPower = Mathf.Min(maxActionPower, actionPower + m);
            if (!inCombat && rumination > 0)
                AddRumination(-ruminationDecayPerSec * dt);
            if (!inCombat && relationshipDrain > 0)
                AddRelationshipDrain(-drainDecayPerSec * dt);
        }

        public void TakePhysicalDamage(float dmg)
        {
            hp = Mathf.Max(0, hp - dmg);
            GameEvents.RaisePlayerHpChanged(hp, maxHp);
            if (IsDead) GameEvents.RaisePlayerDied("physical");
        }

        /// <summary>心理伤害按弱点轴落到对应属性。返回是否触发心理硬直。</summary>
        public bool TakeMentalDamage(Personalization.WeaknessAxis axis, float dmg)
        {
            // 休养生息答题结束后的短暂护体：心理攻击无效（方案 V3.0：返回战斗给 2 秒保护）
            if (Core.QuizSystem.IsMentalShielded) return false;
            dmg *= Core.GrowthSystem.MentalTakenMult(axis);
            // 每次被心理攻击命中都会积累反刍——除非被言语攻防正确化解（那条路径不走这里）。
            AddRumination(dmg * 0.4f);
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
                case Personalization.WeaknessAxis.BoundaryConflict:
                case Personalization.WeaknessAxis.FairnessSensitivity:
                    boundary = Mathf.Max(0, boundary - dmg);
                    GameEvents.RaiseMentalStatChanged("boundary", boundary, maxBoundary);
                    // 被索取/被转嫁责任的攻击同时消耗关系：边界受损的一半转为关系消耗
                    if (axis == Personalization.WeaknessAxis.BoundaryConflict)
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
                case Personalization.WeaknessAxis.FairnessSensitivity:
                    boundary = Mathf.Min(maxBoundary, boundary + amount);
                    GameEvents.RaiseMentalStatChanged("boundary", boundary, maxBoundary);
                    // 守住边界的正确回应，同时回落关系消耗
                    if (axis == Personalization.WeaknessAxis.BoundaryConflict)
                        ReduceRelationshipDrain(amount * 0.6f);
                    break;
                default:
                    will = Mathf.Min(maxWill, will + amount);
                    GameEvents.RaiseMentalStatChanged("will", will, maxWill);
                    break;
            }
        }
    }
}
