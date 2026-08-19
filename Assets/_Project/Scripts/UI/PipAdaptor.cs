using UnityEngine;
using AdversityRoad.Platform;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 悬浮小窗里的画面自适应。
    ///
    /// 小窗只有几百像素宽，而这套 UI 是按 1920×1080 参考系摆的：CanvasScaler 会
    /// 等比缩小，于是摇杆变成指甲盖、字变成一排糊点；更要紧的是**小窗根本收不到触摸**
    /// （点一下小窗只会把应用拉回全屏），所以那些控件在小窗里除了挡画面没有别的作用。
    ///
    /// 因此进小窗时把整块 UI 关掉，只留游戏画面本身——这才是"自适应画面"：
    /// 小窗里是干净的一幕游戏，回到全屏时 UI 原样回来（只是关渲染，状态一个不丢）。
    /// </summary>
    public class PipAdaptor : MonoBehaviour
    {
        Canvas _canvas;
        bool _wasEnabled = true;

        public static PipAdaptor Attach(Canvas canvas)
        {
            var a = canvas.gameObject.AddComponent<PipAdaptor>();
            a._canvas = canvas;
            return a;
        }

        void OnEnable()
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();
            PipMode.Changed += Apply;
            if (PipMode.InPip) Apply(true);
        }

        void OnDisable() => PipMode.Changed -= Apply;

        void Apply(bool inPip)
        {
            if (_canvas == null) return;
            if (inPip)
            {
                _wasEnabled = _canvas.enabled;
                _canvas.enabled = false;
            }
            else
            {
                _canvas.enabled = _wasEnabled;
            }
        }
    }
}
