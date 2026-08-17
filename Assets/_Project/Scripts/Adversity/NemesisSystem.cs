using System;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Core;
using AdversityRoad.OpenWorld;
using AdversityRoad.World;

namespace AdversityRoad.Adversity
{
    /// <summary>宿敌数据（方案 20.4 NemesisData）。</summary>
    [Serializable]
    public class NemesisData
    {
        public string nemesisId;
        public string archetypeId;          // EnemyType 名
        public string displayName;
        public string firstEncounterUtc = "";
        public string firstEncounterPlace = "";
        public int encounterCount;
        public int playerLosses;
        public int playerWins;

        /// <summary>它学到的玩家模式（只来自可观察行为标签）。</summary>
        public List<string> learnedPlayerPatterns = new List<string>();
        /// <summary>它做出的适应（战术名）。</summary>
        public List<string> enemyAdaptations = new List<string>();

        public int currentRank = 1;         // T5 起步，随进化提升
        public string worldLocationState = "";   // 当前巡逻区域 id
        public bool finalVictory;
        public string finalVictoryRecord = "";
        public string finalVictoryKeyAction = "";

        // ---- 逆袭历史（方案 9.5） ----
        public float shortestSurvival = -1f;
        public float longestSurvival;
        public List<string> failureCauses = new List<string>();
        public List<string> strategyShifts = new List<string>();
        public string defeatHistory = "";

        public string RankLabel()
        {
            switch (currentRank)
            {
                case 1: return "T5 宿敌";
                case 2: return "T6 宿敌·进化";
                default: return "T7 人生宿敌";
            }
        }
    }

    [Serializable]
    class NemesisSave
    {
        public List<NemesisData> list = new List<NemesisData>();
        /// <summary>战术权重记忆：被识破的战术权重下降，敌人转而尝试其他公平战术。</summary>
        public List<string> tacticNames = new List<string>();
        public List<float> tacticWeights = new List<float>();
    }

    /// <summary>
    /// Nemesis System（方案 9.4 / 9.5 / 15.4）：宿敌与逆袭历史。
    ///
    /// 生命周期：重要敌人击败玩家 → 获得宿敌候选标记 → 记录战斗地点、失败模式、
    /// 使用招式与阶段结果 → 在后续世界中升级招式/台词/战术（但必须遵守公平边界）→
    /// 玩家可以训练、找情报、拿针对性技能或暂时绕开 → 最终击败时生成逆袭回放。
    ///
    /// 「逆袭」不是 Boss 首杀（方案 15.4），而是系统能证明玩家相对于过去发生了变化：
    /// 同一宿敌的失败模式被改变 / 同一压力下生存与完成度显著提升 /
    /// 曾经必须绕开的敌人现在能稳定应对 / 用新策略而不是纯数值碾压取胜。
    /// </summary>
    public static class NemesisSystem
    {
        const string SaveKey = "adversity_nemesis_v2";

        static NemesisSave _s;

        public static event Action Changed;

        static NemesisSave S()
        {
            if (_s != null) return _s;
            string json = PlayerPrefs.GetString(SaveKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { _s = JsonUtility.FromJson<NemesisSave>(json); }
                catch { _s = null; }
            }
            if (_s == null) _s = new NemesisSave();
            return _s;
        }

        static void Persist()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(S()));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static List<NemesisData> All => S().list;

        public static int Count => S().list.Count;

        public static NemesisData Find(string archetypeId)
        {
            foreach (var n in S().list) if (n.archetypeId == archetypeId) return n;
            return null;
        }

        /// <summary>当前活跃宿敌（战绩最多的一个）。</summary>
        public static NemesisData Primary()
        {
            NemesisData best = null;
            foreach (var n in S().list)
                if (best == null || n.encounterCount > best.encounterCount) best = n;
            return best;
        }

        // ================= 生命周期 =================

        /// <summary>玩家被击败：击杀者获得宿敌候选标记（方案 9.4 第 1 步）。</summary>
        public static NemesisData NoteDefeatedBy(EnemyController killer, float survivalSeconds,
            string failureCause)
        {
            if (killer == null || killer.profile == null) return null;
            string archetype = ArchetypeOf(killer);
            var n = Find(archetype);
            if (n == null)
            {
                n = new NemesisData
                {
                    nemesisId = "nem_" + DateTime.UtcNow.Ticks.ToString("x"),
                    archetypeId = archetype,
                    displayName = killer.profile.displayName,
                    firstEncounterUtc = DateTime.UtcNow.ToString("o"),
                    firstEncounterPlace = ZoneBuilder.ZoneNameOf(
                        ZoneBuilder.IndexOfZone(ZoneBuilder.CurrentZoneId))
                };
                S().list.Add(n);
                GameEvents.RaiseSubtitle("【宿敌成立】" + n.displayName +
                    " 记住了这一场——它会带着今天学到的东西再来找你。");
            }

            n.encounterCount++;
            n.playerLosses++;
            RecordSurvival(n, survivalSeconds);
            if (!string.IsNullOrEmpty(failureCause) && !n.failureCauses.Contains(failureCause))
                n.failureCauses.Add(failureCause);

            // 它学到的东西：只来自可观察行为图谱里已经稳定的模式
            foreach (var e in AdversityProfile.TargetableEdges())
            {
                string pattern = e.triggerTag + "→" + e.behaviorTag;
                if (!n.learnedPlayerPatterns.Contains(pattern)) n.learnedPlayerPatterns.Add(pattern);
            }

            // 升级并改变世界巡逻位置（方案 15.2）
            if (n.playerLosses >= 2 && n.currentRank < 3) n.currentRank++;
            n.worldLocationState = PickPatrolDistrict(n);
            WorldState.SetNemesisDistrict(n.worldLocationState);

            Persist();
            return n;
        }

        /// <summary>玩家击败宿敌：判定这次是不是"逆袭"，而不是简单记一次胜利。</summary>
        public static void NoteVictoryOver(string archetypeId, float survivalSeconds,
            string keyAction, bool usedNewStrategy)
        {
            var n = Find(archetypeId);
            if (n == null) return;
            n.encounterCount++;
            n.playerWins++;
            RecordSurvival(n, survivalSeconds);
            if (!string.IsNullOrEmpty(keyAction)) n.finalVictoryKeyAction = keyAction;
            if (usedNewStrategy && !string.IsNullOrEmpty(keyAction) &&
                !n.strategyShifts.Contains(keyAction))
                n.strategyShifts.Add(keyAction);

            bool comeback = IsComeback(n, survivalSeconds, usedNewStrategy);
            if (comeback && !n.finalVictory)
            {
                n.finalVictory = true;
                n.finalVictoryRecord = ComebackRecord(n);
                GameEvents.RaiseSubtitle("【逆袭达成】" + n.displayName + "：" + n.finalVictoryRecord);
            }
            Persist();
        }

        /// <summary>
        /// 逆袭的正式判定（方案 15.4）：至少满足其一——
        /// 失败模式被改变 / 同一压力下生存显著提升 / 曾需绕开现在能稳定应对 / 用新策略取胜。
        /// </summary>
        static bool IsComeback(NemesisData n, float survivalSeconds, bool usedNewStrategy)
        {
            if (n.playerLosses == 0) return false;          // 从没输过就不叫逆袭
            if (usedNewStrategy) return true;
            if (n.strategyShifts.Count > 0) return true;
            if (n.shortestSurvival > 0f && survivalSeconds > n.shortestSurvival * 2f) return true;
            if (n.playerWins >= 2) return true;
            return false;
        }

        static string ComebackRecord(NemesisData n)
        {
            string cause = n.failureCauses.Count > 0 ? n.failureCauses[0] : "同一个坎";
            string shift = n.strategyShifts.Count > 0 ? n.strategyShifts[0] : n.finalVictoryKeyAction;
            return "第一次你输在「" + cause + "」，输了 " + n.playerLosses + " 次；" +
                (string.IsNullOrEmpty(shift) ? "这一次你扛住了。" : "这一次你换成了「" + shift + "」。");
        }

        static void RecordSurvival(NemesisData n, float seconds)
        {
            if (seconds <= 0f) return;
            if (n.shortestSurvival < 0f || seconds < n.shortestSurvival) n.shortestSurvival = seconds;
            if (seconds > n.longestSurvival) n.longestSurvival = seconds;
        }

        static string ArchetypeOf(EnemyController e)
        {
            // enemyId 形如 "enemy_xxx" / "boss_xxx"（唯一化后带序号）——取 profile 的显示类型更稳
            return e.profile.displayName;
        }

        static string PickPatrolDistrict(NemesisData n)
        {
            var ids = Goals.ChapterModuleLibrary.AllDistrictIds();
            if (ids.Count == 0) return "";
            int i = Mathf.Abs((n.nemesisId + n.playerLosses).GetHashCode()) % ids.Count;
            return ids[i];
        }

        // ================= 战术权重（被识破就降权） =================

        public static float TacticWeight(string tactic)
        {
            var s = S();
            for (int i = 0; i < s.tacticNames.Count; i++)
                if (s.tacticNames[i] == tactic) return s.tacticWeights[i];
            return 1f;
        }

        /// <summary>
        /// 玩家连续识破该战术 → 权重下降，敌人转而尝试其他**公平**策略（方案 9.2）。
        /// 反之说明这条路还有效，权重缓慢回升——但永远不会突破上限变成"必中作弊"。
        /// </summary>
        public static void AdjustTactic(string tactic, bool countered)
        {
            if (string.IsNullOrEmpty(tactic)) return;
            var s = S();
            int idx = -1;
            for (int i = 0; i < s.tacticNames.Count; i++)
                if (s.tacticNames[i] == tactic) { idx = i; break; }
            if (idx < 0)
            {
                s.tacticNames.Add(tactic);
                s.tacticWeights.Add(1f);
                idx = s.tacticNames.Count - 1;
            }
            float w = s.tacticWeights[idx];
            w += countered ? -0.25f : 0.1f;
            s.tacticWeights[idx] = Mathf.Clamp(w, 0.15f, 1.6f);
            Persist();
        }

        public static void DeleteAll()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            _s = null;
            Changed?.Invoke();
        }
    }
}
