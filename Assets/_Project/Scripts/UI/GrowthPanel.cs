using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 技能树面板（安全屋·成长）：五条路线（边界/专注/行动/公平/自尊）× 每线两级节点。
    /// 用「复盘点」解锁（战后复盘归档获得）——把复盘真正变成成长资源。
    /// </summary>
    public class GrowthPanel : MonoBehaviour
    {
        GameObject _panel;
        Text _pointsText;
        readonly List<(Button btn, Text label, string nodeId)> _nodeButtons =
            new List<(Button, Text, string)>();

        public static GrowthPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<GrowthPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            // 版面高度按【实际行数】给，不再假设"每条路线正好两个节点"：
            // V2.1 给自尊线一口气加了五个节点，写死两列一行会让它们叠在一起。
            _panel = UiUtil.MakePanel(canvas, "GrowthPanel", new Vector2(1240, 1460),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "技 能 树 · 五 条 成 长 路 线", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(900, 54));

            _pointsText = UiUtil.MakeText(_panel.transform, "Points", "", 28,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.95f, 0.8f));
            UiUtil.SetRect(_pointsText, new Vector2(0.5f, 1f), new Vector2(0, -100), new Vector2(900, 40));

            // 每条路线两列排布，节点多于两个就在同一路线内换行——
            // 行数由节点数算出来，路线名只写在该路线的第一行
            const float RowStep = 148f;
            string lastRoute = null;
            int row = -1, colInRoute = 0;
            for (int i = 0; i < GrowthSystem.Nodes.Length; i++)
            {
                var node = GrowthSystem.Nodes[i];
                bool newRoute = node.route != lastRoute;
                if (newRoute) { lastRoute = node.route; row++; colInRoute = 0; }
                else if (colInRoute >= 2) { row++; colInRoute = 0; }
                int col = colInRoute++;

                if (col == 0)
                {
                    var routeText = UiUtil.MakeText(_panel.transform, "Route",
                        newRoute ? node.route + "路线" : "", 26,
                        TextAnchor.MiddleLeft, new Color(0.95f, 0.8f, 0.55f));
                    UiUtil.SetRect(routeText, new Vector2(0.5f, 1f),
                        new Vector2(-540, -170 - row * RowStep), new Vector2(130, 40));
                    routeText.fontStyle = FontStyle.Bold;
                }

                var btn = UiUtil.MakeButton(_panel.transform, "",
                    new Vector2(0.5f, 1f), new Vector2(-190 + col * 500, -170 - row * RowStep),
                    new Vector2(480, 128), new Color(0.2f, 0.22f, 0.3f, 0.96f), null, 22);
                var label = btn.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                var lrt = label.GetComponent<RectTransform>();
                lrt.offsetMin = new Vector2(16, 8);
                lrt.offsetMax = new Vector2(-10, -8);

                string nodeId = node.id;
                btn.onClick.AddListener(() => TryUnlock(nodeId));
                _nodeButtons.Add((btn, label, nodeId));
            }

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 54), new Vector2(280, 74),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        void TryUnlock(string nodeId)
        {
            if (GrowthSystem.IsUnlocked(nodeId)) return;
            // 剧情授予的条目不卖：点它不该悄悄花掉一枚复盘点
            foreach (var n in GrowthSystem.Nodes)
                if (n.id == nodeId && n.grantOnly)
                {
                    GameEvents.RaiseSubtitle("「" + n.name + "」不能用复盘点购买——" +
                        "它是第八章最佳结算的凭证，只能在关卡里拿到。");
                    return;
                }
            if (!GrowthSystem.TryUnlock(nodeId))
            {
                GameEvents.RaiseSubtitle("复盘点不足——战斗后打开「复盘」面板归档，即可获得复盘点。");
                Refresh();
                return;
            }
            // 上限类节点立刻生效
            var pc = AdversityRoad.Core.ActorRegistry.Player;
            if (pc != null) GrowthSystem.ApplyMaxBonuses(pc.Stats);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.8f);
            GameEvents.RaiseSubtitle("技能树节点解锁——成长落到了身上。");
            Refresh();
        }

        void Refresh()
        {
            _pointsText.text = "复盘点：" + GrowthSystem.Points +
                "（战后复盘归档 +1；用于解锁节点，每节点 1 点）";
            foreach (var (btn, label, nodeId) in _nodeButtons)
            {
                GrowthNode def = default;
                foreach (var n in GrowthSystem.Nodes)
                    if (n.id == nodeId) { def = n; break; }
                bool unlocked = GrowthSystem.IsUnlocked(nodeId);
                label.text = (unlocked ? "✓ " : "◇ ") + def.name + "\n" + def.desc;
                btn.GetComponent<Image>().color = unlocked
                    ? new Color(0.22f, 0.42f, 0.3f, 0.96f)
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
