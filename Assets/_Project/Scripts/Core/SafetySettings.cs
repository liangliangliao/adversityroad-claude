using UnityEngine;

namespace AdversityRoad.Core
{
    public enum MentalIntensity { Light = 0, Standard = 1, HighPressure = 2 }
    public enum RealityCloseness { Abstract = 0, SemiClose = 1, Close = 2 }

    /// <summary>
    /// 心理安全设置：产品级功能而非补丁。
    /// 所有心理攻击、台词生成、场景匹配都必须读取本设置。
    /// </summary>
    [CreateAssetMenu(menuName = "AdversityRoad/SafetySettings")]
    public class SafetySettings : ScriptableObject
    {
        public MentalIntensity intensity = MentalIntensity.Standard;
        public RealityCloseness closeness = RealityCloseness.SemiClose;

        [Tooltip("玩家禁用的主题标签，例如：家庭、债务、羞耻")]
        public string[] disabledThemes;

        [Tooltip("台词柔化：降低攻击性表达")]
        public bool softenDialogue = true;

        [Tooltip("恢复模式下停止一切心理攻击")]
        public bool recoveryMode = false;

        /// <summary>心理伤害全局倍率：轻度0.5 / 标准1.0 / 高压1.3，恢复模式归零。</summary>
        public float MentalDamageMultiplier()
        {
            if (recoveryMode) return 0f;
            switch (intensity)
            {
                case MentalIntensity.Light: return 0.5f;
                case MentalIntensity.HighPressure: return 1.3f;
                default: return 1.0f;
            }
        }

        public bool IsThemeDisabled(string theme)
        {
            if (disabledThemes == null) return false;
            foreach (var t in disabledThemes)
                if (!string.IsNullOrEmpty(t) && t == theme) return true;
            return false;
        }
    }
}
