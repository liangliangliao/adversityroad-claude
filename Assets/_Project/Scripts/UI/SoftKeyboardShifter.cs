using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 软键盘避让：手机上弹出输入法时，把正在输入的那块面板**整体缩进键盘上方那条可见带里**，
    /// 让输入框和它下面的确认按钮都还在屏幕上。
    ///
    /// 【这里有两个不同的毛病，必须分开治】
    ///
    /// 一、输入法全屏接管（玩家说的"全屏覆盖"）。
    ///     安卓输入法在横屏下默认切到"全屏编辑模式"（extract UI）：它自带一个占满
    ///     整块屏幕的文本框，游戏画面整个被盖掉。这时候把面板往上挪多少都没用——
    ///     屏幕上根本已经没有游戏了。治它要在**输入法这一侧**下手：
    ///     TouchScreenKeyboard.hideInput 会告诉输入法不要弹自己的编辑界面
    ///     （对应安卓的 IME_FLAG_NO_EXTRACT_UI），
    ///     再配合 UiUtil.MakeInput 里的 shouldHideMobileInput，
    ///     文字始终画在游戏内的输入框里，输入法就只在屏幕下方出一块键盘。
    ///
    /// 二、键盘遮住下半屏。这才是本组件做的事：把面板缩放并挪进剩下的可见带。
    ///     只做"上移"是不够的——面板本来就有 900+ 单位高，键盘吃掉一半屏幕之后，
    ///     整块面板根本装不下，上移只会把标题顶出屏幕。所以先按可见带高度等比缩小，
    ///     再居中放进去；收键盘时交回 PanelFit 还原。
    /// </summary>
    public class SoftKeyboardShifter : MonoBehaviour
    {
        /// <summary>可见带上下各留一点，别贴边。</summary>
        const float Pad = 16f;

        /// <summary>拿不到系统键盘区域时的估算高度（占屏幕高度的比例）。</summary>
        const float FallbackRatio = 0.45f;

        RectTransform _canvasRt;
        RectTransform _shifted;          // 当前被挪动的那块面板
        Vector2 _restorePos;
        Vector3 _restoreScale;

        public static SoftKeyboardShifter Attach(Canvas canvas)
        {
            if (canvas == null) return null;
            var c = canvas.GetComponent<SoftKeyboardShifter>() ??
                    canvas.gameObject.AddComponent<SoftKeyboardShifter>();
            c._canvasRt = canvas.transform as RectTransform;
            return c;
        }

        void Awake()
        {
            // 一上来就关掉输入法的全屏编辑界面。这是静态属性，设一次对之后所有
            // 由 InputField 打开的键盘都生效；Update 里还会再确认一遍，
            // 免得别处代码（或某些机型的重建）把它改回去。
            TouchScreenKeyboard.hideInput = true;
        }

        void Update()
        {
            if (_canvasRt == null) return;

            float kbPx = 0f;
            var input = FocusedInput();
            if (input == null || !KeyboardHeightPx(out kbPx))
            {
                Restore();
                return;
            }
            if (!TouchScreenKeyboard.hideInput) TouchScreenKeyboard.hideInput = true;

            var panel = TopPanelOf(input.transform);
            if (panel == null) { Restore(); return; }
            if (_shifted != null && _shifted != panel) Restore();

            if (_shifted != panel)
            {
                _shifted = panel;
                _restorePos = panel.anchoredPosition;
                _restoreScale = panel.localScale;
            }

            // 可见带：画布纵向 [-H/2, H/2] 对应屏幕 [0, Screen.height]，
            // 键盘吃掉底部 kbPx 像素，剩下的那一条就是还能用的地方。
            float h = _canvasRt.rect.height;
            float kbTop = -h * 0.5f + kbPx / Mathf.Max(1f, Screen.height) * h;
            float bandTop = h * 0.5f - Pad;
            float bandH = bandTop - (kbTop + Pad);
            if (bandH < 60f) return;   // 键盘几乎占满屏幕，缩了也看不清，不折腾

            // 面板按可见带高度等比缩小（宽度也别超画布），再居中放进这条带里
            float pw = _shifted.sizeDelta.x, ph = _shifted.sizeDelta.y;
            if (pw < 1f || ph < 1f) return;
            float s = Mathf.Min(_restoreScale.x, bandH / ph, _canvasRt.rect.width * 0.96f / pw);
            float centerY = (bandTop + kbTop + Pad) * 0.5f;

            _shifted.localScale = Vector3.one * s;
            var p = _shifted.anchoredPosition;
            p.y = Mathf.Lerp(p.y, centerY, Time.unscaledDeltaTime * 14f);
            _shifted.anchoredPosition = p;
        }

        void Restore()
        {
            if (_shifted == null) return;
            _shifted.localScale = _restoreScale;
            _shifted.anchoredPosition = _restorePos;
            var fit = _shifted.GetComponent<PanelFit>();
            if (fit != null) fit.Fit();     // 缩放归 PanelFit 管，交还给它算一次
            _shifted = null;
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

        /// <summary>往上找到直接挂在 Canvas 下的那一层——要挪的是整块面板。</summary>
        RectTransform TopPanelOf(Transform t)
        {
            while (t != null && t.parent != null && t.parent != _canvasRt)
                t = t.parent;
            return t != null && t.parent == _canvasRt ? t as RectTransform : null;
        }
    }
}
