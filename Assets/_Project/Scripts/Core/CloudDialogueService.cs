using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using AdversityRoad.Personalization;

namespace AdversityRoad.Core
{
    /// <summary>
    /// 云端 LLM 台词服务：支持 OpenRouter / DeepSeek / EdenAI（OpenAI 兼容 Chat 接口）。
    ///
    /// 延迟解决方案——预取台词池：
    /// 1. 进入区域/切换章节时后台批量请求一组台词（一次 8 条），存入 (区域×弱点轴) 池；
    /// 2. 敌人喊话时从池中即取即用（零延迟），池余量低于阈值时后台补充；
    /// 3. 池为空或未配置 apiKey 时无缝回退本地模板，游戏永不卡顿等待网络。
    /// </summary>
    public class CloudDialogueService : MonoBehaviour
    {
        public static CloudDialogueService Instance { get; private set; }

        const int RefillThreshold = 3;   // 池余量低于该值时补充
        const int BatchSize = 8;

        readonly Dictionary<string, Queue<string>> _pools = new Dictionary<string, Queue<string>>();
        readonly HashSet<string> _pending = new HashSet<string>();

        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("CloudDialogueService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CloudDialogueService>();
        }

        static bool Configured
        {
            get
            {
                var cfg = AIPromptConfig.Load();
                return cfg.useCloud && !string.IsNullOrEmpty(cfg.apiKey);
            }
        }

        /// <summary>即取即用：池中有货立即返回；没货触发后台预取并返回 false（调用方回退本地模板）。</summary>
        public bool TryGetLine(WeaknessAxis axis, string zoneId, out string line)
        {
            line = null;
            if (!Configured) return false;
            string key = zoneId + "|" + axis;
            if (_pools.TryGetValue(key, out var q) && q.Count > 0)
            {
                line = q.Dequeue();
                if (q.Count < RefillThreshold) Prefetch(axis, zoneId);
                return true;
            }
            Prefetch(axis, zoneId);
            return false;
        }

        /// <summary>进入区域时预热常用弱点轴的台词池。</summary>
        public void WarmUp(string zoneId, params WeaknessAxis[] axes)
        {
            if (!Configured) return;
            foreach (var a in axes) Prefetch(a, zoneId);
        }

        /// <summary>配置变更后清空旧池。</summary>
        public void ClearPools()
        {
            _pools.Clear();
        }

        void Prefetch(WeaknessAxis axis, string zoneId)
        {
            string key = zoneId + "|" + axis;
            if (_pending.Contains(key)) return;
            _pending.Add(key);
            StartCoroutine(Fetch(axis, zoneId, key));
        }

        IEnumerator Fetch(WeaknessAxis axis, string zoneId, string key)
        {
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

            string system =
                "你是动作游戏《逆境之路》中“心魔”敌人的台词生成器。" +
                "生成敌人对玩家的心理施压短句。硬性要求：" +
                "1.中文，每条不超过18个字；2.共" + BatchSize + "条，每行一条，不加编号引号；" +
                "3.只做抽象的心理压迫（低语/嘲讽/质疑），禁止真实人名地名、自残暗示、" +
                "现实可操作的操控指令或威胁；4.风格阴冷贴合场景。";
            string user =
                "场景：" + ZoneName(zoneId) +
                "。攻击的弱点：" + AxisName(axis) +
                "。全局提示词：" + Safe(cfg.globalPrompt) +
                "。场景提示词：" + Safe(cfg.GetScenePrompt(zoneId));

            string body = "{\"model\":\"" + Esc(model) + "\",\"messages\":[" +
                "{\"role\":\"system\",\"content\":\"" + Esc(system) + "\"}," +
                "{\"role\":\"user\",\"content\":\"" + Esc(user) + "\"}]," +
                "\"max_tokens\":400,\"temperature\":0.9}";

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + cfg.apiKey.Trim());
                req.timeout = 20;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    string content = ExtractContent(req.downloadHandler.text);
                    if (!string.IsNullOrEmpty(content))
                    {
                        if (!_pools.TryGetValue(key, out var q))
                            _pools[key] = q = new Queue<string>();
                        foreach (var raw in content.Split('\n'))
                        {
                            string line = Sanitize(raw);
                            if (line.Length >= 2 && line.Length <= 30) q.Enqueue(line);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[CloudDialogue] " + cfg.provider + " 请求失败: " + req.error);
                }
            }
            _pending.Remove(key);
        }

        // ---------- 解析与工具 ----------

        /// <summary>从 OpenAI 兼容响应中提取 choices[0].message.content（轻量字符串解析）。</summary>
        static string ExtractContent(string json)
        {
            int msg = json.IndexOf("\"message\"", System.StringComparison.Ordinal);
            if (msg < 0) msg = 0;
            int c = json.IndexOf("\"content\"", msg, System.StringComparison.Ordinal);
            if (c < 0) return null;
            int start = json.IndexOf('"', json.IndexOf(':', c) + 1);
            if (start < 0) return null;
            start++;
            var sb = new StringBuilder();
            for (int i = start; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '\\' && i + 1 < json.Length)
                {
                    char n = json[++i];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': break;
                        case 'u':
                            if (i + 4 < json.Length &&
                                int.TryParse(json.Substring(i + 1, 4),
                                    System.Globalization.NumberStyles.HexNumber, null, out int code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(n); break;
                    }
                }
                else if (ch == '"') break;
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        static string Sanitize(string raw)
        {
            string s = raw.Trim().Trim('"', '“', '”', '‘', '’', '-', '*', '·', ' ');
            // 去掉行首编号 "1." "2、" 等
            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == '、' || s[i] == ')' || s[i] == ' ')) i++;
            return s.Substring(i).Trim();
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "").Replace("\t", " ");
        }

        static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? "（无）" : s.Trim();

        static string ZoneName(string zoneId)
        {
            switch (zoneId)
            {
                case "home": return "深夜的独居小屋，桌上是落灰的计划";
                case "dojo": return "破旧的训练武馆";
                case "street": return "喧嚣的噪声街区，路人车辆与议论声";
                case "plaza": return "华灯初上的城市广场决战地";
                default: return zoneId;
            }
        }

        static string AxisName(WeaknessAxis axis)
        {
            switch (axis)
            {
                case WeaknessAxis.Procrastination: return "拖延与行动迟滞";
                case WeaknessAxis.NoiseSensitivity: return "易受噪声与他人目光干扰";
                case WeaknessAxis.SelfDoubt: return "自我怀疑";
                case WeaknessAxis.Shame: return "羞耻与被评价敏感";
                case WeaknessAxis.LowConfidence: return "低信心";
                case WeaknessAxis.JobAnxiety: return "求职与失业焦虑";
                case WeaknessAxis.BoundaryConflict: return "边界薄弱不敢拒绝";
                case WeaknessAxis.WillpowerCollapse: return "意志力崩塌";
                default: return "内心的脆弱";
            }
        }
    }
}
