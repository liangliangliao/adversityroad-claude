using System.Collections;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 刺激放大器（一声咳嗽的街道 Boss）：
    /// - 噪声放大：周期性把街道的咳嗽/低语放大成一次全场专注冲击（定心格挡/不读心盾可化解）；
    /// - 幻影假目标：制造酷似威胁的幻影，攻击幻影=把力气花在猜测上（掉专注、涨反刍）；
    ///   「注意力回收」全场清除、「不读心盾」使其显形消散——先夺回注意力，再打真身。
    /// 血量越低噪声越密：它在拼命放大一切，别跟着每一个声音跑。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class StimulusAmplifierBoss : MonoBehaviour
    {
        public float noiseInterval = 8f;
        public float decoyInterval = 13f;
        public float noiseRange = 16f;
        public int maxDecoys = 2;

        EnemyController _ec;
        Transform _player;
        PlayerCombatController _playerCombat;
        float _noiseCd = 4f, _decoyCd = 7f;
        bool _busy;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCombat = p.GetComponent<PlayerCombatController>(); }
            GameEvents.RaiseSubtitle("『刺激放大器』出现——它会放大每一个声音。用「不读心盾」「注意力回收」守住专注。");
        }

        void Update()
        {
            if (_ec == null || _player == null || _busy) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 26f) return;

            float rate = Mathf.Lerp(0.6f, 1f, _ec.HpRatio);   // 血越低噪声越密
            float dt = Time.deltaTime;
            _noiseCd -= dt; _decoyCd -= dt;

            if (_noiseCd <= 0)
            {
                _noiseCd = noiseInterval * rate;
                StartCoroutine(NoiseBlast());
                return;
            }
            if (_decoyCd <= 0)
            {
                _decoyCd = decoyInterval * rate;
                SpawnDecoys();
            }
        }

        /// <summary>噪声放大：预警蓄势后全场专注冲击。走的心理攻击通道——
        /// 定心格挡（精准格挡时机）与不读心盾都能整个化解。</summary>
        IEnumerator NoiseBlast()
        {
            _busy = true;
            if (_ec.dialogue != null)
                _ec.dialogue.Show(Random.value < 0.5f ? "听——他们都在议论你！" : "这声咳嗽，就是冲你来的！", 2f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.7f);
            GameEvents.RaiseSubtitle("噪声正在被放大——定心格挡或不读心盾可以化解！");

            yield return new WaitForSeconds(1.0f);

            if (_ec.State != EnemyState.Dead && _player != null &&
                Vector3.Distance(transform.position, _player.position) <= noiseRange)
            {
                CombatFeedback.Shake(0.4f);
                GameAudio.Play(GameAudio.Sfx.Hurt, 0.7f);
                var gm = GameManager.Instance;
                float dmg = MentalDamageSystem.Resolve(
                    _ec.profile.mentalDamage,
                    Personalization.WeaknessAxis.NoiseSensitivity,
                    gm != null ? gm.CurrentProfile : null,
                    gm != null ? gm.safety : null);
                if (_playerCombat != null && dmg > 0f)
                    _playerCombat.TakeHit(new DamageInfo
                    {
                        mentalDamage = dmg,
                        mentalAxis = Personalization.WeaknessAxis.NoiseSensitivity,
                        isMentalOnly = true,
                        attackerId = _ec.profile.enemyId
                    });
            }
            _busy = false;
        }

        /// <summary>制造幻影假目标：从玩家侧后方无声出现——"是不是有人冲我来了？"</summary>
        void SpawnDecoys()
        {
            int alive = FindObjectsOfType<PhantomDecoy>().Length;
            int want = Mathf.Min(maxDecoys - alive, 2);
            if (want <= 0) return;

            for (int i = 0; i < want; i++)
            {
                Vector3 pos = _player.position +
                    Quaternion.Euler(0, Random.Range(90f, 270f), 0) *
                    (_player.forward * Random.Range(5f, 8f));
                pos.y = transform.position.y;
                PhantomDecoy.Spawn(pos);
            }
            if (_ec.dialogue != null) _ec.dialogue.Show("看那边——那是不是针对你？", 2.2f);
            GameEvents.RaiseSubtitle("幻影出现了——无法确认的威胁不必回应，用「注意力回收」清场后打真身。");
            GameAudio.Play(GameAudio.Sfx.Alert, 0.4f);
        }

        void OnDestroy()
        {
            // Boss 倒下：全部幻影随之散去——刺激源没了，猜测也就散了
            foreach (var d in FindObjectsOfType<PhantomDecoy>()) d.Dissolve();
        }
    }
}
