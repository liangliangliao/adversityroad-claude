using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 角色·装备库面板（「角色」按钮打开）。两级结构：
    ///   ① 装备库主页：选角色（角色·壹 / 角色·贰，资产分离、各自动作库）
    ///      + 打开「背包」的入口按钮；
    ///   ② 背包子菜单：玩家的随身装备都在这里——穿戴背包（背在身上）、
    ///      武器库（重选即替换手中武器）、面具库（戴在脸上）。
    /// 选择即时生效并本地持久化。每次进入某页都重建，实时刷新目录与选中状态。
    /// </summary>
    public class CharacterPanel : MonoBehaviour
    {
        enum Page { Main, Backpack }

        GameObject _panel;
        PlayerAppearance _appearance;
        Transform _canvas;
        Page _page = Page.Main;

        public static CharacterPanel Create(Transform canvas, PlayerAppearance appearance)
        {
            var comp = canvas.gameObject.AddComponent<CharacterPanel>();
            comp._appearance = appearance;
            comp._canvas = canvas;
            return comp;
        }

        public void Toggle()
        {
            if (_panel != null && _panel.activeSelf) { Hide(); return; }
            _page = Page.Main;
            Rebuild();
            Time.timeScale = 0f;
        }

        void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
            Time.timeScale = 1f;
        }

        void Rebuild()
        {
            if (_panel != null) Destroy(_panel);
            if (_page == Page.Main) BuildMain();
            else BuildBackpack();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
        }

        void GoTo(Page p) { _page = p; Rebuild(); }

        // ---------- 主页：角色 + 进入背包 ----------
        void BuildMain()
        {
            float height = 300 + 90 + 200;
            _panel = UiUtil.MakePanel(_canvas, "CharacterPanel", new Vector2(1120, height),
                new Color(0.07f, 0.07f, 0.11f, 0.97f));

            Title("角 色 · 装 备 库");

            float y = -104;
            Section("—— 角色（各自模型与动作库） ——", new Color(0.7f, 0.85f, 1f), ref y, 62);
            for (int i = 0; i < PlayerAppearance.PresetNames.Length; i++)
            {
                int idx = i;
                bool current = _appearance != null && _appearance.Preset == i;
                UiUtil.MakeButton(_panel.transform,
                    (current ? "✓ " : "") + PlayerAppearance.PresetNames[i],
                    new Vector2(0.5f, 1f), new Vector2(-215 + i * 430, y), new Vector2(400, 74),
                    current ? Sel : new Color(0.28f, 0.32f, 0.42f, 0.95f),
                    () => { if (_appearance != null) _appearance.SetPreset(idx); Rebuild(); }, 26);
            }
            y -= 118;

            // 背包入口（武器 / 面具 / 穿戴背包都在里面）
            UiUtil.MakeButton(_panel.transform, "打开背包（背包 · 武器 · 面具）",
                new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(680, 80),
                new Color(0.34f, 0.3f, 0.5f, 0.96f), () => GoTo(Page.Backpack), 27);

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "角色贰模型：Resources/Characters/PlayerModel2（.glb/.gltf/.fbx）",
                20, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.5f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 0f), new Vector2(0, 148), new Vector2(1060, 40));

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(0, 56),
                new Vector2(260, 74), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 28);

            _panel.SetActive(false);
        }

        // ---------- 背包子菜单：穿戴背包 + 武器 + 面具 ----------
        void BuildBackpack()
        {
            var backpacks = PlayerAppearance.ListBackpacks();
            var weapons = PlayerAppearance.ListWeapons();
            var masks = PlayerAppearance.ListMasks();
            int bRows = (backpacks.Length + 1 + 2) / 3;
            int wRows = (weapons.Length + 1 + 2) / 3;
            int mRows = (masks.Length + 1 + 2) / 3;
            float height = 240 + bRows * 88 + wRows * 88 + mRows * 88 + 240;
            _panel = UiUtil.MakePanel(_canvas, "CharacterPanel", new Vector2(1120, height),
                new Color(0.07f, 0.07f, 0.11f, 0.97f));

            Title("背 包");

            float y = -104;
            // 穿戴背包
            Section("—— 背包（背在身上·重选即替换） ——", new Color(0.75f, 0.85f, 0.7f), ref y, 54);
            string curB = _appearance != null ? _appearance.CurrentBackpack : "";
            Grid(backpacks, "不背背包", curB, y, new Color(0.3f, 0.4f, 0.34f, 0.95f),
                name => { if (_appearance != null) _appearance.EquipBackpack(name); Rebuild(); });
            y -= bRows * 88 + 22;

            // 武器
            Section("—— 武器库（重选即替换手中武器） ——", new Color(1f, 0.8f, 0.6f), ref y, 54);
            string curW = _appearance != null ? _appearance.CurrentWeapon : "";
            Grid(weapons, "默认（自带武器）", curW, y, new Color(0.4f, 0.32f, 0.26f, 0.95f),
                name => { if (_appearance != null) _appearance.EquipWeapon(name); Rebuild(); });
            y -= wRows * 88 + 22;

            // 面具
            Section("—— 面具库（戴在脸上·重选即替换） ——", new Color(0.85f, 0.7f, 1f), ref y, 54);
            string curM = _appearance != null ? _appearance.CurrentMask : "";
            Grid(masks, "不戴面具", curM, y, new Color(0.42f, 0.32f, 0.5f, 0.95f),
                name => { if (_appearance != null) _appearance.EquipMask(name); Rebuild(); });

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "背包库：Resources/Characters/Backpacks/　武器库：Resources/Characters/Weapons/　面具库：Resources/Characters/Masks/\n" +
                "（.fbx/.glb/.gltf 或 .zip 压缩包，文件名即装备名；缺贴图会自动接回，不再白模）",
                19, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.5f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(1080, 60));

            UiUtil.MakeButton(_panel.transform, "返回", new Vector2(0.5f, 0f), new Vector2(-150, 56),
                new Vector2(240, 74), new Color(0.32f, 0.34f, 0.46f, 0.95f), () => GoTo(Page.Main), 26);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(150, 56),
                new Vector2(240, 74), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 28);

            _panel.SetActive(false);
        }

        // ---------- 复用小工具 ----------
        static readonly Color Sel = new Color(0.32f, 0.5f, 0.34f, 0.95f);

        void Title(string text)
        {
            var title = UiUtil.MakeText(_panel.transform, "Title", text, 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -42), new Vector2(700, 52));
        }

        void Section(string text, Color color, ref float y, float advance)
        {
            var s = UiUtil.MakeText(_panel.transform, "Sec", text, 24, TextAnchor.MiddleCenter, color);
            UiUtil.SetRect(s, new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(760, 34));
            y -= advance;
        }

        /// <summary>装备网格（首格=清除项"默认/不戴/不背"，其后为目录项），3 列排布。</summary>
        void Grid(string[] items, string clearLabel, string current, float y,
            Color baseColor, System.Action<string> onPick)
        {
            for (int i = 0; i < items.Length + 1; i++)
            {
                string name = i == 0 ? "" : items[i - 1];
                string label = i == 0 ? clearLabel : items[i - 1];
                bool cur = current == name;
                int col = i % 3, row = i / 3;
                UiUtil.MakeButton(_panel.transform, (cur ? "✓ " : "") + label,
                    new Vector2(0.5f, 1f), new Vector2(-330 + col * 330, y - row * 88),
                    new Vector2(310, 74), cur ? Sel : baseColor,
                    () => onPick(name), 24);
            }
        }
    }
}
