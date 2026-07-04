using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Combat;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 装备套装面板（安全屋核心 · 方案「十、玩家兵器与装备系统」）：
    /// 左栏列出六套装备（自由行者 + 五套方案套装），点选即装备并本地持久化；
    /// 右栏展示所选套装的象征兵器、战斗效果与核心台词。
    /// </summary>
    public class EquipmentPanel : MonoBehaviour
    {
        GameObject _panel;
        readonly Button[] _btns = new Button[EquipmentSystem.Defs.Length];
        Text _weaponText, _effectsText, _coreText;

        public static EquipmentPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<EquipmentPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "EquipmentPanel", new Vector2(1180, 840),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "兵 器 与 装 备", 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(700, 56));

            var defs = EquipmentSystem.Defs;
            for (int i = 0; i < defs.Length; i++)
            {
                int idx = i;
                var btn = UiUtil.MakeButton(_panel.transform, defs[i].name, new Vector2(0f, 1f),
                    new Vector2(210, -140 - i * 100), new Vector2(360, 84),
                    new Color(0.2f, 0.24f, 0.32f, 0.95f), () => OnPick(idx), 26);
                _btns[i] = btn;
            }

            var wLabel = UiUtil.MakeText(_panel.transform, "WLabel", "象征兵器", 22,
                TextAnchor.MiddleLeft, new Color(0.7f, 0.8f, 0.95f));
            UiUtil.SetRect(wLabel, new Vector2(0f, 1f), new Vector2(700, -150), new Vector2(420, 30));
            wLabel.fontStyle = FontStyle.Bold;
            _weaponText = UiUtil.MakeText(_panel.transform, "WText", "", 26,
                TextAnchor.UpperLeft, Color.white);
            UiUtil.SetRect(_weaponText, new Vector2(0f, 1f), new Vector2(700, -210), new Vector2(440, 60));

            var eLabel = UiUtil.MakeText(_panel.transform, "ELabel", "战斗效果", 22,
                TextAnchor.MiddleLeft, new Color(0.6f, 0.9f, 0.7f));
            UiUtil.SetRect(eLabel, new Vector2(0f, 1f), new Vector2(700, -290), new Vector2(420, 30));
            eLabel.fontStyle = FontStyle.Bold;
            _effectsText = UiUtil.MakeText(_panel.transform, "EText", "", 24,
                TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.92f));
            UiUtil.SetRect(_effectsText, new Vector2(0f, 1f), new Vector2(700, -430), new Vector2(440, 200));

            var cLabel = UiUtil.MakeText(_panel.transform, "CLabel", "核心", 22,
                TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.5f));
            UiUtil.SetRect(cLabel, new Vector2(0f, 1f), new Vector2(700, -560), new Vector2(420, 30));
            cLabel.fontStyle = FontStyle.Bold;
            _coreText = UiUtil.MakeText(_panel.transform, "CText", "", 25,
                TextAnchor.UpperLeft, new Color(1f, 0.92f, 0.78f));
            UiUtil.SetRect(_coreText, new Vector2(0f, 1f), new Vector2(700, -640), new Vector2(440, 120));
            _coreText.fontStyle = FontStyle.Italic;

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 56), new Vector2(300, 74),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        void OnPick(int idx)
        {
            var equip = Equip();
            if (equip != null) equip.Equip(idx);
            Refresh(idx);
        }

        void Refresh(int shown)
        {
            var equip = Equip();
            int current = equip != null ? equip.Index : 0;
            var defs = EquipmentSystem.Defs;
            for (int i = 0; i < _btns.Length; i++)
            {
                var img = _btns[i].GetComponent<Image>();
                img.color = (i == current)
                    ? new Color(0.35f, 0.55f, 0.4f, 0.98f)   // 当前装备高亮
                    : new Color(0.2f, 0.24f, 0.32f, 0.95f);
            }

            var d = defs[Mathf.Clamp(shown, 0, defs.Length - 1)];
            _weaponText.text = d.weapon;
            var sb = new System.Text.StringBuilder();
            if (d.effects != null)
                foreach (var e in d.effects) sb.Append("· ").Append(e).Append('\n');
            _effectsText.text = sb.ToString();
            _coreText.text = "「" + d.coreLine + "」";
        }

        static EquipmentSystem Equip()
        {
            var pc = FindObjectOfType<PlayerController>();
            return pc != null ? pc.GetComponent<EquipmentSystem>() : null;
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            var equip = Equip();
            Refresh(equip != null ? equip.Index : 0);
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        void Hide()
        {
            _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
