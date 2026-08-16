using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 软键盘避让：手机上弹出输入法时，把正在输入的那块面板整体上移，
    /// 让输入框始终露在键盘上沿之上。
    ///
    /// 【为什么必须有】Unity 的 UGUI 完全不管系统输入法这件事：
    /// Canvas 是 ScreenSpaceOverlay，键盘是系统层画上去的一块遮挡，
    /// 两者互不知情。于是玩家点开 API Key 或目标输入框，键盘从下方升起，
    /// 正好盖住输入框本身和下面的确认按钮——他看不到自己打的是什么，
    /// 也够不到"保存"。这不是某一块面板排版错了，是**所有带输入的面板**
    /// 都有的同一个缺口，所以修在一处：挂在 Canvas 上，谁获得焦点就避让谁。
    ///
    /// 避让量按"输入框底边要高于键盘上沿"算，不是拍脑袋定的固定偏移——
    /// 不同机型、不同输入法、带不带候选词栏，键盘高度差得很远。
    /// 拿不到系统给的键盘区域时（部分机型返回空矩形）才退回按屏幕 45% 估。
    /// </summary>
    public class SoftKeyboardShifter : MonoBehaviour
    {
        /// <summary>输入框底边距键盘上沿至少留这么多画布单位。</summary>
        const float Pad = 28f;

        /// <summary>拿不到系统键盘区域时的估算高度（占屏幕高度的比例）。</summary>
        const float FallbackRatio = 0.45f;

        RectTransform _canvasRt;
        RectTransform _shifted;      // 当前被上移的那块面板
        float _restoreY;             // 它原来的 y，收键盘时还回去
        float _targetShift;

        public static SoftKeyboardShifter Attach(Canvas canvas)
        {
            if (canvas == null) return null;
            var c = canvas.GetComponent<SoftKeyboardShifter>() ??
                    canvas.gameObject.AddComponent<SoftKeyboardShifter>();
            c._canvasRt = canvas.transform as RectTransform;
            return c;
        }

        void Update()
        {
            if (_canvasRt == null) return;

            // 先取键盘高度再判焦点：写成 `input != null && KeyboardHeightPx(out kbPx)`
            // 的话，短路掉那一支时 kbPx 从未赋值，编译期就过不去（CS0165）。
            float kbPx = 0f;
            var input = FocusedInput();
            if (input == null || !KeyboardHeightPx(out kbPx))
            {
                Restore();
                return;
            }

            // 键盘上沿在画布坐标里的高度：画布纵向 [-H/2, H/2] 对应屏幕 [0, Screen.height]
            float h = _canvasRt.rect.height;
            float kbTop = -h * 0.5f + kbPx / Mathf.Max(1f, Screen.height) * h;

            var panel = TopPanelOf(input.transform);
            if (panel == null) { Restore(); return; }

            // 换了一块面板：先把上一块还原，避免两块都停在上移状态
            if (_shifted != null && _shifted != panel) Restore();

            if (_shifted != panel)
            {
                _shifted = panel;
                _restoreY = panel.anchoredPosition.y;
                _targetShift = 0f;
            }

            // 输入框底边（画布坐标）——面板可能被 PanelFit 等比缩过，
            // 所以必须走世界坐标换算，不能直接拿 anchoredPosition 相加。
            float bottom = BottomInCanvas(input.GetComponent<RectTransform>());
            float need = kbTop + Pad - (bottom + _targetShift);
            if (need > 1f) _targetShift += need;
            else if (need < -60f) _targetShift = Mathf.Max(0f, _targetShift + need);  // 键盘变矮了就跟着落回

            var p = _shifted.anchoredPosition;
            p.y = Mathf.Lerp(p.y, _restoreY + _targetShift, Time.unscaledDeltaTime * 12f);
            _shifted.anchoredPosition = p;
        }

        void Restore()
        {
            if (_shifted == null) return;
            var p = _shifted.anchoredPosition;
            p.y = Mathf.Lerp(p.y, _restoreY, Time.unscaledDeltaTime * 12f);
            _shifted.anchoredPosition = p;
            if (Mathf.Abs(p.y - _restoreY) < 0.5f)
            {
                p.y = _restoreY;
                _shifted.anchoredPosition = p;
                _shifted = null;
                _targetShift = 0f;
            }
        }

        static InputField FocusedInput()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return null;
            var f = go.GetComponent<InputField>();
            return f != null && f.isFocused ? f : null;
        }

        /// <summary>系统键盘遮住了屏幕底部多少像素（没弹出/拿不到就是 false）。</summary>
        static bool KeyboardHeightPx(out float px)
        {
            px = 0f;
            if (!TouchScreenKeyboard.visible) return false;
            var area = TouchScreenKeyboard.area;
            px = area.height;
            // 部分机型/输入法返回空矩形——不能因此就不避让，按屏幕比例估一个
            if (px < 1f) px = Screen.height * FallbackRatio;
            return px > 1f;
        }

        /// <summary>某个 UI 元素的底边在画布坐标系里的 y。</summary>
        float BottomInCanvas(RectTransform rt)
        {
            if (rt == null) return 0f;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);        // 0=左下 1=左上 2=右上 3=右下
            return _canvasRt.InverseTransformPoint(corners[0]).y;
        }

        /// <summary>往上找到直接挂在 Canvas 下的那一层——要移动的是整块面板。</summary>
        RectTransform TopPanelOf(Transform t)
        {
            while (t != null && t.parent != null && t.parent != _canvasRt)
                t = t.parent;
            return t != null && t.parent == _canvasRt ? t as RectTransform : null;
        }
    }
}
