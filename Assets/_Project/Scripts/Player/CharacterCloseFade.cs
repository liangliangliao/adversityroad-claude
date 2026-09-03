using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 近镜角色淡出（大作标配的镜头保护）：镜头贴近任何角色（玩家或敌人）时，
    /// 把该角色整体淡为半透明——距离越近越透，移开立即淡回。
    /// 根治近身缠斗/贴墙回缩时"整张屏幕被白色模型糊住/镜头穿进身体"的问题：
    /// 镜头碰撞不再需要缩进角色身体也能保画面，角色挡镜时玩家永远看得见战场。
    /// 距离按「镜头到角色躯干竖线段」计算（脚到头），比只算根位置精准。
    /// </summary>
    public class CharacterCloseFade : MonoBehaviour
    {
        public Transform player;
        // 【再收一档：1.35 → 0.80】
        // 玩家报"在住所出入房门时角色皮肤失真、发虚"，截图 HUD 上写着吊杆 1.06m——
        // 门框把镜头挤到 1 米，代入 InverseLerp(0.75, 1.35, 1.0) ≈ 0.42，
        // 角色被**真的画成 42% 不透明度**。那不是观感问题，是它按设计在淡出，
        // 只是触发距离定得太宽：1 米外镜头离躯干还有大半个身位，人完全看得清，
        // 根本不需要让开。
        //
        // 这条淡出存在的唯一理由是"镜头要钻进身体了"。角色胶囊半径约 0.34m，
        // 所以真正需要让开的是 0.4m 以内；0.8m 起淡、0.4m 全透，
        // 既保住原本的职责，又不会在门口、贴墙、家具旁这些吊杆本来就短的地方
        // 把人淡掉——而住所里到处都是这种地方。
        [Tooltip("开始淡出的镜头距离")] public float startDist = 0.80f;
        [Tooltip("最透时的镜头距离")] public float minDist = 0.40f;
        /// <summary>最透时的不透明度。
        /// 【为什么必须是 0】室内镜头被家具挡住时会一路回缩到 0.5 米（那是对的，
        /// 否则镜头会卡在柜子里）。停在 0.32 的半透明上，画面就是**一张占满屏幕的
        /// 半透明大脸**——玩家说的"脸模糊不清"。要么看得清，要么彻底让开，
        /// 半透明的大脸是两头不讨好。</summary>
        [Range(0f, 1f)] public float minAlpha = 0f;
        public float fadeSpeed = 7f;

        class Entry
        {
            public Transform root;
            public Renderer[] renderers;
            public float alpha = 1f;
            public bool isPlayer;
        }

        readonly List<Entry> _entries = new List<Entry>();
        ThirdPersonCamera _tpc;
        float _rescanAt;

        void Awake() => _tpc = GetComponent<ThirdPersonCamera>();

        void Rescan()
        {
            // 保留已跟踪条目的 alpha，重建渲染器列表（角色可能中途生成/销毁/换装）
            var old = new Dictionary<Transform, float>();
            foreach (var e in _entries)
                if (e.root != null) old[e.root] = e.alpha;
            _entries.Clear();

            if (player != null) AddEntry(player, true, old);
            foreach (var ec in AdversityRoad.Core.ActorRegistry.Enemies)
                AddEntry(ec.transform, false, old);
        }

        void AddEntry(Transform root, bool isPlayer, Dictionary<Transform, float> old)
        {
            var list = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is TrailRenderer || r is LineRenderer || r is ParticleSystemRenderer) continue;
                if (r.GetComponent<TextMesh>() != null) continue;   // 浮字/警示不参与淡出
                if (r.GetComponentInParent<Canvas>() != null) continue;
                list.Add(r);
            }
            if (list.Count == 0) return;
            _entries.Add(new Entry
            {
                root = root,
                renderers = list.ToArray(),
                isPlayer = isPlayer,
                alpha = old.TryGetValue(root, out float a) ? a : 1f
            });
        }

        void LateUpdate()
        {
            if (Time.unscaledTime > _rescanAt)
            {
                _rescanAt = Time.unscaledTime + 0.6f;
                Rescan();
            }

            Vector3 cam = transform.position;
            float dt = Time.unscaledDeltaTime;
            bool fp = _tpc != null && _tpc.FirstPerson;

            foreach (var e in _entries)
            {
                if (e.root == null) continue;
                // 第一人称模式玩家本体不淡出（要看见自己的手脚兵器）
                float want = 1f;
                if (!(fp && e.isPlayer))
                {
                    // 镜头到躯干竖线段（脚→头）的最近距离（随标准体型 TargetHeight）
                    float h = Combat.MecanimCharacter.TargetHeight * Mathf.Max(0.4f, e.root.lossyScale.y);
                    Vector3 feet = e.root.position - Vector3.up * (h * 0.5f);
                    float t = Mathf.Clamp(cam.y - feet.y, 0f, h);
                    Vector3 closest = feet + Vector3.up * t;
                    float d = Vector3.Distance(cam, closest);
                    float k = Mathf.InverseLerp(minDist, startDist, d);   // 0=贴脸 1=够远
                    want = Mathf.Lerp(minAlpha, 1f, k);
                }

                float next = Mathf.MoveTowards(e.alpha, want, fadeSpeed * dt);
                if (Mathf.Abs(next - e.alpha) < 0.001f && next >= 0.999f) continue;
                e.alpha = next;
                Apply(e);
            }
        }

        /// <summary>把 alpha 写进材质——URP/Lit 与 glTFast 两套属性名都试。</summary>
        static void SetAlpha(Material m, float a)
        {
            foreach (var prop in new[] { "_BaseColor", "_Color", "baseColorFactor" })
            {
                if (!m.HasProperty(prop)) continue;
                Color c = m.GetColor(prop);
                c.a = a;
                m.SetColor(prop, c);
            }
        }

        static void Apply(Entry e)
        {
            bool opaque = e.alpha >= 0.999f;
            foreach (var r in e.renderers)
            {
                if (r == null) continue;
                var m = r.material;
                if (opaque)
                {
                    CameraOcclusionFade.SetOpaque(m);
                    continue;
                }
                CameraOcclusionFade.SetTransparent(m);
                // 【属性名必须两套都写】角色·贰是 glTFast 的 Shader Graph，
                // 它的底色属性叫 baseColorTexture/baseColorFactor 这一套，
                // 没有 _BaseColor 也没有 _Color。只写 URP/Lit 的名字，
                // 在它身上就是半套生效：渲染队列被挪到透明层了，alpha 却没写进去。
                SetAlpha(m, e.alpha);
            }
        }
    }
}
