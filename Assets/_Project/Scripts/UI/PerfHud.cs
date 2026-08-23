using UnityEngine;
using UnityEngine.UI;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 帧率 / 帧时读数（右上角一行小字）。
    ///
    /// 【为什么要有它】
    /// "穿墙""被拉着转""动作时快时慢""移动变慢"这一串现象，在**低且不稳的帧率**下
    /// 会全部同时出现，而且与代码逻辑无关：
    ///   · 单帧位移 = 速度 × dt，掉帧时 dt 变大 ⇒ 一次 Move 跨过半米甚至更多 ⇒ 穿墙；
    ///   · 搓杆时相邻两帧的角度差变大 ⇒ 读作"人被瞬移/被拉着甩"；
    ///   · 帧时忽长忽短 ⇒ 动画忽快忽慢。
    /// 也就是说：**同一批症状既可能来自逻辑 bug，也可能纯粹来自帧率**，
    /// 光靠现象描述分不开。分不开就只能反复猜，而每猜一轮就是一次真机往返。
    ///
    /// 这一行读数把它变成一个可以直接报出来的数字：
    ///   · FPS 平均值 —— 整体档次；
    ///   · 本秒最长帧（ms）—— 决定"最坏那一帧位移多大"，穿墙看的是它，不是平均值；
    ///   · 场上角色数 —— 动画开销随它线性增长，用来判断是不是人一多就掉。
    /// </summary>
    public class PerfHud : MonoBehaviour
    {
        public static bool Enabled = true;

        Text _text;
        Text _move;              // 第二行：移动诊断
        Player.PlayerController _pc;
        float _accum, _worst;
        int _frames;
        float _nextReport;

        void Awake()
        {
            var go = new GameObject("PerfHudCanvas");
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;          // 压在所有面板之上，任何时候都读得到
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var t = new GameObject("PerfText");
            t.transform.SetParent(go.transform, false);
            _text = t.AddComponent<Text>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = 26;
            _text.alignment = TextAnchor.UpperRight;
            _text.color = new Color(1f, 0.95f, 0.4f);
            _text.raycastTarget = false;          // 绝不吃掉触摸事件
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var rt = _text.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -190f);   // 让开右上角的暂停/菜单/目标
            rt.sizeDelta = new Vector2(700f, 40f);

            // 第二行：移动诊断。速度是好几层倍率连乘出来的，"移动变慢"到底慢在
            // 哪一层——行动力低？减速 debuff？锁定封顶？出招定步卡住？——
            // 只看角色跑得快不快是分不出来的，必须把每一层原样摆出来。
            var m = new GameObject("MoveText");
            m.transform.SetParent(go.transform, false);
            _move = m.AddComponent<Text>();
            _move.font = _text.font;
            _move.fontSize = 24;
            _move.alignment = TextAnchor.UpperRight;
            _move.color = new Color(0.6f, 0.9f, 1f);
            _move.raycastTarget = false;
            _move.horizontalOverflow = HorizontalWrapMode.Overflow;
            var mrt = _move.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(1f, 1f);
            mrt.pivot = new Vector2(1f, 1f);
            mrt.anchoredPosition = new Vector2(-24f, -226f);
            mrt.sizeDelta = new Vector2(700f, 40f);

            // 第三行：**搓杆时的整条链**。"看不到角色自己的移动节奏"这句话，
            // 从摇杆到腿一共经过六个环节，光看画面分不出断在哪一环：
            //   杆角速度 → 身体角速度 → 镜头角速度 → 实际移速/转弯半径
            //   → 喂给动画的夹角/混合角 → 步态相位速率
            // 全打出来，一次搓杆截图就能定死是哪一环。峰值保持 1.2 秒，
            // 否则搓完手一松数字就掉回 0，截图永远抓不到。
            var s3 = new GameObject("SpinText");
            s3.transform.SetParent(go.transform, false);
            _spin = s3.AddComponent<Text>();
            _spin.font = _text.font;
            _spin.fontSize = 24;
            _spin.alignment = TextAnchor.UpperRight;
            _spin.color = new Color(1f, 0.75f, 0.95f);
            _spin.raycastTarget = false;
            _spin.horizontalOverflow = HorizontalWrapMode.Overflow;
            var srt = _spin.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(1f, 1f);
            srt.anchoredPosition = new Vector2(-24f, -262f);
            srt.sizeDelta = new Vector2(760f, 40f);
        }

        Text _spin;
        float _prevStickYaw, _prevBodyYaw, _prevCamYaw;
        bool _yawInit;
        // 峰值保持：搓杆那一下的读数要留在屏幕上足够久，人才截得到
        float _pkStick, _pkBody, _pkCam, _pkActual, _pkRadius, _pkAngle, _pkBlend, _pkPhase;
        float _pkUntil;

        /// <summary>把搓杆整条链每帧采样、取峰值保持，供第三行显示。</summary>
        void SampleSpin(float dt)
        {
            if (_spin == null || dt <= 0.0001f) return;
            var pc = AdversityRoad.Core.ActorRegistry.Player;
            var cam = Camera.main;
            if (pc == null) { _spin.text = ""; return; }

            Vector3 sw = pc.StickWorldDir;
            float stickYaw = sw.sqrMagnitude > 0.04f
                ? Quaternion.LookRotation(sw.normalized).eulerAngles.y : _prevStickYaw;
            float bodyYaw = pc.transform.eulerAngles.y;
            float camYaw = cam != null ? cam.transform.eulerAngles.y : 0f;

            if (!_yawInit)
            {
                _yawInit = true;
                _prevStickYaw = stickYaw; _prevBodyYaw = bodyYaw; _prevCamYaw = camYaw;
                return;
            }

            float sRate = Mathf.Abs(Mathf.DeltaAngle(_prevStickYaw, stickYaw)) / dt;
            float bRate = Mathf.Abs(Mathf.DeltaAngle(_prevBodyYaw, bodyYaw)) / dt;
            float cRate = Mathf.Abs(Mathf.DeltaAngle(_prevCamYaw, camYaw)) / dt;
            _prevStickYaw = stickYaw; _prevBodyYaw = bodyYaw; _prevCamYaw = camYaw;

            var anim = pc.GetComponent<Combat.HumanoidAnimator>();
            float actual = pc.DbgActual;
            // 转弯半径 = v / ω：贴近 0 就是【原地打转】，那正是"被摇杆拖着转"的样子
            float radius = bRate > 5f ? actual / (bRate * Mathf.Deg2Rad) : 999f;

            // 只在真的在搓杆时刷新峰值（杆自己在转），否则保持上一次的读数
            if (sRate > 60f || Time.unscaledTime < _pkUntil)
            {
                if (sRate > 60f) _pkUntil = Time.unscaledTime + 1.2f;
                if (sRate > _pkStick) _pkStick = sRate;
                if (bRate > _pkBody) _pkBody = bRate;
                if (cRate > _pkCam) _pkCam = cRate;
                _pkActual = actual;
                if (radius < _pkRadius || _pkRadius <= 0.01f) _pkRadius = radius;
                _pkAngle = pc.DbgMoveAngle;
                if (anim != null) { _pkBlend = anim.DbgBlendAngle; _pkPhase = anim.DbgPhaseRate; }
            }
            else if (Time.unscaledTime > _pkUntil + 2f)
            {
                // 停手两秒后清零，下一次搓杆重新计峰
                _pkStick = _pkBody = _pkCam = 0f; _pkRadius = 0f;
            }

            _spin.text = string.Format(
                "搓杆 杆{0:F0} 身{1:F0} 镜{2:F0}°/s | 移{3:F1}m/s 半径{4} | 夹角{5:F0}° 混{6:F0}° | 步频{7:F2}/s",
                _pkStick, _pkBody, _pkCam, _pkActual,
                _pkRadius > 100f ? "--" : _pkRadius.ToString("F1") + "m",
                _pkAngle, _pkBlend, _pkPhase);
        }

        void Update()
        {
            if (_text == null) return;
            if (_text.enabled != Enabled) _text.enabled = Enabled;
            if (_move != null && _move.enabled != Enabled) _move.enabled = Enabled;
            if (_spin != null && _spin.enabled != Enabled) _spin.enabled = Enabled;
            if (!Enabled) return;

            // 用不缩放的真实帧时：顿帧/时缓会把 Time.deltaTime 改掉，
            // 而这里要量的是**设备每帧真的花了多久**。
            float dt = Time.unscaledDeltaTime;
            _accum += dt;
            _frames++;
            if (dt > _worst) _worst = dt;

            SampleSpin(dt);   // 搓杆链路要每帧采（角速度是差分出来的，不能只在汇报时算）

            if (Time.unscaledTime < _nextReport) return;
            _nextReport = Time.unscaledTime + 0.5f;

            float fps = _frames > 0 ? _frames / Mathf.Max(0.0001f, _accum) : 0f;
            float worstMs = _worst * 1000f;
            // 最坏那一帧对应的冲刺位移：直接告诉你"这一帧人挪了多远"，
            // 与胶囊半径（0.4m 上下）一比就知道会不会穿墙。
            float worstStep = _worst * 5.2f;
            CountCharacters();
            _text.text = string.Format(
                "FPS {0:F0} | 最长帧 {1:F0}ms（单帧位移 {2:F2}m）| 角色 {3}（动捕 {4}）| 敌 {5}",
                fps, worstMs, worstStep, _charCache, _mocapCache,
                AdversityRoad.Core.ActorRegistry.Enemies.Length);
            // 最坏帧偏红：一眼能看出这半秒里有没有大顿
            _text.color = worstMs > 60f ? new Color(1f, 0.45f, 0.35f)
                                        : new Color(1f, 0.95f, 0.4f);
            _accum = 0f; _frames = 0; _worst = 0f;

            ReportMovement();
        }

        void ReportMovement()
        {
            if (_move == null) return;
            if (_pc == null)
                _pc = AdversityRoad.Core.ActorRegistry.Player;
            if (_pc == null) { _move.text = ""; return; }

            // 实测移速：由控制器每帧量出来的真实位移换算（不是"打算走多快"，
            // 而是"实际走了多快"）——两者对不上本身就是一条线索。
            float want = _pc.DbgFinalSpeed;
            string cap = _pc.DbgStrafeCap > 0.01f
                ? string.Format("锁定封顶{0:F1}", _pc.DbgStrafeCap)
                : _pc.WalkOnly ? "冲刺锁"
                : _pc.IndoorPace ? "室内步速" : "无封顶";
            _move.text = string.Format(
                "杆{0:F2} 目标{1:F1}/{2:F1}m/s | 行动力×{3:F2} 减速×{4:F2} 出招×{5:F2} | {6} | {7}",
                _pc.DbgInputMag, want, _pc.DbgRawSpeed,
                _pc.DbgApMult, _pc.MoveSpeedMultiplier, _pc.DbgAttackFactor,
                cap, _pc.StrafeActive ? "锁定中" : "自由");
            // 任何一层把速度压到七成以下就标红——那就是"变慢"的那一层
            bool slowed = _pc.DbgApMult < 0.95f || _pc.MoveSpeedMultiplier < 0.95f ||
                          _pc.DbgAttackFactor < 0.95f;
            _move.color = slowed ? new Color(1f, 0.6f, 0.35f) : new Color(0.6f, 0.9f, 1f);
        }

        float _nextCount;
        int _charCache;

        int _mocapCache;

        void CountCharacters()
        {
            // 每 2 秒数一次就够——FindObjects 本身不便宜，别让诊断自己变成卡顿源
            if (Time.unscaledTime < _nextCount) return;
            _nextCount = Time.unscaledTime + 2f;
            var all = Object.FindObjectsByType<Combat.HumanoidAnimator>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            _charCache = all.Length;
            int m = 0;
            for (int i = 0; i < all.Length; i++) if (all[i].IsMocap) m++;
            _mocapCache = m;
        }
    }
}
