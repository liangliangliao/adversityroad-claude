using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 自动锁定：无需按键，自动锁定近处最近的存活敌人，供镜头战斗构图使用。
    /// 专注值过低时锁定会随机丢失（噪声干扰机制）。
    /// </summary>
    public class LockOnSystem : MonoBehaviour
    {
        public float acquireRange = 11f;
        public float loseRange = 15f;

        public Transform CurrentTarget { get; private set; }

        PlayerController _player;
        float _nextScan;

        void Update()
        {
            if (_player == null) _player = GetComponent<PlayerController>();

            // 专注值 < 20% 时锁定不稳定：噪声街区核心机制
            if (CurrentTarget != null && _player != null &&
                _player.Stats.focus < _player.Stats.maxFocus * 0.2f &&
                Random.value < 0.01f)
            {
                CurrentTarget = null;
                return;
            }

            if (CurrentTarget != null)
            {
                var ec = CurrentTarget.GetComponentInParent<AI.EnemyController>();
                if (ec == null || ec.State == AI.EnemyState.Dead ||
                    Vector3.Distance(transform.position, CurrentTarget.position) > loseRange)
                    CurrentTarget = null;
            }

            if (CurrentTarget == null && Time.time >= _nextScan)
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
