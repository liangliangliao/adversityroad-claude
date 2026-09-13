using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Goals;
using AdversityRoad.InternalOS;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 第 9-26 章 · 内部障碍线的关卡直通面板（90 关）。
    ///
    /// 【为什么要单开一个面板】
    /// 关卡选择面板是 5 列 × 八行就到底的定尺版面，本身已经排满；
    /// 90 关再加 18 个章节标题塞进去，只会把现有格子挤出屏幕。
    /// 所以这里自己开一张，两级：先选章（18），再选关（5）。
    ///
    /// 【它是测试入口，不是主线入口】
    /// 这 90 关正式的出场方式是 Goal OS 按目标里的障碍轴插进旅程
    /// （<see cref="GoalChapterGenerator.EnsureInternalChapters"/>）——
    /// 顺序不由章节号决定，也不该由这张表决定。这张表只解决一件事：
    /// 想看某一关长什么样时，不必先去凑出一个正好卡在那条障碍上的目标。
    /// </summary>
    public class InternalLevelPanel : MonoBehaviour
    {
        GameObject _panel;
        Text _headerText;
        Transform _grid;
        RectTransform _contentRt;
        readonly List<GameObject> _cells = new List<GameObject>();

        /// <summary>0 = 还在选章；否则是正在看第几章。</summary>
        int _openChapter;

        public static InternalLevelPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<InternalLevelPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            // 和关卡选择面板同一套结构：frame 固定、viewport 裁剪、content 按行数长。
            // 18 个章节格勉强一屏，展开某一章之后还有 Boss 摘要和返回格——
            // 定尺版面迟早又是"下半截在屏幕外"。
            var frame = UiUtil.MakePanel(canvas, "InternalLevelFrame", new Vector2(1300, 900),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(frame.transform, "Title",
                "第 9-26 章 · 内部障碍线", 34,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -38), new Vector2(800, 48));

            _headerText = UiUtil.MakeText(frame.transform, "Header", "", 21,
                TextAnchor.MiddleCenter, new Color(0.82f, 0.86f, 0.92f));
            UiUtil.SetRect(_headerText, new Vector2(0.5f, 1f), new Vector2(0, -80), new Vector2(1180, 32));

            var hint = UiUtil.MakeText(frame.transform, "ScrollHint", "↕ 上下拖动", 19,
                TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.5f));
            UiUtil.SetRect(hint, new Vector2(0f, 1f), new Vector2(150, -38), new Vector2(300, 28));

            var viewGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewGo.transform.SetParent(frame.transform, false);
            var viewRt = viewGo.GetComponent<RectTransform>();
            viewRt.anchorMin = Vector2.zero;
            viewRt.anchorMax = Vector2.one;
            viewRt.offsetMin = new Vector2(8f, 100f);
            viewRt.offsetMax = new Vector2(-8f, -104f);

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewGo.transform, false);
            _contentRt = content.GetComponent<RectTransform>();
            _contentRt.anchorMin = new Vector2(0.5f, 1f);
            _contentRt.anchorMax = new Vector2(0.5f, 1f);
            _contentRt.pivot = new Vector2(0.5f, 1f);
            _contentRt.anchoredPosition = Vector2.zero;
            _contentRt.sizeDelta = new Vector2(1280, 900);

            var scroll = frame.AddComponent<ScrollRect>();
            scroll.content = _contentRt;
            scroll.viewport = viewRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            UiUtil.MakeButton(frame.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 46), new Vector2(260, 64),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 25);

            _panel = frame;
            _grid = content.transform;
            _panel.SetActive(false);
        }

        // ==================== 版面 ====================

        const int Cols = 5;
        const float CellW = 226f, CellH = 116f, StepX = 240f, StepY = 126f, Top = -20f;

        static Vector2 SlotPos(int slot) => new Vector2(
            -(Cols - 1) * StepX * 0.5f + (slot % Cols) * StepX,
            Top - (slot / Cols) * StepY);

        void Cell(ref int slot, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var btn = UiUtil.MakeButton(_grid, "", new Vector2(0.5f, 1f),
                SlotPos(slot), new Vector2(CellW, CellH), color, onClick, 16);
            var txt = btn.GetComponentInChildren<Text>();
            txt.text = label;
            txt.alignment = TextAnchor.MiddleLeft;
            var rt = txt.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(12, 4);
            rt.offsetMax = new Vector2(-8, -4);
            _cells.Add(btn.gameObject);
            slot++;
        }

        void Refresh()
        {
            foreach (var c in _cells)
                if (c != null) { c.SetActive(false); Destroy(c); }
            _cells.Clear();

            if (_openChapter == 0) ListChapters();
            else ListLevels(_openChapter);

            FitContent(_cells.Count);
        }

        /// <summary>内容高度按真实格子数算，并把滚动位置拉回顶部。</summary>
        void FitContent(int slots)
        {
            if (_contentRt == null) return;
            int rows = Mathf.CeilToInt(slots / (float)Cols);
            float h = Mathf.Max(600f, -Top + rows * StepY + 60f);
            _contentRt.sizeDelta = new Vector2(1280, h);
            _contentRt.anchoredPosition = Vector2.zero;
        }

        void ListChapters()
        {
            var chapters = InternalChapterCatalog.Chapters;
            _headerText.text = "18 章 × 5 关（3 普通 + 1 精英 + 1 Boss）—— 敌人全部是内部敌人。点章节展开。";

            int slot = 0;
            for (int i = 0; i < chapters.Count; i++)
            {
                var ch = chapters[i];
                int no = ch.chapterNo;
                string boss = ch.boss != null ? ch.boss.name : "";
                int mastery = RealityVictorySystem.Mastery(ch.chapterId);
                Cell(ref slot,
                    "第" + no + "章 " + ch.title + "\n" +
                    ch.levels.Count + " 关 · Boss：" + boss + "\n" +
                    "Mastery M" + mastery,
                    new Color(0.20f, 0.24f, 0.32f, 0.96f),
                    () => { _openChapter = no; Refresh(); });
            }
        }

        void ListLevels(int chapterNo)
        {
            var ch = InternalChapterCatalog.ChapterByNo(chapterNo);
            if (ch == null) { _openChapter = 0; Refresh(); return; }

            _headerText.text = "第" + chapterNo + "章 " + ch.title + " —— " + ch.core;

            int slot = 0;
            for (int i = 0; i < ch.levels.Count; i++)
            {
                var lv = ch.levels[i];
                string id = lv.levelId;
                string tier = lv.isBossLevel ? "Boss 关" : (lv.tier == "Elite" ? "精英关" : "普通关");
                Cell(ref slot,
                    lv.levelId + "《" + lv.name + "》\n" +
                    tier + " · " + lv.baseKitLabel + "\n" +
                    "通关：" + Clip(lv.realityVictory, 22),
                    lv.isBossLevel ? new Color(0.5f, 0.34f, 0.18f, 0.96f)
                                   : new Color(0.22f, 0.30f, 0.26f, 0.96f),
                    () => Enter(id));
            }

            // 这一章的 Boss 摘要单独占一格：真命门与失效条件是这一章的通关定义，
            // 不写出来玩家只会以为"打不死"是做坏了。
            if (ch.boss != null)
            {
                var b = ch.boss;
                Cell(ref slot,
                    "Boss " + b.bossId + " " + b.name + "\n" +
                    "命门：" + Clip(b.coreMechanism, 18) + "\n" +
                    "失效：" + Clip(b.executionGate, 18),
                    new Color(0.30f, 0.20f, 0.24f, 0.96f), null);
            }

            Cell(ref slot, "← 返回章节列表", new Color(0.26f, 0.26f, 0.32f, 0.96f),
                () => { _openChapter = 0; Refresh(); });
        }

        static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        // ==================== 进关 ====================

        /// <summary>
        /// 进入某一关：登记蓝图 → 现场搭建 → 进场。
        ///
        /// 规则驱动不在这里拉起——它挂在 <c>SiteGate.EnterChapter</c> 上，
        /// 这样从旅程走进来和从这张表走进来是同一条路，行为不会有两套。
        /// </summary>
        void Enter(string levelId)
        {
            var goal = GoalOS.Active;
            if (goal == null)
            {
                // 这 90 关是挂在目标上的，没有目标就没有"它在挡什么"。
                GameEvents.RaiseSubtitle("先在目标面板里钉一个目标——这些关卡是按它插进旅程的。");
                return;
            }

            var bp = GoalChapterGenerator.EnsureInternalLevel(goal, levelId);
            if (bp == null) { GameEvents.RaiseSubtitle("这一关的蓝图没能登记。"); return; }

            Hide();
            GameAudio.Play(GameAudio.Sfx.Cast, 0.6f);

            var binder = OpenWorld.GoalWorldBinder.Instance;
            if (binder == null) { GameEvents.RaiseSubtitle("世界还没准备好——稍后再试。"); return; }
            binder.EnterOnDemand(bp.chapterId);
        }

        // ==================== 开关 ====================

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            Show();
        }

        public void Show()
        {
            _openChapter = 0;
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
