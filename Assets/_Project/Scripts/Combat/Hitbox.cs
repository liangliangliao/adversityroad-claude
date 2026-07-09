using UnityEngine;
using System.Collections.Generic;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 攻击判定框：由 EnableHitbox()/DisableHitbox() 开关，同一次挥击对同一目标只判定一次。
    ///
    /// 主动重叠检测（关键修复）：不再只靠 OnTriggerEnter——Unity 对"启用判定框时
    /// 已经和受击框重叠"的情形【不触发】OnTriggerEnter，而近身出招时敌人往往早已
    /// 站在判定框内，导致命中被静默吞掉（"看不出有没有击中"的根因）。改为判定框
    /// 一开启就立即用 Physics.OverlapBox 扫一遍、并在每个物理帧持续扫描，凡是落在
    /// 判定体积内的受击框都判为命中，且计算兵器/肢体真正接触身体的世界点，供命中
    /// 特效精确定位到"被打中的部位"。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hitbox : MonoBehaviour
    {
        public DamageInfo pendingDamage;
        /// <summary>命中回调（连段积势、命中特效等）。</summary>
        public System.Action<Hurtbox> onHit;
        Collider _col;
        BoxCollider _box;
        readonly HashSet<Hurtbox> _hitThisSwing = new HashSet<Hurtbox>();
        static readonly Collider[] _overlap = new Collider[16];

        void Awake()
        {
            _col = GetComponent<Collider>();
            _box = _col as BoxCollider;
            _col.isTrigger = true;
            _col.enabled = false;
        }

        public void EnableHitbox(DamageInfo dmg)
        {
            pendingDamage = dmg;
            pendingDamage.sourcePosition = transform.root.position;
            _hitThisSwing.Clear();
            _col.enabled = true;
            Scan();   // 立即扫一遍：捕获"开启即重叠"的目标（近身出招的常态）
        }

        public void DisableHitbox() => _col.enabled = false;

        void FixedUpdate()
        {
            if (_col.enabled) Scan();   // 判定窗口内持续扫描，兵器扫过身体即命中
        }

        /// <summary>扫描判定体积内的全部受击框，处理尚未命中的目标。</summary>
        void Scan()
        {
            Vector3 center; Vector3 half; Quaternion rot = transform.rotation;
            if (_box != null)
            {
                center = transform.TransformPoint(_box.center);
                half = Vector3.Scale(_box.size * 0.5f, transform.lossyScale);
            }
            else
            {
                var b = _col.bounds;
                center = b.center; half = b.extents; rot = Quaternion.identity;
            }
            // 略微放大判定盒，容错动画与判定的细微错位（宁可打到也不漏打）
            half += Vector3.one * 0.15f;

            int n = Physics.OverlapBoxNonAlloc(center, half, _overlap, rot,
                ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var hurt = _overlap[i] != null ? _overlap[i].GetComponent<Hurtbox>() : null;
                if (hurt == null || _hitThisSwing.Contains(hurt)) continue;
                if (hurt.OwnerRoot == transform.root) continue;   // 不打自己
                _hitThisSwing.Add(hurt);

                var dmg = pendingDamage;
                dmg.contactPoint = _overlap[i].ClosestPoint(center);   // 真正接触身体的点
                dmg.hasContact = true;
                hurt.ReceiveHit(dmg);
                onHit?.Invoke(hurt);
            }
        }

        // OnTriggerEnter 作为兜底（快速穿越的目标）：主逻辑走 Scan
        void OnTriggerEnter(Collider other)
        {
            if (!_col.enabled) return;
            var hurt = other.GetComponent<Hurtbox>();
            if (hurt == null || _hitThisSwing.Contains(hurt)) return;
            if (hurt.OwnerRoot == transform.root) return;
            _hitThisSwing.Add(hurt);
            var dmg = pendingDamage;
            dmg.contactPoint = other.ClosestPoint(transform.position);
            dmg.hasContact = true;
            hurt.ReceiveHit(dmg);
            onHit?.Invoke(hurt);
        }
    }
}
