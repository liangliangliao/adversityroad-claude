using System.IO;
using UnityEngine;

namespace AdversityRoad.Save
{
    /// <summary>
    /// 本地 JSON 存档（本地优先原则：个人材料默认保存在本地）。
    /// DeleteAll() 实现"数据删除"安全开关。
    /// </summary>
    public static class SaveSystem
    {
        static string Path => Application.persistentDataPath + "/adversity_save.json";

        public static void Save(SaveData data)
        {
            data.savedAtUtc = System.DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path, JsonUtility.ToJson(data, true));
        }

        public static SaveData Load()
        {
            if (!File.Exists(Path)) return null;
            try { return JsonUtility.FromJson<SaveData>(File.ReadAllText(Path)); }
            catch { return null; }
        }

        /// <summary>数据删除开关：删除玩家画像与全部存档。</summary>
        public static void DeleteAll()
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
    }
}
