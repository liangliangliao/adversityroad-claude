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

        /// <summary>攻防进行中（休养生息答题等模态系统据此避让）。</summary>
        public bool IsActive => _active;
        Text _srcTag;
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
            // 版面（锚在【右上角】，坐标相对右上角）：
            // 上一版摆在屏幕左上偏中，正好压在战斗视野的正中偏左——玩家反馈
            // "这个位置容易遮挡战斗视野"。挪到右上角，并与已有元素逐个对齐核对过：
            //   · 「暂停」占右侧 x[-338,-198]、上方 y[-12,-76]；「≡菜单」占 x[-180,-20]
            //     → 面板顶边压到 -92，整块在这两枚按钮【之下】；
            //   · 触屏「跳」键圆心在右下 (-100, 680)、半径 56，即左缘 x=-156
            //     → 面板右缘停在 -176，留 20 单位间隙，永远不压到它；
            //   · 面板底边 -288，而「术」键顶缘在距顶 358 处 → 中间还空着 70 单位；
            //   · 左缘 x=-936，HUD 生命/能量条锚在屏幕左侧，两者之间隔着整个屏幕中央。
            // 于是这块面板与屏幕上任何已有文字/按钮都不重叠，也不再挡住正前方视野。
            const float W = 760f, H = 196f;
            _panel = UiUtil.MakePanel(canvas, "VerbalDefensePanel", new Vector2(W, H),
                new Color(0.06f, 0.05f, 0.1f, 0.94f));
            UiUtil.SetRect(_panel.GetComponent<Image>(), new Vector2(1f, 1f),
                new Vector2(-(176f + W * 0.5f), -(92f + H * 0.5f)), new Vector2(W, H));

            var tag = UiUtil.MakeText(_panel.transform, "Tag", "言语攻防 · 选择你的回应", 18,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.6f, 0.55f));
            UiUtil.SetRect(tag, new Vector2(0.5f, 1f), new Vector2(0, -15), new Vector2(730, 22));

            // 来源标签：内部/外部 + AI/本地
            _srcTag = UiUtil.MakeText(_panel.transform, "SrcTag", "", 16,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.9f));
            UiUtil.SetRect(_srcTag, new Vector2(0.5f, 1f), new Vector2(0, -35), new Vector2(730, 20));

            _lineText = UiUtil.MakeText(_panel.transform, "EnemyLine", "", 20,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.85f));
            UiUtil.SetRect(_lineText, new Vector2(0.5f, 1f), new Vector2(0, -62), new Vector2(736, 40));

            // 三枚横排选项按钮（宽度按面板收窄同步缩到 232，间距 244——仍在面板内）
            for (int i = 0; i < 3; i++)
            {
                int idx = i; // 闭包捕获
                var btn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 0f),
                    new Vector2(-244 + i * 244, 58), new Vector2(232, 62),
                    new Color(0.2f, 0.22f, 0.3f, 0.96f), () => OnChoice(idx), 17);
                _btnLabels[i] = btn.GetComponentInChildren<Text>();
            }

            // 倒计时条（横向填充，非缩放时间驱动）
            var barBg = UiUtil.MakePanel(_panel.transform, "TimerBg", new Vector2(730, 8),
                new Color(0, 0, 0, 0.5f));
            UiUtil.SetRect(barBg.GetComponent<Image>(), new Vector2(0.5f, 0f),
                new Vector2(0, 18), new Vector2(730, 8));
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
            if (QuizPanel.Instance != null && QuizPanel.Instance.Active)
                return false; // 休养生息答题中不弹言语攻防
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
            // 来源标注（两个维度，玩家必须能分清）：
            //   ① 内部 / 外部——这句话是【自己脑子里的声音】还是【外面的心魔在说】。
            //      这是本作的核心区分：对内的要"不认同"，对外的要"不接招"，
            //      两者的正确应对完全不同，混在一起玩家就无从判断该用哪套。
            //      enemy == null 即脑内回声（InnerVoiceSystem 就是这么调的）。
            //   ② AI 现编 / 本地写死——语气稳定性与可信度不同，
            //      玩家有权知道屏幕上这句是模型这一次的发挥还是设计好的台词。
            bool inner = enemy == null;
            string src = AI.DialogueLibrary.LastFromAI ? "AI 生成" : "本地台词";
            if (_srcTag != null)
            {
                _srcTag.text = (inner ? "内部 · 脑内回声" : "外部 · 心魔喊话") + "　|　" + src;
                _srcTag.color = inner ? new Color(0.75f, 0.7f, 1f) : new Color(1f, 0.7f, 0.55f);
            }
            _lineText.text = (inner ? "【内】" : "【外】") + "『" + enemyName + "』：" + enemyLine;
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
                // V2.0：用事实回击是可观察的优势样本；濒临崩溃时它同样能打开逆转窗口
                Adversity.PlayerBehaviorAnalyzer.NoteVerbalCounter();
                if (Adversity.ResolveSystem.Instance != null)
                    Adversity.ResolveSystem.Instance.NoteQualityAction("一次说清事实的回击");
                // 姿态契合：当前姿态正好克制这条弱点轴时，额外奖励（更多回补/更快清反刍）
                var stance = player != null ? player.GetComponent<StanceSystem>() : null;
                bool aligned = stance != null && stance.CoversAxis(_axis);
                if (player != null)
                {
                    player.Stats.RestoreAxis(_axis, aligned ? 34f : 22f);
                    player.Stats.ReduceRumination(aligned ? 30f : 20f);
                }
                Adversity.PlayerBehaviorAnalyzer.NoteVerbalDenial(false);
                bool inner = _enemy == null;   // 内部言语攻击（脑内回声）：措辞对内不对外
                GameEvents.RaiseSubtitle("『" + _bestLine + "』——" +
                    (inner ? "脑内回声散去，念头松开了。" : "回击命中，对方语塞。") +
                    (aligned ? "（姿态契合·加成）" : ""));
                GameAudio.Play(GameAudio.Sfx.Parry, 0.8f);
            }
            else
            {
                var pc = player != null ? player.GetComponent<PlayerCombatController>() : null;
                if (pc != null) pc.TakeHit(_pending); // 内部会按弱点轴扣属性并累积反刍
                if (player != null) player.Stats.AddRumination(picked < 0 ? 8f : 12f);
                // 语言否定之后目标被带偏——这是弱点图谱里唯一的「目标偏移」判据
                Adversity.PlayerBehaviorAnalyzer.NoteVerbalDenial(true);
                bool inner = _enemy == null;
                GameEvents.RaiseSubtitle(picked < 0
                    ? (inner ? "任由回声循环——那个念头钻得更深了。" : "沉默以对——那句话钻进了心里。")
                    : (inner ? "跟念头较劲——只是把自己更深地拖进反刍。" : "以牙还牙，只是把自己拖进反刍。"));
            }

            _enemy = null;
        }
    }
}
