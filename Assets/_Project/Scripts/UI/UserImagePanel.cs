using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.OpenWorld;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 相册面板：打开手机相册、翻看缩略图、点一张挂到家里的画框/相框上。
    ///
    /// 【上一版为什么不算"从相册选图"】上一版列的是**文件名**，玩家得靠名字猜内容，
    /// 而且要求先把照片放进指定目录——那是文件管理器，不是相册。
    ///
    /// 这一版：内容来自系统相册（MediaStore，按时间倒序、带相册名），
    /// 面板里是 3×3 的**缩略图网格**，看图点图；缩略图逐帧加载（一帧一张），
    /// 不会因为一次解码十张四千万像素的照片把画面卡住。
    /// 选中后原图会被复制进游戏目录（见 UserImageLibrary.Assign），
    /// 所以"选了就一定挂得上"，也不怕之后原图被删。
    /// </summary>
    public class UserImagePanel : MonoBehaviour
    {
        GameObject _panel;
        Text _title, _status;
        readonly List<GameObject> _cells = new List<GameObject>();
        readonly List<GalleryImage> _images = new List<GalleryImage>();
        readonly List<Texture2D> _thumbs = new List<Texture2D>();
        UserImageSlot _slot = UserImageSlot.BedroomArtA;
        int _page;
        Coroutine _loading;

        const int Cols = 3, Rows = 3;
        const int PageSize = Cols * Rows;
        const int ThumbSide = 220;      // 缩略图像素边长（够清楚，又不占显存）

        public static UserImagePanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<UserImagePanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "UserImagePanel", new Vector2(1180, 980),
                new Color(0.07f, 0.08f, 0.11f, 0.985f));

            _title = UiUtil.MakeText(_panel.transform, "Title", "相 册", 36,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(_title, new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(1000, 46));

            _status = UiUtil.MakeText(_panel.transform, "Status", "", 20,
                TextAnchor.UpperCenter, new Color(1f, 0.86f, 0.55f, 0.9f));
            UiUtil.SetRect(_status, new Vector2(0.5f, 1f), new Vector2(0, -92), new Vector2(1080, 64));

            // 挂到哪一处：卧室两幅画 + 办公桌相框
            string[] names = { "卧室 · 床头大画", "卧室 · 侧墙竖画", "办公桌 · 相框" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                UiUtil.MakeButton(_panel.transform, names[i], new Vector2(0.5f, 1f),
                    new Vector2(-350 + i * 350, -168), new Vector2(330, 56),
                    new Color(0.22f, 0.30f, 0.36f, 0.95f),
                    () => { _slot = (UserImageSlot)idx; _page = 0; Refresh(); }, 22);
            }

            UiUtil.MakeButton(_panel.transform, "打开相册", new Vector2(0.5f, 0f), new Vector2(-420, 70),
                new Vector2(210, 72), new Color(0.24f, 0.34f, 0.30f, 0.95f), Scan, 24);
            UiUtil.MakeButton(_panel.transform, "上一页", new Vector2(0.5f, 0f), new Vector2(-205, 70),
                new Vector2(180, 72), new Color(0.24f, 0.26f, 0.32f, 0.95f), () => Turn(-1), 24);
            UiUtil.MakeButton(_panel.transform, "下一页", new Vector2(0.5f, 0f), new Vector2(0, 70),
                new Vector2(180, 72), new Color(0.24f, 0.26f, 0.32f, 0.95f), () => Turn(1), 24);
            UiUtil.MakeButton(_panel.transform, "取下这幅", new Vector2(0.5f, 0f), new Vector2(205, 70),
                new Vector2(200, 72), new Color(0.36f, 0.26f, 0.24f, 0.95f), Clear, 24);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(420, 70),
                new Vector2(180, 72), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 24);

            _panel.SetActive(false);
        }

        public void Toggle()
        {
            if (_panel == null) return;
            if (_panel.activeSelf) { Hide(); return; }
            Open();
        }

        /// <summary>打开面板并直接选中某个画框（走近画框按键时用）。</summary>
        public void OpenFor(UserImageSlot slot)
        {
            _slot = slot;
            _page = 0;
            Open();
        }

        void Open()
        {
            if (_panel == null) return;
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
            Scan();
        }

        void Scan()
        {
            _images.Clear();
            _images.AddRange(UserImageLibrary.Browse());
            _page = 0;
            Refresh();
        }

        void Turn(int d)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(_images.Count / (float)PageSize));
            _page = Mathf.Clamp(_page + d, 0, pages - 1);
            Refresh();
        }

        void Clear()
        {
            UserImageLibrary.Assign(_slot, "");
            Refresh();
        }

        void Pick(GalleryImage img)
        {
            if (UserImageLibrary.Assign(_slot, img)) Refresh();
        }

        void Refresh()
        {
            if (_loading != null) { StopCoroutine(_loading); _loading = null; }
            foreach (var c in _cells) if (c != null) Destroy(c);
            _cells.Clear();
            foreach (var t in _thumbs) if (t != null) Destroy(t);
            _thumbs.Clear();

            _title.text = "相册 · 挂到「" + SlotName(_slot) + "」";
            string cur = UserImageLibrary.PathFor(_slot);
            int pages = Mathf.Max(1, Mathf.CeilToInt(_images.Count / (float)PageSize));

            _status.text = _images.Count == 0
                ? "相册里没读到图片。请在系统弹窗里允许访问照片，然后点「打开相册」；" +
                  "也可以把图片放进 " + UserImageLibrary.FolderPath
                : "共 " + _images.Count + " 张（第 " + (_page + 1) + "/" + pages + " 页）——点一张即挂上。" +
                  (string.IsNullOrEmpty(cur) ? "" : "  当前：" + Path.GetFileName(cur));

            var cells = new List<RawImage>();
            var picks = new List<GalleryImage>();
            for (int i = 0; i < PageSize; i++)
            {
                int idx = _page * PageSize + i;
                if (idx >= _images.Count) break;
                var img = _images[idx];
                int col = i % Cols, row = i / Cols;
                var btn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                    new Vector2(-340 + col * 340, -300 - row * 200), new Vector2(320, 186),
                    new Color(0.16f, 0.19f, 0.24f, 0.95f), () => Pick(img), 18);
                _cells.Add(btn.gameObject);

                // 缩略图：先占位，稍后由协程逐张填进去
                var raw = new GameObject("Thumb", typeof(RawImage));
                raw.transform.SetParent(btn.transform, false);
                var rt = raw.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(300, 148);
                rt.anchoredPosition = new Vector2(0, 14);
                var ri = raw.GetComponent<RawImage>();
                ri.color = new Color(1f, 1f, 1f, 0.25f);
                ri.raycastTarget = false;
                cells.Add(ri);
                picks.Add(img);

                var label = btn.GetComponentInChildren<Text>();
                if (label != null)
                {
                    // 显示所属相册（Camera / Screenshots / …）；没有相册名就显示文件名
                    label.text = string.IsNullOrEmpty(img.album) ? img.Display : img.album;
                    label.fontSize = 18;
                    var lrt = label.GetComponent<RectTransform>();
                    lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
                    lrt.sizeDelta = new Vector2(300, 30);
                    lrt.anchoredPosition = new Vector2(0, 18);
                }
            }
            if (cells.Count > 0) _loading = StartCoroutine(LoadThumbs(cells, picks));
        }

        /// <summary>一帧一张地加载缩略图：一次性解码九张手机照片会卡住半秒以上。</summary>
        IEnumerator LoadThumbs(List<RawImage> cells, List<GalleryImage> picks)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                // timeScale=0 期间要用 unscaled 的等待，否则协程永远不往下走
                yield return null;
                if (cells[i] == null) continue;
                Texture2D tex = null;
                var bytes = UserImageLibrary.ReadBytes(picks[i]);
                if (bytes != null) tex = UserImageLibrary.Decode(bytes, ThumbSide);
                if (tex == null || cells[i] == null) continue;
                _thumbs.Add(tex);
                cells[i].texture = tex;
                cells[i].color = Color.white;
            }
            _loading = null;
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
            if (_loading != null) { StopCoroutine(_loading); _loading = null; }
            foreach (var t in _thumbs) if (t != null) Destroy(t);
            _thumbs.Clear();
            if (_panel != null) _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
