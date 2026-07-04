using System;
using System.Collections.Generic;
using AdversityRoad.Core;
using AdversityRoad.Personalization;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 敌人台词库：按弱点轴 + 场景组合恶意低语（抽象化游戏机制表达，
    /// 不输出可复制到现实的操控话术）。玩家配置的 AI 提示词短句会混入台词池。
    /// 云端 LLM 生成挂点：设置 CloudProvider 委托即可整体替换生成逻辑。
    /// </summary>
    public static class DialogueLibrary
    {
        /// <summary>云端生成挂点：(axis, zoneId, globalPrompt, scenePrompt) → 台词。为 null 时用本地模板。</summary>
        public static Func<WeaknessAxis, string, string, string, string> CloudProvider;

        static readonly Dictionary<WeaknessAxis, string[]> AxisLines = new Dictionary<WeaknessAxis, string[]>
        {
            { WeaknessAxis.Procrastination, new[]
                { "明天再开始，也不迟……对吧？", "反正你也坚持不了几天。", "躺下吧，没人会怪你。",
                  "你上次立的目标，现在在哪里？", "急什么？时间还多得很……" } },
            { WeaknessAxis.NoiseSensitivity, new[]
                { "听，他们在议论你。", "那声咳嗽，是冲你来的。", "所有人的眼睛都在看你。",
                  "这么点动静就分心了？" } },
            { WeaknessAxis.SelfDoubt, new[]
                { "你真的觉得自己可以？", "换个人，早就做到了。", "又是三分钟热度罢了。",
                  "你心里其实也不信，对吧？" } },
            { WeaknessAxis.Shame, new[]
                { "站到光下来，让大家看看你。", "他们还记得你出丑的样子。", "别抬头，你不敢的。" } },
            { WeaknessAxis.LowConfidence, new[]
                { "目标定得再高，你也够不到。", "放弃吧，这不适合你。" } },
            { WeaknessAxis.JobAnxiety, new[]
                { "投出去的简历，石沉大海。", "没有回应，永远没有回应。" } },
            { WeaknessAxis.BoundaryConflict, new[]
                { "你不好意思拒绝的，一直都是。", "退一步吧，你习惯了。",
                  "这也是你的责任。", "你不管，就是你不够意思。", "你有能力，就该替他扛。",
                  "就这一次，你先替他承担一下。" } },
            { WeaknessAxis.FairnessSensitivity, new[]
                { "这点小事，你也要计较责任？", "谁对谁错，说不清的，各退一步吧。",
                  "别追究了，追究显得你小气。" } },
            { WeaknessAxis.WillpowerCollapse, new[]
                { "撑不住就松手，多轻松。", "你的意志，比纸还薄。" } },
        };

        static readonly Dictionary<string, string[]> ZoneLines = new Dictionary<string, string[]>
        {
            { "home",   new[] { "这间屋子，就是你的全世界了。", "桌上的计划落灰了。" } },
            { "dojo",   new[] { "练得再多，也改不了你。", "花架子罢了。" } },
            { "street", new[] { "街上每个人都比你从容。", "缩回去吧，这里不欢迎你。" } },
            { "job",    new[] { "投出去的，都石沉大海。", "这扇门，不会为你开。", "已读，不回。" } },
            { "plaza",  new[] { "整座城市都在等着看你失败。", "你走不到终点的。" } },
            { "court",  new[] { "这也是你的责任。", "全都怪你，签字画押吧。", "你不背，谁背？" } },
        };

        static readonly Random Rng = new Random();

        public static string GetTaunt(WeaknessAxis axis, string zoneId)
        {
            var cfg = AIPromptConfig.Load();
            string global = cfg.globalPrompt;
            string scene = cfg.GetScenePrompt(zoneId);

            // 云端台词池：预取缓存即取即用，池空时无缝回退本地模板（零延迟）
            if (CloudDialogueService.Instance != null &&
                CloudDialogueService.Instance.TryGetLine(axis, zoneId, out string cloudLine))
                return cloudLine;

            if (CloudProvider != null)
            {
                try
                {
                    string cloud = CloudProvider(axis, zoneId, global, scene);
                    if (!string.IsNullOrEmpty(cloud)) return cloud;
                }
                catch { /* 云端失败回退本地 */ }
            }

            var pool = new List<string>();
            if (AxisLines.TryGetValue(axis, out var lines)) pool.AddRange(lines);
            if (ZoneLines.TryGetValue(zoneId, out var zl)) pool.AddRange(zl);
            AddPromptPhrases(pool, scene);
            AddPromptPhrases(pool, global);
            if (pool.Count == 0) return "……";
            return pool[Rng.Next(pool.Count)];
        }

        /// <summary>把玩家提示词按标点拆成短句混入台词池（玩家自定义的敌方恶意言语）。</summary>
        static void AddPromptPhrases(List<string> pool, string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return;
            foreach (var raw in prompt.Split('；', ';', '，', ',', '。', '\n'))
            {
                var s = raw.Trim();
                if (s.Length >= 2) pool.Add(s);
            }
        }
    }
}
