using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.AI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 敌人图鉴（安全屋）：全部心魔类型的名称、心理轴、克制要点与累计击败数。
    /// 击败数本地持久化——"识别敌人模式"本身就是十大核心成长能力之一。
    /// </summary>
    public class CodexPanel : MonoBehaviour
    {
        GameObject _panel;
        Text _listLeft, _listRight;

        /// <summary>每种敌人的一句"克制要点"（识别模式 → 知道怎么回应）。</summary>
        static string CounterTip(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.SelfDoubtWhisper: return "意志姿态；言语攻防选事实回应";
                case EnemyType.TomorrowPhantom: return "起步姿态；先动手后想";
                case EnemyType.CoughAssassin: return "定心姿态；定心格挡反制";
                case EnemyType.ShameMirror: return "意志姿态；被看见≠被否定";
                case EnemyType.ProcrastinationShadow: return "五分钟火种起势连段";
                case EnemyType.NoReplyKing: return "拉近距离，别停下脚步";
                case EnemyType.TotalResponsibilityJudge: return "边界盾挡红球，责任归还";
                case EnemyType.OverreactGhost: return "事实姿态；先读证据桌";
                case EnemyType.MockingBystander: return "不理会嘲笑，快速近身";
                case EnemyType.SelfDenialGavel: return "击碎标签回自尊，躲红圈";
                case EnemyType.StimulusAmplifier: return "不读心盾+注意力回收清幻影";
                case EnemyType.TomorrowMud: return "腿法击退，别在泥里缠斗";
                case EnemyType.PerfectPreparer: return "打断它的'再准备一下'";
                case EnemyType.TomorrowKing: return "点燃三座火种台破泥壳";
                case EnemyType.OldVoiceRepeater: return "旧话不是事实，近身打断";
                case EnemyType.PastJudge: return "失败是事实，不是身份";
                case EnemyType.RuminationSwarm: return "范围技扫清，复盘归档降反刍";
                case EnemyType.OldSelf: return "不杀死，整合——站入整合圆环";
                default: return "";
            }
        }

        static string AxisName(EnemyType t)
        {
            switch (EnemyCatalog.Create(t, EnemyTier.Standard, true).targetWeakness)
            {
                case Personalization.WeaknessAxis.Procrastination: return "拖延";
                case Personalization.WeaknessAxis.LowConfidence: return "低信心";
                case Personalization.WeaknessAxis.NoiseSensitivity: return "噪声";
                case Personalization.WeaknessAxis.Shame: return "羞耻";
                case Personalization.WeaknessAxis.JobAnxiety: return "求职";
                case Personalization.WeaknessAxis.BoundaryConflict: return "边界";
                case Personalization.WeaknessAxis.FairnessSensitivity: return "公平";
                case Personalization.WeaknessAxis.SelfDoubt: return "自我怀疑";
                case Personalization.WeaknessAxis.FailureFear: return "旧事";
                default: return "意志";
            }
        }

        public static CodexPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<CodexPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "CodexPanel", new Vector2(1500, 960),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "敌 人 图 鉴 · 识 别 心 魔 模 式", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(1000, 54));

            _listLeft = UiUtil.MakeText(_panel.transform, "ListL", "", 22,
                TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.95f));
            UiUtil.SetRect(_listLeft, new Vector2(0.5f, 1f), new Vector2(-370, -480), new Vector2(690, 760));
            _listLeft.lineSpacing = 1.15f;

            _listRight = UiUtil.MakeText(_panel.transform, "ListR", "", 22,
                TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.95f));
            UiUtil.SetRect(_listRight, new Vector2(0.5f, 1f), new Vector2(370, -480), new Vector2(690, 760));
            _listRight.lineSpacing = 1.15f;

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 50), new Vector2(280, 72),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        void Refresh()
        {
            var types = (EnemyType[])System.Enum.GetValues(typeof(EnemyType));
            var left = new System.Text.StringBuilder();
            var right = new System.Text.StringBuilder();
            for (int i = 0; i < types.Length; i++)
            {
                var t = types[i];
                int kills = GrowthSystem.KillCount(EnemyCatalog.BaseId(t));
                var sb = i < (types.Length + 1) / 2 ? left : right;
                sb.Append(kills > 0 ? "■ " : "□ ")
                  .Append(EnemyCatalog.TypeLabel(t))
                  .Append("〔").Append(AxisName(t)).Append("轴〕  击败 ").Append(kills).Append('\n')
                  .Append("    克制：").Append(CounterTip(t)).Append("\n\n");
            }
            _listLeft.text = left.ToString();
            _listRight.text = right.ToString();
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
