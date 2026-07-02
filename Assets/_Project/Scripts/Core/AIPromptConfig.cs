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
            if (_cached == null) _cached = new AIPromptConfig();
            return _cached;
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
