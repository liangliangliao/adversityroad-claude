using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 遮挡淡出：当树木等物体挡在镜头与玩家之间时，把它淡化为半透明，
    /// 让玩家与战斗始终可见；物体移开后再淡回不透明。
    ///
    /// 为什么用「登记表」而不是射线：树冠的碰撞体在生成时被移除（不挡人/不挡导航），
    /// 物理射线打不到；因此改由场景生成器把可遮挡物登记进来，本组件用
    /// 「镜头→玩家」线段到物体包围盒的距离判断遮挡，无需碰撞体、稳定可控。
    /// </summary>
    public class CameraOcclusionFade : MonoBehaviour
    {
        public Transform target;                 // 玩家
        [Range(0f, 1f)] public float fadedAlpha = 0.22f;
        public float fadeSpeed = 6f;             // 透明度过渡速度
        public float focusHeight = 1.2f;         // 取景点抬到胸口，避免只算脚底

        static readonly List<Renderer> _occluders = new List<Renderer>();
        readonly Dictionary<Renderer, float> _alpha = new Dictionary<Renderer, float>();
        readonly List<Renderer> _scratch = new List<Renderer>();

        /// <summary>场景生成器登记一个可被镜头淡化的遮挡物（如树冠/树干）。</summary>
        public static void RegisterOccluder(Renderer r)
        {
            if (r != null && !_occluders.Contains(r)) _occluders.Add(r);
        }

        /// <summary>重建世界时清空登记表（旧物体已销毁）。</summary>
        public static void ClearOccluders() => _occluders.Clear();

        void LateUpdate()
        {
            if (target == null) return;
            Vector3 camPos = transform.position;
            Vector3 focus = target.position + Vector3.up * focusHeight;
            Vector3 seg = focus - camPos;
            float segLen = seg.magnitude;
            if (segLen < 0.01f) return;
            Vector3 dir = seg / segLen;

            // 本帧正在遮挡的物体 → 目标透明；其余（含正在恢复的）→ 目标不透明
            for (int i = _occluders.Count - 1; i >= 0; i--)
            {
                var r = _occluders[i];
                if (r == null) { _occluders.RemoveAt(i); continue; }
                if (IsBlocking(r, camPos, dir, segLen))
                    _alpha[r] = _alpha.TryGetValue(r, out float a) ? a : 1f;   // 确保被跟踪
            }

            // 推进所有被跟踪物体的透明度
            _scratch.Clear();
            foreach (var kv in _alpha) _scratch.Add(kv.Key);
            foreach (var r in _scratch)
            {
                if (r == null) { _alpha.Remove(r); continue; }
                bool blocking = IsBlocking(r, camPos, dir, segLen);
                float want = blocking ? fadedAlpha : 1f;
                float cur = Mathf.MoveTowards(_alpha[r], want, fadeSpeed * Time.unscaledDeltaTime);
                _alpha[r] = cur;
                ApplyAlpha(r, cur);
                if (cur >= 0.999f && !blocking)   // 完全恢复且不再遮挡 → 还原不透明并停止跟踪
                {
                    SetOpaque(r.material);
                    _alpha.Remove(r);
                }
            }
        }

        bool IsBlocking(Renderer r, Vector3 camPos, Vector3 dir, float segLen)
        {
            Vector3 c = r.bounds.center;
            float t = Vector3.Dot(c - camPos, dir);          // 沿视线的投影距离
            if (t < 0.3f || t > segLen - 0.4f) return false; // 只算真正夹在中间的
            Vector3 closest = camPos + dir * t;
            float radius = r.bounds.extents.magnitude * 0.85f;
            return (c - closest).sqrMagnitude < radius * radius;
        }

        void ApplyAlpha(Renderer r, float a)
        {
            var m = r.material;                 // 取实例（不影响共享材质，避免整片树一起变透明）
            if (a < 0.999f) SetTransparent(m);
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            c.a = a;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        /// <summary>把材质切到半透明混合（近镜角色淡出等复用）。</summary>
        public static void SetTransparent(Material m)
        {
            if (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f) return; // 已是透明
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);              // URP: 0 不透明 / 1 透明
            if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3f);                    // Standard: Transparent
            m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>把材质切回不透明（近镜角色淡出等复用）。</summary>
        public static void SetOpaque(Material m)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 1);
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 0f);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_SURFACE_TYPE_OPAQUE");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            c.a = 1f;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
    }
}
