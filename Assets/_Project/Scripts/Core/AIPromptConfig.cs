using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AdversityRoad.Core
{
    [Serializable]
    public class ScenePrompt
    {
        public string sceneId;
        public string prompt;
    }

    /// <summary>
    /// AI 台词提示词配置：全局提示词 + 各场景提示词，玩家可在游戏内编辑。
    /// 本地模式下提示词短句直接混入敌人台词池；
    /// 未来接入云端 LLM 时，提示词作为生成上下文（见 DialogueLibrary.CloudProvider 挂点）。
    /// 本地保存，支持随存档一键删除。
    /// </summary>
    [Serializable]
    public class AIPromptConfig
    {
        public string globalPrompt = "";
        public List<ScenePrompt> scenePrompts = new List<ScenePrompt>();

        [Header("云端 LLM（OpenAI 兼容 Chat 接口）")]
        public bool useCloud = false;
        public string provider = "openrouter";   // openrouter / deepseek / edenai
        public string apiKey = "";
        public string model = "";                // 留空用各提供商默认模型

        static string FilePath => Application.persistentDataPath + "/aiprompts.json";
        static AIPromptConfig _cached;

        public static AIPromptConfig Load()
        {
            if (_cached != null) return _cached;
            try
            {
                if (File.Exists(FilePath))
                    _cached = JsonUtility.FromJson<AIPromptConfig>(File.ReadAllText(FilePath));
            }
            catch { /* 配置损坏时回退默认 */ }
            if (_cached == null)
            {
                _cached = new AIPromptConfig();
                _cached.ApplyDefaultTemplates();
            }
            return _cached;
        }

        /// <summary>默认提示词模板：首次运行自动填充，玩家可在游戏内修改。</summary>
        public void ApplyDefaultTemplates()
        {
            if (string.IsNullOrEmpty(globalPrompt))
                globalPrompt = "紧盯玩家的拖延、自我怀疑和对他人眼光的敏感；" +
                               "用冷静而阴柔的短句施压；偶尔假装体贴地劝玩家放弃";
            SetIfEmpty("home", "深夜独居的房间；落灰的计划表；劝人躺回床上、明天再说");
            SetIfEmpty("dojo", "嘲笑训练是花架子；质疑坚持不了三天；练了也改变不了什么");
            SetIfEmpty("street", "街上的议论声、咳嗽声、目光都冲着玩家来；劝人缩回家里");
            SetIfEmpty("job", "已读不回的沉默；投出的简历石沉大海；质疑玩家的价值与定位");
            SetIfEmpty("plaza", "终局审判口吻；细数玩家过去的失败；断言他走不到终点");
        }

        void SetIfEmpty(string sceneId, string prompt)
        {
            if (string.IsNullOrEmpty(GetScenePrompt(sceneId)))
                SetScenePrompt(sceneId, prompt);
        }

        public void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(this, true)); }
            catch { /* 磁盘异常不致命 */ }
        }

        public string GetScenePrompt(string sceneId)
        {
            foreach (var s in scenePrompts)
                if (s.sceneId == sceneId) return s.prompt;
            return "";
        }

        public void SetScenePrompt(string sceneId, string prompt)
        {
            foreach (var s in scenePrompts)
                if (s.sceneId == sceneId) { s.prompt = prompt; return; }
            scenePrompts.Add(new ScenePrompt { sceneId = sceneId, prompt = prompt });
        }
    }
}
