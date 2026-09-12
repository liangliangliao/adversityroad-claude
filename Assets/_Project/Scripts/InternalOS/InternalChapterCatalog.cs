using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// 第 9-26 章 · 90 关目录（V2.2 增补 PRD 的冻结数据）。
    ///
    /// 【为什么是 JSON 而不是写死在 C# 里】
    /// 90 关 × 十几栏关卡表 + 18 份 BossDNA，写成 switch 会是一个三千行、
    /// 谁也不敢改的文件；而这批数据的性质是**策划冻结件**：它会被反复核对、
    /// 局部微调，却几乎不参与逻辑分支。放进 Resources 的好处是改一栏不必重编译，
    /// 并且校验器可以在 CI 里把"哪一关缺了 Reality Victory"直接打成一行日志。
    ///
    /// 【和 LegacyChapterCatalog 的关系】
    /// 那一份是 V1.0 七大主题线的通用逆境模块；这一份是 V2.2 的 18 条内部障碍线。
    /// 两份都由 Goal OS 按"当前目标真的被什么挡住"来挑选（见 InternalChapterBridge），
    /// 谁也不是固定主线。
    /// </summary>
    public static class InternalChapterCatalog
    {
        public const string ResourcePath = "Chapters/internal_chapters_v22";
        public const int FirstChapter = 9;
        public const int LastChapter = 26;
        public const int ExpectedChapters = 18;
        public const int ExpectedLevels = 90;

        static InternalChapterBook _book;
        static Dictionary<string, InternalLevelData> _byLevelId;
        static Dictionary<string, InternalChapterInfo> _byChapterId;

        public static InternalChapterBook Book
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
                try { _book = JsonUtility.FromJson<InternalChapterBook>(ta.text); }
                catch (System.Exception e)
                {
                    Debug.LogError("[InternalOS] 章节表解析失败：" + e.Message);
                }
            }
            if (_book == null)
            {
                Debug.LogError("[InternalOS] 找不到 Resources/" + ResourcePath + "，第 9-26 章不可用。");
                _book = new InternalChapterBook();
            }

            _byLevelId = new Dictionary<string, InternalLevelData>();
            _byChapterId = new Dictionary<string, InternalChapterInfo>();
            for (int i = 0; i < _book.chapters.Count; i++)
            {
                var ch = _book.chapters[i];
                if (!string.IsNullOrEmpty(ch.chapterId)) _byChapterId[ch.chapterId] = ch;
                for (int j = 0; j < ch.levels.Count; j++)
                {
                    var lv = ch.levels[j];
                    if (!string.IsNullOrEmpty(lv.levelId)) _byLevelId[lv.levelId] = lv;
                }
            }
        }

        /// <summary>测试与热重载用：丢掉缓存，下次访问重新读盘。</summary>
        public static void Reload() { _book = null; }

        public static List<InternalChapterInfo> Chapters => Book.chapters;

        public static InternalChapterInfo Chapter(string chapterId)
        {
            if (_byChapterId == null) Load();
            InternalChapterInfo ch;
            return _byChapterId.TryGetValue(chapterId ?? "", out ch) ? ch : null;
        }

        public static InternalChapterInfo ChapterByNo(int no) => Chapter("chapter" + no.ToString("00"));

        public static InternalLevelData Level(string levelId)
        {
            if (_byLevelId == null) Load();
            InternalLevelData lv;
            return _byLevelId.TryGetValue(levelId ?? "", out lv) ? lv : null;
        }

        public static List<InternalLevelData> AllLevels()
        {
            var all = new List<InternalLevelData>();
            var chs = Chapters;
            for (int i = 0; i < chs.Count; i++) all.AddRange(chs[i].levels);
            return all;
        }

        public static InternalBossDNA Boss(int bossId)
        {
            var chs = Chapters;
            for (int i = 0; i < chs.Count; i++)
                if (chs[i].boss != null && chs[i].boss.bossId == bossId) return chs[i].boss;
            return null;
        }

        public static InternalBossDNA BossOfLevel(string levelId)
        {
            var lv = Level(levelId);
            if (lv == null) return null;
            var ch = Chapter(lv.chapterId);
            return ch != null && ch.boss != null && ch.boss.levelId == levelId ? ch.boss : null;
        }

        public static WorldKitInfo WorldKit(string id)
        {
            var ks = Book.worldKits;
            for (int i = 0; i < ks.Count; i++) if (ks[i].id == id) return ks[i];
            return null;
        }

        public static RootObstacleInfo RootObstacle(string code)
        {
            var rs = Book.rootObstacles;
            for (int i = 0; i < rs.Count; i++) if (rs[i].code == code) return rs[i];
            return null;
        }

        public static DeathTypeInfo DeathType(string id)
        {
            var ds = Book.deathTypes;
            for (int i = 0; i < ds.Count; i++) if (ds[i].id == id) return ds[i];
            return null;
        }

        /// <summary>一个 DeathType 是不是"打倒即可"。除了 Defeat，其余全部要求特定行为发生。</summary>
        public static bool IsKillDeathType(string deathType) =>
            !string.IsNullOrEmpty(deathType) && deathType.StartsWith("Defeat");

        /// <summary>这个 Boss 是不是"打倒即可"。复合 DeathType 里只要有一项不是 Defeat 就不是。</summary>
        public static bool IsKillBoss(InternalBossDNA boss)
        {
            if (boss == null) return true;
            var atoms = boss.deathTypeAtoms;
            if (atoms.Count == 0) return IsKillDeathType(boss.deathType);
            for (int i = 0; i < atoms.Count; i++)
                if (!IsKillDeathType(atoms[i])) return false;
            return true;
        }

        // ==================== 校验 ====================

        /// <summary>
        /// 数据自检（第 11.3 节验收清单的可自动化部分 + 本增补的"全部内部敌人"约束）。
        ///
        /// 只报**能自动判定**的那几条：章节/关卡数量、每章 3+1+1 的档位、
        /// Reality Victory 非空、Trigger 与 Spawn 命名契约、Boss 的命门与 DeathType、
        /// 以及每一个敌人单位都必须是 Internal。
        /// "这个现实地点为什么和该机制相关"这种只能人读的条目不在这里。
        /// </summary>
        public static List<string> Validate()
        {
            var errors = new List<string>();
            var chs = Chapters;

            if (chs.Count != ExpectedChapters)
                errors.Add("章节数 " + chs.Count + "，应为 " + ExpectedChapters + "（第 9-26 章）。");

            int levelCount = 0;
            var seenLevelIds = new HashSet<string>();
            var seenBossIds = new HashSet<int>();

            for (int i = 0; i < chs.Count; i++)
            {
                var ch = chs[i];
                string tag = ch.chapterId + "（第" + ch.chapterNo + "章）";

                if (ch.chapterNo < FirstChapter || ch.chapterNo > LastChapter)
                    errors.Add(tag + "：章节号超出 9-26。");
                if (string.IsNullOrEmpty(ch.core)) errors.Add(tag + "：缺章节核心。");
                if (string.IsNullOrEmpty(ch.axis)) errors.Add(tag + "：缺弱点轴。");
                if (RootObstacle(ch.rootObstacle) == null)
                    errors.Add(tag + "：根障碍 " + ch.rootObstacle + " 不在七类根障碍里。");

                if (ch.levels.Count != 5)
                    errors.Add(tag + "：关卡数 " + ch.levels.Count + "，应为 5（3 普通 + 1 精英 + 1 Boss）。");

                int elite = 0, boss = 0;
                for (int j = 0; j < ch.levels.Count; j++)
                {
                    var lv = ch.levels[j];
                    levelCount++;
                    if (!seenLevelIds.Add(lv.levelId)) errors.Add("关卡 id 重复：" + lv.levelId);
                    if (lv.tier == "Elite") elite++;
                    if (lv.tier == "Boss") boss++;

                    if (string.IsNullOrEmpty(lv.realityVictory))
                        errors.Add(lv.levelId + "：缺通关 / Reality Victory 条件。");
                    if (string.IsNullOrEmpty(lv.coreMechanic))
                        errors.Add(lv.levelId + "：缺核心物理机制——没有可玩机制的关卡不可入库。");
                    if (string.IsNullOrEmpty(lv.mainPath))
                        errors.Add(lv.levelId + "：缺主路径 / 拓扑。");
                    if (lv.triggerIds.Count == 0)
                        errors.Add(lv.levelId + "：没有任何关键 Trigger。");
                    if (!lv.dynamicBase && lv.worldKitIds.Count == 0)
                        errors.Add(lv.levelId + "：既不是动态 Base，又没有 World Kit。");

                    for (int k = 0; k < lv.worldKitIds.Count; k++)
                        if (WorldKit(lv.worldKitIds[k]) == null)
                            errors.Add(lv.levelId + "：World Kit " + lv.worldKitIds[k] + " 不在 14 套底座里。");

                    for (int k = 0; k < lv.triggerIds.Count; k++)
                        if (!lv.triggerIds[k].StartsWith("TRG_"))
                            errors.Add(lv.levelId + "：Trigger 命名不合契约 —— " + lv.triggerIds[k]);

                    for (int k = 0; k < lv.internalUnits.Count; k++)
                    {
                        var u = lv.internalUnits[k];
                        // 本增补的硬约束：9-26 章不存在外部敌人。
                        if (u.category != "Internal")
                            errors.Add(lv.levelId + "：单位「" + u.displayName + "」category=" +
                                       u.category + "，第 9-26 章只允许 Internal。");
                        if (u.count <= 0)
                            errors.Add(lv.levelId + "：单位「" + u.displayName + "」数量为 " + u.count + "。");
                        if (string.IsNullOrEmpty(u.spawnSocketId) || !u.spawnSocketId.StartsWith("SPN_"))
                            errors.Add(lv.levelId + "：单位「" + u.displayName + "」Spawn Socket 命名不合契约。");
                    }
                }

                if (ch.levels.Count == 5)
                {
                    if (elite != 1) errors.Add(tag + "：精英关 " + elite + " 个，应为 1。");
                    if (boss != 1) errors.Add(tag + "：Boss 关 " + boss + " 个，应为 1。");
                }

                var b = ch.boss;
                if (b == null || b.bossId == 0) { errors.Add(tag + "：缺 Boss。"); continue; }
                if (!seenBossIds.Add(b.bossId)) errors.Add("Boss id 重复：" + b.bossId);
                if (b.bossId < 28 || b.bossId > 45)
                    errors.Add(tag + "：Boss 编号 " + b.bossId + " 超出 28-45。");
                if (string.IsNullOrEmpty(b.coreMechanism)) errors.Add(tag + "：Boss 缺真命门。");
                if (string.IsNullOrEmpty(b.executionGate))
                    errors.Add(tag + "：Boss 缺最终失效条件 —— 那会退化成清血条通关。");
                if (b.deathTypeAtoms.Count == 0)
                    errors.Add(tag + "：Boss 缺 DeathType。");
                for (int k = 0; k < b.deathTypeAtoms.Count; k++)
                    if (DeathType(b.deathTypeAtoms[k]) == null)
                        errors.Add(tag + "：DeathType「" + b.deathTypeAtoms[k] + "」不在语义表里。");
                if (b.fakeWeaknesses.Count == 0)
                    errors.Add(tag + "：Boss 没有假弱点，玩家第一次试探将无从出错。");
                if (b.phases.Count < 2)
                    errors.Add(tag + "：Boss 阶段少于 2，阶段边界无法改变战术。");
            }

            if (levelCount != ExpectedLevels)
                errors.Add("关卡总数 " + levelCount + "，应为 " + ExpectedLevels + "。");

            return errors;
        }
    }
}
