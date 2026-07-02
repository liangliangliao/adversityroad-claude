using UnityEngine;
using AdversityRoad.Save;
using AdversityRoad.Personalization;

namespace AdversityRoad.Core
{
    /// <summary>全局管理器：持有安全设置、玩家画像、存档入口。场景常驻单例。</summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public SafetySettings safety;
        public PlayerProfile CurrentProfile { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            var save = SaveSystem.Load();
            CurrentProfile = (save != null && save.profile != null)
                ? save.profile
                : DefaultProfileFactory.CreateDefaultLifeTemplate();
        }

        public void SetProfile(PlayerProfile p) => CurrentProfile = p;

        /// <summary>一键快速退出：进入恢复模式并加载安全房间。</summary>
        public void QuickEscapeToSafeRoom()
        {
            if (safety != null) safety.recoveryMode = true;
            GameEvents.RaiseRecoveryMode();
            UnityEngine.SceneManagement.SceneManager.LoadScene("SC_HomeRoom");
        }

        public void SaveGame(Player.PlayerStats stats)
        {
            SaveSystem.Save(new SaveData { profile = CurrentProfile, stats = stats });
        }
    }
}
