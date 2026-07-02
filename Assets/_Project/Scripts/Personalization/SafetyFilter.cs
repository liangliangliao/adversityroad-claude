using System.Text.RegularExpressions;

namespace AdversityRoad.Personalization
{
    /// <summary>
    /// 安全过滤器：在任何分析之前先去识别化。
    /// 移除手机号、身份证号、邮箱、URL；原文分析后即弃，不落盘。
    /// </summary>
    public static class SafetyFilter
    {
        public static string Anonymize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string s = raw;
            s = Regex.Replace(s, @"1[3-9]\d{9}", "[号码]");
            s = Regex.Replace(s, @"\d{17}[\dXx]", "[证件]");
            s = Regex.Replace(s, @"[\w.+-]+@[\w-]+\.[\w.]+", "[邮箱]");
            s = Regex.Replace(s, @"https?://\S+", "[链接]");
            return s;
        }

        /// <summary>检查主题是否被玩家禁用（读取画像 avoidedTopics + 安全设置）。</summary>
        public static bool IsTopicAllowed(string topicTag, PlayerProfile profile, Core.SafetySettings safety)
        {
            if (profile != null && profile.avoidedTopics.Contains(topicTag)) return false;
            if (safety != null && safety.IsThemeDisabled(topicTag)) return false;
            return true;
        }
    }
}
