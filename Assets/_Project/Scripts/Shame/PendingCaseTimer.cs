using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 悬案计时器（方案 8.5.2 / 8.13.1 PendingCaseTimer）——8-1 里 Boss 血条的替代物。
    ///
    /// 【它不会因为玩家受伤而变化】
    /// 这是本章最重要的一条结构差异：悬案法官不追求击败玩家，他追求的是永远不结案。
    /// 所以计时器只随**追问次数**与**讨好度**缩短，与伤害系统完全无关。
    /// 玩家可以打他，但打赢不等于结束（8.5.4）。
    ///
    /// 【它是可读的倒计时，不是暗箱】
    /// 「案子一直挂着」必须在 HUD 上看得见剩多少（8.2 设计要点）。
    /// </summary>
    public class PendingCaseTimer : MonoBehaviour
    {
        public static PendingCaseTimer Instance { get; private set; }

        /// <summary>总时长（秒）：单关 25-40 分钟里，悬案段约占 12 分钟。</summary>
        public const float TotalSeconds = 720f;

        /// <summary>每次追问扣掉的段（秒）。</summary>
        public const float PerInquirySeconds = 55f;

        float _remaining = TotalSeconds;
        bool _running;
        int _inquiries;
        int _deferrals;
        bool _expired;

        public bool Running => _running;
        public bool Expired => _expired;
        public int Inquiries => _inquiries;
        public int Deferrals => _deferrals;

        /// <summary>剩余比例：≥40% 时主动进门陈述 = 最佳结算（8.5.5）。</summary>
        public float Remaining01 => Mathf.Clamp01(_remaining / TotalSeconds);

        public static PendingCaseTimer Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PendingCaseTimer");
            Instance = go.AddComponent<PendingCaseTimer>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void StartCase()
        {
            _remaining = TotalSeconds;
            _inquiries = 0;
            _deferrals = 0;
            _expired = false;
            _running = true;
            GameEvents.RaiseSubtitle("案子挂上了。它不会结案，也不会撤案——" +
                "广播室的门从现在起就是开着的，什么时候进去由你决定。");
        }

        public void StopCase() => _running = false;

        void Update()
        {
            if (!_running || _expired) return;
            // 时间本身也在走，但走得很慢：真正让案子逼近的是追问与讨好，不是挂机时长
            _remaining -= Time.deltaTime * 0.35f;
            if (_remaining <= 0f)
            {
                _remaining = 0f;
                _expired = true;
                _running = false;
                var ctl = ShameLineController.Instance;
                if (ctl != null) ctl.OnCaseTimerExpired();
            }
        }

        /// <summary>一次「每周追问」：计时器缩短，讨好度越高缩得越多。</summary>
        public void NoteInquiry()
        {
            if (!_running) return;
            _inquiries++;
            float shrink = PerInquirySeconds;
            var appease = AppeasementSystem.Instance;
            if (appease != null) shrink *= appease.TimerShrinkMultiplier();
            _remaining = Mathf.Max(0f, _remaining - shrink);
            GameEvents.RaiseSubtitle("第 " + _inquiries + " 次追问结束——案子还挂着，只是离尽头更近了。");
        }

        /// <summary>悬案法官的「改期」：Exposure 保持不动，计时器少一段。</summary>
        public void NoteDeferral()
        {
            if (!_running) return;
            _deferrals++;
            _remaining = Mathf.Max(0f, _remaining - PerInquirySeconds * 0.6f);
            GameEvents.RaiseSubtitle("【改期】「这次先不谈，下次再说。」——已延期 " + _deferrals + " 次。");
        }

        /// <summary>讨好度变化后重算：不回补，只可能更紧。</summary>
        public void OnAppeasementChanged()
        {
            if (!_running) return;
            var appease = AppeasementSystem.Instance;
            if (appease == null || appease.Tier <= 0) return;
            _remaining = Mathf.Max(0f, _remaining - 12f * appease.Tier);
        }

        /// <summary>HUD 读的一行字。</summary>
        public string HudLine()
        {
            if (!_running && !_expired) return "";
            int m = Mathf.FloorToInt(_remaining / 60f);
            int s = Mathf.FloorToInt(_remaining % 60f);
            return "悬案 " + m.ToString("00") + ":" + s.ToString("00") +
                "　已延期 " + _deferrals + " 次　追问 " + _inquiries + " 次";
        }
    }
}
