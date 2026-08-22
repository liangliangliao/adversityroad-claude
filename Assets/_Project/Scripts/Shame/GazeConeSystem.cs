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
            var p = FindObjectOfType<PlayerController>();
            if (p == null) return 0;
            int n = 0;
            foreach (var c in _cones)
                if (c != null && c.isActiveAndEnabled && c.Covers(p.transform.position)) n++;
            return n;
        }

        /// <summary>玩家此刻承受的 Exposure 每秒增量（多锥叠加，但按平方根收敛，避免人一多就爆表）。</summary>
        public float ExposureRateOnPlayer()
        {
            var p = FindObjectOfType<PlayerController>();
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

        /// <summary>清空登记（重建世界时调用，避免旧锥体留在表里）。</summary>
        public void Clear() => _cones.Clear();

        public static Material ConeMaterial()
        {
            if (_mat != null) return _mat;
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            if (sh == null) sh = Shader.Find("Standard");
            _mat = new Material(sh) { name = "GazeCone" };
            _mat.color = ConeColor;
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", ConeColor);
            if (_mat.HasProperty("_Surface")) _mat.SetFloat("_Surface", 1f);
            if (_mat.HasProperty("_ZWrite")) _mat.SetInt("_ZWrite", 0);
            _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return _mat;
        }
    }
}
