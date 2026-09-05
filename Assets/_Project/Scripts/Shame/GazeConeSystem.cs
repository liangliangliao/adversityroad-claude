using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Player;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 一枚视线锥（方案 8.6.1 / 8.13.1 GazeConeSystem）。
    ///
    /// 【注视必须永远可读】
    /// 8.7.1 明写：禁止把视线锥做成不可见或不可预测。所以锥体是一块**真的画出来的
    /// 地面扇形**，随持有者头部朝向实时转动，玩家一眼就能读出"哪里被看着"。
    /// 隐形注视在本章是被禁止的设计，不是可选的表现风格。
    ///
    /// 【它不造成伤害】
    /// 锥内只提升 Exposure。低语与注视本身不掉血——那样只会变成一个无法消除的
    /// 骚扰伤害源（8.15 风险表）。压力来自"被看见时还要把事做完"，不是来自读秒扣血。
    /// </summary>
    public class GazeCone : MonoBehaviour
    {
        public GazeConeData data = new GazeConeData();

        /// <summary>头部朝向来源（NPC 的头/身体）。为空则用自身 forward。</summary>
        public Transform headSource;

        /// <summary>被强制指向的目标（Boss 的「凝视」阶段用）。为空则按头部朝向。</summary>
        public Transform aimAt;

        Mesh _mesh;
        MeshRenderer _renderer;
        Transform _visual;
        float _pulse;

        public bool Covers(Vector3 worldPos)
        {
            Vector3 origin = transform.position;
            Vector3 fwd = Facing();
            Vector3 to = worldPos - origin;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > data.range || dist < 0.05f) return dist < 0.05f;
            return Vector3.Angle(fwd, to.normalized) <= data.angle * 0.5f;
        }

        Vector3 Facing()
        {
            if (aimAt != null)
            {
                Vector3 toTarget = aimAt.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.01f) return toTarget.normalized;
            }
            Transform src = headSource != null ? headSource : transform;
            Vector3 f = src.forward;
            f.y = 0f;
            return f.sqrMagnitude < 0.001f ? Vector3.forward : f.normalized;
        }

        void Start()
        {
            BuildVisual();
            GazeConeSystem.Ensure().Register(this);
        }

        void OnDestroy()
        {
            if (GazeConeSystem.Instance != null) GazeConeSystem.Instance.Unregister(this);
        }

        void BuildVisual()
        {
            var go = new GameObject("GazeConeVisual");
            _visual = go.transform;
            _visual.SetParent(transform, false);
            _visual.localPosition = new Vector3(0, 0.06f, 0);

            _mesh = BuildFanMesh(data.angle, data.range);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.sharedMaterial = GazeConeSystem.ConeMaterial();
        }

        static Mesh BuildFanMesh(float angleDeg, float range)
        {
            const int Seg = 18;
            var verts = new Vector3[Seg + 2];
            var tris = new int[Seg * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i <= Seg; i++)
            {
                float t = (float)i / Seg;
                float a = (-angleDeg * 0.5f + angleDeg * t) * Mathf.Deg2Rad;
                verts[i + 1] = new Vector3(Mathf.Sin(a) * range, 0f, Mathf.Cos(a) * range);
            }
            for (int i = 0; i < Seg; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = i + 2;
            }
            var m = new Mesh { name = "GazeConeFan" };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        void Update()
        {
            if (_visual == null) return;
            // 锥体贴着地面转：只取水平朝向，不跟着抬头低头翻上天
            _visual.rotation = Quaternion.LookRotation(Facing(), Vector3.up);

            // 可见度有下限：越靠近玩家越亮，但永远不会灭（禁止隐形注视）
            float vis = Mathf.Clamp(data.visibility, 0.35f, 1f);
            _pulse += Time.deltaTime;
            float breathe = 0.85f + Mathf.Sin(_pulse * 1.6f) * 0.15f;
            if (_renderer != null && _renderer.sharedMaterial != null)
            {
                var c = GazeConeSystem.ConeColor;
                c.a *= vis * breathe;
                _renderer.material.color = c;
                if (_renderer.material.HasProperty("_BaseColor"))
                    _renderer.material.SetColor("_BaseColor", c);
            }
        }
    }

    /// <summary>
    /// 视线锥总控（方案 8.13.1 GazeConeSystem）：登记全场锥体、算出玩家此刻承受的
    /// Exposure 增速，并把"被谁看着"这件事变成一个可被别的系统查询的事实。
    /// </summary>
    public class GazeConeSystem : MonoBehaviour
    {
        public static GazeConeSystem Instance { get; private set; }

        public static readonly Color ConeColor = new Color(0.95f, 0.86f, 0.55f, 0.16f);

        readonly List<GazeCone> _cones = new List<GazeCone>();
        static Material _mat;

        public static GazeConeSystem Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("GazeConeSystem");
            Instance = go.AddComponent<GazeConeSystem>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Register(GazeCone c) { if (c != null && !_cones.Contains(c)) _cones.Add(c); }
        public void Unregister(GazeCone c) { _cones.Remove(c); }

        public int ConeCount => _cones.Count;

        /// <summary>玩家此刻同时被几只锥覆盖（交叉视线区在 8-2 是设计上的难点区）。</summary>
        public int ConesOnPlayer()
        {
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p == null) return 0;
            int n = 0;
            foreach (var c in _cones)
                if (c != null && c.isActiveAndEnabled && c.Covers(p.transform.position)) n++;
            return n;
        }

        /// <summary>玩家此刻承受的 Exposure 每秒增量（多锥叠加，但按平方根收敛，避免人一多就爆表）。</summary>
        public float ExposureRateOnPlayer()
        {
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p == null) return 0f;
            float sum = 0f;
            int n = 0;
            foreach (var c in _cones)
            {
                if (c == null || !c.isActiveAndEnabled) continue;
                if (!c.Covers(p.transform.position)) continue;
                sum += c.data.exposureRate;
                n++;
            }
            if (n <= 1) return sum;
            return sum / Mathf.Sqrt(n);
        }

        /// <summary>某个世界坐标是否落在任意一只锥内——目标交互物用它自检"我在不在注视里"。</summary>
        public bool IsWatched(Vector3 worldPos)
        {
            foreach (var c in _cones)
                if (c != null && c.isActiveAndEnabled && c.Covers(worldPos)) return true;
            return false;
        }

        /// <summary>
        /// 全场锥体收拢向一个目标（后排低语者阶段一「凝视」）：
        /// 锥口变窄、射程变长、增速上调——它不是伤害技，它只是让"没被看见的角落"消失。
        /// </summary>
        public void FocusOn(Transform target, float rateMultiplier)
        {
            foreach (var c in _cones)
            {
                if (c == null) continue;
                c.aimAt = target;
                c.data.exposureRate = Mathf.Min(40f, c.data.exposureRate * rateMultiplier);
            }
        }

        /// <summary>解除收拢：锥体回到各自的头部朝向。</summary>
        public void ReleaseFocus(float rateMultiplier)
        {
            foreach (var c in _cones)
            {
                if (c == null) continue;
                c.aimAt = null;
                c.data.exposureRate = Mathf.Max(2f, c.data.exposureRate * rateMultiplier);
            }
        }

        // ---- 注视的补位（侧目者被打倒之后）----
        struct PendingRelay
        {
            public Vector3 at;
            public Vector3 lookAt;
            public float dueAt;
        }

        readonly List<PendingRelay> _relays = new List<PendingRelay>();

        /// <summary>侧目者被打倒后，多久有人从别处补上这道视线。</summary>
        public const float RelayDelay = 45f;

        /// <summary>补位的下限：场上少于这么多道注视时才会有人接上。</summary>
        public const int MinGaze = 2;

        /// <summary>
        /// 登记一次"注视补位"。
        ///
        /// 【为什么打倒侧目者不能一劳永逸】
        /// 这一章的命题是"被看见的同时仍能行动"，不是"把看你的人清干净"。
        /// 低语链断了 8 秒后从另一处重建，注视同理：打倒它换来的是
        /// 20 秒的空窗（真实的战术回报，够做完一次长按目标动作），
        /// 而不是永远没人看你——那会让三个目标物"全在锥内"这条布局失效。
        /// </summary>
        public void ScheduleRelay(Vector3 deadAt, Vector3 lookAt)
        {
            _relays.Add(new PendingRelay
            {
                at = deadAt,
                lookAt = lookAt,
                dueAt = Time.time + RelayDelay,
            });
        }

        void Update()
        {
            for (int i = _relays.Count - 1; i >= 0; i--)
            {
                if (Time.time < _relays[i].dueAt) continue;
                var r = _relays[i];
                _relays.RemoveAt(i);
                Respawn(r);
            }
        }

        /// <summary>场上还活着几道注视。</summary>
        int LiveCones()
        {
            int n = 0;
            foreach (var c in _cones) if (c != null && c.isActiveAndEnabled) n++;
            return n;
        }

        void Respawn(PendingRelay r)
        {
            // 【补位要克制】不断刷新的敌人是最容易被读成"打不死"的东西。
            // 只有当场上注视已经少于两道时才补一个回来——玩家清掉一两个侧目者
            // 必须换来实打实的喘息，而不是"刚打完又站起来一个"。
            if (LiveCones() >= MinGaze) return;

            // 从**别处**站出来：沿原位横向挪开几米，读作"有人换了个位置继续看"
            Vector3 side = Vector3.Cross(Vector3.up, (r.lookAt - r.at).normalized);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            Vector3 want = r.at + side.normalized * (Random.value > 0.5f ? 4.5f : -4.5f);

            var go = AI.EnemySpawnHook.SpawnNear(AI.EnemyType.SideGlancer, AI.EnemyTier.Novice, want);
            if (go == null) return;
            Vector3 dir = r.lookAt - go.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Core.GameEvents.RaiseSubtitle("另一个人抬起了头——注视换了个位置，没有消失。");
        }

        /// <summary>
        /// 统一设置全场锥体的可见度（压力阶段映射用，见 ShameStressMapping）。
        /// 真正的下限由 GazeCone 自己兜住：注视可以更清楚，但不可能变成隐形。
        /// </summary>
        public void SetVisibility(float v)
        {
            foreach (var c in _cones)
                if (c != null) c.data.visibility = Mathf.Clamp(v, 0.35f, 1f);
        }

        /// <summary>清空登记（重建世界时调用，避免旧锥体留在表里）。</summary>
        public void Clear() => _cones.Clear();

        public static Material ConeMaterial()
        {
            if (_mat != null) return _mat;
            // 兜底链交给 SafeShader：这里原来自己写了一条 Shader.Find 链，最后一档是
            // Standard——URP 下不受支持，真机包里也根本没有这个 shader，落到那一档就是洋红。
            _mat = World.SafeShader.Unlit(ConeColor, "gaze");
            _mat.name = "GazeCone";
            return _mat;
        }
    }
}
