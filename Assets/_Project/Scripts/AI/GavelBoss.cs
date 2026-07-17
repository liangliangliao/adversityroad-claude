using System.Collections;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 自我否定法槌（小题大做审判庭 Boss）三阶段：
    /// 一阶段【标签砸击】——发射"太敏感/小题大做"标签弹幕；
    /// 二阶段【审判冲击波】——以自身为中心的大范围冲击（预警圈内翻滚/跑出可躲）；
    /// 三阶段【自我否定重锤】——追着玩家脚下落锤，更快更狠。
    /// 事实之刃（玩家武器攻击）击碎场中浮动标签可回补自尊——用事实回应否定。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class GavelBoss : MonoBehaviour
    {
        public float volleyInterval = 6.5f;
        public float shockInterval = 10f;
        public float slamInterval = 7.5f;
        public float shockRadius = 6.5f;
        public float slamRadius = 3.4f;

        EnemyController _ec;
        Transform _player;
        PlayerCombatController _playerCombat;
        float _volleyCd = 3f, _shockCd = 8f, _slamCd = 5f, _summonCd = 10f;
        bool _busy;
        int _announcedPhase;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCombat = p.GetComponent<PlayerCombatController>(); }
        }

        int Phase => _ec.HpRatio > 0.66f ? 1 : _ec.HpRatio > 0.33f ? 2 : 3;

        void Update()
        {
            if (_ec == null || _player == null || _busy) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 24f) return;

            AnnouncePhase();

            float dt = Time.deltaTime;
            _volleyCd -= dt; _shockCd -= dt; _slamCd -= dt; _summonCd -= dt;

            // 二阶段起：围攻审判——从旁观席召唤旁观嘲笑者助阵（场上限 2）
            if (Phase >= 2 && _summonCd <= 0)
            {
                _summonCd = 16f;
                SummonBystanders();
            }

            // 三阶段：自我否定重锤优先（追身落锤）
            if (Phase >= 3 && _slamCd <= 0 && dist < 12f)
            {
                _slamCd = slamInterval;
                StartCoroutine(DenialSlam());
                return;
            }
            // 二阶段起：审判冲击波（近身逼退）
            if (Phase >= 2 && _shockCd <= 0 && dist < shockRadius + 2f)
            {
                _shockCd = shockInterval;
                StartCoroutine(JudgmentShockwave());
                return;
            }
            // 全程：标签弹幕
            if (_volleyCd <= 0 && dist < 20f)
            {
                _volleyCd = volleyInterval * (Phase >= 3 ? 0.7f : 1f);
                StartCoroutine(LabelVolley());
            }
        }

        void AnnouncePhase()
        {
            int p = Phase;
            if (p == _announcedPhase) return;
            _announcedPhase = p;
            switch (p)
            {
                case 2:
                    GameEvents.RaiseSubtitle("『自我否定法槌』举起法槌——审判冲击波来了，别站在它脚下！");
                    break;
                case 3:
                    GameEvents.RaiseSubtitle("『自我否定法槌』疯狂自我否定——重锤追身落下，看准红圈翻滚！");
                    break;
            }
        }

        /// <summary>围攻审判：旁观席上的嘲笑声落成实体——召唤旁观嘲笑者从侧面袭来。</summary>
        void SummonBystanders()
        {
            if (EnemySpawnHook.AliveCount("enemy_mocking_bystander") >= 2) return;
            // 从两侧旁观席方向生成（相对 Boss 左右横向 8 米）
            Vector3 side = Random.value < 0.5f ? transform.right : -transform.right;
            var minion = EnemySpawnHook.SpawnNear(EnemyType.MockingBystander,
                EnemyTier.Novice, transform.position + side * 8f);
            if (minion == null) return;
            CombatFeedback.Debris(minion.transform.position, new Color(0.65f, 0.5f, 0.75f), 5);
            if (_ec.dialogue != null) _ec.dialogue.Show("旁听席——都来看看这个小题大做的人！", 2.4f);
            GameEvents.RaiseSubtitle("围攻审判：旁观嘲笑者入场——嘲笑不是事实，别被拖走节奏。");
            GameAudio.Play(GameAudio.Sfx.Alert, 0.45f);
        }

        /// <summary>标签弹幕：三连发"否定标签"心念弹（自尊轴）。</summary>
        IEnumerator LabelVolley()
        {
            _busy = true;
            if (_ec.dialogue != null)
                _ec.dialogue.Show(Random.value < 0.5f ? "你是不是太敏感了？" : "小题大做！", 2.2f);
            for (int i = 0; i < 3 && _ec.State != EnemyState.Dead; i++)
            {
                if (_player == null) break;
                Vector3 origin = transform.position + Vector3.up * 1.6f + transform.forward * 0.6f;
                Vector3 target = _player.position + Vector3.up * 1.0f;
                Projectile.Launch(transform, origin, target - origin, new DamageInfo
                {
                    physicalDamage = _ec.profile.physicalDamage * 0.35f,
                    mentalDamage = _ec.profile.mentalDamage * 0.5f,
                    mentalAxis = Personalization.WeaknessAxis.SelfDoubt,
                    knockback = 0.8f,
                    attackerId = _ec.profile.enemyId
                }, 13f, new Color(0.85f, 0.5f, 0.25f), _ec.baseMaterial);
                GameAudio.Play(GameAudio.Sfx.Swing, 0.4f);
                yield return new WaitForSeconds(0.28f);
            }
            _busy = false;
        }

        /// <summary>审判冲击波：以 Boss 为中心的大范围冲击，预警后爆发（跑出圈或翻滚无敌帧躲开）。</summary>
        IEnumerator JudgmentShockwave()
        {
            _busy = true;
            var ring = MakeWarnRing(transform.position, shockRadius, new Color(0.9f, 0.35f, 0.15f));
            if (_ec.dialogue != null) _ec.dialogue.Show("全场肃静——审判！", 1.6f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.6f);

            float t = 0f;
            while (t < 1.1f)
            {
                t += Time.deltaTime;
                float k = 0.4f + 0.6f * (t / 1.1f);
                ring.transform.localScale = new Vector3(shockRadius * 2f * k, 0.03f, shockRadius * 2f * k);
                yield return null;
            }
            Destroy(ring);

            if (_ec.State != EnemyState.Dead && _player != null)
            {
                CombatFeedback.Shake(0.8f);
                CombatFeedback.RecipeBurst(transform.position, new Color(0.9f, 0.4f, 0.2f));
                GameAudio.Play(GameAudio.Sfx.HeavyHit, 1f);
                var pc = _player.GetComponent<PlayerController>();
                if (Vector3.Distance(_player.position, transform.position) <= shockRadius + 0.5f &&
                    _playerCombat != null && pc != null && !pc.IsInvincible)
                    _playerCombat.TakeHit(new DamageInfo
                    {
                        physicalDamage = _ec.profile.physicalDamage * 1.1f,
                        mentalDamage = _ec.profile.mentalDamage * 0.5f,
                        mentalAxis = Personalization.WeaknessAxis.SelfDoubt,
                        knockback = 5f,
                        sourcePosition = transform.position,
                        attackerId = _ec.profile.enemyId
                    });
            }
            _busy = false;
        }

        /// <summary>自我否定重锤：在玩家脚下预警红圈 → 落锤 AoE（三阶段核心威胁）。</summary>
        IEnumerator DenialSlam()
        {
            _busy = true;
            Vector3 spot = _player.position;
            var ring = MakeWarnRing(spot, slamRadius, new Color(0.9f, 0.2f, 0.15f));
            if (_ec.dialogue != null) _ec.dialogue.Show("否定重锤——你什么都做不好！", 1.4f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.5f);

            float t = 0f;
            while (t < 0.85f)
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
                var pc = _player.GetComponent<PlayerController>();
                if (Vector3.Distance(_player.position, spot) <= slamRadius + 0.5f &&
                    _playerCombat != null && pc != null && !pc.IsInvincible)
                    _playerCombat.TakeHit(new DamageInfo
                    {
                        physicalDamage = _ec.profile.physicalDamage * 1.5f,
                        mentalDamage = _ec.profile.mentalDamage * 0.6f,
                        mentalAxis = Personalization.WeaknessAxis.SelfDoubt,
                        knockback = 4f,
                        sourcePosition = spot,
                        attackerId = _ec.profile.enemyId
                    });

                // 法槌裂缝：重锤落地后槌身出现裂缝——短暂破绽窗口（用事实之刃猛攻）
                yield return new WaitForSeconds(0.25f);
                if (_ec.State != EnemyState.Dead)
                {
                    _ec.ForceBreak(1.6f);
                    GameEvents.RaiseSubtitle("法槌裂缝显现——用事实之刃猛攻这道破绽！");
                }
            }
            _busy = false;
        }

        GameObject MakeWarnRing(Vector3 center, float radius, Color color)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "BossWarnRing";
            center.y = transform.position.y - 0.9f;
            ring.transform.position = center + Vector3.up * 0.05f;
            ring.transform.localScale = new Vector3(radius * 2f, 0.03f, radius * 2f);
            var mat = _ec.baseMaterial != null ? new Material(_ec.baseMaterial)
                : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            ring.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return ring;
        }
    }
}
