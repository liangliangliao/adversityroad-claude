using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.InternalOS;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 走到一件带字的东西旁边，它自己说清楚三件事。
    ///
    /// 【玩家要的】
    /// "玩家站立在所有带有文字牌子旁边自动或者点击牌子显示解释，这是什么？
    /// 为什么出现在这里？如何用它？"
    ///
    /// 【为什么是一块常驻的卡，不是又一条字幕】
    /// 这个项目在字幕上已经栽过一次：几件东西的解释范围互相重叠，同一帧里
    /// 抢着往那一行里写，后写的盖掉先写的，玩家看到的就是"站在旁边没有任何提示"。
    /// 根因不是范围大小，是**一行字幕只装得下一件东西**。
    ///
    /// 所以这里另开一个通道，并且从结构上只允许一个主人：
    /// 所有可查看的东西注册到这里，每帧由这张卡**自己**挑出最近的那一件来显示。
    /// 不是"谁先喊谁赢"，是"只有一个人有资格说话"——
    /// 和 ZoneRoute 用状态机替掉五个触发器是同一条教训。
    ///
    /// 它不暂停游戏、没有按钮、走开就收。按【用】/ R 可以把它收起来
    /// （纯说明牌没有别的动作，这一下就是玩家说的"点击牌子"那一下）。
    ///
    /// 【版面】
    /// 左侧 620×250：上边界让开心法行（y=-338），下边界让开虚拟摇杆
    /// （锚在左下 (260,260)、半径约 180，在 1080 高的画布上占到 y=-640 以上）。
    /// 中间这一条是屏幕左侧唯一空着的地方。每段字数上限见 SignCodex.MaxPart。
    /// </summary>
    public class ExamineCard : MonoBehaviour
    {
        const float Top = -370f;      // 心法行在 -338，往下让开一行
        const float W = 620f, H = 250f;

        static ExamineCard _inst;
        static readonly List<Examinable> _all = new List<Examinable>();

        GameObject _panel;
        Text _title, _body;
        Examinable _shown;
        Examinable _muted;            // 被玩家按【用】收起来的那一件

        public static void Register(Examinable e)
        {
            if (e != null && !_all.Contains(e)) _all.Add(e);
        }

        public static void Unregister(Examinable e)
        {
            _all.Remove(e);
            if (_inst != null && _inst._muted == e) _inst._muted = null;
        }

        /// <summary>此刻正显示着（或正被收起着）的就是这一件吗。</summary>
        public static bool IsShowing(Examinable e)
            => _inst != null && _inst._shown == e;

        /// <summary>把当前这一件收起来／再打开（玩家按【用】时调）。</summary>
        public static void Toggle(Examinable e)
        {
            if (_inst == null || e == null) return;
            _inst._muted = _inst._muted == e ? null : e;
        }

        public static void Ensure()
        {
            // 也要认"面板没了但组件还在"：换场景时画布连同 _panel 一起销毁，
            // 而这个承载物若挂在画布之外就会活下来，于是卡片永远不再出现。
            // 所以挂到画布下面（一起生死），并且两个都查。
            if (_inst != null && _inst._panel != null) return;
            var canvas = UiUtil.MainCanvas();
            if (canvas == null) return;
            var go = new GameObject("ExamineCard");
            go.transform.SetParent(canvas.transform, false);
            _inst = go.AddComponent<ExamineCard>();
            _inst.Build(canvas.transform);
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "ExamineFrame", new Vector2(W, H),
                new Color(0.06f, 0.07f, 0.10f, 0.92f));
            UiUtil.SetRect(_panel.GetComponent<Image>(), new Vector2(0f, 1f),
                new Vector2(22f + W * 0.5f, Top - H * 0.5f), new Vector2(W, H));

            _title = UiUtil.MakeText(_panel.transform, "Title", "", 22,
                TextAnchor.UpperLeft, new Color(0.96f, 0.86f, 0.46f));
            UiUtil.SetRect(_title, new Vector2(0.5f, 1f), new Vector2(0, -20), new Vector2(W - 28, 28));

            _body = UiUtil.MakeText(_panel.transform, "Body", "", 18,
                TextAnchor.UpperLeft, new Color(0.88f, 0.90f, 0.94f));
            UiUtil.SetRect(_body, new Vector2(0.5f, 1f), new Vector2(0, -132), new Vector2(W - 28, 196));
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Truncate;

            _panel.SetActive(false);
        }

        void Update()
        {
            if (_panel == null) return;

            // 最近的那一件说话。挑出唯一的主人，而不是让大家抢同一个出口。
            Examinable best = null;
            float bestD = float.MaxValue;
            var player = ActorRegistry.Player;
            if (player != null)
            {
                Vector3 p = player.transform.position;
                for (int i = _all.Count - 1; i >= 0; i--)
                {
                    var e = _all[i];
                    if (e == null) { _all.RemoveAt(i); continue; }
                    float d = Vector3.Distance(e.transform.position, p);
                    if (d > e.range || d >= bestD) continue;
                    bestD = d; best = e;
                }
            }

            if (best != _shown)
            {
                _shown = best;
                _muted = null;            // 换了一件东西，收起状态跟着清掉
                Render(best);
            }
            bool show = best != null && _muted != best;
            if (_panel.activeSelf != show) _panel.SetActive(show);
        }

        void Render(Examinable e)
        {
            if (e == null) return;
            var c = e.Entry();
            if (_title != null) _title.text = c.title;
            if (_body == null) return;

            var sb = new StringBuilder();
            sb.Append("这是什么　").Append(c.what).Append('\n');
            sb.Append("为什么在这儿　").Append(c.why).Append('\n');
            sb.Append("怎么用　").Append(c.how);
            if (!string.IsNullOrEmpty(c.core))
                sb.Append("\n本章　").Append(c.core);
            _body.text = sb.ToString();
        }
    }

    /// <summary>
    /// "这件东西能被看懂"。挂在牌子、关键物、修改项、地面分段上。
    ///
    /// 内容不在这里写死，而是每次显示时现取（<see cref="Entry"/>）——
    /// 因为同一件东西在"第一趟"和"回访"要说的不是同一句话。
    /// </summary>
    public class Examinable : MonoBehaviour
    {
        public float range = 4.5f;

        /// <summary>
        /// 这件东西自己要用【用】键吗。
        ///
        /// 玩家要的是"自动**或者点击**显示解释"。自动那一半由距离负责；
        /// 点击这一半统一走【用】——但关键物和修改卡的【用】已经是"做那件事"了，
        /// 抢过来会把玩法弄坏。所以它们把这一位设成 true 自己处理，
        /// 剩下的纯说明牌（入口/出口/路线牌/地面分段）由这里接管：按一下收起，
        /// 再按一下打开。
        /// </summary>
        public bool consumesUse;

        System.Func<CodexEntry> _resolve;

        public static Examinable Attach(GameObject host, System.Func<CodexEntry> resolve,
            float range = 4.5f, bool consumesUse = false)
        {
            if (host == null || resolve == null) return null;
            var e = host.AddComponent<Examinable>();
            e._resolve = resolve;
            e.range = range;
            e.consumesUse = consumesUse;
            return e;
        }

        void Update()
        {
            if (consumesUse) return;            // 这件东西自己用【用】做别的事
            if (!ExamineCard.IsShowing(this)) return;
            if (Input.GetKeyDown(KeyCode.R) || Mobile.MobileInput.GetDown("Interact"))
                ExamineCard.Toggle(this);
        }

        public CodexEntry Entry() => _resolve != null ? _resolve() : default(CodexEntry);

        void OnEnable()
        {
            ExamineCard.Ensure();
            ExamineCard.Register(this);
        }

        void OnDisable() => ExamineCard.Unregister(this);
    }
}
