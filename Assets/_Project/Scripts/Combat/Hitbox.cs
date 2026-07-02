using UnityEngine;
using System.Collections.Generic;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 攻击判定框：由动画事件 EnableHitbox()/DisableHitbox() 开关。
    /// 同一次挥击对同一目标只判定一次。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hitbox : MonoBehaviour
    {
        public DamageInfo pendingDamage;
        Collider _col;
        readonly HashSet<Hurtbox> _hitThisSwing = new HashSet<Hurtbox>();

        void Awake()
        {
            _col = GetComponent<Collider>();
            _col.isTrigger = true;
            _col.enabled = false;
        }

        public void EnableHitbox(DamageInfo dmg)
        {
            pendingDamage = dmg;
            pendingDamage.sourcePosition = transform.position;
            _hitThisSwing.Clear();
            _col.enabled = true;
        }

        public void DisableHitbox() => _col.enabled = false;

        void OnTriggerEnter(Collider other)
        {
            var hurt = other.GetComponent<Hurtbox>();
            if (hurt == null || _hitThisSwing.Contains(hurt)) return;
            if (hurt.OwnerRoot == transform.root) return; // 不打自己
            _hitThisSwing.Add(hurt);
            hurt.ReceiveHit(pendingDamage);
        }
    }
}
