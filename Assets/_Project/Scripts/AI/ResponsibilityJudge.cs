using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 全责法官（责任转嫁法院 Boss 专属行为）：周期性把「责任球」抛向玩家。
    /// 大多是虚假责任（红）——玩家举盾挡回即完成「责任归还」削其韧性；偶有真实责任（绿）
    /// ——玩家应接下而非推开。与 EnemyController 的近战/心理攻击叠加，形成"判断真假责任"的战斗核心。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class ResponsibilityJudge : MonoBehaviour
    {
        public float throwInterval = 3.6f;
        public float detectRange = 22f;
        [Range(0f, 1f)] public float falseChance = 0.72f;

        EnemyController _ec;
        Transform _player;
        float _cd;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            _cd = 2.5f;
            var p = FindObjectOfType<PlayerController>();
            if (p != null) _player = p.transform;
        }

        void Update()
        {
            if (_ec == null || _player == null) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;

            _cd -= Time.deltaTime;
            if (_cd > 0) return;

            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > detectRange) return;

            _cd = throwInterval;
            ThrowBall();
        }

        void ThrowBall()
        {
            bool isFalse = Random.value < falseChance;
            Vector3 origin = transform.position + Vector3.up * 1.4f + transform.forward * 0.6f;
            ResponsibilityBall.Spawn(transform, origin, _player, isFalse, _ec.baseMaterial);

            if (_ec.dialogue != null)
                _ec.dialogue.Show(isFalse ? "这也是你的责任。" : "这一件，确实与你有关。", 2.4f);
            GameEvents.RaiseSubtitle(isFalse
                ? "『全责法官』抛来一份责任——不属于你的，用边界盾挡回去。"
                : "『全责法官』抛来一份真实责任——属于你的，接下它。");
            GameAudio.Play(GameAudio.Sfx.Alert, 0.4f);
        }
    }
}
