using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// 90 关的 Canonical 内部语言攻击目录。
    ///
    /// 每一关**恰好**有一份策划冻结事件（增补章第 12 条：不允许空关）。
    /// AI 在运行时可以基于同一个 attackIntent 生成别的措辞，但生成物必须过
    /// <see cref="MentalAttackValidator"/> 三道校验；不过就回退到这里的原件。
    /// </summary>
    public static class MentalAttackCatalog
    {
        public const string ResourcePath = "Chapters/mental_attacks_v22";

        static MentalAttackBook _book;
        static Dictionary<string, List<MentalAttackChoiceEvent>> _byLevel;
        static Dictionary<string, MentalAttackChoiceEvent> _byEventId;

        public static MentalAttackBook Book
        {
            get
            {
                if (_book == null) Load();
                return _book;
            }
        }

        static void Load()
        {
            _book = null;
            var ta = Resources.Load<TextAsset>(ResourcePath);
            if (ta != null)
            {
                try { _book = JsonUtility.FromJson<MentalAttackBook>(ta.text); }
                catch (System.Exception e)
                {
                    Debug.LogError("[InternalOS] 内部语言攻击表解析失败：" + e.Message);
                }
            }
            if (_book == null)
            {
                Debug.LogError("[InternalOS] 找不到 Resources/" + ResourcePath + "。");
                _book = new MentalAttackBook();
            }

            _byLevel = new Dictionary<string, List<MentalAttackChoiceEvent>>();
            _byEventId = new Dictionary<string, MentalAttackChoiceEvent>();
            for (int i = 0; i < _book.events.Count; i++)
            {
                var e = _book.events[i];
                if (!string.IsNullOrEmpty(e.eventId)) _byEventId[e.eventId] = e;
                if (string.IsNullOrEmpty(e.levelId)) continue;
                List<MentalAttackChoiceEvent> list;
                if (!_byLevel.TryGetValue(e.levelId, out list))
                {
                    list = new List<MentalAttackChoiceEvent>();
                    _byLevel[e.levelId] = list;
                }
                list.Add(e);
            }
        }

        public static void Reload() { _book = null; }

        public static List<MentalAttackChoiceEvent> Events => Book.events;

        public static MentalAttackChoiceEvent Event(string eventId)
        {
            if (_byEventId == null) Load();
            MentalAttackChoiceEvent e;
            return _byEventId.TryGetValue(eventId ?? "", out e) ? e : null;
        }

        public static List<MentalAttackChoiceEvent> ForLevel(string levelId)
        {
            if (_byLevel == null) Load();
            List<MentalAttackChoiceEvent> list;
            return _byLevel.TryGetValue(levelId ?? "", out list) ? list : new List<MentalAttackChoiceEvent>();
        }

        /// <summary>关卡档案：把这一关的事件聚合成 LevelMentalAttackProfile。</summary>
        public static LevelMentalAttackProfile Profile(string levelId)
        {
            var p = new LevelMentalAttackProfile { levelId = levelId };
            p.canonicalEvents = ForLevel(levelId);
            for (int i = 0; i < p.canonicalEvents.Count; i++)
            {
                var e = p.canonicalEvents[i];
                if (!string.IsNullOrEmpty(e.attackIntent) && !p.attackIntents.Contains(e.attackIntent))
                    p.attackIntents.Add(e.attackIntent);
                for (int j = 0; j < e.safetyTags.Count; j++)
                    if (!p.safetyTags.Contains(e.safetyTags[j])) p.safetyTags.Add(e.safetyTags[j]);
            }
            return p;
        }

        /// <summary>本关是否已覆盖（第 12 条验收：90 关全部存在 Profile，不允许空关）。</summary>
        public static bool HasProfile(string levelId) => ForLevel(levelId).Count > 0;
    }
}
