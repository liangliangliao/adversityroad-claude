using System.Collections;
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
        public float slamInterval = 8f;
        public float slamRadius = 3.6f;

        EnemyController _ec;
        Transform _player;
        PlayerCombatController _playerCombat;
        Material _mat;
        float _cd, _slamCd;
        bool _slamming;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            _cd = 2.5f;
            _slamCd = 6f;
            var p = FindObjectOfType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCombat = p.GetComponent<PlayerCombatController>(); }
        }

        void Update()
        {
            if (_ec == null || _player == null) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;

            float dist = Vector3.Distance(transform.position, _player.position);
            _slamCd -= Time.deltaTime;
            // 法槌落判：近身时周期性砸下审判重锤（Boss 专属阶段攻击源）
            if (!_slamming && _slamCd <= 0 && dist < 9f)
            {
                _slamCd = slamInterval;
                StartCoroutine(GavelSlam());
                return;
            }

            _cd -= Time.deltaTime;
            if (_cd > 0 || _slamming) return;
            if (dist > detectRange) return;

            _cd = throwInterval;
            ThrowBall();
        }

        /// <summary>法槌落判：在玩家脚下预警红圈 → 落锤 AoE。玩家可翻滚/走出圈外躲开。</summary>
        IEnumerator GavelSlam()
        {
            _slamming = true;
            Vector3 spot = _player.position;
            spot.y = transform.position.y - 0.9f;

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.name = "GavelWarn";
            ring.transform.position = spot + Vector3.up * 0.05f;
            ring.transform.localScale = new Vector3(slamRadius * 2f, 0.03f, slamRadius * 2f);
            if (_mat == null)
                _mat = _ec.baseMaterial != null ? new Material(_ec.baseMaterial)
                    : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            var rm = ring.GetComponent<MeshRenderer>();
            var rc = new Color(0.9f, 0.2f, 0.15f, 1f);
            var m = new Material(_mat); m.color = rc;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", rc);
            rm.sharedMaterial = m;

            if (_ec.dialogue != null) _ec.dialogue.Show("这也是你的责任——落槌！", 1.4f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.5f);

            float t = 0f;
            while (t < 0.9f)
            {
                t += Time.deltaTime;
                float pulse = 0.6f + Mathf.PingPong(t * 3f, 0.6f);
                ring.transform.localScale = new Vector3(slamRadius * 2f * pulse, 0.03f, slamRadius * 2f * pulse);
                yield return null;
            }
            Destroy(ring);

            if (_ec.State != EnemyState.Dead && _player != null)
            {
                CombatFeedback.Shake(0.7f);
                CombatFeedback.RecipeBurst(spot, new Color(0.9f, 0.3f, 0.2f));
                GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.9f);
                bool hit = Vector3.Distance(_player.position, spot) <= slamRadius + 0.5f;
                if (hit && _playerCombat != null && !_player.GetComponent<PlayerController>().IsInvincible)
                    _playerCombat.TakeHit(new DamageInfo
                    {
                        physicalDamage = _ec.profile.physicalDamage * 1.4f,
                        mentalDamage = _ec.profile.mentalDamage * 0.4f,
                        mentalAxis = Personalization.WeaknessAxis.BoundaryConflict,
                        knockback = 4f,
                        sourcePosition = spot,
                        attackerId = _ec.profile.enemyId
                    });
            }
            _slamming = false;
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
