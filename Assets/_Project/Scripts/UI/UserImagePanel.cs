using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.OpenWorld;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 换画面板：把手机/电脑里的图片挂到家里的画框与相框上。
    ///
    /// 【上一版为什么等于没实现】
    /// 上一版只是"约定一个目录，把文件改名放进去"——玩家既看不到这个目录，
    /// 也没有任何界面能确认有没有生效，等于没做。
    ///
    /// 这一版是真的能用的：面板里列出在手机相册/图片/下载目录（以及游戏自己的
    /// UserImages 目录）里找到的图片文件，点一下就贴到选中的画框上，
    /// 选择记在本机存档里，下次进游戏照旧挂着。
    /// 安卓上会先申请读取相册权限；拒绝了也不至于卡住——仍可用游戏自己的目录。
    /// </summary>
    public class UserImagePanel : MonoBehaviour
    {
        GameObject _panel;
        Text _title, _status;
        readonly List<GameObject> _cells = new List<GameObject>();
        readonly List<string> _files = new List<string>();
        UserImageSlot _slot = UserImageSlot.BedroomArtA;
        int _page;

        const int PageSize = 8;

        public static UserImagePanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<UserImagePanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "UserImagePanel", new Vector2(1180, 900),
                new Color(0.07f, 0.08f, 0.11f, 0.985f));

            _title = UiUtil.MakeText(_panel.transform, "Title", "换 一 张 画", 36,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(_title, new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(1000, 48));

            _status = UiUtil.MakeText(_panel.transform, "Status", "", 20,
                TextAnchor.UpperCenter, new Color(1f, 0.86f, 0.55f, 0.9f));
            UiUtil.SetRect(_status, new Vector2(0.5f, 1f), new Vector2(0, -104), new Vector2(1080, 70));

            // 选哪一处：卧室两幅画 + 办公桌相框
            string[] names = { "卧室 · 床头大画", "卧室 · 侧墙竖画", "办公桌 · 相框" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                UiUtil.MakeButton(_panel.transform, names[i], new Vector2(0.5f, 1f),
                    new Vector2(-350 + i * 350, -190), new Vector2(330, 62),
                    new Color(0.22f, 0.30f, 0.36f, 0.95f),
                    () => { _slot = (UserImageSlot)idx; _page = 0; Refresh(); }, 22);
            }

            UiUtil.MakeButton(_panel.transform, "重新扫描", new Vector2(0.5f, 0f), new Vector2(-420, 78),
                new Vector2(200, 74), new Color(0.24f, 0.34f, 0.30f, 0.95f), Scan, 24);
            UiUtil.MakeButton(_panel.transform, "上一页", new Vector2(0.5f, 0f), new Vector2(-200, 78),
                new Vector2(180, 74), new Color(0.24f, 0.26f, 0.32f, 0.95f), () => Turn(-1), 24);
            UiUtil.MakeButton(_panel.transform, "下一页", new Vector2(0.5f, 0f), new Vector2(0, 78),
                new Vector2(180, 74), new Color(0.24f, 0.26f, 0.32f, 0.95f), () => Turn(1), 24);
            UiUtil.MakeButton(_panel.transform, "取下这幅", new Vector2(0.5f, 0f), new Vector2(200, 78),
                new Vector2(200, 74), new Color(0.36f, 0.26f, 0.24f, 0.95f), Clear, 24);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(420, 78),
                new Vector2(180, 74), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 24);

            _panel.SetActive(false);
        }

        public void Toggle()
        {
            if (_panel == null) return;
            if (_panel.activeSelf) { Hide(); return; }
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
            Scan();
        }

        /// <summary>打开面板并直接选中某个画框（走近画框按键时用）。</summary>
        public void OpenFor(UserImageSlot slot)
        {
            _slot = slot;
            _page = 0;
            if (_panel == null) return;
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
            Scan();
        }

        void Scan()
        {
            _files.Clear();
            _files.AddRange(UserImageLibrary.FindImages());
            _page = 0;
            Refresh();
        }

        void Turn(int d)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(_files.Count / (float)PageSize));
            _page = Mathf.Clamp(_page + d, 0, pages - 1);
            Refresh();
        }

        void Clear()
        {
            UserImageLibrary.Assign(_slot, "");
            Refresh();
        }

        void Refresh()
        {
            foreach (var c in _cells) if (c != null) { c.SetActive(false); Destroy(c); }
            _cells.Clear();

            _title.text = "换一张画 · " + SlotName(_slot);
            string cur = UserImageLibrary.PathFor(_slot);
            int pages = Mathf.Max(1, Mathf.CeilToInt(_files.Count / (float)PageSize));

            _status.text = _files.Count == 0
                ? "没有找到图片。\n把图片放到手机的「图片/下载/相机」任一目录，或游戏目录 " +
                  UserImageLibrary.FolderPath + " 里，再点「重新扫描」。"
                : "找到 " + _files.Count + " 张（第 " + (_page + 1) + "/" + pages + " 页）。" +
                  (string.IsNullOrEmpty(cur) ? "这一处还空着。" : "当前：" + Path.GetFileName(cur));

            for (int i = 0; i < PageSize; i++)
            {
                int idx = _page * PageSize + i;
                if (idx >= _files.Count) break;
                string file = _files[idx];
                bool active = file == cur;
                var btn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                    new Vector2(i % 2 == 0 ? -270 : 270, -280 - (i / 2) * 84),
                    new Vector2(520, 74),
                    active ? new Color(0.24f, 0.46f, 0.34f, 0.96f) : new Color(0.20f, 0.24f, 0.30f, 0.95f),
                    () => { UserImageLibrary.Assign(_slot, file); Refresh(); }, 20);
                var label = btn.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = (active ? "✔ " : "") + Path.GetFileName(file);
                    label.alignment = TextAnchor.MiddleLeft;
                    var lrt = label.GetComponent<RectTransform>();
                    lrt.offsetMin = new Vector2(16, 2);
                    lrt.offsetMax = new Vector2(-10, -2);
                }
                _cells.Add(btn.gameObject);
            }
        }

        static string SlotName(UserImageSlot s)
        {
            switch (s)
            {
                case UserImageSlot.BedroomArtB: return "卧室侧墙竖画";
                case UserImageSlot.DeskPhoto: return "办公桌相框";
                default: return "卧室床头大画";
            }
        }

        void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
