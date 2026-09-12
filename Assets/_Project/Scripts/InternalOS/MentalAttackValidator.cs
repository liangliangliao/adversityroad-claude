using System.Collections.Generic;

namespace AdversityRoad.InternalOS
{
    /// <summary>一条校验结论。error=必须拦截；warning=可以入库但要在验收里看得见。</summary>
    public class MentalAttackIssue
    {
        public string levelId;
        public string eventId;
        public string code;
        public string message;
        public bool error;

        public override string ToString() =>
            (error ? "[错误] " : "[提醒] ") + (levelId ?? "") + " " + code + "：" + message;
    }

    /// <summary>
    /// 三选一的三道校验（增补章第 12 条：结构 Validator、安全 Validator、Mechanism Validator）。
    ///
    /// 【它同时管两件事，不要混淆】
    /// ① 策划 Canonical 90 条：入库体检，CI 里跑一遍就知道哪一关出了问题；
    /// ② AI 运行时生成物：必须逐条过这里才能替换 Canonical，不过就回退。
    /// 第二种场景下 <see cref="ValidateGenerated"/> 会额外要求"机制不得被改写"——
    /// AI 可以换措辞，但 best 指向的状态效果与 FollowUpAction 必须和策划稿一致。
    /// </summary>
    public static class MentalAttackValidator
    {
        // PRD 建议值：攻击句 10-36 汉字，选项 8-30 汉字（移动端可读）。
        // 注意量的是**汉字数**，不是 string.Length：这批文案里混着
        // Goal Action / SuccessCondition / Cue-Routine-Reward 这类机制术语，
        // 半角字符按一个汉字算会把它们判成"超长"，而屏幕上它们只占半格。
        public const int AttackLineMin = 10;
        public const int AttackLineMax = 36;
        public const int OptionMin = 8;
        public const int OptionMax = 30;
        /// <summary>best 比最长的干扰项还长这么多字，就成了"最长的那条是答案"。</summary>
        public const int LengthBiasChars = 6;

        /// <summary>显示宽度：汉字算 1，半角字符算 0.5——PRD 的"字"数的是汉字。</summary>
        public static float DisplayWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            float w = 0f;
            for (int i = 0; i < s.Length; i++) w += s[i] < 128 ? 0.5f : 1f;
            return w;
        }

        static MentalAttackIssue Err(MentalAttackChoiceEvent e, string code, string msg) =>
            new MentalAttackIssue { levelId = e != null ? e.levelId : "", eventId = e != null ? e.eventId : "",
                                    code = code, message = msg, error = true };

        static MentalAttackIssue Warn(MentalAttackChoiceEvent e, string code, string msg) =>
            new MentalAttackIssue { levelId = e != null ? e.levelId : "", eventId = e != null ? e.eventId : "",
                                    code = code, message = msg, error = false };

        /// <summary>结构校验：恰好 3 项、恰好 1 个 best、两个干扰项不同家族、必须有 Follow-up。</summary>
        public static void ValidateStructure(MentalAttackChoiceEvent e, List<MentalAttackIssue> into)
        {
            if (e == null) return;

            if (e.options.Count != 3)
                into.Add(Err(e, "OPT_COUNT", "选项数 " + e.options.Count + "，必须恰好 3 个。"));

            int bestCount = 0;
            for (int i = 0; i < e.options.Count; i++) if (e.options[i].IsBest) bestCount++;
            if (bestCount != 1)
                into.Add(Err(e, "BEST_COUNT", "标记为 best 的有 " + bestCount + " 个，必须恰好 1 个。"));

            var best = e.Best();
            if (best == null || e.bestOptionId != (best != null ? best.optionId : null))
                into.Add(Err(e, "BEST_ID", "bestOptionId 与被标记的 best 不一致。"));

            if (string.IsNullOrEmpty(e.followUpAction))
                into.Add(Err(e, "NO_FOLLOWUP",
                    "缺 Follow-up Action —— 答对本身不能通关，必须有一个随后要做的行为。"));

            if (string.IsNullOrEmpty(e.attackIntent))
                into.Add(Err(e, "NO_INTENT", "缺 attackIntent，AI 改写将失去边界。"));

            var families = new List<DistractorFamily>();
            for (int i = 0; i < e.options.Count; i++)
            {
                var o = e.options[i];
                if (string.IsNullOrEmpty(o.text))
                    into.Add(Err(e, "EMPTY_TEXT", "存在空选项。"));
                if (string.IsNullOrEmpty(o.immediateEffect))
                    into.Add(Err(e, "NO_EFFECT",
                        "选项「" + o.text + "」没有状态效果 —— 语言攻击不得只是字幕（第 12 条）。"));
                if (o.IsBest) continue;
                var f = o.Family();
                if (f == DistractorFamily.Unknown || f == DistractorFamily.Reasonable)
                    into.Add(Err(e, "FAMILY_UNKNOWN", "干扰项「" + o.text + "」没有有效的错误家族。"));
                else families.Add(f);
            }
            if (families.Count == 2 && families[0] == families[1])
                into.Add(Err(e, "FAMILY_SAME",
                    "两个干扰项同属 " + families[0] + " —— 同一句换词，三选一会退化成二选一。"));
        }

        /// <summary>
        /// 安全校验（增补章第 6 节）：不得定向羞辱真实第三方、不得写成人格/医学诊断、
        /// 不得生成报复或操控教程。这里只查最硬的那几条关键词，其余由 SafetyFilter 兜底。
        /// </summary>
        public static void ValidateSafety(MentalAttackChoiceEvent e, List<MentalAttackIssue> into)
        {
            if (e == null) return;

            string all = e.attackLine ?? "";
            for (int i = 0; i < e.options.Count; i++) all += "\n" + e.options[i].text;

            string[] diagnosis = { "确诊", "抑郁症", "焦虑症", "人格障碍", "你有病", "精神病", "自闭症" };
            for (int i = 0; i < diagnosis.Length; i++)
                if (all.Contains(diagnosis[i]))
                    into.Add(Err(e, "DIAGNOSIS",
                        "出现诊断式表述「" + diagnosis[i] + "」—— 只允许陈述可见行为证据。"));

            string[] harm = { "报复", "整死", "让他付出代价", "操控他", "骗他" };
            for (int i = 0; i < harm.Length; i++)
                if (all.Contains(harm[i]))
                    into.Add(Err(e, "HARM", "出现报复/操控导向表述「" + harm[i] + "」。"));

            if (e.safetyTags.Count == 0)
                into.Add(Warn(e, "NO_SAFETY_TAG", "没有安全标签，高风险场景将无法回退预置答案。"));

            if (e.aiGenerated && e.confidence < 0.7f)
                into.Add(Err(e, "LOW_CONFIDENCE",
                    "AI 置信度 " + e.confidence.ToString("F2") + " 低于阈值，必须回退策划 Canonical 版本。"));
        }

        /// <summary>
        /// 可读性提醒（增补章第 8 节的长度建议 + "不得把 best 写得明显更长"）。
        ///
        /// 这条单独拆出来是因为它**不阻断入库**，但它是这套系统最容易漏的破绽：
        /// 只要 best 恒定是最长的那条，玩家不读内容也能连对 90 关，
        /// 三选一就从"行为反制窗口"变回了"找最长的那行"。
        /// </summary>
        public static void ValidateReadability(MentalAttackChoiceEvent e, List<MentalAttackIssue> into)
        {
            if (e == null) return;

            float len = DisplayWidth(e.attackLine);
            if (len < AttackLineMin || len > AttackLineMax)
                into.Add(Warn(e, "LINE_LEN",
                    "攻击句 " + len.ToString("0.#") + " 字，建议 " + AttackLineMin + "-" + AttackLineMax + " 字。"));

            float bestLen = 0f, maxDistractor = 0f;
            for (int i = 0; i < e.options.Count; i++)
            {
                var o = e.options[i];
                float ol = DisplayWidth(o.text);
                if (ol < OptionMin || ol > OptionMax)
                    into.Add(Warn(e, "OPT_LEN",
                        "选项「" + o.text + "」" + ol.ToString("0.#") + " 字，建议 " +
                        OptionMin + "-" + OptionMax + " 字。"));
                if (o.IsBest) bestLen = ol;
                else if (ol > maxDistractor) maxDistractor = ol;
            }

            if (bestLen - maxDistractor >= LengthBiasChars)
                into.Add(Warn(e, "BEST_TOO_LONG",
                    "best 比最长的干扰项多 " + (bestLen - maxDistractor).ToString("0.#") +
                    " 字 —— 不读内容也能靠长度挑中答案。"));
        }

        /// <summary>机制校验：AI 生成物不得改写策划定义的机制。</summary>
        public static void ValidateMechanism(MentalAttackChoiceEvent generated,
            MentalAttackChoiceEvent canonical, List<MentalAttackIssue> into)
        {
            if (generated == null || canonical == null) return;

            if (generated.levelId != canonical.levelId)
                into.Add(Err(generated, "LEVEL_MISMATCH", "生成物的 levelId 与策划稿不一致。"));

            if (generated.attackIntent != canonical.attackIntent)
                into.Add(Err(generated, "INTENT_CHANGED",
                    "attackIntent 被改写 —— AI 只允许改措辞与语境。"));

            if (generated.followUpAction != canonical.followUpAction)
                into.Add(Err(generated, "FOLLOWUP_CHANGED",
                    "Follow-up Action 被改写 —— 真正的通关行为不归 AI 决定。"));

            var gb = generated.Best();
            var cb = canonical.Best();
            if (gb != null && cb != null && gb.immediateEffect != cb.immediateEffect)
                into.Add(Err(generated, "EFFECT_CHANGED",
                    "best 的状态效果被改写 —— 真弱点/命门不允许因措辞变化而改变。"));
        }

        /// <summary>单条事件的完整体检（策划稿用）。</summary>
        public static List<MentalAttackIssue> Validate(MentalAttackChoiceEvent e)
        {
            var issues = new List<MentalAttackIssue>();
            ValidateStructure(e, issues);
            ValidateSafety(e, issues);
            ValidateReadability(e, issues);
            return issues;
        }

        /// <summary>AI 生成物的完整体检。任何 error 都意味着回退 canonical。</summary>
        public static List<MentalAttackIssue> ValidateGenerated(MentalAttackChoiceEvent generated,
            MentalAttackChoiceEvent canonical)
        {
            var issues = Validate(generated);
            ValidateMechanism(generated, canonical, issues);
            return issues;
        }

        public static bool HasError(List<MentalAttackIssue> issues)
        {
            for (int i = 0; i < issues.Count; i++) if (issues[i].error) return true;
            return false;
        }

        /// <summary>
        /// 全量体检：90 关是否都有 Profile，每条事件是否都过结构与安全校验。
        /// CI 与验收面板共用。
        /// </summary>
        public static List<MentalAttackIssue> ValidateAll()
        {
            var issues = new List<MentalAttackIssue>();

            var levels = InternalChapterCatalog.AllLevels();
            for (int i = 0; i < levels.Count; i++)
            {
                if (MentalAttackCatalog.HasProfile(levels[i].levelId)) continue;
                issues.Add(new MentalAttackIssue
                {
                    levelId = levels[i].levelId, code = "NO_PROFILE", error = true,
                    message = "这一关没有任何 Mental Attack 事件 —— 第 12 条不允许空关。"
                });
            }

            var events = MentalAttackCatalog.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                ValidateStructure(e, issues);
                ValidateSafety(e, issues);
                ValidateReadability(e, issues);
                if (InternalChapterCatalog.Level(e.levelId) == null)
                    issues.Add(Err(e, "ORPHAN", "事件指向的关卡 " + e.levelId + " 不在 90 关目录里。"));
            }

            return issues;
        }
    }
}
