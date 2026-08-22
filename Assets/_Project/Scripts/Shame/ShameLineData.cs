using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 一条指控（方案 8.13.2 ClaimData）。
    ///
    /// 【truthTag 是本章唯一的分叉】
    /// 指控为真 → 唯一解是「认领不终审」；指控为假 → 正解回到「事实之刃」，
    /// 此时认领不终审无效并产生硬直。系统**不代替玩家判断**：它只如实标记，
    /// 判断由玩家自己下（方案 8.4.1）。
    /// </summary>
    [Serializable]
    public class ClaimData
    {
        public string claimId;
        /// <summary>行为标签（只写行为，不写身份——见 8.12.3）。</summary>
        public string claimTag;
        /// <summary>这条指控是否属实。</summary>
        public bool truthTag;
        public string sourceEnemyId;
        /// <summary>已被认领：本章内不可被任何敌人复用（复用权废除）。</summary>
        public bool used;
        public string firstSeenAt = "";
        /// <summary>被什么反制的：「认领不终审」/「事实之刃」。</summary>
        public string counteredBy = "";
    }

    /// <summary>视线锥（方案 8.13.2 GazeConeData）。注视必须永远可读——visibility 不允许为 0。</summary>
    [Serializable]
    public class GazeConeData
    {
        public string coneId;
        public string ownerNpcId;
        public float angle = 55f;
        public float range = 18f;
        /// <summary>可见度 0.35-1：禁止隐形注视（8.7.1）。</summary>
        public float visibility = 1f;
        /// <summary>锥内每秒 Exposure 增量。</summary>
        public float exposureRate = 9f;
        /// <summary>常驻锥（不随 NPC 头部转动而消失）。</summary>
        public bool isPersistent;
    }

    /// <summary>一次自行陈述的记录（方案 8.13.2 StatementRecord）。</summary>
    [Serializable]
    public class StatementRecord
    {
        /// <summary>进入陈述时悬案计时器的剩余比例——「陈述提前量」，本章的逆袭证据之一。</summary>
        public float timingRatio;
        /// <summary>对谁说：0 一个人 / 1 相关的人 / 2 所有在场的人。</summary>
        public int audienceScope;
        /// <summary>用什么措辞：0 只讲事实 / 1 事实加上我的判断 / 2 事实加上我的下一步。</summary>
        public int wordingProfile;
        /// <summary>结算评价：最佳 / 普通。</summary>
        public string resultRank = "";
        public float selfWorthDelta;
        public string createdAt = "";
    }

    /// <summary>第八章的章节状态（方案 8.13.2 ShameLineData）。本地保存，可整体删除。</summary>
    [Serializable]
    public class ShameLineData
    {
        public string chapterId = ShameLine.ChapterId;
        public string currentLevelId = "";

        public float exposure;
        /// <summary>Exposure 上限：隐瞒与搜查回响都会把它抬高（抬高的是天花板，不是当前值）。</summary>
        public float exposureCap = 100f;

        /// <summary>讨好度（8-1 关卡局部，0-100）。</summary>
        public float appeasement;

        public int nailCount;
        /// <summary>累计否认次数——后排低语者靠它续命，也是逆袭判定项。</summary>
        public int denialCount;
        /// <summary>累计认领次数。</summary>
        public int ownCount;

        public List<StatementRecord> statementHistory = new List<StatementRecord>();
        public List<ClaimData> claims = new List<ClaimData>();

        /// <summary>本章结算评级：best / normal / ""（未结算）。</summary>
        public string outcomeRank = "";

        // ---- 逆袭判定与后续关卡衔接用的观测量（只记可观察的游戏行为）----
        public float exposurePeak;
        public int concealCount;              // 使用隐瞒类交互物的次数（长廊延长的来源）
        public int corridorSegments;          // 当前长廊段数
        public bool searchEchoTaken;          // 是否执行过搜查回响支线
        public bool selfStatementProof;       // 是否已获得武器「自述之证」
        public int nailedRecoverySamples;     // 被挂钉后重新进入有效行动的次数
        public float nailedRecoveryTotal;     // 上述用时合计（秒）
        public string firstNailAt = "";
        public string firstNailTag = "";
    }

    /// <summary>
    /// 第八章状态仓库：本地保存、可查看、可删除（遵 8.9.3、附录 C.2）。
    /// 默认不上传、不进入训练。
    /// </summary>
    public static class ShameLine
    {
        public const string ChapterId = "chapter08_shame";
        public const string LevelDebtCorridor = "Chapter08_Level01_DebtCorridor";
        public const string LevelEchoClassroom = "Chapter08_Level02_EchoClassroom";

        const string SaveKey = "adversity_shameline_v1";

        static ShameLineData _d;

        public static event Action Changed;

        public static ShameLineData Data
        {
            get
            {
                if (_d != null) return _d;
                string json = PlayerPrefs.GetString(SaveKey, "");
                if (!string.IsNullOrEmpty(json))
                {
                    try { _d = JsonUtility.FromJson<ShameLineData>(json); }
                    catch { _d = null; }
                }
                if (_d == null) _d = new ShameLineData();
                return _d;
            }
        }

        public static void Persist()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>只发变更通知、不落盘：每帧变化的量（Exposure）用这个，避免每帧写 PlayerPrefs。</summary>
        public static void Touch() => Changed?.Invoke();

        public static void DeleteAll()
        {
            _d = new ShameLineData();
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>玩家当前是否在第八章的两个关卡里（本章机制一律只在章内生效）。</summary>
        public static bool InChapter =>
            World.ZoneBuilder.CurrentZoneId == "corridor" ||
            World.ZoneBuilder.CurrentZoneId == "classroom";

        /// <summary>当前在哪一关（不在章内返回空串）。</summary>
        public static string CurrentLevelId =>
            World.ZoneBuilder.CurrentZoneId == "corridor" ? LevelDebtCorridor :
            World.ZoneBuilder.CurrentZoneId == "classroom" ? LevelEchoClassroom : "";
    }
}
