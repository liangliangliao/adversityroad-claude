using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 主菜单 / 标题界面 + 暂停菜单（此前完全没有：进游戏直接就在打了）。
    ///
    /// 大型动作游戏的开场惯例是「标题页 → 玩家主动开始 → 进入世界」，理由不是仪式感：
    ///   ① 玩家需要一个**还没开始**的状态来看清操作、改设置、决定继续还是重来；
    ///   ② 进程刚起来时世界还在生成（建场、烘焙寻路、加载动作库），
    ///      直接扔进战斗＝一睁眼就在挨打，且前几秒必然卡顿；
    ///   ③ 暂停要有出口——没有暂停菜单时，手机上只能杀进程。
    ///
    /// 实现上是同一块面板的两种模式：
    ///   Title  开场标题页（不可关闭，必须选一项）
    ///   Pause  游戏中按 ESC / 点「暂停」弹出（可继续）
    /// 两种模式都把 Time.timeScale 归零，并用整屏不透明背板挡住 HUD 与触屏按钮，
    /// 因此不需要再去逐个隐藏 HUD 元素（那样极易漏掉某一个，反而更乱）。
    /// </summary>
    public class MainMenuPanel : MonoBehaviour
    {
        GameObject _root;
        Text _titleText, _hintText;
        Transform _btnHost;
        SettingsPanel _settings;
        bool _isTitle = true;

        /// <summary>标题页是否正挡在最前（供其它系统判断"游戏还没真正开始"）。</summary>
        public static bool AtTitle { get; private set; }

        /// <summary>玩家第一次点「进入世界」后回调（章节开场白挂在这里，
        /// 而不是在 Bootstrap 里直接弹——否则开场白会盖在标题页上面）。</summary>
        public System.Action onEnterWorld;
        bool _entered;

        public static MainMenuPanel Create(Transform canvas, SettingsPanel settings)
        {
            var comp = canvas.gameObject.AddComponent<MainMenuPanel>();
            comp._settings = settings;
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            // 整屏背板：不透明 + 接收射线，把底下的 HUD/摇杆/战斗按钮全部盖住并吃掉点击
            _root = new GameObject("MainMenu", typeof(Image));
            _root.transform.SetParent(canvas, false);
            var rrt = _root.GetComponent<RectTransform>();
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero;
            rrt.offsetMax = Vector2.zero;
            _root.GetComponent<Image>().color = new Color(0.04f, 0.045f, 0.06f, 0.985f);

            _titleText = UiUtil.MakeText(_root.transform, "Title", "逆 境 之 路", 96,
                TextAnchor.MiddleCenter, new Color(0.96f, 0.86f, 0.45f));
            UiUtil.SetRect(_titleText, new Vector2(0.5f, 1f), new Vector2(0, -200), new Vector2(1400, 130));
            _titleText.fontStyle = FontStyle.Bold;

            _hintText = UiUtil.MakeText(_root.transform, "Hint",
                "看见弱点 → 面对困境 → 主动战斗 → 失败复盘 → 强化意志 → 再次挑战", 26,
                TextAnchor.MiddleCenter, new Color(0.72f, 0.76f, 0.84f));
            UiUtil.SetRect(_hintText, new Vector2(0.5f, 1f), new Vector2(0, -300), new Vector2(1400, 40));

            var host = new GameObject("Buttons", typeof(RectTransform));
            host.transform.SetParent(_root.transform, false);
            var hrt = host.GetComponent<RectTransform>();
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
            hrt.anchoredPosition = new Vector2(0, -40);
            hrt.sizeDelta = new Vector2(560, 520);
            _btnHost = host.transform;

            var version = UiUtil.MakeText(_root.transform, "Ver",
                "Unity 6000.5 · URP · 安卓/PC", 20,
                TextAnchor.MiddleCenter, new Color(0.5f, 0.53f, 0.6f));
            UiUtil.SetRect(version, new Vector2(0.5f, 0f), new Vector2(0, 44), new Vector2(900, 30));

            ShowTitle();
        }

        void ClearButtons()
        {
            for (int i = _btnHost.childCount - 1; i >= 0; i--)
                Destroy(_btnHost.GetChild(i).gameObject);
        }

        Button MakeEntry(string label, int index, System.Action action, Color color)
        {
            return UiUtil.MakeButton(_btnHost, label, new Vector2(0.5f, 1f),
                new Vector2(0, -34 - index * 96), new Vector2(520, 78), color,
                () => action?.Invoke(), 30);
        }

        static readonly Color Primary = new Color(0.62f, 0.45f, 0.18f, 0.98f);
        static readonly Color Normal = new Color(0.20f, 0.23f, 0.30f, 0.98f);
        static readonly Color Danger = new Color(0.40f, 0.20f, 0.20f, 0.98f);

        /// <summary>开场标题页：必须选一项才会进入世界。</summary>
        public void ShowTitle()
        {
            _isTitle = true;
            AtTitle = true;
            _titleText.text = "逆 境 之 路";
            _titleText.fontSize = 96;
            _hintText.text = "看见弱点 → 面对困境 → 主动战斗 → 失败复盘 → 强化意志 → 再次挑战";
            ClearButtons();

            var story = StoryManager.Instance;
            string chapter = story != null && !story.AllCleared
                ? story.Current.title : "自由修炼";
            int i = 0;
            MakeEntry("进入世界 · " + chapter, i++, EnterWorld, Primary);
            MakeEntry("操作说明", i++, ShowHelp, Normal);
            if (_settings != null)
                MakeEntry("设置", i++, () => { Close(); _settings.Toggle(); }, Normal);
            MakeEntry("退出游戏", i++, Quit, Danger);

            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        /// <summary>游戏中暂停：可继续、可回标题。</summary>
        public void ShowPause()
        {
            if (_root.activeSelf && !_isTitle) { Close(); return; }
            _isTitle = false;
            AtTitle = false;
            _titleText.text = "暂 停";
            _titleText.fontSize = 72;
            _hintText.text = "喘口气。回去的时候，还是从最小的一步开始。";
            ClearButtons();

            int i = 0;
            MakeEntry("继续游戏", i++, Close, Primary);
            MakeEntry("操作说明", i++, ShowHelp, Normal);
            if (_settings != null)
                MakeEntry("设置", i++, () => { Close(); _settings.Toggle(); }, Normal);
            MakeEntry("返回标题", i++, ShowTitle, Normal);
            MakeEntry("退出游戏", i++, Quit, Danger);

            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        void ShowHelp()
        {
            _titleText.text = "操 作 说 明";
            _titleText.fontSize = 60;
            _hintText.text =
                "触屏：左摇杆移动，右侧拳/剑/重/闪/挡/跳；「术」+核心键=技能；「锁」锁定目标\n" +
                "键盘：WASD 移动，左键=拳，E=剑，右键长按=蓄力，空格=跳，Shift=闪避，Ctrl=格挡，1-6=技能\n" +
                "连招：拳剑随意串；跳跃/闪避/技能/格挡也算连招的一环，串得越杂伤害越高\n" +
                "蓄力：按住「重」聚气护体，松开打出必中二连；意势满 3 点可放超必杀";
            ClearButtons();
            MakeEntry("返回", 0, () => { if (_isTitle) ShowTitle(); else ShowPause(); }, Normal);
        }

        /// <summary>离开标题进入世界：首次进入时放章节开场白。</summary>
        void EnterWorld()
        {
            Close();
            if (_entered) return;
            _entered = true;
            onEnterWorld?.Invoke();
        }

        void Close()
        {
            AtTitle = false;
            _root.SetActive(false);
            Time.timeScale = 1f;
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Update()
        {
            // 保持置顶：MobileControls 的摇杆与战斗按钮是在自己的 Start() 里建的，
            // 比 GameBootstrap.Start 晚一帧——它们会成为更靠后的兄弟节点盖在标题页上。
            // 与其去猜各家的建立时机，不如每帧确认一次自己仍是最后一个（开销可忽略）。
            if (_root.activeSelf)
            {
                var p = _root.transform.parent;
                if (p != null && _root.transform.GetSiblingIndex() != p.childCount - 1)
                    _root.transform.SetAsLastSibling();
            }

            // ESC / 安卓返回键：呼出或收起暂停菜单（标题页不响应，必须先进世界）
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            if (_root.activeSelf && _isTitle) return;
            ShowPause();
        }
    }
}
