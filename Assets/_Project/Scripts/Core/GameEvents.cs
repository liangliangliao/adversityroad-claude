using System;

namespace AdversityRoad.Core
{
    /// <summary>全局事件总线：系统间解耦通信（战斗、心理、任务、UI）。</summary>
    public static class GameEvents
    {
        public static event Action<float, float> OnPlayerHpChanged;
        public static event Action<string, float, float> OnMentalStatChanged;
        public static event Action<string> OnEnemyKilled;
        public static event Action<string> OnPlayerDied;
        public static event Action<string> OnQuestUpdated;
        public static event Action<string> OnSkillUnlocked;
        public static event Action OnRecoveryModeEntered;

        public static void RaisePlayerHpChanged(float cur, float max) => OnPlayerHpChanged?.Invoke(cur, max);
        public static void RaiseMentalStatChanged(string stat, float cur, float max) => OnMentalStatChanged?.Invoke(stat, cur, max);
        public static void RaiseEnemyKilled(string id) => OnEnemyKilled?.Invoke(id);
        public static void RaisePlayerDied(string reason) => OnPlayerDied?.Invoke(reason);
        public static void RaiseQuestUpdated(string id) => OnQuestUpdated?.Invoke(id);
        public static void RaiseSkillUnlocked(string id) => OnSkillUnlocked?.Invoke(id);
        public static void RaiseRecoveryMode() => OnRecoveryModeEntered?.Invoke();
    }
}
