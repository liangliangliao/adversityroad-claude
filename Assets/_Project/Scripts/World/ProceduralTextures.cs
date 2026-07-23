using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.World
{
    /// <summary>表面材质类别：决定运行时生成哪种可平铺纹理。</summary>
    public enum SurfaceKind { None, Plaster, Concrete, Wood, Metal, Ground, Fabric, Brick }

    /// <summary>
    /// 运行时程序化贴图（零美术资产、兼容 CI 无头打包）：
    /// 为每种表面类别生成一张"灰度细节 Albedo"——可无缝平铺、带 mipmap。
    /// 它作为 _BaseMap 与区域配色（_BaseColor）相乘：既保留分区色彩脚本，
    /// 又给墙/地/木/砖/金属加上真实的颗粒、纹理与图案，摆脱"纯哑光塑料"。
    /// 只做 Albedo 细节（安全、跨平台可靠），不做运行时法线贴图（格式风险）。
    /// </summary>
    public static class ProceduralTextures
    {
        const int Res = 256;
        static readonly Dictionary<SurfaceKind, Texture2D> _cache = new Dictionary<SurfaceKind, Texture2D>();

        public static Texture2D Albedo(SurfaceKind kind)
        {
            if (kind == SurfaceKind.None) return null;
            if (_cache.TryGetValue(kind, out var t) && t != null) return t;

            var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, true, false)
            {
                name = "ProcTex_" + kind,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            var px = new Color32[Res * Res];
            for (int y = 0; y < Res; y++)
            {
                float v = (y + 0.5f) / Res;
                for (int x = 0; x < Res; x++)
                {
                    float u = (x + 0.5f) / Res;
                    float g = Mathf.Clamp01(ValueOf(kind, u, v));
                    byte b = (byte)(g * 255f);
                    px[y * Res + x] = new Color32(b, b, b, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            _cache[kind] = tex;
            return tex;
        }

        // ===== 每类别的灰度值函数（输出约 0.55..1.0，作为颜色的乘子）=====

        static float ValueOf(SurfaceKind k, float u, float v)
        {
            switch (k)
            {
                case SurfaceKind.Plaster:  return 0.90f + (Fbm(u, v, 11) - 0.5f) * 0.14f;
                case SurfaceKind.Concrete: return Concrete(u, v);
                case SurfaceKind.Wood:     return Wood(u, v);
                case SurfaceKind.Metal:    return Metal(u, v);
                case SurfaceKind.Ground:   return Ground(u, v);
                case SurfaceKind.Fabric:   return Fabric(u, v);
                case SurfaceKind.Brick:    return Brick(u, v);
                default:                   return 1f;
            }
        }

        static float Concrete(float u, float v)
        {
            float baseN = 0.82f + (Fbm(u, v, 23) - 0.5f) * 0.22f;
            // 偶发的暗色麻点/气孔
            float pit = TN(u, v, 40, 40, 71);
            if (pit > 0.93f) baseN -= (pit - 0.93f) * 6f;
            return baseN;
        }

        static float Wood(float u, float v)
        {
            // 沿 u 的木纹（各向异性：v 方向格子密、u 方向疏），沿 v 的板缝
            float grain = TN(u, v, 5, 34, 41) * 0.5f + TN(u, v, 3, 70, 42) * 0.5f;
            float val = 0.80f + (grain - 0.5f) * 0.26f;
            float planks = Mathf.Repeat(v * 6f, 1f);       // 6 条木板无缝平铺
            float seam = Mathf.Min(planks, 1f - planks);
            if (seam < 0.03f) val -= (0.03f - seam) * 6f;   // 板缝压暗
            return val;
        }

        static float Metal(float u, float v)
        {
            // 拉丝金属：横向细纹，整体较均匀
            float brush = TN(u, v, 4, 96, 51);
            return 0.88f + (brush - 0.5f) * 0.1f;
        }

        static float Ground(float u, float v)
        {
            // 沥青/土地：粗颗粒 + 细砂砾暗点
            float coarse = 0.68f + (Fbm(u, v, 31) - 0.5f) * 0.26f;
            float grit = TN(u, v, 64, 64, 88);
            if (grit > 0.88f) coarse -= (grit - 0.88f) * 2.2f;
            return coarse;
        }

        static float Fabric(float u, float v)
        {
            // 织物/地毯：细密经纬编织
            float weave = 0.5f + 0.5f * (Mathf.Sin(u * Mathf.PI * 2f * 32f) *
                                          Mathf.Sin(v * Mathf.PI * 2f * 32f));
            float n = Fbm(u, v, 61);
            return 0.84f + (weave - 0.5f) * 0.12f + (n - 0.5f) * 0.06f;
        }

        static float Brick(float u, float v)
        {
            const int rows = 7;          // 7 排砖无缝
            const int bricks = 4;        // 每排 4 块
            float ry = v * rows;
            int row = Mathf.FloorToInt(ry);
            float fy = ry - row;
            float offset = (row % 2 == 0) ? 0f : 0.5f;
            float rx = Mathf.Repeat(u * bricks + offset, 1f);
            float fx = rx;
            // 灰浆缝（横 + 竖）
            float mortarV = Mathf.Min(fy, 1f - fy);
            float mortarH = Mathf.Min(fx, 1f - fx);
            float mortar = Mathf.Min(mortarV, mortarH);
            float brickFace = 0.86f + (Fbm(u, v, 17) - 0.5f) * 0.16f;
            if (mortar < 0.06f) return 0.62f + Fbm(u, v, 19) * 0.06f;  // 缝：偏暗
            return brickFace;
        }

        // ===== 可无缝平铺的值噪声（整数格子取模回绕）=====

        static float Fbm(float u, float v, int seed)
        {
            float n = 0f, amp = 0.5f; int cells = 8;
            for (int o = 0; o < 4; o++)
            {
                n += amp * TN(u, v, cells, cells, seed + o * 17);
                amp *= 0.5f; cells *= 2;
            }
            return n; // ≈ 0..1
        }

        static float TN(float u, float v, int cx, int cy, int seed)
        {
            float x = u * cx, y = v * cy;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
            float a = Vlat(xi, yi, cx, cy, seed);
            float b = Vlat(xi + 1, yi, cx, cy, seed);
            float c = Vlat(xi, yi + 1, cx, cy, seed);
            float d = Vlat(xi + 1, yi + 1, cx, cy, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        static float Vlat(int xi, int yi, int cx, int cy, int seed)
        {
            xi = ((xi % cx) + cx) % cx;   // 回绕保证平铺无缝
            yi = ((yi % cy) + cy) % cy;
            uint h = (uint)(xi * 374761393 + yi * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xffffffu) / (float)0xffffffu;
        }
    }
}
