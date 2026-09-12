using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Goals;

namespace AdversityRoad.Core
{
    /// <summary>一关怎么算通关。</summary>
    public enum LevelClearRule
    {
        /// <summary>打倒关底心魔才算通关（内心敌人：它不会因为你走开就消失）。</summary>
        Defeat,
        /// <summary>从入口进、从出口出就算通关（外部敌人：可以完全不交战）。</summary>
        Escape
    }

    /// <summary>
    /// 关卡通关规则 —— 外部敌人与内心敌人的分界线。
    ///
    /// 【这条规则在说什么】
    /// 以前所有关卡只有一种通关方式：把关底心魔打死。这对**内心敌人**是对的——
    /// 自我怀疑、拖延、旧我不会因为你走出房间就留在房间里，它们跟着你走，
    /// 所以必须正面处理。但对**外部敌人**（赖账的人、追着你咳嗽的人、盯着你的目光、
    /// 逼你代付的人、饥饿与寒冷）来说，"必须打死才能过去"教的是错的东西：
    /// 现实里你并不需要赢下每一场无谓的冲突，你只需要**穿过它，继续走你的路**。
    ///
    /// 所以外部敌人的关卡改成：
    ///   · 从这一关的入口进来，朝出口方向走，从另一扇门出去 —— 通关；
    ///   · 这个过程里敌人照常追击、照常出手、照常骂人（身体攻击与言语攻击
    ///     一个字都不改），玩家可以完全不理会、不应战；
    ///   · 但**过程不能省**：必须真的从入口走到出口，不能在门口原地打转。
    ///   · 玩家执意要打也行 —— 打死关底首领同样通关（旧路子一条不拆）。
    ///
    /// 内心敌人的关卡**规则不变**：仍然要打倒它。
    ///
    /// 【规则从哪儿来】只从一个地方来：关底首领的<see cref="EnemyOrigin"/>
    /// （见 <see cref="EnemyOrigins"/> 那张表）。不再在章节表里另抄一份，
    /// 否则两份早晚对不上，而玩家会先撞见对不上的那一半。
    /// </summary>
    public static class LevelRules
    {
        /// <summary>按关底首领的来处定规则。</summary>
        public static LevelClearRule Of(EnemyType bossType) =>
            EnemyOrigins.Of(bossType) == EnemyOrigin.External
                ? LevelClearRule.Escape : LevelClearRule.Defeat;

        /// <summary>主线子章的通关规则。</summary>
        public static LevelClearRule Of(ChapterInfo chapter) =>
            chapter == null ? LevelClearRule.Defeat : Of(chapter.enemyType);

        /// <summary>主线第 index 个子章的通关规则（越界一律按旧规则）。</summary>
        public static LevelClearRule OfChapterIndex(int index)
        {
            var chapters = StoryManager.Chapters;
            if (chapters == null || index < 0 || index >= chapters.Length)
                return LevelClearRule.Defeat;
            return Of(chapters[index]);
        }

        /// <summary>
        /// 某个区域当前对应哪一个子章。
        ///
        /// 同一个区可能在章节序列里出现多次（噪声街区出现两次：刺激线其一与终战），
        /// 取**离玩家当前进度最近的那一次出场**——门的去向、关卡规则、目标行三处
        /// 必须给出同一个答案，所以这段解析只写这一份，Portal 也走它。
        /// </summary>
        public static int ChapterIndexForZone(int zoneIndex)
        {
            var chapters = StoryManager.Chapters;
            if (chapters == null || chapters.Length == 0) return -1;
            var story = StoryManager.Instance;
            int cur = story != null ? Mathf.Clamp(story.Chapter, 0, chapters.Length - 1) : 0;

            int best = -1, bestDist = int.MaxValue;
            for (int i = 0; i < chapters.Length; i++)
            {
                if (chapters[i].zoneIndex != zoneIndex) continue;
                int d = Mathf.Abs(i - cur);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// 这个区域此刻能不能"走出去就算通关"。
        ///
        /// 三个条件缺一不可：
        ///   ① 这个区对应的子章正是玩家**当前**要打的那一章（已通关的老关卡再穿一次
        ///      不该重复推进，未解锁的更不该）；
        ///   ② 那一章的关底首领是外部敌人；
        ///   ③ 那一章没有自己的专属通关动作（advanceOnKill=false 的第八章两关走
        ///      "自行陈述 / 步行离场"那套，本来就不靠打死 Boss 通关，不要再插一条）。
        /// </summary>
        public static bool ZoneClearsByEscape(int zoneIndex, out int chapterIndex)
        {
            chapterIndex = -1;
            var story = StoryManager.Instance;
            if (story == null || story.AllCleared) return false;

            int idx = ChapterIndexForZone(zoneIndex);
            if (idx < 0 || idx != story.Chapter) return false;

            var ch = StoryManager.Chapters[idx];
            if (!ch.advanceOnKill) return false;
            if (Of(ch) != LevelClearRule.Escape) return false;

            chapterIndex = idx;
            return true;
        }

        // ================= AI 现场生成的关卡（Goal OS） =================

        /// <summary>
        /// AI 章节的通关规则。
        ///
        /// 先看关底首领（bossArchetype，或编成里标 chief 的那一条）；
        /// 首领缺失时按编成里外部/内心敌人的多数决——AI 给的这两份名单
        /// 本来就是按来处分开写的（GoalChapterData.externalEnemies / internalEnemies）。
        /// 两边一样多时取内心：那是旧规则，判错了也只是少一条捷径。
        /// </summary>
        public static LevelClearRule Of(GoalChapterData chapter)
        {
            if (chapter == null) return LevelClearRule.Defeat;

            if (ChapterModuleLibrary.TryEnemyFuzzy(chapter.bossArchetype, out var boss))
                return Of(boss);

            if (chapter.enemyPlan != null)
                foreach (var spec in chapter.enemyPlan)
                    if (spec != null && spec.tier == "chief" &&
                        ChapterModuleLibrary.TryEnemyFuzzy(spec.enemyType, out var chief))
                        return Of(chief);

            int ext = chapter.externalEnemies != null ? chapter.externalEnemies.Count : 0;
            int inn = chapter.internalEnemies != null ? chapter.internalEnemies.Count : 0;
            return ext > inn ? LevelClearRule.Escape : LevelClearRule.Defeat;
        }

        // ================= 玩家读得到的文案 =================

        /// <summary>规则名（面板/日志用）。</summary>
        public static string Name(LevelClearRule rule) =>
            rule == LevelClearRule.Escape ? "穿过去" : "打倒它";

        /// <summary>进关时播报的那一句：这一关怎么算赢。</summary>
        public static string Brief(LevelClearRule rule) =>
            rule == LevelClearRule.Escape
                ? "〔外部心魔〕不必应战——从入口走到出口就算通关。它们会追、会打、会说难听的话，你可以一概不理；当然，想打赢它们也照样算通关。"
                : "〔内心心魔〕走开没有用，它跟着你走——打倒关底心魔才算通关。";

        /// <summary>任务行里那半句目标。</summary>
        public static string Objective(LevelClearRule rule, string bossLabel) =>
            rule == LevelClearRule.Escape
                ? "穿过这里，从出口离开（或打倒【" + bossLabel + "】）"
                : "击败【" + bossLabel + "】";
    }
}
