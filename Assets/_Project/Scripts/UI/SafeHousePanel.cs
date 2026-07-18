using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 安全屋面板（主枢纽）：夺回人生主线的象征空间。
    /// 汇总五大子系统入口——复盘 / 技能树（成长）/ 装备套装 / 敌人图鉴 / 旧事档案。
    /// </summary>
    public class SafeHousePanel : MonoBehaviour
    {
        GameObject _panel;
        ReflectionPanel _reflection;
        GrowthPanel _growth;
        EquipmentPanel _equipment;
        CodexPanel _codex;
        ArchivePanel _archive;
        LevelSelectPanel _levelSelect;
        MissionBoardPanel _missionBoard;
        ActionTrackerPanel _actionTracker;
        UnityEngine.UI.Button _actionBtn;

        public static SafeHousePanel Create(Transform canvas, ReflectionPanel reflection,
            GrowthPanel growth, EquipmentPanel equipment, CodexPanel codex, ArchivePanel archive,
            LevelSelectPanel levelSelect, MissionBoardPanel missionBoard, ActionTrackerPanel actionTracker)
        {
            var comp = canvas.gameObject.AddComponent<SafeHousePanel>();
            comp._reflection = reflection;
            comp._growth = growth;
            comp._equipment = equipment;
            comp._codex = codex;
            comp._archive = archive;
            comp._levelSelect = levelSelect;
            comp._missionBoard = missionBoard;
            comp._actionTracker = actionTracker;
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "SafeHousePanel", new Vector2(780, 1130),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "安 全 屋", 42,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(500, 60));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "恢复、整理、升级、重新选择方向的地方。", 24,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.85f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -104), new Vector2(640, 34));

            MakeEntry("复盘 · 把这一战转成经验", 0, () => Open(_reflection != null ? (System.Action)_reflection.Toggle : null));
            _actionBtn = MakeEntry(ActionLabel(), 1, () => Open(_actionTracker != null ? (System.Action)_actionTracker.Toggle : null));
            MakeEntry("目标 · 钉下今日唯一目标", 2, () => Open(_missionBoard != null ? (System.Action)_missionBoard.Toggle : null));
            MakeEntry("成长 · 技能树五条路线", 3, () => Open(_growth != null ? (System.Action)_growth.Toggle : null));
            MakeEntry("装备 · 五大套装被动", 4, () => Open(_equipment != null ? (System.Action)_equipment.Toggle : null));
            MakeEntry("图鉴 · 识别心魔模式", 5, () => Open(_codex != null ? (System.Action)_codex.Toggle : null));
            MakeEntry("档案 · 归档过的旧事", 6, () => Open(_archive != null ? (System.Action)_archive.Toggle : null));
            MakeEntry("关卡 · 传送到已解锁区域", 7, () => Open(_levelSelect != null ? (System.Action)_levelSelect.Toggle : null));

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 52), new Vector2(260, 72),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        /// <summary>「行动」入口标签：有待确认的现实承诺时挂角标提醒。</summary>
        static string ActionLabel()
        {
            int due = ActionSystem.PendingCount;
            return due > 0 ? "行动 · 现实承诺待确认（" + due + "）" : "行动 · 追踪现实里的一小步";
        }

        UnityEngine.UI.Button MakeEntry(string label, int index, UnityEngine.Events.UnityAction onClick)
        {
            return UiUtil.MakeButton(_panel.transform, label, new Vector2(0.5f, 1f),
                new Vector2(0, -186 - index * 100), new Vector2(640, 84),
                new Color(0.2f, 0.24f, 0.32f, 0.96f), onClick, 25);
        }

        void Open(System.Action toggle)
        {
            Hide();
            toggle?.Invoke();
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            if (_actionBtn != null)
            {
                var t = _actionBtn.GetComponentInChildren<UnityEngine.UI.Text>();
                if (t != null) t.text = ActionLabel();
            }
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
