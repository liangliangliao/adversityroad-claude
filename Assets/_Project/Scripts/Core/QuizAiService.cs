using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AdversityRoad.Core
{
    [System.Serializable]
    class QuizAiBatch
    {
        public QuizQuestion[] items;
    }

    /// <summary>
    /// AI 自动命题服务（方案 V3.0 第四十六节·来源约束升级版）：
    /// 复用云端 LLM 配置（AIPromptConfig），按「动态题源选择逻辑」组装上下文——
    /// 当前章节、代表场景、玩家错题概念、当前失衡能量——请求一批候选题（仅输出 JSON 数组）。
    /// 每道候选题经 QuizAiBank 自动校验（结构/来源/单一答案/去重/风险分级）：
    /// 低风险题立即临时可用；高风险题与长期入库一律由人工审核（QuizReviewPanel）定夺。
    /// 当前章节可用 AI 题不足时后台自动补充；全过程写入 AI 调用日志（「日志」面板可查）。
    /// </summary>
    public class QuizAiService : MonoBehaviour
    {
        public static QuizAiService Instance { get; private set; }

        const int TargetUsablePerChapter = 10;  // 本章可用 AI 题低于该值时后台补充
        const int BatchSize = 5;                // 每批请求题数
        const float CheckInterval = 120f;       // 自动补充检查间隔（秒）

        float _nextCheck;
        bool _busy;

        /// <summary>功能开关：只控制临时题是否启用/是否继续生成（配置面板/审核面板切换）。</summary>
        public static bool FeatureEnabled => AIPromptConfig.Load().aiQuizEnabled;

        /// <summary>生成就绪：开关开启且云端 LLM 已配置。</summary>
        public static bool GenerationReady
        {
            get
            {
                var cfg = AIPromptConfig.Load();
                return cfg.aiQuizEnabled && cfg.useCloud && !string.IsNullOrEmpty(cfg.apiKey);
            }
        }

        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("QuizAiService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<QuizAiService>();
        }

        public static void SetEnabled(bool on)
        {
            var cfg = AIPromptConfig.Load();
            cfg.aiQuizEnabled = on;
            cfg.Save();
            CloudDialogueService.AddLog(on
                ? "AI 自动命题已开启：低风险题自动校验后立即临时使用"
                : "AI 自动命题已关闭：临时题暂停使用（已入库题不受影响）");
        }

        void Update()
        {
            if (_busy || !GenerationReady) return;
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckInterval;

            string chapterId = QuizSystem.CurrentChapterId();
            if (QuizAiBank.Usable(chapterId, true).Count < TargetUsablePerChapter)
                RequestBatch("自动补充");
        }

        /// <summary>请求一批候选题（审核面板「立即生成」按钮或自动补充调用）。</summary>
        public void RequestBatch(string reason)
        {
            if (_busy)
            {
                CloudDialogueService.AddLog("命题请求进行中，忽略重复触发（" + reason + "）");
                return;
            }
            if (!GenerationReady)
            {
                CloudDialogueService.AddLog("命题未就绪：需开启 AI 自动命题并配置云端 LLM（AI台词面板）");
                return;
            }
            StartCoroutine(Generate(QuizSystem.CurrentChapterId(), reason));
        }

        IEnumerator Generate(string chapterId, string reason)
        {
            _busy = true;
            var cfg = AIPromptConfig.Load();
            string url, model;
            switch (cfg.provider)
            {
                case "deepseek":
                    url = "https://api.deepseek.com/chat/completions";
                    model = string.IsNullOrEmpty(cfg.model) ? "deepseek-chat" : cfg.model;
                    break;
                case "edenai":
                    url = "https://api.edenai.run/v2/llm/chat";
                    model = string.IsNullOrEmpty(cfg.model) ? "openai/gpt-4o-mini" : cfg.model;
                    break;
                default: // openrouter
                    url = "https://openrouter.ai/api/v1/chat/completions";
                    model = string.IsNullOrEmpty(cfg.model) ? "deepseek/deepseek-chat" : cfg.model;
                    break;
            }

            string body = "{\"model\":\"" + Esc(model) + "\",\"messages\":[" +
                "{\"role\":\"system\",\"content\":\"" + Esc(SystemPrompt()) + "\"}," +
                "{\"role\":\"user\",\"content\":\"" + Esc(UserPrompt(chapterId)) + "\"}]," +
                "\"max_tokens\":3000,\"temperature\":0.7}";

            CloudDialogueService.AddLog("命题请求（" + reason + "）" + cfg.provider + "/" + model +
                " → " + chapterId + " × " + BatchSize + " 题");
            float startAt = Time.realtimeSinceStartup;

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + cfg.apiKey.Trim());
                req.timeout = 60;
                yield return req.SendWebRequest();

                float ms = (Time.realtimeSinceStartup - startAt) * 1000f;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    string content = CloudDialogueService.ExtractContent(req.downloadHandler.text);
                    int temp = 0, highrisk = 0, rejected = 0;
                    foreach (var q in ParseQuestions(content))
                    {
                        if (QuizAiBank.TryAdd(q, chapterId, out string verdict))
                        {
                            if (verdict.Contains("高风险")) highrisk++; else temp++;
                        }
                        else
                        {
                            rejected++;
                            CloudDialogueService.AddLog("候选题被拒：" + verdict);
                        }
                    }
                    CloudDialogueService.AddLog("命题完成 " + Mathf.RoundToInt(ms) + "ms：临时可用 +" +
                        temp + "、待人工复核（高风险）+" + highrisk + "、自动校验拒绝 " + rejected +
                        (temp + highrisk + rejected == 0 ? " ⚠未解析出题目" : ""));
                }
                else
                {
                    CloudDialogueService.AddLog("命题失败 " + Mathf.RoundToInt(ms) + "ms HTTP" +
                        req.responseCode + " " + req.error);
                }
            }
            _busy = false;
        }

        // ===================== 提示词（第四十六节·来源约束升级版） =====================

        static string SystemPrompt()
        {
            return
                "你是《逆境之路》的\u201C来源可追溯常识提醒与恢复题库生成器\u201D。" +
                "只能使用已提供并审核的来源矩阵：HPP-L01 至 HPP-L23（塔尔·本-沙哈尔哈佛积极心理学公开课1—23讲）；" +
                "DDJ-01 至 DDJ-81（老子《道德经》逐章审查表）；CXL 条目（《传习录》核心工夫条目，如 CXL-086）。" +
                "生成题目前必须先选择sourceTags，不得捏造不存在的讲次、章次、条目或原文观点。" +
                "命题目标不是背诵，而是在游戏情境中训练：事实与解释分离、允许自己为人、日损多余反应、" +
                "守静知止、致良知、知行合一、边界、恢复节律和返回行动。硬性要求：" +
                "1.仅输出JSON数组，不加任何说明文字或代码块标记。每题含questionId、chapterId、sceneTag、conceptTag、" +
                "energyTags、difficulty、type、question、options、correctIndex、rationale、sourceTags、sourceType。" +
                "type取值：原则理解/情境应用/日损辨识/迁移结果/恢复提示；difficulty取1-3；" +
                "energyTags从Will/Focus/SelfWorth/Boundary/ActionPower/HP/Stamina/Rumination/RelationshipDrain中选。" +
                "2.options必须正好3项，且只有1项在当前情境下正确；另外两项分别具有可命名偏差" +
                "（读心、灾难化、回避、无限准备、过度负责、强迫积极、报复、身份冻结、消极躺平等）。" +
                "3.rationale需说明正确项为何符合来源与场景，并指出两个错误项的偏差。" +
                "4.不把积极写成否认痛苦；不把无为写成躺平；不把良知写成冲动；不把私欲写成所有正常欲望；" +
                "不鼓励报复或高风险暴露。5.同一题不得堆砌无关来源；sourceTags必须与正确选项的核心判断直接相关。" +
                "6.任何无法从来源矩阵支持的观点必须拒绝生成。";
        }

        /// <summary>动态题源上下文：当前章节、代表场景、玩家错题概念、当前失衡能量。</summary>
        static string UserPrompt(string chapterId)
        {
            var info = QuizSystem.ChapterInfo(chapterId);
            var sb = new StringBuilder();
            sb.Append("生成 ").Append(BatchSize).Append(" 道题。当前章节：").Append(chapterId);
            if (info != null)
                sb.Append("（").Append(info.name).Append("——").Append(info.theme).Append("）");

            var scenes = new List<string>();
            foreach (var q in QuizSystem.ChapterQuestions(chapterId))
                if (!string.IsNullOrEmpty(q.sceneTag) && !scenes.Contains(q.sceneTag))
                {
                    scenes.Add(q.sceneTag);
                    if (scenes.Count >= 3) break;
                }
            if (scenes.Count > 0)
                sb.Append("。代表场景：").Append(string.Join("、", scenes));

            var wrongConcepts = QuizSystem.WrongConceptTags(chapterId, 4);
            if (wrongConcepts.Count > 0)
                sb.Append("。玩家近期答错的概念（优先围绕它们出题）：")
                  .Append(string.Join("、", wrongConcepts));

            var player = FindObjectOfType<Player.PlayerController>();
            if (player != null && player.Stats != null)
            {
                string imbalance = QuizSystem.ImbalanceLabel(player.Stats);
                if (!string.IsNullOrEmpty(imbalance))
                    sb.Append("。玩家当前失衡能量：").Append(imbalance);
            }
            sb.Append("。每题从来源矩阵选取1—4个与正确判断直接相关的标签。");
            return sb.ToString();
        }

        // ===================== 解析 =====================

        /// <summary>从 LLM 回复中截取 JSON 数组并解析为题目列表（容忍代码块标记与前后说明文字）。</summary>
        static List<QuizQuestion> ParseQuestions(string content)
        {
            var list = new List<QuizQuestion>();
            if (string.IsNullOrEmpty(content)) return list;
            int start = content.IndexOf('[');
            int end = content.LastIndexOf(']');
            if (start < 0 || end <= start) return list;
            string arr = content.Substring(start, end - start + 1);
            try
            {
                var batch = JsonUtility.FromJson<QuizAiBatch>("{\"items\":" + arr + "}");
                if (batch != null && batch.items != null)
                    foreach (var q in batch.items)
                        if (q != null) list.Add(q);
            }
            catch
            {
                CloudDialogueService.AddLog("命题解析失败：返回内容不是合法 JSON 数组");
            }
            return list;
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "").Replace("\t", " ");
        }
    }
}
