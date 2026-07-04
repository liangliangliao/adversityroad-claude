using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.AI;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Personalization;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 言语攻防（快速选择式）：敌人发动心理攻击时，玩家在数秒内三选一回应。
    /// 选对——敌人语塞进入破绽、对应属性回稳、反刍下降，且不吃这次心理伤害；
    /// 选错/沉默——照常吃伤害，并额外累积反刍。战斗不暂停，用非缩放时间计时。
    /// 这是本作最核心的机制："语言打语言、事实打赖账、边界打索取、不读心打刺激"。
    /// </summary>
    public class VerbalDefenseController : MonoBehaviour
    {
        public static VerbalDefenseController Instance { get; private set; }

        const float Duration = 5f;   // 选择窗口
        const float Cooldown = 4f;   // 两次言语攻防的最小间隔，避免刷屏

        GameObject _panel;
        Text _lineText;
        readonly Text[] _btnLabels = new Text[3];
        RectTransform _timerFill;

        bool _active;
        float _deadline, _nextAllowed;
        int _correctIndex;
        string _bestLine;
        WeaknessAxis _axis;
        DamageInfo _pending;
        EnemyController _enemy;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Build(transform);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "VerbalDefensePanel", new Vector2(1500, 300),
                new Color(0.06f, 0.05f, 0.1f, 0.92f));
            UiUtil.SetRect(_panel.GetComponent<Image>(), new Vector2(0.5f, 0f),
                new Vector2(0, 250), new Vector2(1500, 300));

            var tag = UiUtil.MakeText(_panel.transform, "Tag", "言语攻防 · 选择你的回应", 24,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.6f, 0.55f));
            UiUtil.SetRect(tag, new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(1400, 34));

            _lineText = UiUtil.MakeText(_panel.transform, "EnemyLine", "", 30,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.85f));
            UiUtil.SetRect(_lineText, new Vector2(0.5f, 1f), new Vector2(0, -78), new Vector2(1420, 48));

            // 三枚横排选项按钮
            for (int i = 0; i < 3; i++)
            {
                int idx = i; // 闭包捕获
                var btn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 0f),
                    new Vector2(-490 + i * 490, 96), new Vector2(470, 96),
                    new Color(0.2f, 0.22f, 0.3f, 0.96f), () => OnChoice(idx), 24);
                _btnLabels[i] = btn.GetComponentInChildren<Text>();
            }

            // 倒计时条（横向填充，非缩放时间驱动）
            var barBg = UiUtil.MakePanel(_panel.transform, "TimerBg", new Vector2(1420, 12),
                new Color(0, 0, 0, 0.5f));
            UiUtil.SetRect(barBg.GetComponent<Image>(), new Vector2(0.5f, 0f),
                new Vector2(0, 40), new Vector2(1420, 12));
            var fillGo = new GameObject("TimerFill", typeof(Image));
            fillGo.transform.SetParent(barBg.transform, false);
            _timerFill = fillGo.GetComponent<RectTransform>();
            // 左对齐，用 anchorMax.x 表示剩余比例（避免依赖 Filled 图片）
            _timerFill.anchorMin = new Vector2(0, 0); _timerFill.anchorMax = new Vector2(1, 1);
            _timerFill.pivot = new Vector2(0, 0.5f);
            _timerFill.offsetMin = Vector2.zero; _timerFill.offsetMax = Vector2.zero;
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.color = new Color(0.95f, 0.55f, 0.3f, 0.95f);
            fillImg.raycastTarget = false;

            _panel.SetActive(false);
        }

        /// <summary>敌人心理攻击命中前发起言语攻防。返回 true 表示已接管此次伤害。</summary>
        public bool Begin(EnemyController enemy, WeaknessAxis axis, string enemyName,
            string enemyLine, DamageInfo pending)
        {
            if (_active || _panel == null) return false;
            if (Time.unscaledTime < _nextAllowed) return false;
            var gm = GameManager.Instance;
            if (gm != null && gm.safety != null && gm.safety.MentalDamageMultiplier() <= 0f)
                return false; // 恢复模式：不发起攻防（也没有心理伤害）

            _active = true;
            _enemy = enemy;
            _axis = axis;
            _pending = pending;

            var (opts, correct, best) = ResponseLibrary.GetChoices(axis);
            _correctIndex = correct;
            _bestLine = best;
            _lineText.text = "『" + enemyName + "』：" + enemyLine;
            for (int i = 0; i < 3; i++)
                if (_btnLabels[i] != null) _btnLabels[i].text = opts[i];

            _deadline = Time.unscaledTime + Duration;
            if (_timerFill != null) _timerFill.anchorMax = new Vector2(1f, 1f);
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            GameAudio.Play(GameAudio.Sfx.Alert, 0.4f);
            return true;
        }

        void Update()
        {
            if (!_active) return;
            float rem = _deadline - Time.unscaledTime;
            if (_timerFill != null)
                _timerFill.anchorMax = new Vector2(Mathf.Clamp01(rem / Duration), 1f);
            if (rem <= 0f) Resolve(-1);
        }

        void OnChoice(int i) => Resolve(i);

        void Resolve(int picked)
        {
            if (!_active) return;
            _active = false;
            _panel.SetActive(false);
            _nextAllowed = Time.unscaledTime + Cooldown;

            var player = FindObjectOfType<PlayerController>();
            bool correct = picked == _correctIndex;

            if (correct)
            {
                if (_enemy != null) _enemy.OnVerbalCountered();
                if (player != null)
                {
                    player.Stats.RestoreAxis(_axis, 22f);
                    player.Stats.ReduceRumination(20f);
                }
                GameEvents.RaiseSubtitle("『" + _bestLine + "』——回击命中，对方语塞。");
                GameAudio.Play(GameAudio.Sfx.Parry, 0.8f);
            }
            else
            {
                var pc = player != null ? player.GetComponent<PlayerCombatController>() : null;
                if (pc != null) pc.TakeHit(_pending); // 内部会按弱点轴扣属性并累积反刍
                if (player != null) player.Stats.AddRumination(picked < 0 ? 8f : 12f);
                GameEvents.RaiseSubtitle(picked < 0
                    ? "沉默以对——那句话钻进了心里。"
                    : "以牙还牙，只是把自己拖进反刍。");
            }

            _enemy = null;
        }
    }
}
