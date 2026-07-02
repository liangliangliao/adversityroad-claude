using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>受击判定框：把命中转发给所属者（玩家或敌人）。</summary>
    public class Hurtbox : MonoBehaviour
    {
        public Transform OwnerRoot => transform.root;

        void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        public void ReceiveHit(DamageInfo dmg)
        {
            var enemy = GetComponentInParent<AI.EnemyController>();
            if (enemy != null) { enemy.TakeHit(dmg); return; }

            var player = GetComponentInParent<PlayerCombatController>();
            if (player != null) player.TakeHit(dmg);
        }
    }
}
