using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AdversityRoad.Core
{
    [Serializable]
    public class AiQuizEntry
    {
        public QuizQuestion question;
        public string status;    // temp（低风险·临时可用）/ highrisk（待人工复核·不可用）/ approved（已入长期正式题库）
        public string riskNote;  // 高风险命中的关键词说明
        public string createdAt; // 生成时间（本地展示用）
    }

    [Serializable]
    class AiQuizStoreData
    {
        public List<AiQuizEntry> entries = new List<AiQuizEntry>();
        public int nextId = 1;
    }

    /// <summary>
    /// AI 自动命题的题库与审核状态机（方案 V3.0 第四十六节·来源约束升级版）：
    /// 每道 AI 候选题依次经过——结构校验（恰好 3 个不同选项、单一 correctIndex、字段齐全）、
    /// 来源校验（sourceTags 必须落在来源矩阵 HPP-L01—23 / DDJ-01—81 / CXL·SEQ 条目内，
    /// 否则按 SOURCE_GAP 拒绝）、语义去重（与固定题库及已有 AI 题去重）、风险分级
    /// （涉及现实威胁、贫困、病房、创伤、人身安全的题 100% 人工复核）。
    ///
    /// 通过全部自动校验的低风险题标记为 temp——立即进入临时池参与抽题；
    /// 人工审核只决定能否进入长期正式题库：通过 → approved（即使关闭 AI 命题也保留使用），
    /// 否决 → 删除（同时移出临时池）。高风险题在人工通过前一律不可用。
    /// 本地文件持久化，可随存档一键删除。
    /// </summary>
    public static class QuizAiBank
    {
        public const string StatusTemp = "temp";
        public const string StatusHighRisk = "highrisk";
        public const string StatusApproved = "approved";

        static string FilePath => Application.persistentDataPath + "/ai_quiz_bank.json";
        static AiQuizStoreData _d;

        // 训练形态白名单（缺失或不合法时回退为情境应用）
        static readonly string[] ValidTypes = { "原则理解", "情境应用", "日损辨识", "迁移结果", "恢复提示" };

        // 来源矩阵合法标签（方案：不得捏造不存在的讲次、章次、条目）。
        // SEQ-序 是固定题库实际使用的《传习录》序言条目标签。
        static readonly Regex SourceTagPattern = new Regex(
            @"^(HPP-L(0[1-9]|1\d|2[0-3])|DDJ-(0[1-9]|[1-7]\d|8[01])|CXL-\d{1,3}|SEQ-(\d{1,3}|序))$");

        // 高风险主题关键词（方案：涉及现实威胁、贫困、病房、创伤和人身安全的题 100% 人工复核）
        static readonly string[] HighRiskKeywords =
        {
            "威胁", "人身安全", "贫困", "病房", "医院", "创伤",
            "自杀", "自残", "自伤", "轻生", "暴力", "殴打", "家暴", "虐待", "报警", "死亡"
        };

        static AiQuizStoreData D()
        {
            if (_d == null)
            {
                try
                {
                    if (File.Exists(FilePath))
                        _d = JsonUtility.FromJson<AiQuizStoreData>(File.ReadAllText(FilePath));
                }
                catch { /* 文件损坏时回退空库 */ }
                if (_d == null) _d = new AiQuizStoreData();
            }
            return _d;
        }

        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(D())); }
            catch { /* 磁盘异常不致命 */ }
        }

        // ===================== 查询 =====================

        /// <summary>
        /// 当前可参与抽题的 AI 题：approved 永远可用（已是长期正式题库的一部分）；
        /// temp 仅在 AI 自动命题开启期间可用（关闭即回收临时池，等待人工定夺）。
        /// </summary>
        public static List<QuizQuestion> Usable(string chapterId, bool aiQuizEnabled)
        {
            var list = new List<QuizQuestion>();
            foreach (var e in D().entries)
            {
                if (e.question == null || e.question.chapterId != chapterId) continue;
                if (e.status == StatusApproved || (aiQuizEnabled && e.status == StatusTemp))
                    list.Add(e.question);
            }
            return list;
        }

        /// <summary>待人工审核的条目（temp 已在临时使用、highrisk 未启用），审核面板逐条展示。</summary>
        public static List<AiQuizEntry> PendingReview()
        {
            var list = new List<AiQuizEntry>();
            foreach (var e in D().entries)
                if (e.status == StatusTemp || e.status == StatusHighRisk) list.Add(e);
            return list;
        }

        public static int CountOf(string status)
        {
            int n = 0;
            foreach (var e in D().entries) if (e.status == status) n++;
            return n;
        }

        public static string StatusOf(string questionId)
        {
            foreach (var e in D().entries)
                if (e.question != null && e.question.questionId == questionId) return e.status;
            return null;
        }

        // ===================== 自动校验入库 =====================

        /// <summary>
        /// AI 候选题送审：全部自动校验通过后立即入库——低风险 → temp（临时可用），
        /// 高风险 → highrisk（等待人工）。失败返回 false 并给出拒绝原因（写入 AI 日志）。
        /// </summary>
        public static bool TryAdd(QuizQuestion q, string expectChapterId, out string verdict)
        {
            if (q == null) { verdict = "空题目"; return false; }

            // —— 结构校验：字段齐全、恰好 3 个互不相同的选项、单一合法 correctIndex ——
            if (string.IsNullOrWhiteSpace(q.question)) { verdict = "结构：缺题干"; return false; }
            if (q.options == null || q.options.Length != 3) { verdict = "结构：选项数不是 3"; return false; }
            for (int i = 0; i < 3; i++)
                if (string.IsNullOrWhiteSpace(q.options[i])) { verdict = "结构：存在空选项"; return false; }
            if (Norm(q.options[0]) == Norm(q.options[1]) || Norm(q.options[0]) == Norm(q.options[2]) ||
                Norm(q.options[1]) == Norm(q.options[2])) { verdict = "结构：选项重复"; return false; }
            if (q.correctIndex < 0 || q.correctIndex > 2) { verdict = "结构：correctIndex 非法"; return false; }
            if (string.IsNullOrWhiteSpace(q.rationale) || q.rationale.Trim().Length < 10)
            { verdict = "结构：依据缺失或过短（需说明正确项理由与干扰项偏差）"; return false; }

            // —— 来源校验：标签必须落在来源矩阵内，凭空来源按 SOURCE_GAP 拒绝 ——
            if (q.sourceTags == null || q.sourceTags.Length == 0)
            { verdict = "SOURCE_GAP：无来源标签"; return false; }
            foreach (var t in q.sourceTags)
                if (string.IsNullOrWhiteSpace(t) || !SourceTagPattern.IsMatch(t.Trim()))
                { verdict = "SOURCE_GAP：非法来源标签「" + t + "」"; return false; }

            // —— 语义去重：与固定题库及已有 AI 题（含已否决重生成的差异容忍）按归一化题干去重 ——
            string norm = Norm(q.question);
            foreach (var f in QuizSystem.ChapterQuestions(expectChapterId))
                if (Norm(f.question) == norm) { verdict = "去重：与固定题库重复"; return false; }
            foreach (var e in D().entries)
                if (e.question != null && Norm(e.question.question) == norm)
                { verdict = "去重：与已有 AI 题重复"; return false; }

            // —— 归一化修正：章节强制为请求章节；形态/难度/来源类型回填合法值 ——
            q.chapterId = expectChapterId;
            q.difficulty = Mathf.Clamp(q.difficulty <= 0 ? 2 : q.difficulty, 1, 3);
            if (Array.IndexOf(ValidTypes, q.type) < 0) q.type = "情境应用";
            if (string.IsNullOrWhiteSpace(q.sourceType))
                q.sourceType = q.sourceTags.Length > 1 ? "CROSS_SOURCE_SYNTHESIS" : "SINGLE_SOURCE";
            if (string.IsNullOrWhiteSpace(q.sceneTag)) q.sceneTag = "当前章节";
            if (string.IsNullOrWhiteSpace(q.conceptTag)) q.conceptTag = "常识提醒";
            if (q.energyTags == null) q.energyTags = new string[0];

            // AI 题一律重新编号，保证与固定题库及历史记录不冲突
            var d = D();
            q.questionId = "AI-" + expectChapterId + "-" + (d.nextId++).ToString("D4");

            // —— 风险分级：高风险题 100% 人工复核后才可用；低风险题立即临时可用 ——
            string risk = RiskScan(q);
            var entry = new AiQuizEntry
            {
                question = q,
                status = risk == null ? StatusTemp : StatusHighRisk,
                riskNote = risk ?? "",
                createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };
            d.entries.Add(entry);
            Save();
            verdict = risk == null
                ? "通过自动校验 → 临时可用（待人工定长期入库）"
                : "通过自动校验，但命中高风险主题「" + risk + "」→ 待人工复核后才可用";
            return true;
        }

        /// <summary>扫描题干/选项/依据中的高风险主题；返回命中的关键词（null = 低风险）。</summary>
        static string RiskScan(QuizQuestion q)
        {
            var sb = new StringBuilder(q.question).Append(q.rationale);
            foreach (var o in q.options) sb.Append(o);
            string all = sb.ToString();
            foreach (var k in HighRiskKeywords)
                if (all.Contains(k)) return k;
            return null;
        }

        /// <summary>归一化：去空白与常见标点后比较，用于语义去重。</summary>
        static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c) && c != '　')
                    sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        // ===================== 人工审核 =====================

        /// <summary>人工通过：进入长期正式题库（此后不受 AI 开关影响，持续参与抽题）。</summary>
        public static bool Approve(string questionId)
        {
            foreach (var e in D().entries)
                if (e.question != null && e.question.questionId == questionId)
                {
                    e.status = StatusApproved;
                    Save();
                    return true;
                }
            return false;
        }

        /// <summary>人工否决：彻底删除（临时使用中的题同时移出临时池）。</summary>
        public static bool Reject(string questionId)
        {
            var d = D();
            for (int i = 0; i < d.entries.Count; i++)
                if (d.entries[i].question != null && d.entries[i].question.questionId == questionId)
                {
                    d.entries.RemoveAt(i);
                    Save();
                    return true;
                }
            return false;
        }

        public static void DeleteAll()
        {
            _d = null;
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch { }
        }
    }
}
