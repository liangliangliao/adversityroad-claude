using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.OpenWorld;
using AdversityRoad.Platform;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 电视面板：换台、开关、静音、后台播放、切悬浮小窗。
    ///
    /// 【为什么控制全在这儿，而不是点屏幕上的播放器】
    /// 电视画面是一块盖在游戏画面上的网页层，它**一律不吃触摸**
    /// （不然站在 5 米宽的电视前时，摇杆就被网页挡住了，玩家会卡死在原地）。
    /// 所以播放控制走这个面板，由它调 WebScreen 的接口去操作页面。
    /// </summary>
    public class TvPanel : MonoBehaviour
    {
        GameObject _panel;
        Text _status;
        InputField _link;
        Button _powerBtn, _muteBtn, _bgBtn;
        readonly Button[] _chanBtns = new Button[8];

        public static TvPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<TvPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "TvPanel", new Vector2(1120, 900),
                new Color(0.06f, 0.07f, 0.10f, 0.985f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "客 厅 电 视", 36,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(1000, 46));

            _status = UiUtil.MakeText(_panel.transform, "Status", "", 20,
                TextAnchor.UpperCenter, new Color(1f, 0.88f, 0.6f, 0.92f));
            UiUtil.SetRect(_status, new Vector2(0.5f, 1f), new Vector2(0, -96), new Vector2(1030, 92));

            // ---- 频道 ----
            var chanLabel = UiUtil.MakeText(_panel.transform, "ChanLabel", "频 道", 24,
                TextAnchor.MiddleLeft, new Color(0.75f, 0.85f, 0.95f));
            UiUtil.SetRect(chanLabel, new Vector2(0.5f, 1f), new Vector2(-430, -200), new Vector2(300, 32));

            var list = TvSet.Channels;
            for (int i = 0; i < list.Count && i < _chanBtns.Length; i++)
            {
                int idx = i;
                float x = (i % 2 == 0) ? -270 : 270;
                float y = -252 - (i / 2) * 74;
                _chanBtns[i] = UiUtil.MakeButton(_panel.transform, list[i].name,
                    new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(500, 62),
                    new Color(0.18f, 0.24f, 0.32f, 0.96f), () => Tune(idx), 22);
            }

            // ---- 粘链接 ----
            var tip = UiUtil.MakeText(_panel.transform, "Tip",
                "粘一条 YouTube 链接（watch / youtu.be / shorts 都认），存进「我的频道」：", 19,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.82f, 0.86f));
            UiUtil.SetRect(tip, new Vector2(0.5f, 1f), new Vector2(-14, -480), new Vector2(1000, 28));

            _link = UiUtil.MakeInput(_panel.transform, "https://www.youtube.com/watch?v=...",
                new Vector2(0.5f, 1f), new Vector2(-160, -530), new Vector2(700, 58), false);

            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                UiUtil.MakeButton(_panel.transform, "存到 " + (i + 1), new Vector2(0.5f, 1f),
                    new Vector2(280 + i * 130, -530), new Vector2(120, 58),
                    new Color(0.22f, 0.36f, 0.28f, 0.96f), () => Save(slot), 20);
            }

            // ---- 控制 ----
            _powerBtn = UiUtil.MakeButton(_panel.transform, "开机", new Vector2(0.5f, 0f),
                new Vector2(-420, 220), new Vector2(200, 68),
                new Color(0.26f, 0.40f, 0.30f, 0.96f), TogglePower, 24);
            UiUtil.MakeButton(_panel.transform, "播放", new Vector2(0.5f, 0f),
                new Vector2(-200, 220), new Vector2(180, 68),
                new Color(0.22f, 0.30f, 0.40f, 0.96f), () => Act(t => t.Play()), 24);
            UiUtil.MakeButton(_panel.transform, "暂停", new Vector2(0.5f, 0f),
                new Vector2(0, 220), new Vector2(180, 68),
                new Color(0.22f, 0.30f, 0.40f, 0.96f), () => Act(t => t.Pause()), 24);
            _muteBtn = UiUtil.MakeButton(_panel.transform, "静音：关", new Vector2(0.5f, 0f),
                new Vector2(210, 220), new Vector2(220, 68),
                new Color(0.30f, 0.28f, 0.22f, 0.96f), ToggleMute, 24);
            UiUtil.MakeButton(_panel.transform, "下一个台", new Vector2(0.5f, 0f),
                new Vector2(430, 220), new Vector2(200, 68),
                new Color(0.22f, 0.30f, 0.40f, 0.96f), () => Act(t => t.Next()), 24);

            _bgBtn = UiUtil.MakeButton(_panel.transform, "后台播放：开", new Vector2(0.5f, 0f),
                new Vector2(-330, 140), new Vector2(320, 68),
                new Color(0.24f, 0.34f, 0.42f, 0.96f), ToggleBackground, 24);
            UiUtil.MakeButton(_panel.transform, "切悬浮小窗", new Vector2(0.5f, 0f),
                new Vector2(20, 140), new Vector2(320, 68),
                new Color(0.34f, 0.30f, 0.44f, 0.96f), EnterPip, 24);
            UiUtil.MakeButton(_panel.transform, "在 YouTube 打开", new Vector2(0.5f, 0f),
                new Vector2(370, 140), new Vector2(340, 68),
                new Color(0.36f, 0.26f, 0.24f, 0.96f), OpenExternal, 24);

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 62), new Vector2(240, 66),
                new Color(0.30f, 0.30f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        // ================= 开关面板 =================

        public void Toggle()
        {
            if (_panel == null) return;
            if (_panel.activeSelf) Hide(); else Show();
        }

        public void Show()
        {
            if (_panel == null) return;
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            // 面板打开期间收起电视的网页层：它是原生视图，会盖在面板上面
            TvSet.Suppressed = true;
            Refresh();
        }

        public void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
            TvSet.Suppressed = false;
        }

        // ================= 动作 =================

        void Act(System.Action<TvSet> fn)
        {
            var tv = TvSet.Current;
            if (tv == null)
            {
                GameEvents.RaiseSubtitle("电视在住所一层的休息厅（沙发正对的那面墙）。");
                Refresh();
                return;
            }
            fn(tv);
            Refresh();
        }

        void Tune(int i) => Act(t => t.SetChannel(i));
        void TogglePower() => Act(t => t.Toggle());
        void ToggleMute() => Act(t => t.SetMuted(!t.Muted));
        void ToggleBackground() => Act(t => t.SetBackground(!t.Background));

        void Save(int slot)
        {
            string text = _link != null ? _link.text : "";
            if (!TvSet.SetCustom(slot, text))
            {
                GameEvents.RaiseSubtitle("这条链接看不出视频 id——把浏览器地址栏里那条整段粘进来。");
                Refresh();
                return;
            }
            var list = TvSet.Channels;
            int idx = 3 + slot;
            if (idx < _chanBtns.Length && _chanBtns[idx] != null)
            {
                var t = _chanBtns[idx].GetComponentInChildren<Text>();
                if (t != null) t.text = list[idx].name + " ✓";
            }
            if (_link != null) _link.text = "";
            GameEvents.RaiseSubtitle("已存进「我的频道 " + (slot + 1) + "」，正在切过去。");
            Act(t => t.SetChannel(idx));
        }

        void EnterPip()
        {
            // 小窗里游戏继续跑，电视的声音也继续——这是最靠得住的"后台播放"形态
            PipMode.Enter();
            Hide();
        }

        void OpenExternal()
        {
            var tv = TvSet.Current;
            string link = tv != null ? tv.CurrentLink() : "";
            if (string.IsNullOrEmpty(link))
            {
                GameEvents.RaiseSubtitle("当前频道还没有链接。");
                return;
            }
            WebScreen.OpenExternal(link);
        }

        // ================= 状态行 =================

        void Refresh()
        {
            var tv = TvSet.Current;
            if (_powerBtn != null)
            {
                var t = _powerBtn.GetComponentInChildren<Text>();
                if (t != null) t.text = (tv != null && tv.On) ? "关机" : "开机";
            }
            if (_muteBtn != null)
            {
                var t = _muteBtn.GetComponentInChildren<Text>();
                if (t != null) t.text = (tv != null && tv.Muted) ? "静音：开" : "静音：关";
            }
            if (_bgBtn != null)
            {
                var t = _bgBtn.GetComponentInChildren<Text>();
                if (t != null) t.text = (tv == null || tv.Background) ? "后台播放：开" : "后台播放：关";
            }
            if (_status == null) return;

            if (tv == null)
            {
                _status.text = "电视在住所一层休息厅——沙发正对的那面墙。走近按「用」也能打开这个面板。";
                return;
            }

            string where;
            if (!WebScreen.Available)
                where = "这台设备没有网页层（编辑器/PC），屏幕显示本机生成的动态画面。";
            else if (!WebScreen.Accelerated)
                where = "注意：这台设备没能给网页层开出硬件加速窗口，视频可能只有声音没有画面。";
            else
                where = "站到电视正面**站定**约半秒，画面就嵌进屏幕里不再动；一走动画面收起、"
                      + "声音继续（屏幕改显示本机画面）。悬浮小窗里也只有声音。";
            _status.text = (tv.On ? "已开机 · " : "已关机 · ") + tv.ChannelName + "\n" + where;
        }
    }
}
