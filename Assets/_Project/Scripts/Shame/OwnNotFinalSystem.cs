using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.AI;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Mobile;
using AdversityRoad.Player;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 认领不终审 Own-Not-Final（方案 8.4）——本章的机制创新，也是它与前七章的分界线。
    ///
    /// 【前七章的防御建立在"我是对的"之上】
    /// 事实之刃击穿模糊叙事、边界盾挡下索取、不读心盾拒绝揣测——它们全都以
    /// "指控不成立"为前提。本章的处境是**指控成立**，于是这些反制在这里全部失效，
    /// 而且每用一次都在给对方续命。
    ///
    /// 【这一招认领的是事实，不是身份】
    /// 强制文案要求（8.4.1）：它不是"承认一切指控"，不是"接受批评"，更不是认罪教学。
    /// 对 truthTag = false 的指控，它无效并产生硬直——正解回到事实之刃。
    /// 两套语法由玩家自己判断，系统不代判。
    ///
    /// 【输入】
    /// 敌人「指认」招式的前摇明显长于普通攻击（≥22 帧，见 8.2），
    /// 随后开一个 8 帧的判定窗（辅助/降压时放宽到 14 帧）：
    ///   · 按【挡】= 认领不终审（对真指控成立）
    ///   · 按【拳】= 事实之刃（对假指控成立；对真指控 = 否认，为 Boss 续命）
    ///   · 不输入 = 吃满这次指认并挂上身份钉（不额外惩罚，允许立即重试）
    /// </summary>
    public class OwnNotFinalSystem : MonoBehaviour
    {
        public static OwnNotFinalSystem Instance { get; private set; }

        /// <summary>指认前摇：必须明显长于普通攻击（22 帧 @60fps ≈ 0.37s，这里取更宽的 0.75s）。</summary>
        public const float AccusationWindup = 0.75f;

        /// <summary>判定窗：8 帧。</summary>
        public const float BaseWindow = 8f / 60f;

        /// <summary>降压/辅助下的判定窗：14 帧。</summary>
        public const float AssistWindow = 14f / 60f;

        /// <summary>慢速判定（连续认领窗口的第三次）：16 帧。</summary>
        public const float SlowWindow = 16f / 60f;

        /// <summary>SelfWorth 低于此比例时禁止生成新的指认招式（8.7.1）。</summary>
        public const float NoAccuseSelfWorthRatio = 0.25f;

        EnemyController _src;
        ClaimData _claim;
        float _mentalDamage;
        float _windowUntil = -1f;
        float _windupUntil = -1f;
        float _accuseStart;
        bool _resolved;
        int _ownStreak;                 // 连续认领成功次数（8.7.2 优势窗口）
        float _lastAccusationAt = -99f;
        bool _guardPrev, _lightPrev;

        GameObject _panel;
        Text _claimText, _hintText;
        RectTransform _timerFill;

        /// <summary>指认进行中（前摇或判定窗内）。</summary>
        public bool Active => Time.time < _windowUntil || Time.time < _windupUntil;

        /// <summary>判定窗开着——此刻按【挡】才是认领不终审。</summary>
        public bool WindowOpen => !_resolved && Time.time < _windowUntil && Time.time >= _windupUntil;

        public int OwnStreak => _ownStreak;

        public static OwnNotFinalSystem Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("OwnNotFinalSystem");
            Instance = go.AddComponent<OwnNotFinalSystem>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ================= 开窗 =================

        /// <summary>
        /// 敌人发动一次指认。返回 false 表示这一招按安全条款被拒绝生成。
        ///
        /// 两条硬约束（8.7.1）：
        ///   ① 玩家 SelfWorth 低于 25% 时不生成新的指认招式；
        ///   ② 不允许连续两次挂钉而不给出至少一个认领窗口——本方法必开窗，故天然满足。
        /// </summary>
        public bool Accuse(EnemyController src, ClaimData claim, float mentalDamage)
        {
            if (Active || claim == null) return false;
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p == null) return false;
            if (p.Stats.maxSelfWorth > 0 &&
                p.Stats.selfWorth / p.Stats.maxSelfWorth < NoAccuseSelfWorthRatio)
                return false;
            // 单次遭遇的指认密度上限（8.7 预算表：不超过 5 次）由发招方按冷却控制，
            // 这里再兜一道最小间隔，避免多个身份钉兵同时开口把窗口糊成一片。
            if (Time.time - _lastAccusationAt < 3.5f) return false;

            _src = src;
            _claim = claim;
            _mentalDamage = mentalDamage;
            _resolved = false;
            _lastAccusationAt = Time.time;
            _accuseStart = Time.time;
            _windupUntil = Time.time + AccusationWindup;
            _windowUntil = _windupUntil + WindowLength();

            EnsurePanel();
            if (_panel != null)
            {
                _panel.SetActive(true);
                _claimText.text = "「" + claim.claimTag + "」";
                _hintText.text = "[挡] 认领不终审　　[拳] 事实之刃";
            }
            if (src != null && src.dialogue != null)
                src.dialogue.Show(claim.claimTag, 2.6f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.7f);
            return true;
        }

        float WindowLength()
        {
            // 连续 2 次认领成功后，第 3 次进入慢速判定（8.7.2 优势窗口）
            if (_ownStreak >= 2) return SlowWindow;
            return AssistEnabled() ? AssistWindow : BaseWindow;
        }

        /// <summary>
        /// 放宽判定窗的条件：强度设为轻度，或本章的连续失败保护已经触发（8.12.1）。
        /// 8 帧对手柄玩家已经很紧，这里的放宽不是难度让步，是可达性。
        /// </summary>
        static bool AssistEnabled()
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.safety != null &&
                (gm.safety.intensity == MentalIntensity.Light || gm.safety.recoveryMode)) return true;
            return ShameLineController.ChallengeCeilingActive;
        }

        // ================= 判定 =================

        void Update()
        {
            if (_claim == null) return;

            if (_panel != null && _panel.activeSelf && _timerFill != null)
            {
                // 整条 = 前摇 + 判定窗。前摇段是暗红（还不能按），
                // 判定窗一开变亮黄（现在按）——颜色本身就是"可以出手了"的信号。
                float total = Mathf.Max(0.001f, _windowUntil - _accuseStart);
                float left = Mathf.Clamp01((_windowUntil - Time.time) / total);
                _timerFill.sizeDelta = new Vector2(560f * left, _timerFill.sizeDelta.y);
                var img = _timerFill.GetComponent<Image>();
                if (img != null)
                    img.color = WindowOpen ? new Color(0.95f, 0.9f, 0.5f)
                                           : new Color(0.62f, 0.3f, 0.3f);
            }

            if (_resolved) { if (Time.time > _windowUntil + 0.6f) Close(); return; }

            // 【为什么这里自己做边沿检测，而不用 MobileInput.GetDown】
            // GetDown 是**消费式**读取（读到即清除）。战斗控制器也在同一帧读「挡」与「拳」，
            // 谁先跑谁拿走——脚本执行顺序一变，玩家按下的那一下就可能落到另一边，
            // 表现为"我明明按了认领，却只出了个格挡"。
            // 改成读非消费的按住状态、自己算上升沿：两边都能收到同一次按键，
            // 而且"一直按着挡"不会自动认领——窗口内必须**真的按一下**。
            bool guardHeld = Input.GetKey(KeyCode.LeftControl) || MobileInput.GetHeld("Guard");
            bool lightHeld = Input.GetMouseButton(0) || MobileInput.GetHeld("Light");
            bool guardEdge = guardHeld && !_guardPrev;
            bool lightEdge = lightHeld && !_lightPrev;
            _guardPrev = guardHeld;
            _lightPrev = lightHeld;

            if (WindowOpen && guardEdge) { ResolveOwn(); return; }
            if (WindowOpen && lightEdge) { ResolveFactBlade(); return; }

            if (Time.time >= _windowUntil) ResolveMissed();
        }

        /// <summary>认领不终审：站定、正面、抬头——身体语言必须与顺从信号相反（8.4.1）。</summary>
        void ResolveOwn()
        {
            _resolved = true;
            var p = AdversityRoad.Core.ActorRegistry.Player;

            if (!_claim.truthTag)
            {
                // 对虚假指控认领无效并产生硬直：这条不是惩罚玩家的诚实，
                // 而是守住"认领的是事实，不是身份"这条线——不实之事没有可认领的事实。
                _ownStreak = 0;
                GameEvents.RaiseSubtitle("这一条不是真的——认领无效。不实的指控要用事实之刃，不能替它认下来。");
                GameAudio.Play(GameAudio.Sfx.Block, 0.8f);
                var fsm = p != null ? p.GetComponent<CombatStateMachine>() : null;
                if (fsm != null) fsm.RequestState(CombatState.HitReaction, 0.55f);
                ApplyAccusationDamage(false);
                return;
            }

            _ownStreak++;
            ClaimRegistry.MarkOwned(_claim);

            // 动画语言（8.4.1）：不是格挡姿势，也不是低头——站定、正面、抬头。
            // 身体语言必须与顺从信号相反，否则这一招在观感上就变成了认罪。
            var poser = p != null ? p.GetComponent<HumanoidAnimator>() : null;
            if (poser != null) poser.PlayFirstClip(1f, 0.18f, "Opening", "Warming Up", "Breathing Idle");

            var nails = IdentityNailSystem.Instance;
            if (nails != null) nails.ClearAll("事实成立，判词不终审。");

            var exposure = ExposureSystem.Instance;
            if (exposure != null)
            {
                exposure.Add(-25f, "认领不终审——你没有辩解，也没有低头");
                exposure.ClearRevealed();
            }

            if (p != null)
            {
                p.Stats.RestoreAxis(Personalization.WeaknessAxis.Shame, 10f);
                p.Stats.ReduceRumination(12f);
                CombatFeedback.ShockRing(p.transform.position, new Color(0.95f, 0.92f, 0.62f), 3.2f);
                CombatFeedback.MoveName(p.transform.position + Vector3.up * 2.2f, "认领不终审", false);
            }
            // 指认方失去这一招的着力点：硬直，但不掉血——打赢他从来不是本章的通关条件
            if (_src != null) _src.ForceBreak(1.6f);

            GameAudio.Play(GameAudio.Sfx.Parry, 1f);
            GameEvents.RaiseSubtitle("「这件事我做了。但你不能据此宣判我是什么人。」" +
                "——这条指控本章内不再可用。");

            ShameComboTracker.Push(ShameComboTracker.TagOwn);
            Adversity.AdversityProfile.ObserveStrength("认领不终审", ShameLine.CurrentLevelId);
            Adversity.PlayerBehaviorAnalyzer.NoteVerbalCounter();
            var resolve = Adversity.ResolveSystem.Instance;
            if (resolve != null) resolve.NoteQualityAction("在满身份钉状态下完成认领不终审");
            if (_ownStreak == 2)
                GameEvents.RaiseSubtitle("【连续认领窗口】下一次指认进入慢速判定——窗口放宽到 16 帧。");
        }

        /// <summary>事实之刃：对虚假指控的正解；对真指控则是一次否认，为对方续命。</summary>
        void ResolveFactBlade()
        {
            _resolved = true;
            var p = AdversityRoad.Core.ActorRegistry.Player;

            if (IdentityNailSystem.FactBladeLocked)
            {
                GameEvents.RaiseSubtitle("事实之刃此刻拔不出来——在羞耻里，人不认为自己有资格陈述事实。" +
                    "先完成一次与目标相关的行动。");
                ApplyAccusationDamage(true);
                return;
            }

            if (!_claim.truthTag)
            {
                ClaimRegistry.MarkRefuted(_claim);
                GameEvents.RaiseSubtitle("事实之刃击穿了这条不实的指控——不是所有指控都成立，判断是你自己的事。");
                GameAudio.Play(GameAudio.Sfx.Parry, 0.95f);
                if (_src != null) _src.ForceBreak(1.4f);
                var ex = ExposureSystem.Instance;
                if (ex != null) ex.Add(-10f, "指控被击穿");
                ShameComboTracker.Push(ShameComboTracker.TagFactBlade);
                Adversity.AdversityProfile.ObserveStrength("事实判断", ShameLine.CurrentLevelId);
                if (p != null)
                    CombatFeedback.MoveName(p.transform.position + Vector3.up * 2.2f, "事实之刃", false);
                return;
            }

            // 对真指控用事实之刃 = 否认。它在这里是**无效动作**，而且有代价。
            _ownStreak = 0;
            var d = ShameLine.Data;
            d.denialCount++;
            ShameLine.Persist();
            var exposure = ExposureSystem.Instance;
            if (exposure != null) exposure.Add(8f, "否认了一件成立的事");
            GameEvents.RaiseSubtitle("否认落空——事实站在他那一边。每一次否认都在给他续命。");
            Adversity.AdversityProfile.Observe("被指认", "否认优先于认领", true,
                ShameLine.CurrentLevelId, "认领不终审");
            Adversity.PlayerBehaviorAnalyzer.NoteVerbalDenial(false);
            ApplyAccusationDamage(true);
        }

        /// <summary>没有输入：吃满这次指认并挂钉。不额外惩罚，允许立即重试（8.4.1）。</summary>
        void ResolveMissed()
        {
            _resolved = true;
            _ownStreak = 0;
            if (_claim.truthTag)
            {
                Adversity.AdversityProfile.Observe("被指认", "否认优先于认领", false,
                    ShameLine.CurrentLevelId, "认领不终审");
                // 8.9.1 的第三条弱点边：辩解不出来的时候，动作节奏先垮。
                // 它的用途是**触发优势窗口**，不是拿来追加压力（8.9.1 明写）。
                Adversity.AdversityProfile.Observe("无法辩解",
                    Adversity.PlayerBehaviorAnalyzer.BehaviorRhythmBreak, true,
                    ShameLine.CurrentLevelId, "认领不终审", "锥内稳态");
            }
            GameEvents.RaiseSubtitle("这一句落在了身上——钉子进去了，但拔它的动作随时可以再打一次。");
            ApplyAccusationDamage(true);
        }

        void ApplyAccusationDamage(bool mountNail)
        {
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p != null)
            {
                // Exposure 的放大倍率由 PlayerStats 统一施加，这里只管强度设置
                float dmg = _mentalDamage;
                var gm = GameManager.Instance;
                if (gm != null && gm.safety != null) dmg *= gm.safety.MentalDamageMultiplier();
                p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.Shame, dmg);
            }
            // 被指认招式命中 → 暴露度上升（方案 8.3 上升来源之一）。
            // 一句话落在身上，本来就是"更多人知道了"。
            var ex = ExposureSystem.Instance;
            if (ex != null) ex.Add(10f, null);
            if (mountNail)
            {
                var nails = IdentityNailSystem.Instance;
                if (nails != null) nails.Mount(_claim);
            }
            GameAudio.Play(GameAudio.Sfx.Hurt, 0.7f);
        }

        void Close()
        {
            _claim = null;
            _src = null;
            if (_panel != null) _panel.SetActive(false);
        }

        // ================= 提示面板 =================

        void EnsurePanel()
        {
            if (_panel != null) return;
            var canvas = UiUtil.MainCanvas();
            if (canvas == null) return;

            // 摆在屏幕正下方偏上：指认是要正面接住的动作，提示不该躲在角落里。
            // 触屏「术/挡/拳」按钮在右下与左下，这块面板宽 620、贴着屏幕底 200 处，
            // 与两侧按钮各留出一大段横向间隙。
            _panel = UiUtil.MakePanel(canvas.transform, "AccusationPanel",
                new Vector2(620, 132), new Color(0.08f, 0.06f, 0.07f, 0.92f));
            UiUtil.SetRect(_panel.GetComponent<Image>(), new Vector2(0.5f, 0f),
                new Vector2(0, 210), new Vector2(620, 132));

            var tag = UiUtil.MakeText(_panel.transform, "Tag", "指　认", 20,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.72f, 0.55f));
            UiUtil.SetRect(tag, new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(600, 24));

            _claimText = UiUtil.MakeText(_panel.transform, "Claim", "", 21,
                TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.88f));
            UiUtil.SetRect(_claimText, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(596, 30));

            _hintText = UiUtil.MakeText(_panel.transform, "Hint", "", 19,
                TextAnchor.MiddleCenter, new Color(0.85f, 0.88f, 0.95f));
            UiUtil.SetRect(_hintText, new Vector2(0.5f, 1f), new Vector2(0, -78), new Vector2(596, 26));

            var barBg = new GameObject("TimerBg", typeof(Image));
            barBg.transform.SetParent(_panel.transform, false);
            barBg.GetComponent<Image>().color = new Color(0.2f, 0.18f, 0.2f, 0.9f);
            UiUtil.SetRect(barBg.GetComponent<Image>(), new Vector2(0.5f, 0f),
                new Vector2(0, 18), new Vector2(560, 10));

            var fill = new GameObject("TimerFill", typeof(Image));
            fill.transform.SetParent(_panel.transform, false);
            fill.GetComponent<Image>().color = new Color(0.95f, 0.9f, 0.5f);
            _timerFill = UiUtil.SetRect(fill.GetComponent<Image>(), new Vector2(0.5f, 0f),
                new Vector2(0, 18), new Vector2(560, 10));

            _panel.SetActive(false);
        }
    }
}
