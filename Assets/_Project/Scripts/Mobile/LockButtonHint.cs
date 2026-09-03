using UnityEngine;
using UnityEngine.UI;

namespace AdversityRoad.Mobile
{
    /// <summary>
    /// 「锁」键的自提示：**确实有敌人在画面外**的时候，让这颗键自己亮起来。
    ///
    /// 【为什么答案是锁定，而不是让镜头自己去追敌人】
    /// 移动是镜头相对的，恒等式 H = C + θ：玩家按着摇杆时，镜头每自动转 1°，
    /// 角色的世界朝向就被带转 1°。所以"敌人跑出画面就把镜头转回去"这条路，
    /// 代价一定是玩家被迫绕着敌人画弧——他报过的"像被磁铁吸住、逃不掉"。
    /// 这个坑在本项目里被独立验证否决过两次（见 PlayerController.CameraRelative
    /// 里的记述），不能再走。
    ///
    /// 成熟动作游戏（魂系/只狼/怪猎/战神/鬼泣）解决"看不见敌人"从来不是靠
    /// 无锁定时自动转镜头，而是三件事：
    ///   ① 锁定——玩家显式表达"我要打这个"，此后镜头才有权替他取景，
    ///      而且移动语义同时变成绕步，转镜头不再是"意外被带偏"；
    ///   ② 屏幕边缘的方位记号（见 UI.ThreatIndicator 的在场标）；
    ///   ③ 交战时加宽视野（见 ThirdPersonCamera 的 CombatFovBoost）。
    /// ②③ 已经做了，剩下的问题是①的**可发现性**：锁定键只有 84 像素、
    /// 缩在右边缘，玩家在最需要它的那一刻未必想得起来。
    /// 所以在"有敌人在追你、而且不在画面里、你又没锁定"这个精确时刻，
    /// 让它呼吸式地亮一下——不替玩家做决定，只是把入口指出来。
    /// </summary>
    public class LockButtonHint : MonoBehaviour
    {
        const float Poll = 0.2f;
        const float Range = 32f;

        Image _img;
        Color _base;
        Transform _player;
        Player.LockOnSystem _lock;
        Camera _cam;
        float _next;
        bool _want;
        float _blend;

        void Awake()
        {
            _img = GetComponent<Image>();
            if (_img != null) _base = _img.color;
        }

        void Update()
        {
            if (_img == null) return;
            float dt = Time.unscaledDeltaTime;

            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + Poll;
                _want = Evaluate();
            }
            _blend = Mathf.MoveTowards(_blend, _want ? 1f : 0f, dt / 0.25f);

            if (_blend <= 0.001f)
            {
                _img.color = _base;
                transform.localScale = Vector3.one;
                return;
            }
            // 呼吸：亮度与大小同步起伏，余光里也看得见，又不至于像报警
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.2f);
            var hot = new Color(1f, 0.62f, 0.30f, 0.95f);
            _img.color = Color.Lerp(_base, hot, _blend * (0.55f + 0.45f * pulse));
            transform.localScale = Vector3.one * (1f + _blend * (0.06f + 0.06f * pulse));
        }

        bool Evaluate()
        {
            if (_player == null)
            {
                var pc = Core.ActorRegistry.Player;
                if (pc == null) return false;
                _player = pc.transform;
                _lock = pc.GetComponent<Player.LockOnSystem>();
            }
            if (_lock != null && _lock.CurrentTarget != null) return false;   // 已经锁上了
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return false;

            foreach (var e in Core.ActorRegistry.Enemies)
            {
                if (e == null || e.State == AI.EnemyState.Dead) continue;
                if (e.State != AI.EnemyState.Chase && e.State != AI.EnemyState.Attack &&
                    e.State != AI.EnemyState.MentalAttack) continue;
                if (Vector3.Distance(e.transform.position, _player.position) > Range) continue;
                Vector3 sp = _cam.WorldToViewportPoint(e.transform.position + Vector3.up * 1.2f);
                bool onScreen = sp.z > 0f && sp.x > 0.06f && sp.x < 0.94f &&
                                sp.y > 0.06f && sp.y < 0.94f;
                if (!onScreen) return true;
            }
            return false;
        }
    }
}
