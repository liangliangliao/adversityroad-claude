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

        /// <summary>判定盒的额外容差（米）。
        /// 玩家侧保持 0.15 的宽容（宁可打到也不漏打，手感好）；
        /// 敌人侧调到 0.04——同样的容差用在敌人身上就变成"明明跳过去了还是被扫到"，
        /// 尤其是正踢/侧踹这类贴地招，0.15 的上下外扩正好吃掉玩家起跳让开的高度差。
        /// 攻防两侧的容差本来就不该同号：一个是"帮玩家"，一个是"坑玩家"。</summary>
        public float pad = 0.15f;
        /// <summary>命中回调（连段积势、命中特效等）。</summary>
        public System.Action<Hurtbox> onHit;
        Collider _col;
        BoxCollider _box;
        readonly HashSet<Hurtbox> _hitThisSwing = new HashSet<Hurtbox>();
        // 同一次挥击对同一个【角色】只结算一次（不是对同一块受击框只结算一次）。
        // 拆出部位受击框之后，一记横斩会同时罩到胸、手臂、全身兜底三块——
        // 只按受击框去重就等于一刀打三次。这里按角色根去重，并在本帧扫描中
        // **择优**：取最精确、最贴近判定框中心的那一块作为"真正打中的部位"。
        readonly HashSet<Transform> _ownersHit = new HashSet<Transform>();
        static readonly Collider[] _overlap = new Collider[24];

        struct Pick { public Hurtbox hurt; public Vector3 contact; public float dist; }
        // 实例级（不是静态）：多个判定框可能在同一物理帧各自扫描，共用一份缓存会互相清空。
        readonly Dictionary<Transform, Pick> _best = new Dictionary<Transform, Pick>();
        readonly List<Pick> _picks = new List<Pick>(8);
        // 重入保护：结算命中会触发反击/技能，那些可能当场再开一个判定框并回调到这里。
        bool _scanning;

        void Awake()
        {
            _col = GetComponent<Collider>();
            _box = _col as BoxCollider;
            _col.isTrigger = true;
            _col.enabled = false;
        }

        /// <summary>按招式设置判定框形状：size=(宽X, 高Y, 长Z纵深)，center=相对角色根的偏移。
        /// 每招独立范围——突刺长而窄、横斩横宽、旋风斩/扫堂环身 360°、蓄力/绝招最大。</summary>
        public void SetShape(Vector3 size, Vector3 center)
        {
            transform.localPosition = center;
            transform.localScale = Vector3.one;
            if (_box == null) _box = GetComponent<Collider>() as BoxCollider;
            if (_box != null)
            {
                _box.center = Vector3.zero;
                _box.size = size;
            }
        }

        public void EnableHitbox(DamageInfo dmg)
        {
            pendingDamage = dmg;
            pendingDamage.sourcePosition = transform.root.position;
            _hitThisSwing.Clear();
            _ownersHit.Clear();
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
            if (_scanning) return;
            _scanning = true;
            try { ScanInner(); }
            finally { _scanning = false; }
        }

        void ScanInner()
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
            // 略微放大判定盒，容错动画与判定的细微错位（容差见 pad 字段的说明）
            half += Vector3.one * pad;

            int n = Physics.OverlapBoxNonAlloc(center, half, _overlap, rot,
                ~0, QueryTriggerInteraction.Collide);

            // ① 先按角色分组择优：每个角色只留下"最该算作被打中"的那一块受击框。
            //    择优规则（顺序即优先级）：
            //      精确度高的胜出（部位框 > 全身兜底框），
            //      同精确度取接触点离判定框中心更近的那块（兵器实际扫到的位置）。
            _best.Clear();
            for (int i = 0; i < n; i++)
            {
                var col = _overlap[i];
                if (col == null) continue;
                var hurt = col.GetComponent<Hurtbox>();
                if (hurt == null) continue;
                var owner = hurt.OwnerRoot;
                if (owner == transform.root) continue;             // 不打自己
                if (owner == null || _ownersHit.Contains(owner)) continue;  // 本次挥击已结算过

                Vector3 contact = col.ClosestPoint(center);
                float d = (contact - center).sqrMagnitude;
                if (_best.TryGetValue(owner, out var cur) &&
                    (cur.hurt.specificity > hurt.specificity ||
                     (cur.hurt.specificity == hurt.specificity && cur.dist <= d)))
                    continue;
                _best[owner] = new Pick { hurt = hurt, contact = contact, dist = d };
            }

            // ② 先把结果拍成快照再结算：结算过程会执行游戏逻辑（反击、技能、销毁），
            //    在字典的迭代过程中做这些事随时可能抛"集合已修改"。
            _picks.Clear();
            foreach (var kv in _best)
            {
                _ownersHit.Add(kv.Key);
                _picks.Add(kv.Value);
            }
            foreach (var pick in _picks)
            {
                if (pick.hurt == null) continue;
                _hitThisSwing.Add(pick.hurt);

                var dmg = pendingDamage;
                dmg.contactPoint = pick.contact;   // 真正接触身体的点
                dmg.hasContact = true;
                pick.hurt.ReceiveHit(dmg);
                onHit?.Invoke(pick.hurt);
            }
        }

        // OnTriggerEnter 作为兜底（快速穿越的目标）：主逻辑走 Scan
        void OnTriggerEnter(Collider other)
        {
            if (!_col.enabled) return;
            var hurt = other.GetComponent<Hurtbox>();
            if (hurt == null || _hitThisSwing.Contains(hurt)) return;
            if (hurt.OwnerRoot == transform.root) return;
            if (hurt.OwnerRoot == null || _ownersHit.Contains(hurt.OwnerRoot)) return;
            _ownersHit.Add(hurt.OwnerRoot);
            _hitThisSwing.Add(hurt);
            var dmg = pendingDamage;
            dmg.contactPoint = other.ClosestPoint(transform.position);
            dmg.hasContact = true;
            hurt.ReceiveHit(dmg);
            onHit?.Invoke(hurt);
        }
    }
}
