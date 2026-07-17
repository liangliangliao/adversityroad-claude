using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 装备套装面板（安全屋·装备栏）：五大套装被动，一次穿一套。
    /// 对齐方案：边界守卫套 / 专注夺回套 / 公平复盘套 / 行动起步套 / 旧我整合套。
    /// 选择即生效并本地持久化（被动倍率在 GrowthSystem 查询）。
    /// </summary>
    public class EquipmentPanel : MonoBehaviour
    {
        GameObject _panel;
        readonly List<(Button btn, Text label, string setId)> _setButtons =
            new List<(Button, Text, string)>();

        public static EquipmentPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<EquipmentPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "EquipmentPanel", new Vector2(1100, 1000),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "装 备 套 装", 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(700, 56));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "一次只能穿一套。把套装对准你正在面对的困境线。", 24,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.85f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -96), new Vector2(900, 34));

            for (int i = 0; i < GrowthSystem.Sets.Length; i++)
            {
                var set = GrowthSystem.Sets[i];
                var btn = UiUtil.MakeButton(_panel.transform, "",
                    new Vector2(0.5f, 1f), new Vector2(0, -210 - i * 150),
                    new Vector2(1000, 136), new Color(0.2f, 0.22f, 0.3f, 0.96f), null, 22);
                var label = btn.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                var lrt = label.GetComponent<RectTransform>();
                lrt.offsetMin = new Vector2(20, 8);
                lrt.offsetMax = new Vector2(-12, -8);

                string setId = set.id;
                btn.onClick.AddListener(() => Select(setId));
                _setButtons.Add((btn, label, setId));
            }

            UiUtil.MakeButton(_panel.transform, "卸下套装", new Vector2(0.5f, 0f),
                new Vector2(-170, 54), new Vector2(300, 74),
                new Color(0.4f, 0.32f, 0.3f, 0.95f), () => Select(""), 26);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(170, 54), new Vector2(300, 74),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        void Select(string setId)
        {
            GrowthSystem.EquippedSet = setId;
            if (!string.IsNullOrEmpty(setId))
            {
                foreach (var s in GrowthSystem.Sets)
                    if (s.id == setId)
                    {
                        GameEvents.RaiseSubtitle("已穿上【" + s.name + "】——「" + s.mantra + "」");
                        break;
                    }
                GameAudio.Play(GameAudio.Sfx.Parry, 0.7f);
            }
            else GameEvents.RaiseSubtitle("已卸下套装。");
            Refresh();
        }

        void Refresh()
        {
            string equipped = GrowthSystem.EquippedSet;
            foreach (var (btn, label, setId) in _setButtons)
            {
                EquipmentSet def = default;
                foreach (var s in GrowthSystem.Sets)
                    if (s.id == setId) { def = s; break; }
                bool on = equipped == setId;
                label.text = (on ? "【已穿戴】" : "") + def.name + "\n" +
                    def.desc + "\n「" + def.mantra + "」";
                btn.GetComponent<Image>().color = on
                    ? new Color(0.25f, 0.45f, 0.32f, 0.96f)
                    : new Color(0.2f, 0.22f, 0.3f, 0.96f);
            }
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            Refresh();
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
