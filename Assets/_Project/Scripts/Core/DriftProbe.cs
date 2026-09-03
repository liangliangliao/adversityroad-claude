using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace AdversityRoad.Core
{
    /// <summary>
    /// 漂移自检：用**脚本化的摇杆**把玩家报的三个场景各跑一遍，量身体相对胶囊
    /// 滑了多少，然后在屏幕上给出结论。
    ///
    /// 【为什么要有它】这个 bug 追了十几轮没收敛，很大一部分卡在测量方式上：
    /// 每次都是"你玩一局、导出 CSV、我来读"。可是每一局的推杆方式都不一样——
    /// 转多快、推多满、什么时候松手全不同——两份日志之间根本没有可比性，
    /// 一个改动到底有没有用，永远说不清。而且每验证一次就要占用玩家几分钟。
    ///
    /// 脚本化摇杆把这个变量消掉：同样的输入、同样的时长、同样的五段，
    /// 每次跑出来的数字可以直接跟上一次比。玩家只需要点一下按钮、念四个数。
    ///
    /// 【量什么】身体相对胶囊的**矢量**滑动：
    ///     slide = Δ(髋骨世界坐标) − Δ(胶囊世界坐标)
    /// 日志已经证明胶囊本身是干净的（逐帧全覆盖，单帧位移从没超过 0.13m），
    /// 所以玩家看见的漂移只可能在这一段里。用矢量差而不是两个标量相减：
    /// 方向不同的两段位移，标量相减会互相抵消看不出来。
    ///
    /// 【怎么判】直线跑那一段是基准——正常步态里髋骨本来就左右摇摆，
    /// 所以 slide 不可能是 0。真正有意义的是转圈/起停/急转比直线**高多少**：
    ///   * 明显更高 ⇒ 漂移在身体相对胶囊这一段，继续往动画后处理里查；
    ///   * 差不多  ⇒ 身体没有相对胶囊滑，那玩家看见的就不是身体在动，
    ///               得去查镜头（转向时镜头的角速度/位移正好也在这三段里最大）。
    /// 两条路都比现在"什么都不知道"强。
    /// </summary>
    public class DriftProbe : MonoBehaviour
    {
        /// <summary>自检进行中：VirtualJoystick 让位，摇杆由本组件驱动。</summary>
        public static bool Active { get; private set; }
        public static Vector2 Stick { get; private set; }

        static DriftProbe _inst;

        /// <summary>从设置面板调用：开始一次自检。</summary>
        public static void Run()
        {
            if (_inst == null)
            {
                var go = new GameObject("DriftProbe");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<DriftProbe>();
            }
            _inst.Begin();
        }

        // ===== 五段脚本 =====
        // 前两段是基准（静止、直线），后三段正是玩家点名的三个场景：
        // "主要集中在转圈、刚启动移动、转向时"。
        enum Seg { Idle, Straight, Circle, StartStop, Reverse, Done }

        static readonly float[] SegDur = { 0.8f, 2.5f, 6f, 4f, 4f };
        static readonly string[] SegName = { "静止", "直线跑", "360°转圈", "起停起", "急转向" };

        const float CircleDegPerSec = 120f;   // 转圈：摇杆方向每秒转多少度
        const float StartStopPeriod = 0.9f;   // 起停：一个"推-松"周期多长
        const float ReversePeriod = 0.8f;     // 急转：多久翻一次 180°

        Seg _seg;
        float _segT, _phaseAng;
        Player.PlayerController _pc;
        Combat.HumanoidAnimator _anim;
        Vector3 _prevVis, _prevCap;
        bool _prevOk;
        float _prevYaw;   // 偏航速率没有现成的公开读数，自己按帧差算

        // 每段的统计
        readonly float[] _maxSlide = new float[5];
        readonly float[] _sumSlide = new float[5];
        readonly int[] _frames = new int[5];
        readonly float[] _maxLeak = new float[5];
        readonly float[] _maxYaw = new float[5];

        Text _hud;

        void Begin()
        {
            _pc = ActorRegistry.Player;
            if (_pc == null) { Show("自检失败：找不到玩家角色"); return; }
            _anim = _pc.GetComponent<Combat.HumanoidAnimator>();
            for (int i = 0; i < 5; i++)
            { _maxSlide[i] = _sumSlide[i] = _maxLeak[i] = _maxYaw[i] = 0f; _frames[i] = 0; }
            _seg = Seg.Idle; _segT = 0f; _phaseAng = 0f; _prevOk = false;
            _prevYaw = _pc.transform.eulerAngles.y;
            Active = true; Stick = Vector2.zero;
            MoveLogger.Event("漂移自检 开始");
            // 撞墙会把位移吃掉，量出来的数字就没意义了——所以先提醒站空地。
            Show("漂移自检（请站在空地上，全程别碰摇杆）\n当前：" + SegName[0]);
        }

        void Update()
        {
            if (!Active || _pc == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            int i = (int)_seg;
            if (i >= 5) { Finish(); return; }

            _segT += dt;
            Stick = StickFor(_seg, _segT, dt);
            Sample(i, dt);

            if (_segT >= SegDur[i])
            {
                MoveLogger.Event(string.Format(
                    "漂移自检 {0}：滑动 峰值 {1:F3}m 均值 {2:F4}m  钉髋残留 {3:F3}m  最大偏航 {4:F0}°/s",
                    SegName[i], _maxSlide[i], Mean(i), _maxLeak[i], _maxYaw[i]));
                _seg = (Seg)(i + 1);
                _segT = 0f; _phaseAng = 0f;
                if ((int)_seg < 5)
                    Show("漂移自检（请站在空地上，全程别碰摇杆）\n当前：" + SegName[(int)_seg]);
            }
        }

        Vector2 StickFor(Seg s, float t, float dt)
        {
            switch (s)
            {
                case Seg.Idle: return Vector2.zero;
                case Seg.Straight: return new Vector2(0f, 1f);
                case Seg.Circle:
                    // 满推、方向匀速绕一圈——这就是"360 度转圈推杆"
                    _phaseAng += CircleDegPerSec * dt;
                    float r = _phaseAng * Mathf.Deg2Rad;
                    return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
                case Seg.StartStop:
                    // 推一半时间、松一半时间——"刚启动移动"每个周期复现一次
                    return Mathf.Repeat(t, StartStopPeriod) < StartStopPeriod * 0.5f
                        ? new Vector2(0f, 1f) : Vector2.zero;
                case Seg.Reverse:
                    // 每 ReversePeriod 秒翻 180°——最狠的一档"转向"
                    return Mathf.FloorToInt(t / ReversePeriod) % 2 == 0
                        ? new Vector2(0f, 1f) : new Vector2(0f, -1f);
                default: return Vector2.zero;
            }
        }

        void Sample(int i, float dt)
        {
            Vector3 cap = _pc.transform.position;
            bool ok = _anim != null && _anim.DbgVisValid;
            Vector3 vis = ok ? _anim.DbgVisPos : cap;
            if (_prevOk && ok)
            {
                // 身体相对胶囊滑了多远：两段位移的矢量差，只看水平
                Vector3 rel = (vis - _prevVis) - (cap - _prevCap);
                rel.y = 0f;
                float slide = rel.magnitude;
                if (slide > _maxSlide[i]) _maxSlide[i] = slide;
                _sumSlide[i] += slide; _frames[i]++;
                if (_anim != null && _anim.DbgHipLeak > _maxLeak[i]) _maxLeak[i] = _anim.DbgHipLeak;
                float yawNow = _pc.transform.eulerAngles.y;
                float yaw = Mathf.Abs(Mathf.DeltaAngle(_prevYaw, yawNow)) / dt;
                if (yaw > _maxYaw[i]) _maxYaw[i] = yaw;
            }
            _prevVis = vis; _prevCap = cap; _prevOk = ok;
            _prevYaw = _pc.transform.eulerAngles.y;
        }

        float Mean(int i) => _frames[i] > 0 ? _sumSlide[i] / _frames[i] : 0f;

        void Finish()
        {
            Active = false; Stick = Vector2.zero;
            // 直线跑是基准：正常步态里髋骨本来就摇摆，slide 不可能为 0。
            // 有意义的是后三段比它高多少。
            float base1 = Mathf.Max(0.0005f, _maxSlide[1]);
            var sb = new StringBuilder();
            sb.Append("漂移自检结果（身体相对胶囊的滑动）\n");
            for (int i = 0; i < 5; i++)
                sb.AppendFormat("{0,-9} 峰值 {1:F3}m  均值 {2:F4}m  ×基准 {3:F1}\n",
                    SegName[i], _maxSlide[i], Mean(i), _maxSlide[i] / base1);
            float worst = Mathf.Max(_maxSlide[2], Mathf.Max(_maxSlide[3], _maxSlide[4]));
            float leak = Mathf.Max(_maxLeak[2], Mathf.Max(_maxLeak[3], _maxLeak[4]));
            sb.Append(leak > 0.02f
                ? string.Format("→ 钉髋漏了 {0:F3}m：片段自带位移进了画面\n", leak)
                : "→ 钉髋正常（残留 ≈ 0）\n");
            sb.Append(worst > base1 * 2f
                ? "→ 转向三段明显高于直线：漂移在身体相对胶囊这一段"
                : "→ 转向三段与直线相当：身体没相对胶囊滑，得去查镜头");
            string txt = sb.ToString();
            MoveLogger.Event("漂移自检 结束｜" + txt.Replace("\n", " ｜ "));
            Show(txt);
        }

        // ===== 屏幕输出 =====
        // 结论必须能在手机上直接读出来：这份自检的意义就是把"玩一局导出 CSV"
        // 换成"点一下、念四个数"。
        void Show(string s)
        {
            if (_hud == null)
            {
                var go = new GameObject("DriftProbeCanvas");
                go.transform.SetParent(transform, false);
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 6000;      // 压在 PerfHud 和设置面板之上
                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);

                var t = new GameObject("Text");
                t.transform.SetParent(go.transform, false);
                _hud = t.AddComponent<Text>();
                _hud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _hud.fontSize = 30;
                _hud.alignment = TextAnchor.UpperLeft;
                _hud.color = new Color(1f, 0.85f, 0.3f);
                _hud.raycastTarget = false;       // 绝不吃掉触摸事件
                _hud.horizontalOverflow = HorizontalWrapMode.Overflow;
                _hud.verticalOverflow = VerticalWrapMode.Overflow;
                var rt = _hud.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(40f, -300f);
                rt.sizeDelta = new Vector2(1100f, 300f);
            }
            _hud.text = s;
        }
    }
}
