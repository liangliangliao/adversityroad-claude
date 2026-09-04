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
        Coroutine _loading, _scanning;

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

            UiUtil.MakeButton(_panel.transform, "打开手机相册", new Vector2(0.5f, 0f), new Vector2(-420, 70),
                new Vector2(210, 72), new Color(0.24f, 0.40f, 0.32f, 0.98f), OpenSystemGallery, 22);
            UiUtil.MakeButton(_panel.transform, "上一页", new Vector2(0.5f, 0f), new Vector2(-205, 70),
                new Vector2(180, 72), new Color(0.24f, 0.26f, 0.32f, 0.95f), () => Turn(-1), 24);
            UiUtil.MakeButton(_panel.transform, "下一页", new Vector2(0.5f, 0f), new Vector2(0, 70),
                new Vector2(180, 72), new Color(0.24f, 0.26f, 0.32f, 0.95f), () => Turn(1), 24);
            UiUtil.MakeButton(_panel.transform, "取下这幅", new Vector2(0.5f, 0f), new Vector2(205, 70),
                new Vector2(200, 72), new Color(0.36f, 0.26f, 0.24f, 0.95f), Clear, 24);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(420, 70),
                new Vector2(180, 72), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 24);

            UiUtil.MakeButton(_panel.transform, "打 开 手 机 相 册", new Vector2(0.5f, 1f),
                new Vector2(0, -232), new Vector2(480, 66),
                new Color(0.26f, 0.44f, 0.34f, 0.98f), OpenSystemGallery, 26);
            UiUtil.MakeButton(_panel.transform, "重新扫描", new Vector2(0.5f, 1f),
                new Vector2(330, -232), new Vector2(160, 66),
                new Color(0.24f, 0.26f, 0.32f, 0.95f), Scan, 22);
            // 手动摆正：EXIF 已经自动处理，但有些图的方向信息本身就是错的/没有，
            // 给一个"再转 90°"的口子，按四下就转回来了
            UiUtil.MakeButton(_panel.transform, "旋转 90°", new Vector2(0.5f, 1f),
                new Vector2(-330, -232), new Vector2(160, 66),
                new Color(0.30f, 0.28f, 0.22f, 0.95f), RotateCurrent, 22);

            _panel.SetActive(false);
        }

        public void Toggle()
        {
            if (_panel == null) return;
            if (_panel.activeSelf) { Hide(); return; }
            Open(true);
        }

        /// <summary>打开面板并直接选中某个画框（走近画框按键时用）。
        /// 手机上**先直接拉起系统相册**——玩家要的就是那个界面；
        /// 取消或这台设备拉不起来，才用面板里的游戏内列表兜底。</summary>
        public void OpenFor(UserImageSlot slot)
        {
            _slot = slot;
            _page = 0;
            // 能拉起系统相册时**不要**同时扫描游戏内列表：那会立刻弹一个"允许读取相册"
            // 的权限框，和正在打开的系统相册撞在一起，两个弹窗叠着谁也看不懂。
            // 系统选图本来就不需要那个权限。
            Open(!NativeGallery.Available);
            OpenSystemGallery();
        }

        void Open(bool autoScan)
        {
            if (_panel == null) return;
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
            if (autoScan) Scan(); else Refresh();
        }

        /// <summary>拉起系统相册（安卓）。</summary>
        void OpenSystemGallery()
        {
            if (_status != null)
                _status.text = "正在打开手机相册……选完会自动挂到「" + SlotName(_slot) + "」上。";
            var slot = _slot;
            bool started = NativeGallery.Pick(uri =>
            {
                if (string.IsNullOrEmpty(uri))
                {
                    if (_status != null)
                        _status.text = "没有选图（可以在下面的列表里挑，或再点一次「打开手机相册」）。";
                    return;
                }
                if (UserImageLibrary.AssignFromUri(slot, uri))
                {
                    GameEvents.RaiseSubtitle("图片已挂到「" + SlotName(slot) + "」上。");
                    Hide();
                }
                else if (_status != null)
                {
                    _status.text = "这张图读不出来，换一张试试。";
                }
            });
            if (!started) Scan();   // 编辑器/PC：直接用游戏内列表
        }

        void Scan()
        {
            if (_scanning != null) StopCoroutine(_scanning);
            _scanning = StartCoroutine(ScanRoutine());
        }

        /// <summary>
        /// 扫描游戏内可读到的图片。
        ///
        /// 【上一版为什么"点了没反应"】权限弹窗是**异步**的，而上一版申请完立刻就查
        /// MediaStore——那一刻用户还没点"允许"，于是永远查到 0 张、界面毫无变化。
        /// 这里改成先申请、再等弹窗结果（最多 20 秒），全程把状态写在面板上。
        /// </summary>
        IEnumerator ScanRoutine()
        {
            _images.Clear();
            _page = 0;
            if (!UserImageLibrary.HasReadPermission)
            {
                if (_status != null) _status.text = "正在申请相册权限……请在系统弹窗里选择「允许」。";
                UserImageLibrary.RequestReadPermission();
                float waited = 0f;
                while (!UserImageLibrary.HasReadPermission && waited < 20f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            _images.AddRange(UserImageLibrary.Browse());
            Refresh();
            _scanning = null;
        }

        void Turn(int d)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(_images.Count / (float)PageSize));
            _page = Mathf.Clamp(_page + d, 0, pages - 1);
            Refresh();
        }

        void RotateCurrent()
        {
            if (UserImageLibrary.Rotate(_slot, true))
            {
                if (_status != null) _status.text = "已把「" + SlotName(_slot) + "」的图片转了 90°。";
            }
            else if (_status != null)
            {
                _status.text = "这一处还没有挂图片。";
            }
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
                ? "点上面的【打 开 手 机 相 册】直接从手机相册里挑一张（不需要任何权限）。\n" +
                  "下面这个列表是备用的：它需要「读取相册」权限，点「重新扫描」并允许即可。"
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
                    new Vector2(-340 + col * 340, -338 - row * 190), new Vector2(320, 176),
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
            if (_scanning != null) { StopCoroutine(_scanning); _scanning = null; }
            foreach (var t in _thumbs) if (t != null) Destroy(t);
            _thumbs.Clear();
            if (_panel != null) _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
