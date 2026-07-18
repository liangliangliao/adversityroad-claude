using System.Collections;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 旧我（旧事回声馆终局 Boss）——不是需要杀死的敌人，而是过去形成的保护模式。四阶段：
    /// 一【旧话复读】：反复发射旧标签心念弹（"你以前就是这样"）；
    /// 二【身份冻结】：释放冻结波，命中则大幅减速+行动力流失——用「五分钟火种」解冻；
    /// 三【失败召回】：召唤前面章节敌人的幻影（小题大做鬼/咳声刺客/明日泥怪）；
    /// 四【整合选择】：血量残存时停手站定、无法再被攻击——场地中央浮现整合圆环，
    ///    站入完成「旧我整合式」：旧我化为影子护卫，站到你身后。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class OldSelfBoss : MonoBehaviour
    {
        public float volleyInterval = 6f;
        public float freezeInterval = 12f;
        public float summonInterval = 15f;
        public float freezeRadius = 6f;

        EnemyController _ec;
        Transform _player;
        PlayerCombatController _playerCombat;
        float _volleyCd = 3f, _freezeCd = 9f, _summonCd = 10f;
        bool _busy;
        int _phase = 1;
        bool _integrationStarted;

        static readonly string[] OldLines =
        {
            "你以前就是这样。", "你改不了。", "你失败过太多次。", "过去才是真正的你。",
        };

        static readonly EnemyType[] RecallTypes =
        {
            EnemyType.OverreactGhost, EnemyType.CoughAssassin, EnemyType.TomorrowMud,
        };

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCombat = p.GetComponent<PlayerCombatController>(); }
            // 血线保护：旧我不能被打死——终局必须走「整合」而非击杀
            _ec.minHpFloor = 0.12f;
            GameEvents.RaiseSubtitle("『旧我』站在镜面平台中央——它有你的轮廓。它不是怪物，是曾经保护过你的旧模式。");
        }

        void Update()
        {
            if (_ec == null || _ec.State == EnemyState.Dead) return;

            UpdatePhase();
            if (_phase >= 4 || _integrationStarted) return;

            if (_player == null || _busy || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 26f) return;

            float dt = Time.deltaTime;
            _volleyCd -= dt; _freezeCd -= dt; _summonCd -= dt;

            if (_phase >= 3 && _summonCd <= 0)
            {
                _summonCd = summonInterval;
                FailureRecall();
                return;
            }
            if (_phase >= 2 && _freezeCd <= 0 && dist < freezeRadius + 3f)
            {
                _freezeCd = freezeInterval;
                StartCoroutine(IdentityFreeze());
                return;
            }
            if (_volleyCd <= 0 && dist < 22f)
            {
                _volleyCd = volleyInterval * (_phase >= 3 ? 0.75f : 1f);
                StartCoroutine(OldVoiceVolley());
            }
        }

        void UpdatePhase()
        {
            float hp = _ec.HpRatio;
            int want = hp > 0.75f ? 1 : hp > 0.5f ? 2 : hp > 0.25f ? 3 : 4;
            if (want <= _phase) return;
            _phase = want;
            switch (_phase)
            {
                case 2:
                    GameEvents.RaiseSubtitle("【第二阶段·身份冻结】旧我要把你钉在过去——被冻住就点「五分钟火种」，行动打破冻结！");
                    break;
                case 3:
                    GameEvents.RaiseSubtitle("【第三阶段·失败召回】旧我唤回一路走来的旧敌幻影——用学过的技能各个击破！");
                    break;
                case 4:
                    BeginIntegrationPhase();
                    break;
            }
        }

        /// <summary>旧话复读：三连发旧标签心念弹（旧事回声轴）。</summary>
        IEnumerator OldVoiceVolley()
        {
            _busy = true;
            if (_ec.dialogue != null)
                _ec.dialogue.Show(OldLines[Random.Range(0, OldLines.Length)], 2.4f);
            for (int i = 0; i < 3 && _ec.State != EnemyState.Dead && !_integrationStarted; i++)
            {
                if (_player == null) break;
                Vector3 origin = transform.position + Vector3.up * 1.5f + transform.forward * 0.6f;
                Vector3 target = _player.position + Vector3.up * 1.0f;
                Projectile.Launch(transform, origin, target - origin, new DamageInfo
                {
                    physicalDamage = _ec.profile.physicalDamage * 0.3f,
                    mentalDamage = _ec.profile.mentalDamage * 0.55f,
                    mentalAxis = Personalization.WeaknessAxis.FailureFear,
                    knockback = 0.6f,
                    attackerId = _ec.profile.enemyId
                }, 12f, new Color(0.45f, 0.42f, 0.6f), _ec.baseMaterial);
                GameAudio.Play(GameAudio.Sfx.Swing, 0.4f);
                yield return new WaitForSeconds(0.3f);
            }
            _busy = false;
        }

        /// <summary>身份冻结：预警后的大范围冻结波——命中即挂上 FrozenDebuff（五分钟火种可解）。</summary>
        IEnumerator IdentityFreeze()
        {
            _busy = true;
            if (_ec.dialogue != null) _ec.dialogue.Show("别动了——留在过去，多安全。", 1.8f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.6f);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "FreezeWarnRing";
            ring.transform.position = new Vector3(transform.position.x,
                transform.position.y - 0.85f, transform.position.z);
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.55f, 0.75f, 1f), 0.4f);

            float t = 0f;
            while (t < 1.0f)
            {
                t += Time.deltaTime;
                float k = 0.3f + 0.7f * t;
                ring.transform.localScale = new Vector3(freezeRadius * 2f * k, 0.03f, freezeRadius * 2f * k);
                yield return null;
            }
            Destroy(ring);

            if (_ec.State != EnemyState.Dead && !_integrationStarted && _player != null)
            {
                CombatFeedback.Shake(0.5f);
                GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.8f);
                var pc = _player.GetComponent<PlayerController>();
                if (Vector3.Distance(_player.position, transform.position) <= freezeRadius + 0.5f &&
                    pc != null && !pc.IsInvincible)
                {
                    var frozen = pc.GetComponent<FrozenDebuff>();
                    if (frozen == null) frozen = pc.gameObject.AddComponent<FrozenDebuff>();
                    frozen.Arm(4.5f);
                    if (_playerCombat != null)
                        _playerCombat.TakeHit(new DamageInfo
                        {
                            mentalDamage = _ec.profile.mentalDamage * 0.5f,
                            mentalAxis = Personalization.WeaknessAxis.Procrastination,
                            isMentalOnly = true,
                            attackerId = _ec.profile.enemyId
                        });
                    GameEvents.RaiseSubtitle("身份被冻结——脚像灌了铅。按「五分钟火种」（键4/触屏『火』）解冻！");
                }
            }
            _busy = false;
        }

        /// <summary>失败召回：召唤一路走来的旧敌幻影（场上限 3 只，见习级）。</summary>
        void FailureRecall()
        {
            int alive = 0;
            foreach (var t in RecallTypes)
                alive += EnemySpawnHook.AliveCount(EnemyCatalog.BaseId(t));
            if (alive >= 3) return;

            var type = RecallTypes[Random.Range(0, RecallTypes.Length)];
            Vector3 pos = transform.position +
                Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * 6f;
            var minion = EnemySpawnHook.SpawnNear(type, EnemyTier.Novice, pos);
            if (minion == null) return;
            CombatFeedback.Debris(pos, new Color(0.4f, 0.38f, 0.5f), 6);
            if (_ec.dialogue != null)
                _ec.dialogue.Show("还记得这个吗？你没有赢过它。", 2.4f);
            GameEvents.RaiseSubtitle("失败召回——旧敌幻影【" + EnemyCatalog.TypeLabel(type) +
                "】重现。这一次，你有了当时没有的技能。");
        }

        /// <summary>第四阶段·整合选择：旧我停手站定、不再能被攻击；中央浮现整合圆环。</summary>
        void BeginIntegrationPhase()
        {
            _integrationStarted = true;
            _ec.pacified = true;
            _ec.externalDamageMult = 0f;
            StopAllCoroutines();
            _busy = false;

            if (_ec.dialogue != null) _ec.dialogue.Show("……我只是，不想你再受伤。", 4f);
            GameEvents.RaiseSubtitle("【第四阶段·整合选择】旧我停下了。不必杀死它——走进光环，完成「旧我整合式」。");

            // 整合圆环生成在旧我与玩家之间
            Vector3 center = _player != null
                ? Vector3.Lerp(transform.position, _player.position, 0.5f)
                : transform.position + transform.forward * 3f;
            center.y = transform.position.y - 0.9f;

            var circle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(circle.GetComponent<Collider>());
            circle.name = "IntegrationCircleVisual";
            circle.transform.position = center + Vector3.up * 0.04f;
            circle.transform.localScale = new Vector3(6f, 0.02f, 6f);
            circle.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.85f, 0.9f, 1f), 0.4f);

            var zone = new GameObject("IntegrationCircle");
            zone.transform.position = center;
            var ic = zone.AddComponent<IntegrationCircle>();
            ic.onIntegrated = () => StartCoroutine(Integrate(circle, zone));
        }

        /// <summary>旧我整合式：旧我化为影子护卫——终局演出与主线完结。</summary>
        IEnumerator Integrate(GameObject circleVisual, GameObject zone)
        {
            GameEvents.RaiseSubtitle("「你曾经保护过我，但现在我要继续往前。」");
            CombatFeedback.SlowMo(0.4f, 0.5f);
            CombatFeedback.RecipeBurst(transform.position, new Color(0.85f, 0.9f, 1f));
            GameAudio.Play(GameAudio.Sfx.Parry, 1f);

            var p = FindObjectOfType<PlayerController>();
            if (p != null)
            {
                p.Stats.ReduceRumination(999f);
                p.Stats.RestoreMental(999f);
            }

            yield return new WaitForSeconds(1.6f);

            // 旧我消散为光点，从原地升起影子护卫
            CombatFeedback.Debris(transform.position + Vector3.up, new Color(0.7f, 0.75f, 0.95f), 12);
            ShadowGuardian.Spawn(transform.position);
            PlayerPrefs.SetInt("adversity_shadow_guardian", 1);
            PlayerPrefs.Save();

            GameEvents.RaiseSubtitle("旧我化为影子护卫，站到了你身后。——过去发生过，但不是你的全部。");

            if (circleVisual != null) Destroy(circleVisual);
            if (zone != null) Destroy(zone);

            // 推进终局章节（整合不是击杀，但同样是"战胜"）
            GameEvents.RaiseEnemyKilled(_ec.profile.enemyId);

            // 旧我本体安静离场（无死亡演出——它没有死，它被更新了）
            if (_ec.statusBar != null) _ec.statusBar.Hide();
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Destroy(gameObject, 0.4f);
        }
    }
}
