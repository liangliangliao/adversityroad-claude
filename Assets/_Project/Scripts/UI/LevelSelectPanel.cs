using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.World;
using AdversityRoad.Goals;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 关卡选择（安全屋·传送）：列出全部区域与解锁状态，一键传送到已解锁区域出生点。
    /// 当前章节的目标区域高亮标注——解决"通关后不知道下一章入口在哪"的引导问题。
    /// 未解锁区域按剧情锁定（跟随 StoryManager.ZoneUnlocked）。
    ///
    /// V2.0 起这里分两段：**为你的目标生成的关卡**排在最上面（它们才是必选主线），
    /// 经典 24 关列在下面作为附加可选项。生成关卡是运行时才存在的，
    /// 所以按钮不能在 Build 时一次造完——每次打开面板都要重列一遍。
    /// </summary>
    public class LevelSelectPanel : MonoBehaviour
    {
        GameObject _panel;
        Text _headerText;
        Transform _grid;
        readonly List<GameObject> _buttons = new List<GameObject>();

        public static LevelSelectPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<LevelSelectPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            // 版面按"两段 + 三十来个格子"算足：五列比四列每行多放一个，
            // 整块高度因此矮一截，PanelFit 缩放后字还看得清（四列时会被压到读不了）
            _panel = UiUtil.MakePanel(canvas, "LevelSelectPanel", new Vector2(1260, 1180),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "关 卡 选 择 · 传 送", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(700, 54));

            _headerText = UiUtil.MakeText(_panel.transform, "Header", "", 24,
                TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 0.8f));
            UiUtil.SetRect(_headerText, new Vector2(0.5f, 1f), new Vector2(0, -98), new Vector2(1000, 36));

            _grid = _panel.transform;

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 46), new Vector2(260, 68),
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
            player.NotifyTeleported();
            player.MoveSpeedMultiplier = 1f;
            ZoneBuilder.CurrentZoneId = ZoneBuilder.ZoneIdOf(zone);

            Hide();
            GameEvents.RaiseSubtitle("—— 传送至 " + ZoneBuilder.ZoneNameOf(zone) + " ——" +
                (zone == CurrentTargetZone() ? "（当前章节的战场就在这里）" : ""));
            GameAudio.Play(GameAudio.Sfx.Cast, 0.6f);
        }

        void Refresh()
        {
            // 先隐藏再销毁：Destroy 要到帧末才生效，只 Destroy 的话
            // 旧格子会和刚建好的新格子在同一帧里叠着渲染一次
            foreach (var b in _buttons)
                if (b != null) { b.SetActive(false); Destroy(b); }
            _buttons.Clear();

            var story = StoryManager.Instance;
            int target = CurrentTargetZone();
            var goal = Goals.GoalOS.Active;

            _headerText.text = goal != null && !goal.completed
                ? "当前目标：" + goal.title + "（专属关卡在最上面一段）"
                : story != null && !story.AllCleared && story.Current != null
                    ? "当前主线：" + story.Current.title + "（目标区域已用 ▶ 标出）"
                    : "主线已完结——所有区域自由通行。";

            int slot = 0;

            // ---- 第一段：为这个目标生成的关卡（必选主线）----
            if (goal != null)
            {
                var made = new List<GoalChapterData>();
                foreach (var c in goal.chapters)
                    if (c.source == ChapterSource.AIRequired || c.source == ChapterSource.UserPreset)
                        made.Add(c);

                if (made.Count > 0)
                {
                    Header(ref slot, "◆ 为「" + goal.title + "」生成的关卡（" + made.Count + "）");
                    foreach (var ch in made)
                    {
                        bool ready = OpenWorld.ProceduralQuestAssembler.IsAssembled(ch.chapterId);
                        string siteName = ch.site != null && !string.IsNullOrEmpty(ch.site.siteName)
                            ? ch.site.siteName : ch.chapterName;
                        string cid = ch.chapterId;
                        var ob = goal.FindObstacle(ch.primaryObstacle);

                        // 没建好也照样能点：点下去就现场搭建再进去（预生成/临时组装）
                        Cell(ref slot,
                            (ch.cleared ? "✓ " : "") + siteName + "\n" +
                            (ob != null ? ob.label : ch.chapterName) + "\n" +
                            (ch.cleared ? "已通关——可重进" : ready ? "已就绪——点击进入" : "点击现场搭建并进入"),
                            ch.cleared ? new Color(0.24f, 0.32f, 0.26f, 0.96f)
                                       : new Color(0.5f, 0.38f, 0.18f, 0.96f),
                            () => EnterSite(cid, siteName));
                    }
                }
            }

            // ---- 第二段：经典 24 关 + 开放城区（附加可选）----
            Header(ref slot, "◇ 经典关卡与开放城区（附加可选）");
            for (int zone = 0; zone < ZoneBuilder.ZoneCount; zone++)
            {
                if (ZoneBuilder.IsDynamicZone(zone)) continue;   // 生成场景已在上面一段列过
                int z = zone;
                bool unlocked = story == null || story.ZoneUnlocked(z);
                bool isTarget = z == target;
                Cell(ref slot,
                    (isTarget ? "▶ " : "") + ZoneBuilder.ZoneNameOf(z) + "\n" +
                    (unlocked ? (isTarget ? "当前章节目标——点击传送" : "已解锁——点击传送")
                              : "未解锁（推进主线开启）"),
                    isTarget ? new Color(0.5f, 0.38f, 0.18f, 0.96f)
                             : unlocked ? new Color(0.2f, 0.28f, 0.24f, 0.96f)
                                        : new Color(0.16f, 0.16f, 0.2f, 0.9f),
                    () => Teleport(z));
            }
        }

        // 网格：五列，格子 226×96，行距 106，首行顶在 -178
        const int Cols = 5;
        const float CellW = 226f, CellH = 96f, StepX = 240f, StepY = 106f, Top = -178f;

        static Vector2 SlotPos(int slot) => new Vector2(
            -(Cols - 1) * StepX * 0.5f + (slot % Cols) * StepX,
            Top - (slot / Cols) * StepY);

        /// <summary>分段标题：占满一整行，让"必选"和"可选"两段一眼分得开。</summary>
        void Header(ref int slot, string text)
        {
            if (slot % Cols != 0) slot += Cols - (slot % Cols);   // 标题另起一行
            var t = UiUtil.MakeText(_grid, "Header_" + slot, text, 22,
                TextAnchor.MiddleLeft, new Color(0.85f, 0.88f, 0.95f));
            UiUtil.SetRect(t, new Vector2(0.5f, 1f),
                new Vector2(-10, SlotPos(slot).y + 26f), new Vector2(1160, 30));
            _buttons.Add(t.gameObject);
            slot += Cols;
        }

        void Cell(ref int slot, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var btn = UiUtil.MakeButton(_grid, "", new Vector2(0.5f, 1f),
                SlotPos(slot), new Vector2(CellW, CellH), color, onClick, 17);
            var txt = btn.GetComponentInChildren<Text>();
            txt.text = label;
            txt.alignment = TextAnchor.MiddleLeft;
            var lrt = txt.GetComponent<RectTransform>();
            lrt.offsetMin = new Vector2(14, 4);
            lrt.offsetMax = new Vector2(-8, -4);
            _buttons.Add(btn.gameObject);
            slot++;
        }

        /// <summary>直达生成场景：走 SiteGate 的同一套进场流程（台词接管 + 规则播报）。</summary>
        void EnterSite(string chapterId, string siteName)
        {
            Hide();
            GameAudio.Play(GameAudio.Sfx.Cast, 0.6f);
            var binder = OpenWorld.GoalWorldBinder.Instance;
            if (binder == null) { GameEvents.RaiseSubtitle("世界还没准备好——稍后再试。"); return; }
            binder.EnterOnDemand(chapterId);   // 没建的现场建，建好的直接进
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
