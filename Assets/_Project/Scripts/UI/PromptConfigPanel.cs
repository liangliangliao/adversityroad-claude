using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.World;

namespace AdversityRoad.UI
{
    /// <summary>
    /// AI 台词提示词面板：全局提示词 + 当前场景提示词。
    /// 短句（用 ；、，、换行分隔）会混入敌人恶意台词池；
    /// 未来接入云端 LLM 后作为生成上下文。
    /// </summary>
    public class PromptConfigPanel : MonoBehaviour
    {
        GameObject _panel;
        InputField _globalInput;
        InputField _sceneInput;
        Text _sceneLabel;

        public static PromptConfigPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<PromptConfigPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "PromptPanel", new Vector2(1000, 720),
                new Color(0.08f, 0.08f, 0.12f, 0.96f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "AI 台词提示词", 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(800, 56));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "敌人会用这些提示词组合恶意低语攻击你（短句用；，或换行分隔）", 22,
                TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.55f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -90), new Vector2(940, 36));

            var gLabel = UiUtil.MakeText(_panel.transform, "GLabel", "全局提示词", 26,
                TextAnchor.MiddleLeft, Color.white);
            UiUtil.SetRect(gLabel, new Vector2(0.5f, 1f), new Vector2(-330, -140), new Vector2(300, 40));
            _globalInput = UiUtil.MakeInput(_panel.transform, "例：你总是三分钟热度；别人早就做到了",
                new Vector2(0.5f, 1f), new Vector2(0, -250), new Vector2(920, 170), true);

            _sceneLabel = UiUtil.MakeText(_panel.transform, "SLabel", "当前场景提示词", 26,
                TextAnchor.MiddleLeft, Color.white);
            UiUtil.SetRect(_sceneLabel, new Vector2(0.5f, 1f), new Vector2(-330, -370), new Vector2(400, 40));
            _sceneInput = UiUtil.MakeInput(_panel.transform, "只在当前区域生效的台词提示词",
                new Vector2(0.5f, 1f), new Vector2(0, -480), new Vector2(920, 170), true);

            UiUtil.MakeButton(_panel.transform, "保存", new Vector2(0.5f, 0f), new Vector2(-130, 66),
                new Vector2(230, 80), new Color(0.2f, 0.55f, 0.3f, 0.95f), Save, 30);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(130, 66),
                new Vector2(230, 80), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 30);

            _panel.SetActive(false);
        }

        void Save()
        {
            var cfg = AIPromptConfig.Load();
            cfg.globalPrompt = _globalInput.text;
            cfg.SetScenePrompt(ZoneBuilder.CurrentZoneId, _sceneInput.text);
            cfg.Save();
            GameEvents.RaiseSubtitle("AI 台词提示词已保存，敌人的低语将随之改变……");
            Hide();
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            var cfg = AIPromptConfig.Load();
            _globalInput.text = cfg.globalPrompt;
            _sceneInput.text = cfg.GetScenePrompt(ZoneBuilder.CurrentZoneId);
            _sceneLabel.text = "当前场景提示词（" + CurrentZoneName() + "）";
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        static string CurrentZoneName()
        {
            switch (ZoneBuilder.CurrentZoneId)
            {
                case "home": return "独居小屋";
                case "dojo": return "训练武馆";
                case "street": return "噪声街区";
                case "plaza": return "城市广场";
                default: return ZoneBuilder.CurrentZoneId;
            }
        }

        void Hide()
        {
            _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
