using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using AdversityRoad.Player;

namespace AdversityRoad.EditorTools
{
    /// <summary>
    /// CI 资产诊断：在 CI 里加载武器/背包资源，把【运行时真实数据】（导入后的节点名、
    /// 网格顶点数、包围盒、轴向实测、剑鞘识别与装配核对）打进构建日志——
    /// 用于远程排查"导入后层级/坐标与源文件不一致"这类只有运行时才能看到的问题，
    /// 不再依赖真机截图往返。由 workflow 的 diagnoseAssets 作业以 buildMethod 调用。
    /// </summary>
    public static class CIDiagnostics
    {
        public static void Run()
        {
            var sb = new StringBuilder("\n===== [CIDIAG] 资产运行时诊断开始 =====\n");
            int exit = 0;
            try
            {
                DiagWeapon(sb, "scene");
                DiagBackpacks(sb);
            }
            catch (System.Exception e)
            {
                sb.Append("[CIDIAG][EXC] ").Append(e).Append('\n');
                exit = 1;
            }
            sb.Append("===== [CIDIAG] 诊断结束 =====\n");
            Debug.Log(sb.ToString());
            EditorApplication.Exit(exit);
        }

        // ---------- 武器（带鞘套件） ----------
        static void DiagWeapon(StringBuilder sb, string name)
        {
            GameObject prefab = null;
            var all = Resources.LoadAll<GameObject>("Characters/Weapons");
            sb.Append("[CIDIAG][武器] 武器库共 ").Append(all.Length).Append(" 个预制体：");
            foreach (var p in all) sb.Append(p != null ? p.name : "null").Append("、");
            sb.Append('\n');
            foreach (var p in all)
                if (p != null && p.name == name) { prefab = p; break; }
            if (prefab == null) { sb.Append("[CIDIAG][武器] 未找到 ").Append(name).Append('\n'); return; }

            var w = Object.Instantiate(prefab);
            try
            {
                sb.Append("[CIDIAG][武器] ").Append(name).Append(" 实例化层级：\n");
                Dump(sb, w.transform, 0);

                var byName = PlayerAppearance.FindDeep(w.transform, "scabbard")
                    ?? PlayerAppearance.FindDeep(w.transform, "sheath")
                    ?? PlayerAppearance.FindDeep(w.transform, "鞘");
                sb.Append("[CIDIAG][武器] 按名识别剑鞘：").Append(byName != null ? byName.name : "落空").Append('\n');

                // 细长件候选表（与 DetectScabbardByGeometry 同口径，便于核对阈值）
                sb.Append("[CIDIAG][武器] 网格候选表（长度/长径比/顶点）：\n");
                foreach (var mf in w.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh; if (m == null) continue;
                    Bounds mb = m.bounds;
                    int ax = mb.size.x >= mb.size.y && mb.size.x >= mb.size.z ? 0 : mb.size.y >= mb.size.z ? 1 : 2;
                    float second = 0f;
                    for (int i = 0; i < 3; i++) if (i != ax) second = Mathf.Max(second, mb.size[i]);
                    Vector3 eA = mb.center, eB = mb.center;
                    eA[ax] = mb.min[ax]; eB[ax] = mb.max[ax];
                    float len = (mf.transform.TransformPoint(eB) - mf.transform.TransformPoint(eA)).magnitude;
                    sb.Append("    ").Append(Path(mf.transform, w.transform))
                      .Append(" 长=").Append(len.ToString("F3"))
                      .Append(" 长径比=").Append((second > 1e-6f ? mb.size[ax] / second : 999f).ToString("F1"))
                      .Append(" 顶点=").Append(m.vertexCount).Append('\n');
                }

                var geo = PlayerAppearance.DetectScabbardByGeometry(w.transform);
                sb.Append("[CIDIAG][武器] 几何识别剑鞘：").Append(geo != null ? Path(geo, w.transform) : "落空").Append('\n');

                var scab = byName != null ? byName : geo;
                if (scab == null) { sb.Append("[CIDIAG][武器] 无剑鞘可用，诊断到此\n"); return; }

                PlayerAppearance.AdoptScabbardAccessories(w.transform, scab);
                var parts = PlayerAppearance.BladeParts(w.transform, scab);
                sb.Append("[CIDIAG][武器] 剑身部件 ").Append(parts.Count).Append(" 件：");
                foreach (var p in parts) sb.Append(Path(p, w.transform)).Append("、");
                sb.Append('\n');
                if (parts.Count == 0) return;

                // 复演装配核心并做世界空间核对（与 SetupSheathedWeapon 同公式）
                PlayerAppearance.LocalBounds(scab, out Bounds sbnd);
                var blade = new GameObject("BladeGroup").transform;
                blade.SetParent(scab, false);
                blade.localPosition = Vector3.zero; blade.localRotation = Quaternion.identity; blade.localScale = Vector3.one;
                foreach (var p in parts) p.SetParent(blade, true);
                PlayerAppearance.LocalBounds(blade, out Bounds bbnd);
                PlayerAppearance.LongAxisEnds(bbnd, out Vector3 ba0, out Vector3 ba1);
                PlayerAppearance.LongAxisEnds(sbnd, out Vector3 sa0, out Vector3 sa1);
                int og = PlayerAppearance.GripEndByModelOrigin(blade, ba0, ba1);
                bool gripAtA = og != 1;   // 诊断口径：0/-1 视为 endA（真实代码另有截面/标记兜底）
                Vector3 gripL = gripAtA ? ba0 : ba1, tipL = gripAtA ? ba1 : ba0;
                Vector3 bDir = tipL - gripL, sDir = sa0 - sa1;
                float bladeLen = bDir.magnitude, scabLen = sDir.magnitude;
                sb.Append("[CIDIAG][武器] 原点判柄=").Append(og)
                  .Append(" 剑长=").Append(bladeLen.ToString("F3"))
                  .Append(" 鞘长=").Append(scabLen.ToString("F3")).Append('\n');
                if (bladeLen > 1e-4f && scabLen > 1e-4f)
                {
                    bDir /= bladeLen; sDir /= scabLen;
                    Quaternion q = Quaternion.FromToRotation(bDir, sDir);
                    Vector3 sLP = (sa0 - sDir * (scabLen * 0.02f)) - (q * tipL);
                    blade.localRotation = q; blade.localPosition = sLP;
                    Vector3 dCtr = blade.TransformPoint(bbnd.center) - scab.TransformPoint(sbnd.center);
                    sb.Append("[CIDIAG][武器] 装配后剑心-鞘心偏差=")
                      .Append(dCtr.magnitude.ToString("F4"))
                      .Append("（鞘长的 ").Append((dCtr.magnitude / scabLen).ToString("F2")).Append("）")
                      .Append(dCtr.magnitude < scabLen * 0.35f ? " ✔入鞘\n" : " ✘偏离\n");
                }
            }
            finally { Object.DestroyImmediate(w); }
        }

        // ---------- 背包 ----------
        static void DiagBackpacks(StringBuilder sb)
        {
            var all = Resources.LoadAll<GameObject>("Characters/Backpacks");
            sb.Append("[CIDIAG][背包] 背包库共 ").Append(all.Length).Append(" 个预制体\n");
            foreach (var prefab in all)
            {
                if (prefab == null) continue;
                // 与运行时同构：包装父节点（模型根可能自带轴向旋转+非均匀缩放，
                // 必须经由子节点 TRS 测量真实视觉几何）
                var holder = new GameObject("BackpackHolder").transform;
                var bp = Object.Instantiate(prefab, holder, false);
                try
                {
                    sb.Append("[CIDIAG][背包] ").Append(prefab.name).Append(" 层级：\n");
                    Dump(sb, holder, 0);
                    if (PlayerAppearance.LocalBounds(holder, out Bounds lb))
                        sb.Append("[CIDIAG][背包] 包装节点空间包围盒 size=").Append(lb.size.ToString("F3"))
                          .Append(" center=").Append(lb.center.ToString("F3")).Append('\n');
                    foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true))
                        if (mf.sharedMesh != null)
                            sb.Append("    网格 ").Append(Path(mf.transform, holder))
                              .Append(" 顶点=").Append(mf.sharedMesh.vertexCount)
                              .Append(" 可读=").Append(mf.sharedMesh.isReadable).Append('\n');
                    bool ok = PlayerAppearance.TryMeasureBackpack(holder,
                        out int thin, out int big, out int strapSign);
                    sb.Append("[CIDIAG][背包] 实测（包装节点空间=视觉几何）：成功=").Append(ok)
                      .Append(" 高轴=").Append("XYZ"[big])
                      .Append(" 厚轴=").Append("XYZ"[thin])
                      .Append(" 肩带朝=").Append(strapSign > 0 ? "+" : "-").Append("XYZ"[thin]).Append('\n');
                }
                finally { Object.DestroyImmediate(holder.gameObject); }
            }
        }

        // ---------- 工具 ----------
        static void Dump(StringBuilder sb, Transform t, int depth)
        {
            sb.Append("    ");
            for (int i = 0; i < depth; i++) sb.Append("  ");
            sb.Append(t.name);
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                sb.Append(" [mesh v=").Append(mf.sharedMesh.vertexCount)
                  .Append(" b=").Append(mf.sharedMesh.bounds.size.ToString("F2")).Append(']');
            var smr = t.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null)
                sb.Append(" [skin v=").Append(smr.sharedMesh.vertexCount).Append(']');
            sb.Append(" P=").Append(t.localPosition.ToString("F2"))
              .Append(" R=").Append(t.localRotation.eulerAngles.ToString("F0"))
              .Append(" S=").Append(t.localScale.ToString("F2")).Append('\n');
            for (int i = 0; i < t.childCount; i++) Dump(sb, t.GetChild(i), depth + 1);
        }

        static string Path(Transform t, Transform root)
        {
            var s = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
