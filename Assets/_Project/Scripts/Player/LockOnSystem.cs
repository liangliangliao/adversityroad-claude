using UnityEngine;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 锁定系统（对齐大型动作游戏惯例）：
    /// · 默认【手动锁定】——按 Q 键 / 触屏「锁」按钮切换锁定与解除（魂系惯例），
    ///   目标死亡或超出距离自动解除，不会擅自换目标；
    /// · 可选【自动锁定】（设置面板切换）：保留旧行为，自动咬住最近敌人；
    /// · 攻击吸附（软磁吸）是独立开关：关闭后出招只朝摇杆/当前朝向，不吸附敌人——
    ///   习惯硬核手感的玩家可以完全手操；
    /// · 专注值过低时锁定会随机丢失（噪声干扰机制，两种模式都生效）。
    /// </summary>
    public class LockOnSystem : MonoBehaviour
    {
        public float acquireRange = 11f;
        public float loseRange = 15f;

        const string AutoKey = "lockon_auto";
        const string AssistKey = "attack_aim_assist";

        /// <summary>自动锁定模式（默认关＝手动，大作惯例）。设置面板切换，本地持久化。</summary>
        public static bool AutoAcquire
        {
            get => PlayerPrefs.GetInt(AutoKey, 0) == 1;
            set { PlayerPrefs.SetInt(AutoKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>攻击软吸附（默认开）。关闭后出招不吸附敌人，完全按摇杆方向。</summary>
        public static bool AimAssist
        {
            get => PlayerPrefs.GetInt(AssistKey, 1) == 1;
            set { PlayerPrefs.SetInt(AssistKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public Transform CurrentTarget { get; private set; }

        PlayerController _player;
        float _nextScan;
        bool _wasLocked;

        void LateUpdate()
        {
            bool locked = CurrentTarget != null;
            if (locked != _wasLocked)
            {
                _wasLocked = locked;
                Core.GameEvents.RaiseLockState(locked);
            }
        }

        void Update()
        {
            if (_player == null) _player = GetComponent<PlayerController>();

            // 手动锁定切换：Q 键 / 触屏「锁」按钮（已锁→解除；未锁→锁最近敌人）
            if (Input.GetKeyDown(KeyCode.Q) || MobileInput.GetDown("Lock"))
            {
                if (CurrentTarget != null)
                {
                    CurrentTarget = null;
                    Core.GameEvents.RaiseSubtitle("解除锁定。");
                }
                else
                {
                    CurrentTarget = FindNearestEnemy();
                    Core.GameEvents.RaiseSubtitle(CurrentTarget != null
                        ? "锁定目标。" : "附近没有可锁定的敌人。");
                }
            }

            // 专注值 < 20% 时锁定不稳定：噪声街区核心机制
            if (CurrentTarget != null && _player != null &&
                _player.Stats.focus < _player.Stats.maxFocus * 0.2f &&
                Random.value < 0.01f)
            {
                CurrentTarget = null;
                return;
            }

            // 目标失效（死亡/脱离距离）→ 解除
            if (CurrentTarget != null)
            {
                var ec = CurrentTarget.GetComponentInParent<AI.EnemyController>();
                if (ec == null || ec.State == AI.EnemyState.Dead ||
                    Vector3.Distance(transform.position, CurrentTarget.position) > loseRange)
                    CurrentTarget = null;
            }

            // 只有自动模式才擅自重新锁定；手动模式由玩家决定何时锁
            if (AutoAcquire && CurrentTarget == null && Time.time >= _nextScan)
            {
                _nextScan = Time.time + 0.3f;
                CurrentTarget = FindNearestEnemy();
            }
        }

        Transform FindNearestEnemy()
        {
            Transform best = null;
            float bestDist = acquireRange;
            foreach (var e in FindObjectsOfType<AI.EnemyController>())
            {
                if (e.State == AI.EnemyState.Dead) continue;
                float d = Vector3.Distance(transform.position, e.transform.position);
                if (d < bestDist) { bestDist = d; best = e.transform; }
            }
            return best;
        }
    }
}
