using System.Collections.Generic;
using AdversityRoad.Personalization;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 言语攻防·玩家回应库：面对敌人的言语攻击，给出一句"最优回击"（事实/边界/不读心/
    /// 注意力回收/行动）与两句"无效回应"（互骂/退缩/反刍）。玩家选对削弱敌人并回补属性，
    /// 选错或沉默则把自己拖进反刍。所有回应都抽象化、服务战斗机制，绝不输出现实操控话术。
    /// </summary>
    public static class ResponseLibrary
    {
        /// <summary>各弱点轴的"最优回击"——短句，克制对应的言语攻击。</summary>
        static readonly Dictionary<WeaknessAxis, string> BestResponse = new Dictionary<WeaknessAxis, string>
        {
            { WeaknessAxis.Procrastination,     "先做五分钟，动起来再说。" },
            { WeaknessAxis.LowConfidence,       "不用够到全部，先够到下一步。" },
            { WeaknessAxis.NoiseSensitivity,    "我听见了，但我不跟随。" },
            { WeaknessAxis.Shame,               "被看见，不等于被否定。" },
            { WeaknessAxis.JobAnxiety,          "没有回应不代表没有价值，我再来一次。" },
            { WeaknessAxis.BoundaryConflict,    "帮助有边界，这次你自己承担。" },
            { WeaknessAxis.FairnessSensitivity, "我记住事实，但不被它困住。" },
            { WeaknessAxis.SelfDoubt,           "怀疑可以在，我照样行动。" },
            { WeaknessAxis.FailureFear,         "失败是事实，不是身份。" },
            { WeaknessAxis.WillpowerCollapse,   "我害怕，但我继续向前。" },
        };

        /// <summary>无效回应池（互骂/退缩/过度辩护/反刍）——短期看似解气，实则累积反刍。</summary>
        static readonly string[] WeakResponses =
        {
            "你才有问题，凭什么说我。",
            "算了……也许你说得对。",
            "我不是那样的，我可以解释……",
            "为什么总是我，这不公平。",
            "你懂什么，别管我。",
        };

        /// <summary>敌人被正确回击后的语塞台词。</summary>
        static readonly string[] BrokenLines =
        {
            "我……", "你、你怎么变了。", "这次……不好使了。", "哼，算你狠。", "……无话可说。",
        };

        static readonly System.Random Rng = new System.Random();

        public static string GetBest(WeaknessAxis axis) =>
            BestResponse.TryGetValue(axis, out var s) ? s : "这不是我的责任，我把注意力收回来。";

        public static string GetBrokenLine() => BrokenLines[Rng.Next(BrokenLines.Length)];

        /// <summary>
        /// 生成一次三选一：一句最优回击 + 两句无效回应，随机打乱。
        /// 返回选项文本、正确项下标、最优回击原文（用于胜利字幕）。
        /// </summary>
        public static (string[] options, int correctIndex, string bestLine) GetChoices(WeaknessAxis axis)
        {
            string best = GetBest(axis);

            // 取两句互不相同的无效回应
            int a = Rng.Next(WeakResponses.Length);
            int b = Rng.Next(WeakResponses.Length);
            while (b == a) b = Rng.Next(WeakResponses.Length);

            var opts = new List<string> { best, WeakResponses[a], WeakResponses[b] };

            // 洗牌，记录 best 落到哪个下标
            for (int i = opts.Count - 1; i > 0; i--)
            {
                int j = Rng.Next(i + 1);
                (opts[i], opts[j]) = (opts[j], opts[i]);
            }
            int correct = opts.IndexOf(best);
            return (opts.ToArray(), correct, best);
        }
    }
}
