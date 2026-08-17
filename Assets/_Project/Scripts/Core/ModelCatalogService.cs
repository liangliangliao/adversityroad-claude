using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace AdversityRoad.Core
{
    /// <summary>
    /// 模型目录：向所选提供商**问一遍它现在到底有哪些模型**，让玩家从真实列表里挑。
    ///
    /// 【为什么不能再用写死的预设】
    /// 面板上原来摆着三个固定型号的按钮，是我按记忆写进代码里的。
    /// 这类清单每个月都在变：型号下线、改名、新出——写死的那一刻就开始过期，
    /// 而模型名写错的失败最难自查（EdenAI 回 "model does not exist"，
    /// OpenRouter 上不存在的名字直接挂到超时）。玩家的要求也很直接：
    /// 选了 OpenRouter 就把它全部可用模型列出来，我自己挑。
    ///
    /// 三家的目录接口都是 OpenAI 风格的 GET /models（EdenAI 的路径不同），
    /// 返回 {"data":[{"id":"..."}]}。为防某家换了形状，解析分两层：
    /// 先按结构解，解不出再从原文里扫 "id":"..."——只要它还叫 id 就抓得到。
    /// 一个都抓不到时如实说"没取到"，并退回内置备选，而不是假装成功。
    /// </summary>
    public static class ModelCatalogService
    {
        [Serializable] class ModelEntry { public string id; public string name; }
        [Serializable] class ModelList { public List<ModelEntry> data; }

        /// <summary>各家的模型目录地址（按顺序试，第一个成功的算数）。</summary>
        static string[] EndpointsFor(string provider)
        {
            switch (provider)
            {
                case "deepseek":
                    return new[] { "https://api.deepseek.com/models" };
                case "edenai":
                    // EdenAI 的统一 LLM 接口与它的老 feature 接口是两套路径，
                    // 两个都试一下：哪个能给出模型 id 就用哪个。
                    return new[]
                    {
                        "https://api.edenai.run/v2/llm/models",
                        "https://api.edenai.run/v2/info/provider_subfeatures?feature__name=llm&subfeature__name=chat"
                    };
                default:
                    return new[] { "https://openrouter.ai/api/v1/models" };
            }
        }

        /// <summary>这家的目录接口要不要带 Key（OpenRouter 的是公开的）。</summary>
        static bool NeedsKey(string provider) => provider != "openrouter";

        /// <summary>
        /// 拉取模型列表。
        /// 成功：models 有内容，error 为空；失败：models 为空，error 是能读懂的原因。
        /// </summary>
        public static IEnumerator Fetch(string provider, string apiKey,
            Action<List<string>, string> done)
        {
            provider = string.IsNullOrEmpty(provider) ? "openrouter" : provider;
            var key = (apiKey ?? "").Trim();
            if (NeedsKey(provider) && string.IsNullOrEmpty(key))
            {
                done?.Invoke(new List<string>(), "这家的目录接口需要 API Key——先把 Key 填上再拉取。");
                yield break;
            }

            string lastError = "";
            foreach (var url in EndpointsFor(provider))
            {
                using (var req = UnityWebRequest.Get(url))
                {
                    if (!string.IsNullOrEmpty(key))
                        req.SetRequestHeader("Authorization", "Bearer " + key);
                    req.timeout = 30;
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        lastError = "HTTP" + req.responseCode + " " + req.error;
                        CloudDialogueService.AddLog("模型目录失败 " + provider + " " + lastError);
                        continue;
                    }

                    var ids = Parse(req.downloadHandler.text);
                    if (ids.Count == 0)
                    {
                        lastError = "接口通了，但没解析出任何模型 id";
                        CloudDialogueService.AddLog("模型目录 " + provider + "：" + lastError);
                        continue;
                    }

                    ids.Sort(StringComparer.Ordinal);
                    CloudDialogueService.AddLog("模型目录 " + provider + "：取到 " + ids.Count + " 个模型");
                    done?.Invoke(ids, "");
                    yield break;
                }
            }
            done?.Invoke(new List<string>(), string.IsNullOrEmpty(lastError) ? "没取到" : lastError);
        }

        static List<string> Parse(string json)
        {
            var ids = new List<string>();
            if (string.IsNullOrEmpty(json)) return ids;

            // 第一层：标准 {"data":[{"id":...}]}
            try
            {
                var parsed = JsonUtility.FromJson<ModelList>(json);
                if (parsed != null && parsed.data != null)
                    foreach (var m in parsed.data)
                        if (m != null && !string.IsNullOrEmpty(m.id)) Add(ids, m.id);
            }
            catch (Exception) { /* 形状不对就交给第二层 */ }

            // 第二层：直接从原文里扫 id 字段（形状变了也还能用）
            if (ids.Count == 0)
                foreach (Match m in Regex.Matches(json, "\"id\"\\s*:\\s*\"([^\"]{2,120})\""))
                    Add(ids, m.Groups[1].Value);

            return ids;
        }

        static void Add(List<string> ids, string id)
        {
            id = id.Trim();
            if (id.Length == 0 || ids.Contains(id)) return;
            ids.Add(id);
        }
    }
}
