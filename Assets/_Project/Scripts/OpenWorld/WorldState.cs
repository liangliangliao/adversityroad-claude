using System;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.OpenWorld
{
    /// <summary>三层世界（方案 6.4）。</summary>
    public enum WorldLayer { Reality, Adversity, Inner }

    [Serializable]
    public class DistrictState
    {
        public string districtId;
        /// <summary>逆境化程度 0-1：现实结构仍在，但灯光/声音/NPC 行为/敌人随之改变。</summary>
        public float adversity;
        /// <summary>该区域是否已被目标旅程占用（有章节落地）。</summary>
        public bool hasChapter;
        /// <summary>裂隙是否已打开（可进入内心世界）。</summary>
        public bool riftOpen;
        /// <summary>裂隙通往的 V1 经典区域索引（-1 = 未指定）。</summary>
        public int riftZoneIndex = -1;
        public string riftLabel = "";
    }

    [Serializable]
    class WorldStateSave
    {
        public int day = 1;
        public List<DistrictState> districts = new List<DistrictState>();
        public List<string> flags = new List<string>();
        public List<string> closedRoutes = new List<string>();
        public int totalDefeats;
        public string nemesisDistrictId = "";
    }

    /// <summary>
    /// World State Manager（方案 6.6 / 15.2 / 23.2 第 22 条）：
    /// 世界状态必须在失败、里程碑和目标完成后**持久变化**——
    /// 不是每次进入都回到同一张干净地图。
    ///
    /// 失败不是只有重生：临时资源损失、路线关闭但出现替代路线、宿敌升级并改变巡逻区、
    /// 时间推进、机会窗口变化、新的训练/情报/复盘节点——这些都记在这里。
    /// </summary>
    public static class WorldState
    {
        const string SaveKey = "adversity_worldstate_v2";

        static WorldStateSave _s;

        public static event Action Changed;

        static WorldStateSave S()
        {
            if (_s != null) return _s;
            string json = PlayerPrefs.GetString(SaveKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { _s = JsonUtility.FromJson<WorldStateSave>(json); }
                catch { _s = null; }
            }
            if (_s == null) _s = new WorldStateSave();
            return _s;
        }

        static void Persist()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(S()));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static int Day => S().day;

        /// <summary>时间推进：某些机会窗口随之变化（方案 15.2）。</summary>
        public static void AdvanceDay(string reason)
        {
            S().day++;
            Persist();
            GameEvents.RaiseSubtitle("时间推进到第 " + S().day + " 天——" + reason);
        }

        public static DistrictState Get(string districtId)
        {
            var s = S();
            foreach (var d in s.districts)
                if (d.districtId == districtId) return d;
            var nd = new DistrictState { districtId = districtId };
            s.districts.Add(nd);
            return nd;
        }

        public static List<DistrictState> All => S().districts;

        /// <summary>提高某区域的逆境化程度（压力事件、连续失败、障碍未处理）。</summary>
        public static void AddAdversity(string districtId, float delta)
        {
            var d = Get(districtId);
            float before = d.adversity;
            d.adversity = Mathf.Clamp01(d.adversity + delta);
            if (Mathf.Abs(d.adversity - before) > 0.001f) Persist();
        }

        public static float AdversityOf(string districtId) => Get(districtId).adversity;

        /// <summary>打开一个心理裂隙：现实地点长出通往 V1 经典章节的入口。</summary>
        public static void OpenRift(string districtId, int zoneIndex, string label)
        {
            var d = Get(districtId);
            if (d.riftOpen && d.riftZoneIndex == zoneIndex) return;
            d.riftOpen = true;
            d.riftZoneIndex = zoneIndex;
            d.riftLabel = label;
            Persist();
            GameEvents.RaiseSubtitle("这个地方裂开了一道缝——【" + label + "】的入口出现在" +
                DistrictCatalog.NameOf(districtId) + "。");
        }

        public static void CloseRift(string districtId)
        {
            var d = Get(districtId);
            if (!d.riftOpen) return;
            d.riftOpen = false;
            Persist();
        }

        // ---------- 世界级失败后果（方案 15.2） ----------

        public static void NoteDefeat(string districtId)
        {
            var s = S();
            s.totalDefeats++;
            AddAdversity(districtId, 0.18f);
            if (!s.closedRoutes.Contains(districtId)) s.closedRoutes.Add(districtId);
            Persist();
        }

        /// <summary>路线关闭，但必须同时出现替代路线——失败要有重量，不能变成死路。</summary>
        public static bool RouteClosed(string districtId) => S().closedRoutes.Contains(districtId);

        public static void ReopenRoute(string districtId)
        {
            var s = S();
            if (s.closedRoutes.Remove(districtId)) Persist();
        }

        public static int TotalDefeats => S().totalDefeats;

        /// <summary>宿敌改变世界巡逻位置。</summary>
        public static void SetNemesisDistrict(string districtId)
        {
            S().nemesisDistrictId = districtId ?? "";
            Persist();
        }

        public static string NemesisDistrict => S().nemesisDistrictId;

        // ---------- 通用标记 ----------

        public static bool HasFlag(string flag) => S().flags.Contains(flag);

        public static void SetFlag(string flag)
        {
            var s = S();
            if (s.flags.Contains(flag)) return;
            s.flags.Add(flag);
            Persist();
        }

        /// <summary>里程碑达成后的可见世界变化。</summary>
        public static void OnMilestoneReached(string districtId)
        {
            var d = Get(districtId);
            d.adversity = Mathf.Clamp01(d.adversity - 0.35f);
            ReopenRoute(districtId);
            Persist();
            GameEvents.RaiseSubtitle(DistrictCatalog.NameOf(districtId) + "的压迫感明显退了一层——世界记住了你的推进。");
        }

        public static void DeleteAll()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            _s = null;
            Changed?.Invoke();
        }
    }
}
