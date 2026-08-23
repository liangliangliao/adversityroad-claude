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
            rt.sizeDelta = new Vector2(560f, 40f);
        }

        void Update()
        {
            if (_text == null) return;
            if (_text.enabled != Enabled) _text.enabled = Enabled;
            if (!Enabled) return;

            // 用不缩放的真实帧时：顿帧/时缓会把 Time.deltaTime 改掉，
            // 而这里要量的是**设备每帧真的花了多久**。
            float dt = Time.unscaledDeltaTime;
            _accum += dt;
            _frames++;
            if (dt > _worst) _worst = dt;

            if (Time.unscaledTime < _nextReport) return;
            _nextReport = Time.unscaledTime + 0.5f;

            float fps = _frames > 0 ? _frames / Mathf.Max(0.0001f, _accum) : 0f;
            float worstMs = _worst * 1000f;
            // 最坏那一帧对应的冲刺位移：直接告诉你"这一帧人挪了多远"，
            // 与胶囊半径（0.4m 上下）一比就知道会不会穿墙。
            float worstStep = _worst * 5.2f;
            int chars = CountCharacters();
            _text.text = string.Format(
                "FPS {0:F0} | 最长帧 {1:F0}ms（单帧位移 {2:F2}m）| 角色 {3}",
                fps, worstMs, worstStep, chars);
            // 最坏帧偏红：一眼能看出这半秒里有没有大顿
            _text.color = worstMs > 60f ? new Color(1f, 0.45f, 0.35f)
                                        : new Color(1f, 0.95f, 0.4f);
            _accum = 0f; _frames = 0; _worst = 0f;
        }

        float _nextCount;
        int _charCache;

        int CountCharacters()
        {
            // 每 2 秒数一次就够——FindObjects 本身不便宜，别让诊断自己变成卡顿源
            if (Time.unscaledTime < _nextCount) return _charCache;
            _nextCount = Time.unscaledTime + 2f;
            _charCache = Object.FindObjectsByType<Combat.HumanoidAnimator>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            return _charCache;
        }
    }
}
