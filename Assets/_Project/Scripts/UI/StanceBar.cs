using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Combat;

namespace AdversityRoad.UI
{
    /// <summary>
    /// HUD 姿态条：属性条下方一排五枚姿态按钮（起步/边界/定心/事实/意志）。
    /// 点选切换姿态（移动端可点、PC 可 Tab/F 循环），高亮当前姿态并在右侧显示心法。
    /// 订阅 GameEvents.OnStanceChanged，与 StanceSystem 解耦。
    /// </summary>
    public class StanceBar : MonoBehaviour
    {
        static readonly Color Off = new Color(0.18f, 0.2f, 0.28f, 0.9f);
        static readonly Color On = new Color(0.85f, 0.6f, 0.25f, 0.98f);

        // 与 GameBootstrap.HudLayout 的 StanceY / MantraY 对齐（状态区背板下方两行）
        const float StanceY = -290f;
        const float MantraY = -338f;

        readonly Button[] _btns = new Button[StanceSystem.Defs.Length];
        Text _mantra;

        public static StanceBar Create(Transform canvas, StanceSystem stance)
        {
            var go = new GameObject("StanceBar", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var bar = go.AddComponent<StanceBar>();
            bar.Build(canvas, stance);
            return bar;
        }

        void Build(Transform canvas, StanceSystem stance)
        {
            // 五枚姿态按钮排一行；心法【另起一行】。
            // 此前心法文字与按钮同在 y=-366：文字框 x 180~940、按钮 x 25~499，
            // 两者重叠 300 多像素——截图里"起步"按钮上糊着"边界心法：…"就是这个。
            var defs = StanceSystem.Defs;
            for (int i = 0; i < defs.Length; i++)
            {
                int idx = i;
                var btn = UiUtil.MakeButton(canvas, defs[i].name, new Vector2(0, 1),
                    new Vector2(62 + i * 86, StanceY), new Vector2(80, 44), Off,
                    () => { if (stance != null) stance.SetStance(idx); }, 22);
                _btns[i] = btn;
            }

            _mantra = UiUtil.MakeText(canvas, "StanceMantra", "", 20,
                TextAnchor.MiddleLeft, new Color(1f, 0.88f, 0.6f));
            var mrt = UiUtil.SetRect(_mantra, new Vector2(0, 1),
                new Vector2(22, MantraY), new Vector2(720, 32));
            mrt.pivot = new Vector2(0, 0.5f);
            var outline = _mantra.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            GameEvents.OnStanceChanged += OnStance;
            // 初始高亮（StanceSystem.Start 可能已在本帧之前广播过，这里兜底刷新一次）
            if (stance != null) OnStance(stance.Index, stance.CurrentDef.name, stance.CurrentDef.mantra);
        }

        void OnDestroy() => GameEvents.OnStanceChanged -= OnStance;

        void OnStance(int index, string name, string mantra)
        {
            for (int i = 0; i < _btns.Length; i++)
                if (_btns[i] != null)
                    _btns[i].GetComponent<Image>().color = i == index ? On : Off;
            if (_mantra != null) _mantra.text = "心法：" + mantra;
        }
    }
}
