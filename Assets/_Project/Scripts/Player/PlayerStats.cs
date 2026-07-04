using System;
using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 玩家七维属性：生命、体力 + 五维心理属性（意志/专注/自尊/边界/决断）。
    /// 心理属性归零不直接死亡，而是触发对应的"心理硬直"状态。
    /// </summary>
    [Serializable]
    public class PlayerStats
    {
        public float hp = 100, maxHp = 100;
        public float stamina = 100, maxStamina = 100;
        public float will = 100, maxWill = 100;           // 意志：抵抗心理攻击、释放内功
        public float focus = 100, maxFocus = 100;         // 专注：抵抗噪声、凝视
        public float selfWorth = 100, maxSelfWorth = 100; // 自尊：抵抗羞耻、否定
        public float boundary = 100, maxBoundary = 100;   // 边界：抵抗操控、责任转嫁
        public float resolve = 100, maxResolve = 100;     // 决断：抵抗拖延、行动瘫痪

        // 反刍值：与其它属性相反——越高越糟。未被回应/被错误回应的言语攻击会累积反刍，
        // 满值触发旧事回声；战后复盘（归档）可清零。战斗外缓慢消退，避免软锁。
        public float rumination = 0, maxRumination = 100;

        public float staminaRegenPerSec = 15f;
        public float mentalRegenPerSec = 2f;
        public float ruminationDecayPerSec = 1.5f;

        // 反刍累积倍率（由装备套装设置）：公平复盘套/旧我整合套让刺痛更难转化为反刍。
        public float ruminationGainMult = 1f;

        public bool IsDead => hp <= 0;

        public void TickRegen(float dt, bool inCombat)
        {
            stamina = Mathf.Min(maxStamina, stamina + staminaRegenPerSec * dt);
            float m = (inCombat ? 0.3f : 1f) * mentalRegenPerSec * dt;
            will = Mathf.Min(maxWill, will + m);
            focus = Mathf.Min(maxFocus, focus + m);
            selfWorth = Mathf.Min(maxSelfWorth, selfWorth + m);
            boundary = Mathf.Min(maxBoundary, boundary + m);
            resolve = Mathf.Min(maxResolve, resolve + m);
            if (!inCombat && rumination > 0)
                AddRumination(-ruminationDecayPerSec * dt);
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
            // 每次被心理攻击命中都会积累反刍——除非被言语攻防正确化解（那条路径不走这里）。
            // 装备套装（公平复盘/旧我整合）可降低这一累积倍率。
            AddRumination(dmg * 0.4f * ruminationGainMult);
            switch (axis)
            {
                case Personalization.WeaknessAxis.Procrastination:
                case Personalization.WeaknessAxis.WillpowerCollapse:
                    resolve = Mathf.Max(0, resolve - dmg);
                    GameEvents.RaiseMentalStatChanged("resolve", resolve, maxResolve);
                    return resolve <= 0;
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
                    return boundary <= 0;
                default:
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
            resolve = Mathf.Min(maxResolve, resolve + amount);
        }

        /// <summary>反刍值增减（可为负）。触发 UI 更新。</summary>
        public void AddRumination(float amount)
        {
            rumination = Mathf.Clamp(rumination + amount, 0, maxRumination);
            GameEvents.RaiseMentalStatChanged("rumination", rumination, maxRumination);
        }

        public void ReduceRumination(float amount) => AddRumination(-amount);

        /// <summary>言语攻防正确回击：把伤害本应削减的那条弱点属性回补一部分。</summary>
        public void RestoreAxis(Personalization.WeaknessAxis axis, float amount)
        {
            switch (axis)
            {
                case Personalization.WeaknessAxis.Procrastination:
                case Personalization.WeaknessAxis.WillpowerCollapse:
                    resolve = Mathf.Min(maxResolve, resolve + amount);
                    GameEvents.RaiseMentalStatChanged("resolve", resolve, maxResolve);
                    break;
                case Personalization.WeaknessAxis.NoiseSensitivity:
                    focus = Mathf.Min(maxFocus, focus + amount);
                    GameEvents.RaiseMentalStatChanged("focus", focus, maxFocus);
                    break;
                case Personalization.WeaknessAxis.Shame:
                case Personalization.WeaknessAxis.LowConfidence:
                case Personalization.WeaknessAxis.SelfDoubt:
                    selfWorth = Mathf.Min(maxSelfWorth, selfWorth + amount);
                    GameEvents.RaiseMentalStatChanged("selfWorth", selfWorth, maxSelfWorth);
                    break;
                case Personalization.WeaknessAxis.BoundaryConflict:
                case Personalization.WeaknessAxis.FairnessSensitivity:
                    boundary = Mathf.Min(maxBoundary, boundary + amount);
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
