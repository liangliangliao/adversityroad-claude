using UnityEngine;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>锁定系统：Q 键锁定最近敌人。专注值过低时锁定会随机丢失（噪声干扰机制）。</summary>
    public class LockOnSystem : MonoBehaviour
    {
        public float lockRange = 15f;
        public LayerMask enemyMask = ~0;
        public Transform CurrentTarget { get; private set; }

        PlayerController _player;

        void Awake() => _player = GetComponent<PlayerController>();

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Q) || MobileInput.GetDown("Lock"))
            {
                if (CurrentTarget != null) { CurrentTarget = null; return; }
                CurrentTarget = FindNearestEnemy();
            }

            // 专注值 < 20% 时锁定不稳定：噪声街区核心机制
            if (CurrentTarget != null && _player != null &&
                _player.Stats.focus < _player.Stats.maxFocus * 0.2f &&
                Random.value < 0.01f)
            {
                CurrentTarget = null;
            }

            if (CurrentTarget != null)
            {
                Vector3 dir = CurrentTarget.position - transform.position; dir.y = 0;
                if (dir.magnitude > lockRange * 1.3f) { CurrentTarget = null; return; }
                if (dir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(dir), 10f * Time.deltaTime);
            }
        }

        Transform FindNearestEnemy()
        {
            var hits = Physics.OverlapSphere(transform.position, lockRange, enemyMask);
            Transform best = null; float bestDist = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.GetComponentInParent<AI.EnemyController>() == null) continue;
                float d = (h.transform.position - transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = h.transform; }
            }
            return best;
        }
    }
}
