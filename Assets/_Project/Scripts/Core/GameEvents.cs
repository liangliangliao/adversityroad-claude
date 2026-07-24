using System;

namespace AdversityRoad.Core
{
    /// <summary>全局事件总线：系统间解耦通信（战斗、心理、任务、剧情、UI）。</summary>
    public static class GameEvents
    {
        public static event Action<float, float> OnPlayerHpChanged;
        public static event Action<string, float, float> OnMentalStatChanged;
        public static event Action<string> OnEnemyKilled;
        public static event Action<string> OnPlayerDied;
        public static event Action<string> OnQuestUpdated;
        public static event Action<string> OnSkillUnlocked;
        public static event Action OnRecoveryModeEntered;
        public static event Action<int> OnChapterAdvanced;      // 章节推进（参数：新章节序号）
        public static event Action<string> OnSubtitle;          // 底部字幕：敌人台词/系统提示
        public static event Action<int> OnMomentumChanged;      // 意势变化（0-3）
        public static event Action<string> OnComboSeqChanged;   // 连段序列（"拳·拳·腿"）
        public static event Action<bool> OnLockStateChanged;    // 锁定敌人状态（电影黑边）
        public static event Action<string> OnSkillBanner;       // 招式大字横幅（屏幕中央）
        public static event Action<int> OnComboCount;           // 连击计数（屏幕固定计数器）
        public static event Action<int, string, string> OnStanceChanged; // 战斗姿态切换（序号/名称/心法）
        public static event Action OnGoalChanged;               // 今日目标钉下/完成（目标板系统）
        public static event Action OnLifeThreatened;            // 生命穿越垂危线/濒死守护触发（强制弹出垂危决策）

        public static void RaisePlayerHpChanged(float cur, float max) => OnPlayerHpChanged?.Invoke(cur, max);
        public static void RaiseMentalStatChanged(string stat, float cur, float max) => OnMentalStatChanged?.Invoke(stat, cur, max);
        public static void RaiseEnemyKilled(string id) => OnEnemyKilled?.Invoke(id);
        public static void RaisePlayerDied(string reason) => OnPlayerDied?.Invoke(reason);
        public static void RaiseQuestUpdated(string id) => OnQuestUpdated?.Invoke(id);
        public static void RaiseSkillUnlocked(string id) => OnSkillUnlocked?.Invoke(id);
        public static void RaiseRecoveryMode() => OnRecoveryModeEntered?.Invoke();
        public static void RaiseChapterAdvanced(int chapter) => OnChapterAdvanced?.Invoke(chapter);
        public static void RaiseSubtitle(string text) => OnSubtitle?.Invoke(text);
        public static void RaiseMomentumChanged(int m) => OnMomentumChanged?.Invoke(m);
        public static void RaiseComboSeq(string seq) => OnComboSeqChanged?.Invoke(seq);
        public static void RaiseLockState(bool locked) => OnLockStateChanged?.Invoke(locked);
        public static void RaiseSkillBanner(string name) => OnSkillBanner?.Invoke(name);
        public static void RaiseComboCount(int n) => OnComboCount?.Invoke(n);
        public static void RaiseStanceChanged(int index, string name, string mantra) => OnStanceChanged?.Invoke(index, name, mantra);
        public static void RaiseGoalChanged() => OnGoalChanged?.Invoke();
        public static void RaiseLifeThreatened() => OnLifeThreatened?.Invoke();
    }
}
