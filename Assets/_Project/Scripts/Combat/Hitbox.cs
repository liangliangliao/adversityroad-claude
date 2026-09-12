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
        // 缓冲要足够大：拆出部位受击框之后，**每个角色身上有 8 个碰撞体**
        // （7 块部位 + 1 个全身兜底），再加上判定盒扫到的墙、地板、道具。
        // 旧的 16/24 格在群战里会被这些东西塞满，OverlapBoxNonAlloc 一旦填满就
        // 直接截断——多出来的目标**被静默丢掉**，表现为"明明砍到人了却没伤害"。
        // 这类漏判查起来极难（没有报错、只是偶尔不生效），宁可多占几百字节。
        static readonly Collider[] _overlap = new Collider[128];

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

        /// <summary>
        /// 这个角色的实测攻击距离（手臂/腿/刃长，见 ReachModel）。
        /// 由拥有者在出招前刷新；valid=false 时一律不裁剪，保持旧行为。
        /// </summary>
        public ReachProfile reach;

        /// <summary>上一次因为够不着而被裁掉的长度（米，诊断用；没裁就是 0）。</summary>
        public float LastReachTrim { get; private set; }

        /// <summary>按招式设置判定框形状：size=(宽X, 高Y, 长Z纵深)，center=相对角色根的偏移。
        /// 每招独立范围——突刺长而窄、横斩横宽、旋风斩/扫堂环身 360°、蓄力/绝招最大。
        ///
        /// 【pose 是必填的，没有默认值】判定框前沿要按这一招真正靠什么够到对方
        /// （手/腿/刃）来裁剪，而那件事只有调用方知道。给它一个默认值，就等于
        /// 允许某个调用点悄悄跳过裁剪——这一轮已经吃够了"某个地方忘了跟上"的亏，
        /// 所以让编译器去记，而不是让我去记。
        /// </summary>
        public void SetShape(Vector3 size, Vector3 center, PoseState pose)
        {
            LastReachTrim = 0f;
            // ===== 够不够得着：按实际的手臂/腿/刃长裁掉判定框够不到的那一截 =====
            // 空手或剑还在鞘里的时候，横斩的判定框照样伸到身前 1.85 米——
            // 那是大剑的长度，不是手臂的长度。这里把前沿收回到真正够得到的地方。
            // 只裁前沿，不动宽高，也不改伤害：招式形状仍然是招式形状。
            if (reach.valid)
            {
                float limit = reach.LimitOf(ReachModel.LimbOf(pose));
                float far = center.z + size.z * 0.5f;
                float near = center.z - size.z * 0.5f;
                // 环身招（旋风斩/扫堂腿：判定框前后都罩住身体）的"距离"是**半径**，
                // 不是前向纵深——够不到身前 2 米，同样也够不到身后 2 米。
                // 对这一类按半径收，而不是只把前沿削掉（那会收成一个偏后的怪形状）。
                if (near < -0.05f && far > 0.05f)
                {
                    float half = Mathf.Min(size.z * 0.5f, limit);
                    if (half < size.z * 0.5f) { LastReachTrim = size.z * 0.5f - half; size.z = half * 2f; }
                    if (size.x * 0.5f > limit) size.x = limit * 2f;
                }
                else if (far > limit)
                {
                    // 近端不动（贴身那一侧本来就该判到），只把远端收回来。
                    if (limit <= near)
                    {
                        // 连近端都够不到：留一片贴身的薄判定，而不是把判定框整个抹掉
                        // （那会变成"贴脸挥拳也没反应"，比够得太远更糟）。
                        LastReachTrim = far - limit;
                        size.z = 0.2f;
                        center.z = Mathf.Max(0.1f, limit) - 0.1f;
                    }
                    else
                    {
                        LastReachTrim = far - limit;
                        size.z = limit - near;
                        center.z = near + size.z * 0.5f;
                    }
                }
            }
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
                ApplyHitQuality(ref dmg, pick);
                pick.hurt.ReceiveHit(dmg);
                onHit?.Invoke(pick.hurt);
            }
        }

        /// <summary>
        /// 命中质量：同一招、打在同一个部位上，也不该都是同一个伤害。
        ///
        /// 【为什么要有这一层】此前判定是二值的——受击框和判定体积有没有相交。
        /// 于是"剑尖擦到胳膊边缘"和"剑刃中段整个划过胸口"结算完全相同；
        /// 贴脸到用剑柄怼在对方身上，也和挥到位是一个价。现实里这三件事差得很远，
        /// 大作也普遍区分（怪物猎人的"肉质+击中位置"、魂系的剑尖判定、
        /// 格斗游戏的 deep hit / 空刃差）。这里用两个能真的从几何算出来的量：
        ///
        /// ① 接触体积占比（≈深度与面积）：受击部位的包围盒有多少体积落在这一刀
        ///    扫过的空间里。擦到边 → 接近 0；整块罩住 → 接近 1。
        ///    它同时代表了"扎进去多深"和"接触面多大"，不必分成两个数各算一遍。
        /// ② 刃位：接触点沿出手方向的相对位置。0 ≈ 贴身（剑柄/肘），
        ///    1 ≈ 够到最远（剑尖）。中段偏前（0.55~0.9）是刃真正吃上力的位置。
        ///
        /// 两者相乘并夹在 0.55~1.25：**最差与最好之间约 2.3 倍**，
        /// 足以让"打得瓷实"和"擦到"读得出来，又不会大到让人觉得伤害在随机跳。
        /// 投射物与心理攻击不走判定框，hitQuality 保持 0，接收端按 1 处理。
        /// </summary>
        void ApplyHitQuality(ref DamageInfo dmg, Pick pick)
        {
            // ① 接触体积占比：用世界 AABB 求交，够用且极便宜（每帧每目标一次）
            var hb = _col.bounds;
            var tb = pick.hurt.GetComponent<Collider>() != null
                ? pick.hurt.GetComponent<Collider>().bounds : hb;
            float ox = Mathf.Max(0f, Mathf.Min(hb.max.x, tb.max.x) - Mathf.Max(hb.min.x, tb.min.x));
            float oy = Mathf.Max(0f, Mathf.Min(hb.max.y, tb.max.y) - Mathf.Max(hb.min.y, tb.min.y));
            float oz = Mathf.Max(0f, Mathf.Min(hb.max.z, tb.max.z) - Mathf.Max(hb.min.z, tb.min.z));
            float tvol = Mathf.Max(1e-4f, tb.size.x * tb.size.y * tb.size.z);
            float area01 = Mathf.Clamp01(ox * oy * oz / tvol);

            // ② 刃位：接触点沿【出手方向】投影，除以这一招够得到的最远距离。
            //    最远距离由判定框自己的几何给出（localPosition.z + 深度的一半），
            //    所以突刺、横斩、旋风斩各自的"剑尖"位置都是对的，不需要另建一张表。
            var root = transform.root;
            float reach01 = 0.5f;
            if (root != null && _box != null)
            {
                float maxReach = Mathf.Max(0.15f, transform.localPosition.z + _box.size.z * 0.5f);
                Vector3 toHit = pick.contact - root.position;
                reach01 = Mathf.Clamp01(Vector3.Dot(toHit, root.forward) / maxReach);
            }

            // 接触体积：擦到边 0.82 倍 → 罩满 1.12 倍
            float areaMult = Mathf.Lerp(0.82f, 1.12f, Mathf.Sqrt(Mathf.Clamp01(area01 * 2.2f)));
            // 刃位：贴身（剑柄）0.80 倍 → 中前段（0.75）1.12 倍 → 极限距离回落到 0.95
            float reachMult = reach01 < 0.35f
                ? Mathf.Lerp(0.80f, 1.0f, reach01 / 0.35f)
                : reach01 < 0.75f
                    ? Mathf.Lerp(1.0f, 1.12f, (reach01 - 0.35f) / 0.40f)
                    : Mathf.Lerp(1.12f, 0.95f, (reach01 - 0.75f) / 0.25f);

            dmg.hitArea01 = area01;
            dmg.hitReach01 = reach01;
            dmg.hitQuality = Mathf.Clamp(areaMult * reachMult, 0.55f, 1.25f);
        }

        /// <summary>命中质量的可读标签（伤害数字旁边显示，让玩家看得出差在哪儿）。</summary>
        public static string QualityLabel(DamageInfo d)
        {
            if (d.hitQuality <= 0.001f) return "";
            if (d.hitReach01 < 0.32f) return "贴身·力不透";     // 用剑柄/肘部怼上去
            if (d.hitQuality >= 1.12f) return "刃中·扎实";       // 中前段刃、罩得满
            if (d.hitQuality <= 0.82f) return "擦到";
            return "";
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
