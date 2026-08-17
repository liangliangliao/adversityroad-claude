using System;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.Adversity
{
    /// <summary>
    /// 弱点图谱的一条边（方案 20.3 VulnerabilityEdge）。
    ///
    /// 它**不是**"你是怎样的人"的标签，而是"哪些可观察条件容易使游戏决策失守"的关系：
    /// 公开挑衅 → 追击概率高；多敌围攻 → 过度翻滚；低血量 → 攻击节奏崩坏。
    /// 每条边必须保存样本数与置信度，低置信度推断不得用于高压针对。
    /// </summary>
    [Serializable]
    public class VulnerabilityEdge
    {
        public string triggerTag;        // 可观察触发条件
        public string behaviorTag;       // 可观察行为
        public int sampleCount;
        public int hitCount;             // 触发条件下真的出现该行为的次数
        public float confidence;         // 0-1
        public string lastObservedAt = "";
        public List<string> sceneDiversity = new List<string>();
        public List<string> counterStrategies = new List<string>();
        public bool playerVisible = true;

        public float Rate => sampleCount > 0 ? (float)hitCount / sampleCount : 0f;

        /// <summary>证据等级（方案 8.5）：<3 次只是假设；3-9 次弱证据；≥10 次且跨场景才算稳定模式。</summary>
        public string EvidenceLabel()
        {
            if (sampleCount < 3) return "假设";
            if (sampleCount < 10) return "弱证据";
            return sceneDiversity.Count >= 2 ? "稳定模式" : "单场景模式";
        }

        /// <summary>只有稳定模式才允许被自适应敌人用于针对性战术。</summary>
        public bool UsableForTargeting =>
            sampleCount >= 10 && sceneDiversity.Count >= 2 && confidence >= 0.6f && Rate >= 0.55f;
    }

    /// <summary>优势图谱的一条边（方案 8.4：明确禁止只建立"缺陷画像"）。</summary>
    [Serializable]
    public class StrengthEdge
    {
        public string strengthTag;
        public int sampleCount;
        public float confidence;
        public string lastObservedAt = "";
        public List<string> sceneDiversity = new List<string>();

        public string EvidenceLabel()
        {
            if (sampleCount < 3) return "假设";
            if (sampleCount < 10) return "弱证据";
            return sceneDiversity.Count >= 2 ? "稳定优势" : "单场景优势";
        }

        /// <summary>达到稳定优势时，导演必须为它设计"优势发挥窗口"（方案 7.5）。</summary>
        public bool DeservesShowcase => sampleCount >= 8 && confidence >= 0.55f;
    }

    /// <summary>
    /// Experience Graph（方案 8.2）：真实经历以"结构化事件"保存——
    /// 环境、角色类型、事件、玩家行为、对方行为、结果、遗憾、希望的替代策略。
    /// **真实第三方姓名、地址等绝不作为运行时关卡素材**，只保留角色类型与事件结构。
    /// </summary>
    [Serializable]
    public class ExperienceEvent
    {
        public string eventId;
        public string environment = "";    // 「办公场所」「家里」——类型，不是地址
        public string roleType = "";       // 「索取型角色」「评价型角色」——类型，不是姓名
        public string what = "";
        public string myBehavior = "";
        public string theirBehavior = "";
        public string result = "";
        public string regret = "";
        public string altStrategy = "";
        public string atUtc = "";
    }

    [Serializable]
    class AdversityProfileSave
    {
        public List<VulnerabilityEdge> vulnerabilities = new List<VulnerabilityEdge>();
        public List<StrengthEdge> strengths = new List<StrengthEdge>();
        public List<ExperienceEvent> experiences = new List<ExperienceEvent>();
        /// <summary>玩家可以在设置里关掉全部个性化推断（方案 8.5 / 23.3 第 26 条）。</summary>
        public bool personalizationEnabled = true;
    }

    /// <summary>
    /// 玩家逆境画像（方案第 8 章）：只用**可观察的游戏行为**，绝不越界推断现实人格或精神疾病。
    ///
    /// 两条红线：
    /// - 玩家主动输入可以提取目标、经历结构、偏好与禁用主题，但不能把主观描述当成诊断事实；
    /// - 游戏行为可以统计追击、闪避、死亡、失败恢复、目标偏移、资源使用等可观察模式，
    ///   但不能推断现实人格。
    /// 所有推断都带置信度、可查看、可删除、可关闭。
    /// </summary>
    public static class AdversityProfile
    {
        const string SaveKey = "adversity_profile_v2";

        static AdversityProfileSave _s;

        public static event Action Changed;

        static AdversityProfileSave S()
        {
            if (_s != null) return _s;
            string json = PlayerPrefs.GetString(SaveKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { _s = JsonUtility.FromJson<AdversityProfileSave>(json); }
                catch { _s = null; }
            }
            if (_s == null) _s = new AdversityProfileSave();
            return _s;
        }

        static void Persist()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(S()));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static bool Enabled
        {
            get => S().personalizationEnabled;
            set { S().personalizationEnabled = value; Persist(); }
        }

        public static List<VulnerabilityEdge> Vulnerabilities => S().vulnerabilities;
        public static List<StrengthEdge> Strengths => S().strengths;
        public static List<ExperienceEvent> Experiences => S().experiences;

        // ================= 观察写入 =================

        /// <summary>
        /// 观察一次「触发条件 → 行为」：hit=true 表示这次触发下真的出现了该行为。
        /// 只记录可观察事件本身，不下任何关于玩家是谁的结论。
        /// </summary>
        public static void Observe(string triggerTag, string behaviorTag, bool hit,
            string sceneId, params string[] counters)
        {
            if (!Enabled || string.IsNullOrEmpty(triggerTag)) return;
            var e = FindOrCreate(triggerTag, behaviorTag);
            e.sampleCount++;
            if (hit) e.hitCount++;
            e.lastObservedAt = DateTime.UtcNow.ToString("o");
            if (!string.IsNullOrEmpty(sceneId) && !e.sceneDiversity.Contains(sceneId))
                e.sceneDiversity.Add(sceneId);
            foreach (var c in counters)
                if (!string.IsNullOrEmpty(c) && !e.counterStrategies.Contains(c))
                    e.counterStrategies.Add(c);

            // 置信度：样本量 × 场景多样性 × 一致性——三者都要，缺一不可
            float volume = Mathf.Clamp01(e.sampleCount / 12f);
            float diversity = Mathf.Clamp01(e.sceneDiversity.Count / 3f);
            float consistency = Mathf.Abs(e.Rate - 0.5f) * 2f;
            e.confidence = Mathf.Clamp01(0.45f * volume + 0.25f * diversity + 0.30f * consistency);
            Persist();
        }

        public static void ObserveStrength(string strengthTag, string sceneId)
        {
            if (!Enabled || string.IsNullOrEmpty(strengthTag)) return;
            StrengthEdge e = null;
            foreach (var s in S().strengths) if (s.strengthTag == strengthTag) { e = s; break; }
            if (e == null)
            {
                e = new StrengthEdge { strengthTag = strengthTag };
                S().strengths.Add(e);
            }
            e.sampleCount++;
            e.lastObservedAt = DateTime.UtcNow.ToString("o");
            if (!string.IsNullOrEmpty(sceneId) && !e.sceneDiversity.Contains(sceneId))
                e.sceneDiversity.Add(sceneId);
            e.confidence = Mathf.Clamp01(0.6f * Mathf.Clamp01(e.sampleCount / 10f) +
                0.4f * Mathf.Clamp01(e.sceneDiversity.Count / 3f));
            Persist();
        }

        static VulnerabilityEdge FindOrCreate(string trigger, string behavior)
        {
            foreach (var e in S().vulnerabilities)
                if (e.triggerTag == trigger && e.behaviorTag == behavior) return e;
            var ne = new VulnerabilityEdge { triggerTag = trigger, behaviorTag = behavior };
            S().vulnerabilities.Add(ne);
            return ne;
        }

        // ================= 查询 =================

        public static VulnerabilityEdge Find(string trigger, string behavior)
        {
            foreach (var e in S().vulnerabilities)
                if (e.triggerTag == trigger && e.behaviorTag == behavior) return e;
            return null;
        }

        /// <summary>可用于针对性战术的弱点边（只有稳定模式才允许）。</summary>
        public static List<VulnerabilityEdge> TargetableEdges()
        {
            var list = new List<VulnerabilityEdge>();
            if (!Enabled) return list;
            foreach (var e in S().vulnerabilities)
                if (e.UsableForTargeting) list.Add(e);
            return list;
        }

        /// <summary>需要被设计"优势发挥窗口"的优势（方案 7.5：逆境不是只攻击弱点）。</summary>
        public static List<StrengthEdge> ShowcaseStrengths()
        {
            var list = new List<StrengthEdge>();
            if (!Enabled) return list;
            foreach (var s in S().strengths)
                if (s.DeservesShowcase) list.Add(s);
            return list;
        }

        // ================= 经历图谱 =================

        /// <summary>
        /// 记录一条结构化经历。任何看起来像真实姓名/地址/联系方式的内容都会被拒绝写入——
        /// 游戏生成时只使用角色类型与事件结构。
        /// </summary>
        public static bool AddExperience(ExperienceEvent ev)
        {
            if (!Enabled || ev == null) return false;
            string blob = ev.environment + ev.roleType + ev.what + ev.myBehavior +
                ev.theirBehavior + ev.result + ev.regret + ev.altStrategy;
            if (LooksLikePrivateIdentity(blob))
            {
                GameEvents.RaiseSubtitle("这条记录里像是有真实的姓名或联系方式——" +
                    "游戏只保存角色类型与事件结构，请改写后再保存。");
                return false;
            }
            ev.eventId = "exp_" + DateTime.UtcNow.Ticks.ToString("x");
            ev.atUtc = DateTime.UtcNow.ToString("o");
            S().experiences.Insert(0, ev);
            if (S().experiences.Count > 40) S().experiences.RemoveRange(40, S().experiences.Count - 40);
            Persist();
            return true;
        }

        static bool LooksLikePrivateIdentity(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int digits = 0;
            foreach (char c in s) if (c >= '0' && c <= '9') digits++;
            if (digits >= 7) return true;   // 手机号/身份证/银行卡长数字串
            foreach (var k in new[] { "@", "微信号", "身份证", "住在", "家住", "地址是" })
                if (s.Contains(k)) return true;
            return false;
        }

        // ================= 删除 =================

        public static void DeleteEdge(VulnerabilityEdge e)
        {
            if (S().vulnerabilities.Remove(e)) Persist();
        }

        public static void DeleteStrength(StrengthEdge e)
        {
            if (S().strengths.Remove(e)) Persist();
        }

        public static void DeleteExperience(ExperienceEvent e)
        {
            if (S().experiences.Remove(e)) Persist();
        }

        public static void DeleteAll()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            _s = null;
            Changed?.Invoke();
        }
    }
}
