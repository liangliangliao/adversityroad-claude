using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.World;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 关卡选择（安全屋·传送）：列出九大区域与解锁状态，一键传送到已解锁区域出生点。
    /// 当前章节的目标区域高亮标注——解决"通关后不知道下一章入口在哪"的引导问题。
    /// 未解锁区域按剧情锁定（跟随 StoryManager.ZoneUnlocked）。
    /// </summary>
    public class LevelSelectPanel : MonoBehaviour
    {
        GameObject _panel;
        Text _headerText;
        readonly List<(Button btn, Text label, int zone)> _zoneButtons =
            new List<(Button, Text, int)>();

        public static LevelSelectPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<LevelSelectPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "LevelSelectPanel", new Vector2(1180, 980),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "关 卡 选 择 · 传 送", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(700, 54));

            _headerText = UiUtil.MakeText(_panel.transform, "Header", "", 24,
                TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 0.8f));
            UiUtil.SetRect(_headerText, new Vector2(0.5f, 1f), new Vector2(0, -98), new Vector2(1000, 36));

            for (int i = 0; i < ZoneBuilder.ZoneCount; i++)
            {
                int zone = i;
                var btn = UiUtil.MakeButton(_panel.transform, "",
                    new Vector2(0.5f, 1f), new Vector2(-270 + (i % 2) * 540, -180 - (i / 2) * 130),
                    new Vector2(520, 112), new Color(0.2f, 0.22f, 0.3f, 0.96f),
                    () => Teleport(zone), 24);
                var label = btn.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                var lrt = label.GetComponent<RectTransform>();
                lrt.offsetMin = new Vector2(18, 6);
                lrt.offsetMax = new Vector2(-10, -6);
                _zoneButtons.Add((btn, label, zone));
            }

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 52), new Vector2(260, 72),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        /// <summary>当前章节的目标区域（-1 = 主线完结）。</summary>
        static int CurrentTargetZone()
        {
            var story = StoryManager.Instance;
            return story != null && !story.AllCleared && story.Current != null
                ? story.Current.zoneIndex : -1;
        }

        void Teleport(int zone)
        {
            var story = StoryManager.Instance;
            if (story != null && !story.ZoneUnlocked(zone))
            {
                GameEvents.RaiseSubtitle("【" + ZoneBuilder.ZoneNameOf(zone) +
                    "】仍被心魔封锁——先完成当前章节的试炼。");
                Refresh();
                return;
            }

            var player = FindObjectOfType<PlayerController>();
            if (player == null) return;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = ZoneBuilder.PlayerSpawnOf(zone);
            if (cc != null) cc.enabled = true;
            player.MoveSpeedMultiplier = 1f;
            ZoneBuilder.CurrentZoneId = ZoneBuilder.ZoneIdOf(zone);

            Hide();
            GameEvents.RaiseSubtitle("—— 传送至 " + ZoneBuilder.ZoneNameOf(zone) + " ——" +
                (zone == CurrentTargetZone() ? "（当前章节的战场就在这里）" : ""));
            GameAudio.Play(GameAudio.Sfx.Cast, 0.6f);
        }

        void Refresh()
        {
            var story = StoryManager.Instance;
            int target = CurrentTargetZone();
            _headerText.text = story != null && !story.AllCleared && story.Current != null
                ? "当前主线：" + story.Current.title + "（目标区域已用 ▶ 标出）"
                : "主线已完结——所有区域自由通行。";

            foreach (var (btn, label, zone) in _zoneButtons)
            {
                bool unlocked = story == null || story.ZoneUnlocked(zone);
                bool isTarget = zone == target;
                label.text = (isTarget ? "▶ " : "") + ZoneBuilder.ZoneNameOf(zone) +
                    "\n" + (unlocked ? (isTarget ? "当前章节目标——点击传送" : "已解锁——点击传送")
                                     : "未解锁（推进主线开启）");
                btn.GetComponent<Image>().color = isTarget
                    ? new Color(0.5f, 0.38f, 0.18f, 0.96f)
                    : unlocked
                        ? new Color(0.2f, 0.28f, 0.24f, 0.96f)
                        : new Color(0.16f, 0.16f, 0.2f, 0.9f);
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
