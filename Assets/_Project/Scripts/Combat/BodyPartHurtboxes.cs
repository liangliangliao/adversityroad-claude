using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 部位受击框：把一具「一个胶囊罩全身」的受击体，拆成头/胸/腰腹/双臂/双腿七块，
    /// 各自挂在**对应骨骼**下，于是它们跟着动画走——抬手格挡时手臂真的抬起来，
    /// 弯腰时头真的低下去，扫腿真的只能扫到腿。
    ///
    /// 【为什么不靠"命中点高度"分区就够了】
    /// 之前敌人侧确实按接触点高度粗分过三档（头>0.5、腿<-0.45）。问题是它把
    /// **动作**完全排除在外：角色下蹲、倒地、跳起、被击飞的时候，同一个世界高度
    /// 对应的部位完全不同；而且玩家侧一档都没有——玩家不论被打中哪儿都是一个价。
    /// 挂在骨骼上的受击框没有这个问题：部位是骨骼自己走过来的，不是猜出来的。
    ///
    /// 【与全身胶囊的关系】
    /// 全身胶囊保留为**兜底受击框**（specificity=0）：部位框覆盖不到的缝隙照样能打中，
    /// 绝不会出现"明明砍上去了却没判定"。同一次挥击同时罩到兜底与部位时，
    /// <see cref="Hitbox"/> 只取最精确的那一块结算（见其 Scan 的择优规则）。
    ///
    /// 【自愈】外观可以在运行时重建（换装/换模型），骨骼引用会随之失效。
    /// 这里每 0.5 秒验一次，发现骨骼没了就重新装配——不需要谁记得来通知它。
    /// </summary>
    public class BodyPartHurtboxes : MonoBehaviour
    {
        /// <summary>角色标准身高（米）：部位框尺寸按它等比缩放。</summary>
        public float bodyHeight = 2f;

        /// <summary>
        /// 部位受击框专用层（ProjectSettings 里命名为 BodyPart）。
        ///
        /// 【为什么必须单独占一层】
        /// 场景里的触发体（拖延泥潭、噪声区、传送门、室内区…）判定"谁进来了"
        /// 用的是 <c>other.GetComponentInParent&lt;PlayerController&gt;()</c>——
        /// 也就是说**角色身上每多一个碰撞体，这些区域就会多收到一次进入/离开事件**。
        /// 七块部位框意味着：一只手伸出泥潭边缘就触发一次 OnTriggerExit，
        /// 减速被提前解除；抬手放手来回几次，区域效果就彻底乱套。
        ///
        /// 解法不是去改那十几个区域，而是让部位框**根本不产生触发事件**：
        /// 把它们放到一个与所有层都不发生碰撞的层上。
        /// 而攻击判定走的是 <c>Physics.OverlapBox</c> 主动查询——
        /// 主动查询只看层遮罩、不看碰撞矩阵，照样能扫到它们。
        /// 于是"打得中"与"不打扰场景触发体"两件事同时成立。
        /// </summary>
        public const int Layer = 30;

        static bool _layerIsolated;

        static void EnsureLayerIsolated()
        {
            if (_layerIsolated) return;
            _layerIsolated = true;
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Layer, i, true);
        }

        readonly List<Transform> _built = new List<Transform>();
        Transform _anchorProbe;      // 装配时用过的骨骼之一：它没了就说明外观重建过
        float _nextVerify;

        /// <summary>在角色根上装配部位受击框（已有则复用）。</summary>
        public static BodyPartHurtboxes Attach(Transform root, float bodyHeight = 2f)
        {
            if (root == null) return null;
            var c = root.GetComponent<BodyPartHurtboxes>();
            if (c == null) c = root.gameObject.AddComponent<BodyPartHurtboxes>();
            c.bodyHeight = bodyHeight;
            EnsureLayerIsolated();
            c.Rebuild();
            return c;
        }

        void Update()
        {
            if (Time.time < _nextVerify) return;
            // 骨骼被销毁（外观重建）→ 重新装配。
            // 重试间隔随失败次数拉长：万一某个角色根本没有可识别的骨骼，
            // 也不会每半秒白扫一次层级（那会在群战时累积成可观的开销）。
            if (_anchorProbe == null || _built.Count == 0)
            {
                Rebuild();
                _failStreak = _built.Count > 0 ? 0 : _failStreak + 1;
            }
            _nextVerify = Time.time + Mathf.Min(0.5f * (1 + _failStreak), 8f);
        }

        int _failStreak;

        void Rebuild()
        {
            foreach (var t in _built) if (t != null) Destroy(t.gameObject);
            _built.Clear();
            _anchorProbe = null;

            var bones = ResolveBones();
            float s = Mathf.Max(0.5f, bodyHeight) / 2f;   // 以 2m 为基准的体型比例

            // 头：球。半径给足（0.19×s）——头本来就该比"数学上的头"稍大一点，
            // 否则会心区小到玩家永远打不到，等于这条规则不存在。
            Add(bones.head, BodyPart.Head, new Vector3(0, 0.1f * s, 0), 0.19f * s, 0f);
            // 胸：立方带高度，罩住肩到腰
            Add(bones.chest, BodyPart.Chest, new Vector3(0, 0.16f * s, 0), 0.22f * s, 0.34f * s);
            // 腰腹
            Add(bones.hips, BodyPart.Abdomen, new Vector3(0, 0.06f * s, 0), 0.21f * s, 0.24f * s);
            // 上臂（挂在上臂骨上，沿骨延伸罩住前臂）
            Add(bones.armL, BodyPart.ArmL, new Vector3(0, -0.18f * s, 0), 0.11f * s, 0.42f * s);
            Add(bones.armR, BodyPart.ArmR, new Vector3(0, -0.18f * s, 0), 0.11f * s, 0.42f * s);
            // 大腿（沿骨罩到小腿）
            Add(bones.legL, BodyPart.LegL, new Vector3(0, -0.24f * s, 0), 0.13f * s, 0.5f * s);
            Add(bones.legR, BodyPart.LegR, new Vector3(0, -0.24f * s, 0), 0.13f * s, 0.5f * s);
        }

        /// <summary>挂一块部位受击框到骨骼下。height=0 用球，否则用胶囊（沿骨骼 Y 轴）。</summary>
        void Add(Transform bone, BodyPart part, Vector3 localOffset, float radius, float height)
        {
            if (bone == null) return;
            if (_anchorProbe == null) _anchorProbe = bone;

            var go = new GameObject("Hurt_" + part);
            go.layer = Layer;   // 不产生任何触发事件，只被攻击判定的主动查询扫到
            go.transform.SetParent(bone, false);
            go.transform.localPosition = localOffset;
            go.transform.localRotation = Quaternion.identity;

            // 骨骼自身可能被整体缩放（模型贴合身高时会缩放）：把世界尺寸换算回局部
            Vector3 ls = bone.lossyScale;
            float sc = Mathf.Max(0.0001f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);
            go.transform.localScale = Vector3.one / sc;

            if (height <= 0.001f)
            {
                var col = go.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = radius;
            }
            else
            {
                var col = go.AddComponent<CapsuleCollider>();
                col.isTrigger = true;
                col.radius = radius;
                col.height = Mathf.Max(height, radius * 2f);
                col.direction = 1;   // 沿骨骼 Y
            }

            var hb = go.AddComponent<Hurtbox>();
            hb.part = part;
            hb.specificity = 1;      // 比全身兜底框更精确：同一次挥击择优时胜出
            _built.Add(go.transform);
        }

        // ===================== 骨骼解析 =====================

        struct Bones { public Transform head, chest, hips, armL, armR, legL, legR; }

        Bones ResolveBones()
        {
            var b = new Bones();

            // ① 程序化方块骨骼：直接拿 rig 的关节引用（名字有歧义，不能靠关键词猜）
            var poser = GetComponentInChildren<HumanoidAnimator>();
            if (poser != null && poser.rig != null && poser.rig.head != null)
            {
                var r = poser.rig;
                b.head = r.head; b.chest = r.torso; b.hips = r.pelvis;
                b.armL = r.shoulderL; b.armR = r.shoulderR;
                b.legL = r.hipL; b.legR = r.hipR;
                return b;
            }

            // ② 动捕骨架：按关键词找（mixamorig 与异源骨架都能命中）
            var all = GetComponentsInChildren<Transform>(true);
            b.head = Find(all, "head");
            b.chest = Find(all, "spine2", "spine1", "spine", "chest", "torso");
            b.hips = Find(all, "hips", "pelvis");
            b.armL = Find(all, "leftarm", "lupperarm", "upperarml", "shoulderl");
            b.armR = Find(all, "rightarm", "rupperarm", "upperarmr", "shoulderr");
            b.legL = Find(all, "leftupleg", "leftthigh", "upperlegl", "hipl");
            b.legR = Find(all, "rightupleg", "rightthigh", "upperlegr", "hipr");
            return b;
        }

        /// <summary>按关键词优先级找骨骼；同词多个候选取名字最短的
        /// （mixamorigHead 优先于 mixamorigHeadTopEnd）。</summary>
        static Transform Find(Transform[] all, params string[] keys)
        {
            foreach (var k in keys)
            {
                Transform best = null;
                int bestLen = int.MaxValue;
                foreach (var t in all)
                {
                    if (t == null) continue;
                    string n = Norm(t.name);
                    if (!n.Contains(k) || n.Length >= bestLen) continue;
                    bestLen = n.Length; best = t;
                }
                if (best != null) return best;
            }
            return null;
        }

        static string Norm(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }
    }
}
