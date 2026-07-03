using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// AI 调用日志面板：查看云端 LLM 请求/响应/耗时/错误，方便调试
    /// apiKey、模型名和网络问题。面板打开时实时刷新。
    /// </summary>
    public class AiLogPanel : MonoBehaviour
    {
        GameObject _panel;
        Text _content;

        public static AiLogPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<AiLogPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "AiLogPanel", new Vector2(1400, 900),
                new Color(0.06f, 0.07f, 0.1f, 0.97f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "AI 调用日志", 36,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(800, 50));

            var bg = new GameObject("LogBg", typeof(Image));
            bg.transform.SetParent(_panel.transform, false);
            UiUtil.SetRect(bg.GetComponent<Image>(), new Vector2(0.5f, 0.5f), new Vector2(0, 20),
                new Vector2(1320, 660));
            bg.GetComponent<Image>().color = new Color(0, 0, 0, 0.5f);

            _content = UiUtil.MakeText(bg.transform, "Content", "", 22,
                TextAnchor.UpperLeft, new Color(0.85f, 0.95f, 0.85f));
            var crt = _content.GetComponent<RectTransform>();
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(16, 12);
            crt.offsetMax = new Vector2(-16, -12);

            UiUtil.MakeButton(_panel.transform, "清空", new Vector2(0.5f, 0f), new Vector2(-130, 60),
                new Vector2(220, 72), new Color(0.5f, 0.3f, 0.25f, 0.95f),
                () => { CloudDialogueService.ClearLogs(); Refresh(); }, 28);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(130, 60),
                new Vector2(220, 72), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 28);

            _panel.SetActive(false);
        }

        void OnEnable() => CloudDialogueService.LogChanged += Refresh;
        void OnDisable() => CloudDialogueService.LogChanged -= Refresh;

        void Refresh()
        {
            if (_content == null || !_panel.activeSelf) return;
            var cfg = AIPromptConfig.Load();
            var sb = new StringBuilder();
            sb.Append("状态：云端生成 ").Append(cfg.useCloud ? "开" : "关")
              .Append(" | 提供商 ").Append(cfg.provider)
              .Append(" | 模型 ").Append(string.IsNullOrEmpty(cfg.model) ? "(默认)" : cfg.model)
              .Append(" | Key ").Append(string.IsNullOrEmpty(cfg.apiKey) ? "未配置" : "已配置")
              .Append('\n').Append("--------------------------------\n");
            var logs = CloudDialogueService.Logs;
            if (logs.Count == 0) sb.Append("（暂无调用记录：进入区域或保存配置后会触发预取）");
            int shown = 0;
            for (int i = logs.Count - 1; i >= 0 && shown < 18; i--, shown++)
                sb.Append(logs[i]).Append('\n');
            _content.text = sb.ToString();
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Refresh();
            Time.timeScale = 0f;
        }

        void Hide()
        {
            _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
