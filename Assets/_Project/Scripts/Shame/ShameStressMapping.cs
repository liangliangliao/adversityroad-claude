using UnityEngine;
using AdversityRoad.Adversity;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// Stress State Machine 在第八章的映射（方案 8.10.3）。
    ///
    /// 压力六阶段本身是全局系统，这里只做**本章的表现层落位**：
    /// 同一个阶段，在羞耻线里表现成的是"注视越来越清楚、低语越来越连得上"，
    /// 而不是别处那套。它不改任何数值曲线，只改这一章里压力**看起来**是什么样子。
    ///
    /// 【Breakdown 的硬约束】
    /// 遵 §12.3：短暂低头 / 解除锁定，不超过 12 秒，且**禁止围观特写**。
    /// 全局机器已经把 Breakdown 压在 3 秒以内，这里只补低头与解除锁定，
    /// 一个围观镜头都不加——本章的失败态不制造额外羞辱（8.5.5）。
    /// </summary>
    public class ShameStressMapping : MonoBehaviour
    {
        public static ShameStressMapping Instance { get; private set; }

        StressStateMachine _stress;
        bool _hooked;

        public static ShameStressMapping Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ShameStressMapping");
            Instance = go.AddComponent<ShameStressMapping>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            Unhook();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            // StressStateMachine 是运行时创建的，订阅要等它出现；订阅一次就够。
            // 但"挂过了"不等于"还挂着"：那台机器被销毁重建之后（重载世界），
            // 旧引用会变成空，这时必须重新挂一次，否则本章的阶段表现从此再不触发。
            if (_hooked && _stress != null) return;
            if (_hooked) _hooked = false;
            _stress = StressStateMachine.Instance;
            if (_stress == null) return;
            _stress.StageChanged += OnStage;
            _hooked = true;
        }

        void Unhook()
        {
            if (_stress != null && _hooked) _stress.StageChanged -= OnStage;
            _hooked = false;
        }

        void OnStage(StressStage stage)
        {
            if (!ShameLine.InChapter) return;

            switch (stage)
            {
                case StressStage.Strained:
                    // 环境音开始出现方向性低语，视线锥边缘微亮
                    SetConeVisibility(0.5f);
                    GameEvents.RaiseSubtitle("背后开始有声音了——听不清内容，但听得出方向。");
                    break;

                case StressStage.Destabilized:
                    // 视线锥可见度提升，呼吸加重，Exposure 首次显示
                    SetConeVisibility(0.75f);
                    var exposure = ExposureSystem.Instance;
                    // 推一点点：让那一组 HUD 在这一刻真的出现（它平时是隐藏的）
                    if (exposure != null) exposure.Add(0.8f, "有人朝这边看了一眼");
                    break;

                case StressStage.Overloaded:
                    // 多条视线锥交叉，低语链完整成形，身份钉可被挂载
                    SetConeVisibility(1f);
                    var whisper = WhisperChainSystem.Instance;
                    if (whisper != null && ShameLine.CurrentLevelId == ShameLine.LevelEchoClassroom)
                        whisper.EnableForLevel(true);
                    GameEvents.RaiseSubtitle("几道视线交叉在同一处，低语也连上了——这一段是本章最重的地方。");
                    break;

                case StressStage.NearCollapse:
                    // 画面边缘收缩、脚步声被放大；逆转窗口由全局机器打开
                    GameEvents.RaiseSubtitle("脚步声变得很响。〔本章的逆转触发〕" +
                        "锥内完成一次目标交互 / 满钉时认领一次 / 低语活着时走完全场，任选其一。");
                    break;

                case StressStage.Breakdown:
                    // 短暂低头 + 解除锁定。没有围观特写，没有慢镜头羞辱
                    var p = FindObjectOfType<PlayerController>();
                    var poser = p != null ? p.GetComponent<HumanoidAnimator>() : null;
                    if (poser != null) poser.PlayFirstClip(1f, 0.2f, "Sad Idle", "Kneeling Down");
                    var lockOn = p != null ? p.GetComponent<LockOnSystem>() : null;
                    if (lockOn != null) lockOn.Release();
                    break;
            }
        }

        /// <summary>
        /// 调整全场视线锥的可见度。**下限由 GazeCone 自己兜在 0.35**——
        /// 这里再怎么调也不可能把注视调成隐形（8.7.1 禁止项）。
        /// </summary>
        static void SetConeVisibility(float v)
        {
            var gaze = GazeConeSystem.Instance;
            if (gaze == null) return;
            gaze.SetVisibility(v);
        }
    }
}
