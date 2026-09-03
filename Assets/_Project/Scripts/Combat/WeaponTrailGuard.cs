using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 刀光拖尾的自管开关：没人驱动就自己关掉并清点。
    ///
    /// 【为什么要它自管，而不是再补一处判断】
    /// 白色剑痕这个问题我已经修过两轮，每次都是在"驱动方"补一句 Clear()，
    /// 每次都还有别的路径漏掉。真正的结构问题是：**拖尾的生命周期不归驱动方管**。
    ///   · 收刀时 PlayerAppearance 把 poser.weaponTrail 置空——引用一断，
    ///     HumanoidAnimator 的 `if (weaponTrail == null) return;` 立刻放手，
    ///     而拖尾物体还挂在武器上，emitting 停在开着的状态，从此没人再管它，
    ///     它就跟着武器到处画白带子（玩家截图里糊在躯干和脸上的那条）。
    ///   · 拔刀/换手时武器换父节点，刀尖在世界里瞬移，一帧就拉出一条
    ///     横跨半个身子的长条——加色材质，读起来就是"人被洗白了"。
    ///   · timeScale 打到 0（言语攻防面板/暂停/顿帧）时拖尾按缩放时间老化，
    ///     等于永不消失。
    /// 这三条的共同点是：出问题的时候，驱动方要么已经放手，要么根本不知情。
    ///
    /// 所以把兜底放在拖尾**自己**身上：谁在用它，谁就每帧盖一次时间戳；
    /// 超过 KeepAlive 没人盖，它就判定自己是孤儿，关掉并清点。
    /// 再加一条瞬移检测：刀尖一帧挪了半米以上，那不是挥砍，是换了父节点。
    /// 以后不管谁、从哪条路径接管或放手，都不会再留下白带子。
    /// </summary>
    [RequireComponent(typeof(TrailRenderer))]
    public class WeaponTrailGuard : MonoBehaviour
    {
        /// <summary>多久没人驱动就判定为孤儿（秒，走非缩放时间——顿帧时也要能救）。</summary>
        const float KeepAlive = 0.25f;
        /// <summary>刀尖单帧位移超过它 = 换父节点造成的瞬移，不是挥砍。</summary>
        const float TeleportStep = 0.5f;

        TrailRenderer _trail;
        float _lastDriven;
        Vector3 _prev;
        bool _hasPrev;

        void Awake() => _trail = GetComponent<TrailRenderer>();

        /// <summary>驱动方每帧调用一次，表示"这条拖尾还有人管"。</summary>
        public void KeepDriven() => _lastDriven = Time.unscaledTime;

        void LateUpdate()
        {
            if (_trail == null) return;

            // 瞬移：换父节点/换手/拔刀收刀。清掉这一帧本来会连出去的那条长带子。
            Vector3 now = transform.position;
            if (_hasPrev && (now - _prev).sqrMagnitude > TeleportStep * TeleportStep)
                _trail.Clear();
            _prev = now; _hasPrev = true;

            // 孤儿：没人驱动了就自己收摊
            if (Time.unscaledTime - _lastDriven > KeepAlive)
            {
                if (_trail.emitting) _trail.emitting = false;
                if (_trail.positionCount > 0) _trail.Clear();
            }
        }
    }
}
