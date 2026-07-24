using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 敌方远程攻击的「凝念能量」预算（对齐大型动作游戏的敌人资源约束）：
    /// 敌人不能不间断、无限制、零成本地泼洒远程弹幕——
    /// · 每发心念弹消耗凝念能量（默认 100 能量 / 每发 18：一轮 4—5 发就见底）；
    /// · 打空后必须停火回气（停火 1.2s 后才开始回复，回满约需数秒），
    ///   期间头顶弹出「凝念枯竭」提示——这是玩家反打的窗口；
    /// · 所有发射统一走 TryFire 门禁，同时执行距离硬上限（默认 14m）：
    ///   离得太远直接不发射，只能靠近再打。
    /// 组件在首次发射时自动挂到敌人身上，无需逐个配置。
    /// </summary>
    public class EnemyRangedEnergy : MonoBehaviour
    {
        public float maxEnergy = 100f;
        public float regenPerSec = 9f;
        public float regenDelay = 1.2f;   // 停火多久后才开始回气

        float _energy;
        float _lastFireT = -999f;
        bool _depletedShown;

        void Awake() => _energy = maxEnergy;

        void Update()
        {
            if (_energy >= maxEnergy) return;
            if (Time.time - _lastFireT < regenDelay) return;
            _energy = Mathf.Min(maxEnergy, _energy + regenPerSec * Time.deltaTime);
            if (_depletedShown && _energy > maxEnergy * 0.5f) _depletedShown = false;
        }

        bool TrySpend(float cost)
        {
            if (_energy < cost)
            {
                if (!_depletedShown)
                {
                    _depletedShown = true;
                    CombatFeedback.DamageNumber(transform.position + Vector3.up * 1.8f,
                        "凝念枯竭", new Color(0.65f, 0.65f, 0.75f), 1.1f);
                }
                return false;
            }
            _energy -= cost;
            _lastFireT = Time.time;
            return true;
        }

        /// <summary>
        /// 发射门禁：距离超限或凝念能量不足返回 false（调用方跳过这一发/中断整轮弹幕）。
        /// owner 为敌人身上的任意组件；组件不存在时自动挂载。
        /// </summary>
        public static bool TryFire(Component owner, Vector3 playerPos,
            float cost = 18f, float maxRange = 14f)
        {
            if (owner == null) return false;
            if (Vector3.Distance(owner.transform.position, playerPos) > maxRange) return false;
            var e = owner.GetComponent<EnemyRangedEnergy>();
            if (e == null) e = owner.gameObject.AddComponent<EnemyRangedEnergy>();
            return e.TrySpend(cost);
        }
    }
}
