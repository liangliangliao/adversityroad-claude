using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.Save;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 设置菜单（第六阶段）：心理安全系统 UI 化——
    /// 心理强度分级 / 台词柔化 / 恢复模式 / 镜头自动跟随 / 数据删除（二次确认）。
    /// 所有心理攻击与台词生成都读取这些开关。
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        GameObject _panel;
        readonly List<(Button btn, MentalIntensity val)> _intensityBtns =
            new List<(Button, MentalIntensity)>();
        Button _softenBtn, _recoveryBtn, _followBtn, _deleteBtn;
        bool _deleteArmed;

        static readonly Color Off = new Color(0.25f, 0.25f, 0.3f, 0.95f);
        static readonly Color On = new Color(0.2f, 0.55f, 0.35f, 0.95f);

        public static SettingsPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<SettingsPanel>();
            comp.Build(canvas);
            return comp;
        }

        SafetySettings Safety =>
            GameManager.Instance != null ? GameManager.Instance.safety : null;

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "SettingsPanel", new Vector2(1100, 860),
                new Color(0.08f, 0.08f, 0.12f, 0.97f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "设 置 · 心理安全", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(700, 52));

            var l1 = UiUtil.MakeText(_panel.transform, "L1", "心理攻击强度", 26,
                TextAnchor.MiddleLeft, Color.white);
            UiUtil.SetRect(l1, new Vector2(0.5f, 1f), new Vector2(-300, -120), new Vector2(400, 40));
            (string, MentalIntensity)[] levels =
            {
                ("轻度", MentalIntensity.Light),
                ("标准", MentalIntensity.Standard),
                ("高压", MentalIntensity.HighPressure)
            };
            for (int i = 0; i < levels.Length; i++)
            {
                var lv = levels[i];
                var btn = UiUtil.MakeButton(_panel.transform, lv.Item1, new Vector2(0.5f, 1f),
                    new Vector2(-220 + i * 240, -190), new Vector2(220, 70), Off, () =>
                    {
                        if (Safety != null) Safety.intensity = lv.Item2;
                        Refresh();
                    }, 26);
                _intensityBtns.Add((btn, lv.Item2));
            }

            _softenBtn = MakeToggle("台词柔化（降低攻击性表达）", -290, () =>
            {
                if (Safety != null) Safety.softenDialogue = !Safety.softenDialogue;
                Refresh();
            });
            _recoveryBtn = MakeToggle("恢复模式（停止一切心理攻击）", -380, () =>
            {
                if (Safety != null)
                {
                    Safety.recoveryMode = !Safety.recoveryMode;
                    if (Safety.recoveryMode) GameEvents.RaiseRecoveryMode();
                }
                Refresh();
            });
            _followBtn = MakeToggle("镜头自动跟随", -470, () =>
            {
                var cam = FindObjectOfType<ThirdPersonCamera>();
                if (cam != null) cam.autoFollow = !cam.autoFollow;
                Refresh();
            });

            _deleteBtn = UiUtil.MakeButton(_panel.transform, "删除全部数据（存档/画像/提示词/进度）",
                new Vector2(0.5f, 1f), new Vector2(0, -590), new Vector2(760, 74),
                new Color(0.5f, 0.2f, 0.18f, 0.95f), OnDelete, 24);

            var note = UiUtil.MakeText(_panel.transform, "Note",
                "个人材料仅保存在本机；删除后自新的第一章重新开始。",
                20, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.45f));
            UiUtil.SetRect(note, new Vector2(0.5f, 1f), new Vector2(0, -650), new Vector2(900, 32));

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(0, 56),
                new Vector2(260, 74), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 28);

            _panel.SetActive(false);
        }

        Button MakeToggle(string label, float y, UnityEngine.Events.UnityAction onClick) =>
            UiUtil.MakeButton(_panel.transform, label, new Vector2(0.5f, 1f),
                new Vector2(0, y), new Vector2(760, 70), Off, onClick, 24);

        void OnDelete()
        {
            if (!_deleteArmed)
            {
                _deleteArmed = true;
                _deleteBtn.GetComponentInChildren<Text>().text = "再点一次确认删除！";
                return;
            }
            SaveSystem.DeleteAll();
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            try
            {
                string p = Application.persistentDataPath + "/aiprompts.json";
                if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
            }
            catch { }
            GameEvents.RaiseSubtitle("数据已全部删除。重启游戏后从第一章重新开始。");
            _deleteArmed = false;
            _deleteBtn.GetComponentInChildren<Text>().text = "已删除（重启游戏生效）";
        }

        void Refresh()
        {
            var s = Safety;
            foreach (var (btn, val) in _intensityBtns)
                btn.GetComponent<Image>().color = s != null && s.intensity == val ? On : Off;
            if (_softenBtn != null)
                _softenBtn.GetComponent<Image>().color = s != null && s.softenDialogue ? On : Off;
            if (_recoveryBtn != null)
                _recoveryBtn.GetComponent<Image>().color = s != null && s.recoveryMode ? On : Off;
            var cam = FindObjectOfType<ThirdPersonCamera>();
            if (_followBtn != null)
                _followBtn.GetComponent<Image>().color = cam != null && cam.autoFollow ? On : Off;
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            _deleteArmed = false;
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
