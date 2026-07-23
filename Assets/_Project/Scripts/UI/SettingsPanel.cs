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
        Button _softenBtn, _recoveryBtn, _followBtn, _debugBtn, _deleteBtn;
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
            _panel = UiUtil.MakePanel(canvas, "SettingsPanel", new Vector2(1100, 1040),
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
            _debugBtn = MakeToggle("调试模式（敌人耐揍，不易被打死）", -560, () =>
            {
                GameDebug.TankyEnemies = !GameDebug.TankyEnemies;
                Refresh();
            });

            // 跳章快进：主线结构重排后老玩家可快速回到原进度（视为完成，不发奖励）
            UiUtil.MakeButton(_panel.transform, "跳过当前子章（调试/老玩家快进）",
                new Vector2(0.5f, 1f), new Vector2(0, -650), new Vector2(760, 70),
                new Color(0.45f, 0.4f, 0.25f, 0.95f), () =>
                {
                    var story = StoryManager.Instance;
                    if (story == null || story.AllCleared)
                    {
                        GameEvents.RaiseSubtitle("主线已完结，没有可跳过的子章。");
                        return;
                    }
                    string skipped = story.Current.title;
                    story.SkipChapter();
                    GameEvents.RaiseSubtitle("已跳过【" + skipped + "】——主线推进到下一子章。");
                }, 24);

            // 心理安全系统：快速退出战斗——任何时刻一键传送回安全屋（独居小屋）
            UiUtil.MakeButton(_panel.transform, "一键返回安全屋（立刻脱离当前战斗）",
                new Vector2(0.5f, 1f), new Vector2(0, -740), new Vector2(760, 70),
                new Color(0.25f, 0.4f, 0.55f, 0.95f), ReturnToSafeHouse, 24);

            _deleteBtn = UiUtil.MakeButton(_panel.transform, "删除全部数据（存档/画像/提示词/进度）",
                new Vector2(0.5f, 1f), new Vector2(0, -830), new Vector2(760, 74),
                new Color(0.5f, 0.2f, 0.18f, 0.95f), OnDelete, 24);

            var note = UiUtil.MakeText(_panel.transform, "Note",
                "个人材料仅保存在本机；删除后自新的第一章重新开始。",
                20, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.45f));
            UiUtil.SetRect(note, new Vector2(0.5f, 1f), new Vector2(0, -894), new Vector2(900, 32));

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(0, 56),
                new Vector2(260, 74), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 28);

            _panel.SetActive(false);
        }

        Button MakeToggle(string label, float y, UnityEngine.Events.UnityAction onClick) =>
            UiUtil.MakeButton(_panel.transform, label, new Vector2(0.5f, 1f),
                new Vector2(0, y), new Vector2(760, 70), Off, onClick, 24);

        /// <summary>一键返回安全屋：不论身处哪个区域/是否交战，立即传送回独居小屋。</summary>
        void ReturnToSafeHouse()
        {
            var player = FindObjectOfType<PlayerController>();
            if (player == null) return;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = new Vector3(0, 1.1f, -5);   // 独居小屋出生点
            if (cc != null) cc.enabled = true;
            player.MoveSpeedMultiplier = 1f;
            World.ZoneBuilder.CurrentZoneId = "home";
            Hide();
            GameEvents.RaiseSubtitle("—— 已返回安全屋。喘口气，需要时再出发。——");
        }

        void OnDelete()
        {
            if (!_deleteArmed)
            {
                _deleteArmed = true;
                _deleteBtn.GetComponentInChildren<Text>().text = "再点一次确认删除！";
                return;
            }
            SaveSystem.DeleteAll();
            Core.GrowthSystem.DeleteAll();   // 清空成长/图鉴/档案的内存缓存
            Core.QuizSystem.DeleteAll();     // 清空答题记录的内存缓存
            Core.QuizAiBank.DeleteAll();     // 删除 AI 命题题库（含本地文件）
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
            if (_debugBtn != null)
                _debugBtn.GetComponent<Image>().color = GameDebug.TankyEnemies ? On : Off;
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
