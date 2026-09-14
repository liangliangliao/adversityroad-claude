using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.InternalOS;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 现实回执台：**通关之后再回来，要做的是这件事，而不是把关卡重打一遍。**
    ///
    /// 【它补上的是一条一直断着的路】
    /// PRD 第 8 节把胜利分三层：
    ///   Simulation —— 你在游戏里做到了（按下提交台就拿到了）
    ///   Reality    —— 你在现实里真的做了那件事
    ///   Transfer   —— 换一个情境，你又做了一次
    /// Mastery（M0-M5）靠的是后两层往上走，第一层不管重复多少遍都不动。
    ///
    /// 而 <see cref="InternalLevelRunner.ConfirmRealityVictory"/> 和
    /// <see cref="InternalLevelRunner.ConfirmTransferVictory"/> 这两个方法，
    /// 在这一版之前**一个调用者都没有**——后两层在游戏里根本够不着。
    /// 于是通关卡上写着"现实胜利的条件是……"，玩家读完却没有任何地方可以回来说
    /// "我做到了"。Mastery 因此永远停在通关那一档。
    ///
    /// 所以回访不是重玩：回来是交回执。这也正好回答了玩家那句
    /// "通关之后再回来，不要再重新玩一遍"——不重玩，但这一趟有它自己的事要办。
    ///
    /// 【为什么迁移要选场合，而不是一个按钮按到底】
    /// 迁移胜利的定义是"**另一个**情境"（见 RealityVictorySystem.DistinctTransferContexts：
    /// 同一个 context 反复记不算数）。给一个笼统的"我迁移了"按钮，玩家连按三下
    /// 也只记到一个情境——那不是迁移，是刷数字。所以这里让他挑具体在哪一类场合
    /// 用上了，选过的那一档会标记成已记，逼着下一次换一个真的不同的地方。
    /// </summary>
    public class RealityCheckPanel : MonoBehaviour
    {
        static RealityCheckPanel _open;

        /// <summary>
        /// 迁移场合。
        ///
        /// 这几类取自 PRD 里"同一能力跨情境复用"的那条要求：它们要足够不一样，
        /// 玩家才分得清自己到底是换了场合还是换了个说法。
        /// </summary>
        static readonly string[] Contexts = { "工作/学习", "家人/亲密关系", "朋友/陌生人" };

        GameObject _panel;
        InternalLevelData _lv;
        string _chapterId = "";
        float _restoreTimeScale = 1f;

        public static bool AnyOpen => _open != null;

        public static void Show(InternalLevelData lv)
        {
            if (lv == null || _open != null) return;
            var canvas = UiUtil.MainCanvas();
            if (canvas == null) return;

            var go = new GameObject("RealityCheckPanel");
            _open = go.AddComponent<RealityCheckPanel>();
            _open.Build(canvas.transform, lv);
        }

        void Build(Transform canvas, InternalLevelData lv)
        {
            _lv = lv;
            var ch = InternalChapterCatalog.Chapter(lv.chapterId);
            // 和 RealityVictorySystem.Record 写进证据里的那个值取同一处，
            // 否则读回来永远是空的（Record 用的是 lv.chapterId）
            _chapterId = lv.chapterId;

            var frame = UiUtil.MakePanel(canvas, "RealityCheckFrame", new Vector2(1180, 800),
                new Color(0.07f, 0.09f, 0.12f, 0.98f));

            var title = UiUtil.MakeText(frame.transform, "Title",
                "回执台 · " + lv.levelId + "《" + lv.name + "》", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.45f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(1080, 54));

            _body = UiUtil.MakeText(frame.transform, "Body", "", 24,
                TextAnchor.UpperLeft, new Color(0.88f, 0.9f, 0.94f));
            UiUtil.SetRect(_body, new Vector2(0.5f, 1f), new Vector2(0, -352), new Vector2(1040, 500));
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Truncate;

            // ① 现实回执：一次就够，记过之后这枚按钮变成"已记下"
            _realityBtn = UiUtil.MakeButton(frame.transform, "", new Vector2(0.5f, 0f),
                new Vector2(0, 214), new Vector2(760, 64),
                new Color(0.20f, 0.42f, 0.30f, 0.98f), OnReality, 24);

            // ② 迁移回执：按场合分三枚，选过的那一档标记成已记
            for (int i = 0; i < Contexts.Length; i++)
            {
                int idx = i;
                _transferBtns[i] = UiUtil.MakeButton(frame.transform, "", new Vector2(0.5f, 0f),
                    new Vector2(-260 + i * 260, 134), new Vector2(248, 60),
                    new Color(0.22f, 0.30f, 0.46f, 0.98f), () => OnTransfer(idx), 21);
            }

            UiUtil.MakeButton(frame.transform, "还没有 · 先走", new Vector2(0.5f, 0f),
                new Vector2(0, 52), new Vector2(360, 64),
                new Color(0.26f, 0.26f, 0.30f, 0.98f), Close, 24);

            _panel = frame;
            _panel.transform.SetAsLastSibling();
            _restoreTimeScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            Refresh();
        }

        Text _body;
        Text _realityLabel;
        // UiUtil.MakeButton 返回的是 Button，不是 GameObject——
        // 写成 GameObject 是 CS0029，而本机没有 Unity DLL，csbuild 看不出来。
        Button _realityBtn;
        readonly Button[] _transferBtns = new Button[3];
        readonly Text[] _transferLabels = new Text[3];

        /// <summary>把当前的三层进度重画一遍。每按一次回执都要看得见变化。</summary>
        void Refresh()
        {
            // 现实胜利按**关**问：每关的 realityVictory 条件各写各的。
            // 按章问会让这一章第二关起永远显示"已记下"（见 HasLayer 的说明）。
            bool hasReality = RealityVictorySystem.HasLayer(_lv.levelId, VictoryLayer.Reality);
            int contexts = RealityVictorySystem.DistinctTransferContexts(_chapterId);
            int mastery = RealityVictorySystem.Mastery(_chapterId);

            var sb = new StringBuilder();
            sb.Append("这一关在游戏里的账已经结了。游戏里的胜利只是排练——\n")
              .Append("真正要记的是后面两层，它们只有你自己知道，所以得你自己回来说。\n\n");

            sb.Append(hasReality ? "✔ " : "○ ").Append("现实胜利　")
              .Append(hasReality ? "已记下" : "还没有").Append('\n')
              .Append("　  条件：").Append(string.IsNullOrEmpty(_lv.realityVictory)
                  ? _lv.Objective : _lv.realityVictory).Append("\n\n");

            sb.Append(contexts > 0 ? "✔ " : "○ ").Append("迁移胜利（按整章算）　已记 ")
              .Append(contexts).Append(" 个不同场合\n")
              .Append("　  同一个本事，换一件事、换一个人、换一个场合，你又用上了一次。\n")
              .Append("　  同一个场合记多少遍都只算一个——那不是迁移。\n\n");

            sb.Append("当前 Mastery：M").Append(mastery).Append('\n')
              .Append("　  M0-M5 靠上面两层往上走。在游戏里把这一关再打十遍，它一格都不动。\n")
              .Append("　  M2 要一次现实回执；M4 要两个不同场合；M5 要三个场合 + 全章三次现实回执。");

            if (_body != null) _body.text = sb.ToString();

            SetLabel(ref _realityLabel, _realityBtn,
                hasReality ? "✔ 现实里我已经做到了（已记下）" : "① 我在现实里做到了");
            for (int i = 0; i < Contexts.Length; i++)
                SetLabel(ref _transferLabels[i], _transferBtns[i],
                    (HasContext(Contexts[i]) ? "✔ " : "② ") + Contexts[i]);
        }

        static void SetLabel(ref Text cache, Button btn, string text)
        {
            if (cache == null && btn != null) cache = btn.GetComponentInChildren<Text>();
            if (cache != null) cache.text = text;
        }

        bool HasContext(string context)
        {
            var list = RealityVictorySystem.EvidenceOf(_chapterId, VictoryLayer.Transfer);
            for (int i = 0; i < list.Count; i++)
                if (list[i].context == context) return true;
            return false;
        }

        void OnReality()
        {
            if (RealityVictorySystem.HasLayer(_lv.levelId, VictoryLayer.Reality))
            {
                GameEvents.RaiseSubtitle("这一关的现实回执记过了。同一件事记两遍不会更真。");
                return;
            }
            InternalLevelRunner.ConfirmRealityVictory(_lv.levelId,
                string.IsNullOrEmpty(_lv.realityVictory) ? _lv.Objective : _lv.realityVictory,
                "reality");
            // Record 内部已经 Save + ReevaluateMastery，这里不必再算一遍
            GameAudio.Play(GameAudio.Sfx.Cast, 0.5f);
            GameEvents.RaiseSubtitle("现实胜利记下了 —— 这一次不是在游戏里做到的。");
            Refresh();
        }

        void OnTransfer(int index)
        {
            if (index < 0 || index >= Contexts.Length) return;
            string context = Contexts[index];
            if (HasContext(context))
            {
                GameEvents.RaiseSubtitle("「" + context +
                    "」这一档记过了。迁移要的是**另一个**场合，同一个记多少遍都只算一个。");
                return;
            }
            InternalLevelRunner.ConfirmTransferVictory(_lv.levelId,
                string.IsNullOrEmpty(_lv.realityVictory) ? _lv.Objective : _lv.realityVictory,
                context);
            GameAudio.Play(GameAudio.Sfx.Cast, 0.5f);
            GameEvents.RaiseSubtitle("迁移胜利记下了（" + context +
                "）—— 同一个本事，换了个地方还用得上，它才算你的。");
            Refresh();
        }

        void Close()
        {
            Time.timeScale = _restoreTimeScale;
            RealityVictorySystem.Flush();     // 回执要落盘，不能只活在这一局里
            if (_panel != null) Destroy(_panel);
            if (_open == this) _open = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_open == this)
            {
                _open = null;
                if (Mathf.Approximately(Time.timeScale, 0f)) Time.timeScale = _restoreTimeScale;
            }
        }
    }
}
