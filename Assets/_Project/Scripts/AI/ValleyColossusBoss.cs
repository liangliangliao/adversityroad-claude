using System.Collections;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 低谷巨像（低谷与生存线 Boss）：由无力感、内疚重石和生存恐慌凝成的庞然大物。
    /// - 无力威压：大范围预警冲击——命中大量流失意志（"撑不住就松手，多轻松"）；
    /// - 内疚重石：往玩家脚下砸落巨石（预警红圈，物理重击）；
    /// - 血量过半召唤医药债影；
    /// - 破防机制（方案核心）：走廊两侧的**求助电话亭**——拨通求助电话，
    ///   巨像的"无力感"当场松动，进入大破绽。承认需要帮助，不等于失去尊严。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class ValleyColossusBoss : MonoBehaviour
    {
        public float pressureInterval = 10f;
        public float pressureRadius = 7f;
        public float rockInterval = 7f;
        public float rockRadius = 3f;
        public float summonInterval = 16f;

        EnemyController _ec;
        Transform _player;
        PlayerCombatController _playerCombat;
        float _pressureCd = 6f, _rockCd = 4f, _summonCd = 10f, _hintAt = -99f;
        bool _busy;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCombat = p.GetComponent<PlayerCombatController>(); }
            GameEvents.RaiseSubtitle("『低谷巨像』矗立在回廊尽头——场边的求助电话亭是破局关键：求助不是羞耻。");
        }

        void Update()
        {
            if (_ec == null || _player == null || _busy) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 26f) return;

            if (Time.time - _hintAt > 25f && dist < 18f)
            {
                _hintAt = Time.time;
                GameEvents.RaiseSubtitle("它太重了，硬扛很吃力——去拨场边发光的求助电话，让它的「无力感」松动。");
            }

            float dt = Time.deltaTime;
            _pressureCd -= dt; _rockCd -= dt; _summonCd -= dt;

            if (_pressureCd <= 0 && dist < pressureRadius + 4f)
            {
                _pressureCd = pressureInterval;
                StartCoroutine(PowerlessPressure());
                return;
            }
            if (_rockCd <= 0 && dist < 16f)
            {
                _rockCd = rockInterval;
                StartCoroutine(GuiltRock());
                return;
            }
            if (_summonCd <= 0 && _ec.HpRatio < 0.55f)
            {
                _summonCd = summonInterval;
                if (EnemySpawnHook.AliveCount("enemy_med_debt_shadow") < 2)
                {
                    Vector3 pos = transform.position +
                        Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * 5f;
                    if (EnemySpawnHook.SpawnNear(EnemyType.MedDebtShadow, EnemyTier.Novice, pos) != null &&
                        _ec.dialogue != null)
                        _ec.dialogue.Show("账单，还在一张张打印。", 2.4f);
                }
            }
        }

        /// <summary>无力威压：预警后的大范围意志冲击。</summary>
        IEnumerator PowerlessPressure()
        {
            _busy = true;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "PressureWarnRing";
            ring.transform.position = new Vector3(transform.position.x,
                transform.position.y - 0.9f, transform.position.z);
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.4f, 0.42f, 0.55f), 0.4f);
            if (_ec.dialogue != null) _ec.dialogue.Show("撑不住就松手，多轻松……", 1.8f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.6f);

            float t = 0f;
            while (t < 1.1f)
            {
                t += Time.deltaTime;
                float k = 0.3f + 0.7f * (t / 1.1f);
                ring.transform.localScale = new Vector3(pressureRadius * 2f * k, 0.03f, pressureRadius * 2f * k);
                yield return null;
            }
            Destroy(ring);

            if (_ec.State != EnemyState.Dead && _player != null)
            {
                CombatFeedback.Shake(0.5f);
                CombatFeedback.RecipeBurst(transform.position, new Color(0.4f, 0.42f, 0.55f));
                GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.85f);
                var pc = _player.GetComponent<PlayerController>();
                if (Vector3.Distance(_player.position, transform.position) <= pressureRadius + 0.5f &&
                    _playerCombat != null && pc != null && !pc.IsInvincible)
                    _playerCombat.TakeHit(new DamageInfo
                    {
                        mentalDamage = _ec.profile.mentalDamage * 0.85f,
                        mentalAxis = Personalization.WeaknessAxis.WillpowerCollapse,
                        isMentalOnly = true,
                        attackerId = _ec.profile.enemyId
                    });
            }
            _busy = false;
        }

        /// <summary>内疚重石：玩家脚下预警红圈 → 巨石砸落（物理重击）。</summary>
        IEnumerator GuiltRock()
        {
            _busy = true;
            Vector3 spot = _player.position;
            spot.y = transform.position.y - 0.9f;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "RockWarnRing";
            ring.transform.position = spot + Vector3.up * 0.05f;
            var mat = _ec.baseMaterial != null ? new Material(_ec.baseMaterial)
                : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            var rc = new Color(0.85f, 0.25f, 0.2f);
            mat.color = rc;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", rc);
            ring.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (_ec.dialogue != null) _ec.dialogue.Show("这块内疚，也归你背。", 1.4f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.5f);

            float t = 0f;
            while (t < 0.9f)
            {
                t += Time.deltaTime;
                float pulse = 0.6f + Mathf.PingPong(t * 3f, 0.6f);
                ring.transform.localScale = new Vector3(rockRadius * 2f * pulse, 0.03f, rockRadius * 2f * pulse);
                yield return null;
            }
            Destroy(ring);

            if (_ec.State != EnemyState.Dead && _player != null)
            {
                // 落石演出：一块巨石短暂出现又碎裂
                var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(rock.GetComponent<Collider>());
                rock.transform.position = spot + Vector3.up * 1f;
                rock.transform.localScale = Vector3.one * 1.8f;
                rock.transform.rotation = Random.rotation;
                var rm = new Material(mat);
                rm.color = new Color(0.35f, 0.33f, 0.3f);
                if (rm.HasProperty("_BaseColor")) rm.SetColor("_BaseColor", rm.color);
                rock.GetComponent<MeshRenderer>().sharedMaterial = rm;
                Destroy(rock, 1.2f);

                CombatFeedback.Shake(0.7f);
                CombatFeedback.Debris(spot + Vector3.up, new Color(0.4f, 0.38f, 0.34f), 8);
                GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.95f);
                var pc = _player.GetComponent<PlayerController>();
                if (Vector3.Distance(_player.position, spot) <= rockRadius + 0.5f &&
                    _playerCombat != null && pc != null && !pc.IsInvincible)
                    _playerCombat.TakeHit(new DamageInfo
                    {
                        physicalDamage = _ec.profile.physicalDamage * 1.4f,
                        mentalDamage = _ec.profile.mentalDamage * 0.4f,
                        mentalAxis = Personalization.WeaknessAxis.WillpowerCollapse,
                        knockback = 4f,
                        sourcePosition = spot,
                        attackerId = _ec.profile.enemyId
                    });
            }
            _busy = false;
        }
    }
}
