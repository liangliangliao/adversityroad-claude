namespace AdversityRoad.Core
{
    /// <summary>
    /// 调试开关（全局静态，运行时可切换）。默认开启"敌人耐揍"便于测试各种招式、
    /// 镜头、动画表现而不被敌人秒杀。正式发布把 TankyEnemies 设为 false 即可。
    /// 设置面板里有一个开关驱动它（见 SettingsPanel）。
    /// </summary>
    public static class GameDebug
    {
        /// <summary>敌人耐揍模式：大幅削减敌人受到的伤害，让其不易被打死（方便测试）。</summary>
        public static bool TankyEnemies = true;

        /// <summary>耐揍时敌人实际承受的伤害系数（越小越耐揍）。</summary>
        public const float TankyDamageScale = 0.1f;
    }
}
