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
        const float FoldedH = 46f;    // 折起来只留标题那一行

        static ExamineCard _inst;
        static readonly List<Examinable> _all = new List<Examinable>();

        GameObject _panel;
        RectTransform _frame;
        Text _title, _body, _hint;
        Examinable _shown;
        /// <summary>玩家点了卡片把它折起来了（只留标题那一行）。换一件东西时自动展开。</summary>
        bool _folded;

        public static void Register(Examinable e)
        {
            if (e != null && !_all.Contains(e)) _all.Add(e);
        }

        public static void Unregister(Examinable e)
        {
            _all.Remove(e);
        }

        /// <summary>此刻正显示着的就是这一件吗。</summary>
        public static bool IsShowing(Examinable e)
            => _inst != null && _inst._shown == e;

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
            _frame = UiUtil.SetRect(_panel.GetComponent<Image>(), new Vector2(0f, 1f),
                new Vector2(22f + W * 0.5f, Top - H * 0.5f), new Vector2(W, H));

            // 【"点击"那一半放在这儿，不放在【用】键上】
            // 玩家要的是"自动**或者点击**显示解释"。上一版把点击接到了【用】键上，
            // 结果只读的牌子把按键从工作台那儿吃掉，直接卡关。
            // 现在点的是卡片自己：一次折起（只留标题）、再一次展开。
            // 纯 UI 事件，和世界里的任何输入都不共享通道，不可能再抢。
            var btn = _panel.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { _folded = !_folded; Apply(); });

            _title = UiUtil.MakeText(_panel.transform, "Title", "", 22,
                TextAnchor.UpperLeft, new Color(0.96f, 0.86f, 0.46f));
            UiUtil.SetRect(_title, new Vector2(0.5f, 1f), new Vector2(0, -20), new Vector2(W - 28, 28));

            _hint = UiUtil.MakeText(_panel.transform, "Hint", "（点一下收起）", 15,
                TextAnchor.UpperRight, new Color(0.62f, 0.66f, 0.72f));
            UiUtil.SetRect(_hint, new Vector2(0.5f, 1f), new Vector2(0, -20), new Vector2(W - 28, 24));

            _body = UiUtil.MakeText(_panel.transform, "Body", "", 18,
                TextAnchor.UpperLeft, new Color(0.88f, 0.90f, 0.94f));
            UiUtil.SetRect(_body, new Vector2(0.5f, 1f), new Vector2(0, -132), new Vector2(W - 28, 196));
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Truncate;

            _panel.SetActive(false);
        }

        /// <summary>按当前的折叠状态调整高度与正文可见性。</summary>
        void Apply()
        {
            if (_frame != null)
                _frame.sizeDelta = new Vector2(W, _folded ? FoldedH : H);
            if (_frame != null)
                _frame.anchoredPosition = new Vector2(22f + W * 0.5f,
                    Top - (_folded ? FoldedH : H) * 0.5f);
            if (_body != null) _body.gameObject.SetActive(!_folded);
            if (_hint != null) _hint.text = _folded ? "（点一下展开）" : "（点一下收起）";
        }

        void Update()
        {
            if (_panel == null) return;

            // 挑出唯一的主人，而不是让大家抢同一个出口。
            //
            // 【能操作的优先于只能读的】距离不是唯一标准：地面分段线和关键物常常
            // 落在同一个位置上（起手位那条线与工作台都在 t=0.32），只比距离的话
            // 玩家站在工作台跟前，卡上写的却是那条线——他要的是这件东西怎么用。
            Examinable best = null;
            int bestRank = int.MaxValue;
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
                    if (d > e.range) continue;
                    int rank = e.interactive ? 0 : 1;
                    if (rank > bestRank || (rank == bestRank && d >= bestD)) continue;
                    bestRank = rank; bestD = d; best = e;
                }
            }

            if (best != _shown)
            {
                _shown = best;
                _folded = false;          // 换了一件东西，折叠状态跟着复位
                Render(best);
                Apply();
            }
            bool show = best != null;
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
        /// 这件东西是**能操作的**（按【用】会发生事），还是只能读的牌子。
        ///
        /// 【这一位原来叫 consumesUse，而且干了一件闯祸的事】
        /// 上一版让只读的牌子也去读【用】键，好让玩家"点一下收起卡片"。
        /// 后果是卡关：MobileInput.GetDown 是**消费式**的（读到就把这一次按下删掉），
        /// 谁先读谁独吞。地面那五条分段每条都挂着一个只读牌，而【起手位】那条线
        /// 和工作台在同一个 t（0.32）上——分段的 Update 先跑就把按键吃掉了，
        /// 工作台永远收不到，第一版做不出来，这一关就过不去。
        ///
        /// 教训写在这儿：**动作键属于你要操作的那件东西，只读的牌子一律不许碰。**
        /// 现在这一位不再管输入，只管两件事：
        ///   · 谁该占住说明卡——能操作的优先于只能读的（你需要的是它的用法）；
        ///   · 提醒读代码的人：这件东西自己会处理【用】。
        /// "点击"那一半改由卡片自己承担（点卡片收起/展开），纯 UI，不碰世界输入。
        /// </summary>
        public bool interactive;

        System.Func<CodexEntry> _resolve;

        public static Examinable Attach(GameObject host, System.Func<CodexEntry> resolve,
            float range = 4.5f, bool interactive = false)
        {
            if (host == null || resolve == null) return null;
            var e = host.AddComponent<Examinable>();
            e._resolve = resolve;
            e.range = range;
            e.interactive = interactive;
            return e;
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
