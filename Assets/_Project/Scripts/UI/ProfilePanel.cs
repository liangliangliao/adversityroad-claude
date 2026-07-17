using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Personalization;
using AdversityRoad.Save;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 个性化画像面板（第 5 阶段）：玩家输入自己的经历/困扰文本 →
    /// 去识别化 → 弱点轴分析 → 场景推荐 → 应用画像并本地保存。
    /// 画像决定敌人心理攻击的弱点加成与台词方向。原文分析后即弃，不落盘。
    /// </summary>
    public class ProfilePanel : MonoBehaviour
    {
        GameObject _panel;
        InputField _input;
        Text _result;
        PlayerProfile _pendingProfile;
        readonly List<(Button btn, string phrase, bool[] state)> _quickTags =
            new List<(Button, string, bool[])>();

        static readonly (string label, string phrase)[] QuickTagDefs =
        {
            ("总是拖延", "我总是拖延，事情想做却迟迟不开始，明天再说"),
            ("怕被议论", "害怕被议论被看不起，别人的眼神咳嗽让我分心"),
            ("求职焦虑", "找工作投简历没有回应，面试被拒，很焦虑"),
            ("不敢拒绝", "不敢拒绝别人，被欺负被借钱不还也不吭声"),
            ("自我怀疑", "我常常自我怀疑，觉得自己不行做不到"),
        };

        static readonly List<SceneTemplate> ZoneTemplates = new List<SceneTemplate>
        {
            new SceneTemplate { sceneId = "home", displayName = "独居小屋", primaryAxis = WeaknessAxis.SelfDoubt, themeTag = "自尊", baseIntensity = 0.3f },
            new SceneTemplate { sceneId = "dojo", displayName = "训练武馆", primaryAxis = WeaknessAxis.Procrastination, themeTag = "决断", baseIntensity = 0.45f },
            new SceneTemplate { sceneId = "street", displayName = "噪声街区", primaryAxis = WeaknessAxis.NoiseSensitivity, themeTag = "专注", baseIntensity = 0.6f },
            new SceneTemplate { sceneId = "job", displayName = "求职荒原", primaryAxis = WeaknessAxis.JobAnxiety, themeTag = "求职", baseIntensity = 0.7f },
            new SceneTemplate { sceneId = "plaza", displayName = "城市广场", primaryAxis = WeaknessAxis.Procrastination, themeTag = "意志", baseIntensity = 0.8f },
            new SceneTemplate { sceneId = "court", displayName = "责任转嫁法院", primaryAxis = WeaknessAxis.BoundaryConflict, themeTag = "边界,责任", baseIntensity = 0.7f },
            new SceneTemplate { sceneId = "judgment", displayName = "小题大做审判庭", primaryAxis = WeaknessAxis.FairnessSensitivity, themeTag = "公平,自尊", baseIntensity = 0.65f },
            new SceneTemplate { sceneId = "swamp", displayName = "拖延沼泽", primaryAxis = WeaknessAxis.Procrastination, themeTag = "拖延,行动", baseIntensity = 0.6f },
            new SceneTemplate { sceneId = "echo", displayName = "旧事回声馆", primaryAxis = WeaknessAxis.FailureFear, themeTag = "旧事,失败,整合", baseIntensity = 0.75f },
        };

        public static ProfilePanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<ProfilePanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "ProfilePanel", new Vector2(1240, 940),
                new Color(0.08f, 0.08f, 0.12f, 0.97f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "个 人 画 像", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -42), new Vector2(800, 50));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "写下你的经历或困扰（拖延？被议论？找工作？被欺负？）——\n" +
                "游戏会看见你的弱点，据此塑造敌人的攻击方向。手机号/邮箱等会先脱敏，原文不保存。",
                22, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.6f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -108), new Vector2(1140, 64));

            _input = UiUtil.MakeInput(_panel.transform,
                "例：最近总是拖延，定了目标又不敢开始；在人多的地方容易分心，总觉得别人在议论我……",
                new Vector2(0.5f, 1f), new Vector2(0, -238), new Vector2(1160, 170), true);

            // 快答问卷：点亮符合自己的标签（与文字描述叠加分析）
            for (int i = 0; i < QuickTagDefs.Length; i++)
            {
                var def = QuickTagDefs[i];
                var state = new bool[1];
                var btn = UiUtil.MakeButton(_panel.transform, def.label, new Vector2(0.5f, 1f),
                    new Vector2(-448 + i * 224, -358), new Vector2(210, 58),
                    new Color(0.25f, 0.25f, 0.3f, 0.95f), null, 22);
                var captured = (btn, def.phrase, state);
                btn.onClick.AddListener(() =>
                {
                    state[0] = !state[0];
                    btn.GetComponent<Image>().color = state[0]
                        ? new Color(0.8f, 0.5f, 0.2f, 0.95f)
                        : new Color(0.25f, 0.25f, 0.3f, 0.95f);
                });
                _quickTags.Add(captured);
            }

            var resultBg = new GameObject("ResultBg", typeof(Image));
            resultBg.transform.SetParent(_panel.transform, false);
            UiUtil.SetRect(resultBg.GetComponent<Image>(), new Vector2(0.5f, 1f),
                new Vector2(0, -580), new Vector2(1160, 380));
            resultBg.GetComponent<Image>().color = new Color(0, 0, 0, 0.45f);

            _result = UiUtil.MakeText(resultBg.transform, "Result",
                "点击「分析」查看你的弱点画像与推荐试炼场景。", 24,
                TextAnchor.UpperLeft, new Color(0.85f, 0.95f, 0.85f));
            var rrt = _result.GetComponent<RectTransform>();
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = new Vector2(18, 12);
            rrt.offsetMax = new Vector2(-18, -12);

            UiUtil.MakeButton(_panel.transform, "分析", new Vector2(0.5f, 0f), new Vector2(-330, 76),
                new Vector2(240, 80), new Color(0.25f, 0.4f, 0.65f, 0.95f), Analyze, 30);
            UiUtil.MakeButton(_panel.transform, "应用画像", new Vector2(0.5f, 0f), new Vector2(-40, 76),
                new Vector2(280, 80), new Color(0.2f, 0.55f, 0.3f, 0.95f), Apply, 30);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(260, 76),
                new Vector2(240, 80), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 30);

            _panel.SetActive(false);
        }

        void Analyze()
        {
            string text = _input.text ?? "";
            foreach (var (btn, phrase, state) in _quickTags)
                if (state[0]) text += "。" + phrase;
            if (string.IsNullOrWhiteSpace(text))
            {
                _result.text = "请写点什么，或点亮上面符合你的标签。";
                return;
            }
            var safety = GameManager.Instance != null ? GameManager.Instance.safety : null;
            var ranked = PersonalizationPipeline.Process(text, ZoneTemplates, safety, out _pendingProfile);

            // 弱点排序取前四
            var scores = new List<WeaknessScore>(_pendingProfile.weaknessScores);
            scores.Sort((a, b) => b.score.CompareTo(a.score));

            var sb = new StringBuilder();
            sb.Append("◤ 看见的弱点 ◢\n");
            for (int i = 0; i < Mathf.Min(4, scores.Count); i++)
            {
                var w = scores[i];
                sb.Append("  ").Append(AxisName(w.axis)).Append("  ")
                  .Append(Bar(w.score)).Append("  ")
                  .Append(Mathf.RoundToInt(w.score * 100)).Append("%\n");
            }
            sb.Append('\n').Append("◤ 推荐试炼 ◢\n");
            for (int i = 0; i < Mathf.Min(2, ranked.Count); i++)
                sb.Append("  ").Append(i + 1).Append(". ").Append(ranked[i].scene.displayName)
                  .Append("（针对：").Append(AxisName(ranked[i].scene.primaryAxis)).Append("）\n");
            sb.Append('\n').Append("应用后：敌人将针对你的高分弱点强化心理攻击与台词方向。");
            _result.text = sb.ToString();
        }

        void Apply()
        {
            if (_pendingProfile == null)
            {
                _result.text = "请先点击「分析」。";
                return;
            }
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.SetProfile(_pendingProfile);
                var player = FindObjectOfType<Player.PlayerController>();
                SaveSystem.Save(new SaveData
                {
                    profile = _pendingProfile,
                    stats = player != null ? player.Stats : null
                });
            }
            GameEvents.RaiseSubtitle("画像已应用——心魔已看见你的弱点，也看见了你的勇气。");
            Hide();
        }

        static string Bar(float v)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(v * 10), 0, 10);
            return new string('█', n) + new string('░', 10 - n);
        }

        static string AxisName(WeaknessAxis a)
        {
            switch (a)
            {
                case WeaknessAxis.Procrastination: return "拖延迟滞";
                case WeaknessAxis.LowConfidence: return "目标低信心";
                case WeaknessAxis.NoiseSensitivity: return "噪声敏感";
                case WeaknessAxis.Shame: return "羞耻敏感";
                case WeaknessAxis.JobAnxiety: return "求职焦虑";
                case WeaknessAxis.BoundaryConflict: return "边界薄弱";
                case WeaknessAxis.FairnessSensitivity: return "公平敏感";
                case WeaknessAxis.SelfDoubt: return "自我怀疑";
                case WeaknessAxis.FailureFear: return "失败恐惧";
                default: return "意志波动";
            }
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
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
