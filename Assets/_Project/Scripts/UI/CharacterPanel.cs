using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 角色·武器库面板（「角色」按钮打开）：
    /// 上半区选角色（角色·壹 / 角色·贰，资产分离、各自动作库），
    /// 下半区从武器库选武器（默认佩剑 + Resources/Characters/Weapons/ 下全部武器，
    /// 重选即替换手中武器）。选择即时生效并本地持久化。
    /// </summary>
    public class CharacterPanel : MonoBehaviour
    {
        GameObject _panel;
        PlayerAppearance _appearance;
        Transform _canvas;

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
            // 每次打开重建：武器库/当前选中状态实时刷新
            if (_panel != null) Destroy(_panel);
            Build();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
            Time.timeScale = 1f;
        }

        void Build()
        {
            var weapons = PlayerAppearance.ListWeapons();
            var masks = PlayerAppearance.ListMasks();
            int wRows = (weapons.Length + 1 + 2) / 3;    // 武器网格行数（含默认佩剑）
            int mRows = (masks.Length + 1 + 2) / 3;      // 面具网格行数（含无面具）
            // 面板高度随内容自适应，保证三区不重叠
            float height = 300 + wRows * 88 + mRows * 88 + 200;
            _panel = UiUtil.MakePanel(_canvas, "CharacterPanel", new Vector2(1120, height),
                new Color(0.07f, 0.07f, 0.11f, 0.97f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "角 色 · 装 备 库", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -42), new Vector2(700, 52));

            float y = -100;
            // ---- 角色区 ----
            var secC = UiUtil.MakeText(_panel.transform, "SecChar", "—— 角色（各自模型与动作库） ——",
                24, TextAnchor.MiddleCenter, new Color(0.7f, 0.85f, 1f));
            UiUtil.SetRect(secC, new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(700, 34));
            y -= 62;
            for (int i = 0; i < PlayerAppearance.PresetNames.Length; i++)
            {
                int idx = i;
                bool current = _appearance != null && _appearance.Preset == i;
                UiUtil.MakeButton(_panel.transform,
                    (current ? "✓ " : "") + PlayerAppearance.PresetNames[i],
                    new Vector2(0.5f, 1f), new Vector2(-215 + i * 430, y),
                    new Vector2(400, 74),
                    current ? new Color(0.32f, 0.5f, 0.34f, 0.95f) : new Color(0.28f, 0.32f, 0.42f, 0.95f),
                    () => { if (_appearance != null) _appearance.SetPreset(idx); Refresh(); }, 26);
            }
            y -= 92;

            // ---- 武器区 ----
            var secW = UiUtil.MakeText(_panel.transform, "SecWeapon",
                "—— 武器库（重选即替换手中武器） ——", 24,
                TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.6f));
            UiUtil.SetRect(secW, new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(700, 34));
            y -= 54;
            string curW = _appearance != null ? _appearance.CurrentWeapon : "";
            for (int i = 0; i < weapons.Length + 1; i++)
            {
                string wName = i == 0 ? "" : weapons[i - 1];
                string label = i == 0 ? "默认佩剑" : weapons[i - 1];
                bool current = curW == wName;
                int col = i % 3, row = i / 3;
                UiUtil.MakeButton(_panel.transform, (current ? "✓ " : "") + label,
                    new Vector2(0.5f, 1f), new Vector2(-330 + col * 330, y - row * 88),
                    new Vector2(310, 74),
                    current ? new Color(0.32f, 0.5f, 0.34f, 0.95f) : new Color(0.4f, 0.32f, 0.26f, 0.95f),
                    () => { if (_appearance != null) _appearance.EquipWeapon(wName); Refresh(); }, 24);
            }
            y -= wRows * 88 + 24;

            // ---- 面具区 ----
            var secM = UiUtil.MakeText(_panel.transform, "SecMask",
                "—— 面具库（戴在脸部·重选即替换） ——", 24,
                TextAnchor.MiddleCenter, new Color(0.85f, 0.7f, 1f));
            UiUtil.SetRect(secM, new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(700, 34));
            y -= 54;
            string curM = _appearance != null ? _appearance.CurrentMask : "";
            for (int i = 0; i < masks.Length + 1; i++)
            {
                string mName = i == 0 ? "" : masks[i - 1];
                string label = i == 0 ? "不戴面具" : masks[i - 1];
                bool current = curM == mName;
                int col = i % 3, row = i / 3;
                UiUtil.MakeButton(_panel.transform, (current ? "✓ " : "") + label,
                    new Vector2(0.5f, 1f), new Vector2(-330 + col * 330, y - row * 88),
                    new Vector2(310, 74),
                    current ? new Color(0.32f, 0.5f, 0.34f, 0.95f) : new Color(0.42f, 0.32f, 0.5f, 0.95f),
                    () => { if (_appearance != null) _appearance.EquipMask(mName); Refresh(); }, 24);
            }

            // 资产目录提示（把下载的文件放进这些目录即自动出现在本面板）
            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "角色贰模型：Resources/Characters/PlayerModel2（.glb/.gltf/.fbx）　武器库：Resources/Characters/Weapons/\n" +
                "面具库：Resources/Characters/Masks/（.fbx/.glb/.gltf 或 .zip 压缩包，文件名即装备名）",
                20, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.5f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 0f), new Vector2(0, 148), new Vector2(1060, 60));

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(0, 56),
                new Vector2(260, 74), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 28);

            _panel.SetActive(false);
        }

        /// <summary>选择后重建面板（刷新 ✓ 选中标记），保持打开状态。</summary>
        void Refresh()
        {
            if (_panel != null) Destroy(_panel);
            Build();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
        }
    }
}
