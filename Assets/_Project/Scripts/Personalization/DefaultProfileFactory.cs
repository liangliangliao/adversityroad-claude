namespace AdversityRoad.Personalization
{
    /// <summary>
    /// 默认人生模板画像：基于方案第 3 节的十条困境轴构建。
    /// 注意：这是抽象化的困境原型，不含任何真实人物/地点/事件细节。
    /// </summary>
    public static class DefaultProfileFactory
    {
        public static PlayerProfile CreateDefaultLifeTemplate()
        {
            var p = new PlayerProfile { playerId = "default_template" };
            p.SetWeaknessScore(WeaknessAxis.Procrastination, 0.92f, "目标设定后迟迟不开始，缺乏紧迫感");
            p.SetWeaknessScore(WeaknessAxis.LowConfidence, 0.81f, "设定目标后不相信自己能实现");
            p.SetWeaknessScore(WeaknessAxis.JobAnxiety, 0.78f, "求职投递焦虑，定位不清");
            p.SetWeaknessScore(WeaknessAxis.NoiseSensitivity, 0.74f, "易被言语、眼神、噪声干扰");
            p.SetWeaknessScore(WeaknessAxis.Shame, 0.70f, "对被评价和被看不起敏感");
            p.SetWeaknessScore(WeaknessAxis.BoundaryConflict, 0.68f, "难以面对阻挠、欺负与承诺违约");
            p.SetWeaknessScore(WeaknessAxis.FairnessSensitivity, 0.66f, "对公平与责任高度敏感");
            p.SetWeaknessScore(WeaknessAxis.SelfDoubt, 0.65f, "容易自我怀疑");
            p.SetWeaknessScore(WeaknessAxis.FailureFear, 0.60f, "经历过低谷与自律崩塌");
            p.SetWeaknessScore(WeaknessAxis.WillpowerCollapse, 0.55f, "意志力在压力下波动");
            p.lifeThemes.Add("逆袭"); p.lifeThemes.Add("意志力"); p.lifeThemes.Add("重新站起");
            p.preferredGrowthThemes.Add("决断"); p.preferredGrowthThemes.Add("专注");
            p.preferredGrowthThemes.Add("边界"); p.preferredGrowthThemes.Add("勇气");
            p.unlockedSceneIds.Add("SC_HomeRoom");
            p.unlockedSceneIds.Add("SC_TrainingDojo");
            return p;
        }
    }
}
