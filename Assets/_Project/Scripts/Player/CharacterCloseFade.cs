using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 近镜角色让位（镜头保护）：镜头快要钻进某个角色身体时，把它整体收起来
    /// （只留影子），移开立刻恢复。根治"镜头穿进身体 / 整张屏幕被模型糊住"。
    ///
    /// 【为什么是"收起来"而不是"淡出"——这一版的重点】
    /// 上一版是渐进半透明：距离越近越透。玩家的截图证明这条路走不通——
    /// 门口吊杆 0.74m 时角色被画成八成不透明度，画面上能**透过脸看到耳朵、
    /// 透过下巴看到脖子、透过肩膀看到夹克内面**，脸整个是花的。
    /// 那不是"淡"，是坏掉了。
    ///
    /// 原因是半透明渲染必须关掉深度写入（ZWrite=0，见 CameraOcclusionFade
    /// .SetTransparent）。对一个箱子、一根柱子这类凸的环境物件没问题；
    /// 但角色是自我遮挡极重的网格——关掉深度之后，同一个人的正面与背面、
    /// 外套的外面与里面、五官与后脑勺全都按提交顺序胡乱混在一起。
    /// **任何**不为零的半透明度都会出现这个现象，越透越明显，
    /// 所以调阈值只能让它少发生，不可能让它变对。
    ///
    /// 成熟做法要么是抖动/屏幕门透明（保留深度写入，需要改着色器，
    /// 而本项目的角色横跨 URP/Lit 与 glTFast 两套着色器，运行时改不动），
    /// 要么就是**二值收起**。这里选后者：
    ///   · 阈值定在"镜头确实要进身体了"（角色胶囊半径约 0.34m）；
    ///   · 到那个距离角色本来就占满屏幕，收掉反而露出房间，正是玩家要的；
    ///   · 用 ShadowsOnly 而不是关渲染器——影子留着，地上仍看得出人在哪儿；
    ///   · 带迟滞，避免在阈值上反复闪。
    ///
    /// 【玩家本体不归这里管】ThirdPersonCamera.HideSelfWhenTight 已经在做同一件
    /// 事（且与"近第一人称"过渡绑在一起，0.66m 起收）。两处都写
    /// shadowCastingMode 会互相覆盖——它有 `hide == _selfHidden` 的早退，
    /// 被这里翻回去之后就再也不补写了。所以这里只管敌人。
    /// </summary>
    public class CharacterCloseFade : MonoBehaviour
    {
        /// <summary>收起来的距离（米）：镜头到躯干竖线段近于它就收。
        /// 角色胶囊半径约 0.34m，0.55m 时镜头基本贴到身上了。</summary>
        [Tooltip("收起角色的镜头距离")] public float hideDist = 0.38f;
        /// <summary>恢复距离（米）：比 hideDist 大一截，形成迟滞，阈值上不会闪。</summary>
        [Tooltip("恢复显示的镜头距离")] public float showDist = 0.55f;

        class Entry
        {
            public Transform root;
            public Renderer[] renderers;
            /// <summary>每个渲染器**原本**的投影模式。还原时直接写 On 是错的：
            /// 敌人脚下的危险圈贴片就是被刻意设成不投影的（EnemyController），
            /// 一律写 On 会让一张平贴片开始投影。</summary>
            public UnityEngine.Rendering.ShadowCastingMode[] modes;
            public bool hidden;
        }

        readonly List<Entry> _entries = new List<Entry>();
        float _rescanAt;

        void Rescan()
        {
            var old = new Dictionary<Transform, bool>();
            foreach (var e in _entries)
                if (e.root != null) old[e.root] = e.hidden;
            // 重扫之前先把当前隐藏的恢复回来：渲染器列表要重建，
            // 漏掉的那几个会永远停在 ShadowsOnly 上（换装/生成时会发生）。
            foreach (var e in _entries) if (e.hidden) SetHidden(e, false);
            _entries.Clear();

            foreach (var ec in AdversityRoad.Core.ActorRegistry.Enemies)
            {
                if (ec == null) continue;
                AddEntry(ec.transform, old);
            }
        }

        void AddEntry(Transform root, Dictionary<Transform, bool> old)
        {
            var list = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is TrailRenderer || r is LineRenderer || r is ParticleSystemRenderer) continue;
                if (r.GetComponent<TextMesh>() != null) continue;   // 浮字/警示不参与
                if (r.GetComponentInParent<Canvas>() != null) continue;
                list.Add(r);
            }
            if (list.Count == 0) return;
            var arr = list.ToArray();
            var modes = new UnityEngine.Rendering.ShadowCastingMode[arr.Length];
            for (int i = 0; i < arr.Length; i++) modes[i] = arr[i].shadowCastingMode;
            var e = new Entry { root = root, renderers = arr, modes = modes, hidden = false };
            _entries.Add(e);
            if (old.TryGetValue(root, out bool wasHidden) && wasHidden) SetHidden(e, true);
        }

        void LateUpdate()
        {
            if (Time.unscaledTime > _rescanAt)
            {
                _rescanAt = Time.unscaledTime + 0.6f;
                Rescan();
            }

            Vector3 cam = transform.position;
            foreach (var e in _entries)
            {
                if (e.root == null) continue;
                // 镜头到躯干竖线段（脚→头）的最近距离，比只算根位置准
                float h = Combat.MecanimCharacter.TargetHeight * Mathf.Max(0.4f, e.root.lossyScale.y);
                Vector3 feet = e.root.position - Vector3.up * (h * 0.5f);
                float t = Mathf.Clamp(cam.y - feet.y, 0f, h);
                float d = Vector3.Distance(cam, feet + Vector3.up * t);

                if (!e.hidden && d < hideDist) SetHidden(e, true);
                else if (e.hidden && d > showDist) SetHidden(e, false);
            }
        }

        void OnDisable()
        {
            foreach (var e in _entries) if (e.hidden) SetHidden(e, false);
        }

        static void SetHidden(Entry e, bool hide)
        {
            e.hidden = hide;
            for (int i = 0; i < e.renderers.Length; i++)
            {
                var r = e.renderers[i];
                if (r == null) continue;
                r.shadowCastingMode = hide
                    ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                    : e.modes[i];
            }
        }
    }
}
