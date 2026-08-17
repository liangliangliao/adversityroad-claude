using UnityEngine;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 面板自适应：比画布还高（或还宽）的面板整体等比缩小，直到完整装得下。
    ///
    /// 【为什么需要】所有面板的尺寸都是按 1920×1080 参考系写死的（900~1130 高），
    /// 而 CanvasScaler 用 matchWidthOrHeight=0.5：在 2712×1220 这种 2.22 超宽屏上，
    /// 画布的**参考高度被压到 ≈864 单位**。于是几乎每一块面板都比画布高，
    /// 顶部的标题与右上角的「关闭」直接被裁到屏幕外——这正是"文字显示不全、
    /// 关闭按钮看不全"的统一成因，不是某一个面板写错了。
    ///
    /// 逐个面板改尺寸要动十七处、且每改一次内部排版都要重算；等比缩放只动一个
    /// localScale，内部所有相对布局原样保持，一处修好全部。
    ///
    /// 只作用于**直接挂在 Canvas 下的顶层面板**：MakePanel 也被用来做面板内部的
    /// 小色块（计时条底之类），那些不能跟着缩。
    /// </summary>
    public class PanelFit : MonoBehaviour
    {
        RectTransform _rt;

        void OnEnable() => Fit();

        /// <summary>面板留白比例：不要顶满屏幕边缘（刘海/圆角/系统手势区）。</summary>
        const float Margin = 0.94f;

        public void Fit()
        {
            if (_rt == null) _rt = GetComponent<RectTransform>();
            if (_rt == null) return;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRt = canvas.transform as RectTransform;
            if (canvasRt == null || transform.parent != canvasRt) return;   // 只缩顶层面板

            float w = _rt.sizeDelta.x, h = _rt.sizeDelta.y;
            if (w < 1f || h < 1f) return;
            float s = Mathf.Min(1f,
                canvasRt.rect.width * Margin / w,
                canvasRt.rect.height * Margin / h);
            if (s > 0.01f) _rt.localScale = Vector3.one * s;
        }
    }
}
