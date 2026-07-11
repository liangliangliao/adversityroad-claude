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
        [Tooltip("开始淡出的镜头距离")] public float startDist = 2.1f;
        [Tooltip("最透时的镜头距离")] public float minDist = 0.75f;
        [Range(0f, 1f)] public float minAlpha = 0.12f;
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
            foreach (var ec in FindObjectsOfType<AI.EnemyController>())
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
                    // 镜头到躯干竖线段（脚→头）的最近距离（随标准体型 4.1m）
                    float h = 3.9f * Mathf.Max(0.4f, e.root.lossyScale.y);
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
                Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                c.a = e.alpha;
                m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            }
        }
    }
}
