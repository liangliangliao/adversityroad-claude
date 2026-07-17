using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.AI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 敌人配置面板：玩家自由添加不同类型 × 不同难度的心魔。
    /// 场景默认只有一个章节敌人，更多挑战由玩家自选。
    /// </summary>
    public class EnemySpawnerPanel : MonoBehaviour
    {
        public Action<EnemyType, EnemyTier> spawnAction;

        GameObject _panel;
        EnemyType _type = EnemyType.TomorrowPhantom;
        EnemyTier _tier = EnemyTier.Standard;
        readonly List<(Button btn, EnemyType type)> _typeButtons = new List<(Button, EnemyType)>();
        readonly List<(Button btn, EnemyTier tier)> _tierButtons = new List<(Button, EnemyTier)>();

        static readonly Color Off = new Color(0.25f, 0.25f, 0.3f, 0.95f);
        static readonly Color On = new Color(0.85f, 0.5f, 0.2f, 0.95f);

        public static EnemySpawnerPanel Create(Transform canvas, Action<EnemyType, EnemyTier> spawnAction)
        {
            var comp = canvas.gameObject.AddComponent<EnemySpawnerPanel>();
            comp.spawnAction = spawnAction;
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "EnemySpawnerPanel", new Vector2(980, 960),
                new Color(0.08f, 0.08f, 0.12f, 0.96f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "添 加 心 魔 挑 战", 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(800, 60));

            var typeLabel = UiUtil.MakeText(_panel.transform, "TypeLabel", "类型", 28,
                TextAnchor.MiddleLeft, Color.white);
            UiUtil.SetRect(typeLabel, new Vector2(0.5f, 1f), new Vector2(-380, -120), new Vector2(120, 40));

            EnemyType[] types =
            {
                EnemyType.TomorrowPhantom, EnemyType.CoughAssassin,
                EnemyType.SelfDoubtWhisper, EnemyType.ShameMirror,
                EnemyType.ProcrastinationShadow, EnemyType.NoReplyKing,
                EnemyType.TotalResponsibilityJudge,
                EnemyType.OverreactGhost, EnemyType.MockingBystander,
                EnemyType.SelfDenialGavel, EnemyType.StimulusAmplifier,
                EnemyType.TomorrowMud, EnemyType.PerfectPreparer,
                EnemyType.TomorrowKing, EnemyType.OldVoiceRepeater,
                EnemyType.PastJudge, EnemyType.RuminationSwarm,
                EnemyType.OldSelf
            };
            for (int i = 0; i < types.Length; i++)
            {
                var t = types[i];
                var btn = UiUtil.MakeButton(_panel.transform, EnemyCatalog.TypeLabel(t),
                    new Vector2(0.5f, 1f), new Vector2(-345 + (i % 4) * 230, -166 - (i / 4) * 80),
                    new Vector2(218, 66), Off, () => SelectType(t), 21);
                _typeButtons.Add((btn, t));
            }

            var tierLabel = UiUtil.MakeText(_panel.transform, "TierLabel", "难度", 28,
                TextAnchor.MiddleLeft, Color.white);
            UiUtil.SetRect(tierLabel, new Vector2(0.5f, 1f), new Vector2(-400, -620), new Vector2(120, 40));

            EnemyTier[] tiers = { EnemyTier.Novice, EnemyTier.Standard, EnemyTier.Elite, EnemyTier.Chief };
            for (int i = 0; i < tiers.Length; i++)
            {
                var t = tiers[i];
                var btn = UiUtil.MakeButton(_panel.transform, EnemyCatalog.TierLabel(t),
                    new Vector2(0.5f, 1f), new Vector2(-290 + i * 195, -690),
                    new Vector2(175, 70), Off, () => SelectTier(t), 26);
                _tierButtons.Add((btn, t));
            }

            UiUtil.MakeButton(_panel.transform, "生成挑战", new Vector2(0.5f, 0f), new Vector2(-130, 70),
                new Vector2(230, 82), new Color(0.75f, 0.3f, 0.2f, 0.95f), Spawn, 30);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(130, 70),
                new Vector2(230, 82), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 30);

            RefreshSelection();
            _panel.SetActive(false);
        }

        void SelectType(EnemyType t) { _type = t; RefreshSelection(); }
        void SelectTier(EnemyTier t) { _tier = t; RefreshSelection(); }

        void RefreshSelection()
        {
            foreach (var (btn, type) in _typeButtons)
                btn.GetComponent<Image>().color = type == _type ? On : Off;
            foreach (var (btn, tier) in _tierButtons)
                btn.GetComponent<Image>().color = tier == _tier ? On : Off;
        }

        void Spawn()
        {
            spawnAction?.Invoke(_type, _tier);
            GameEvents.RaiseSubtitle("【" + EnemyCatalog.TierLabel(_tier) + "·" +
                EnemyCatalog.TypeLabel(_type) + "】已现身！");
            Hide();
        }

        public void Toggle()
        {
            if (_panel.activeSelf) Hide();
            else
            {
                _panel.SetActive(true);
                _panel.transform.SetAsLastSibling();
                Time.timeScale = 0f;
            }
        }

        void Hide()
        {
            _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
