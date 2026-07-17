using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 不读心盾：持续期内抵消下一次心理攻击（"无法确认，所以我不把猜测当事实"）。
    /// 同时让刺激放大器制造的幻影假目标显形消散。带淡蓝色护罩可视化。
    /// </summary>
    public class MindShieldBuff : MonoBehaviour
    {
        public float duration = 10f;

        float _until;
        GameObject _visual;

        /// <summary>盾当前是否在任意玩家身上生效（幻影假目标查询用）。</summary>
        public static bool IsActive { get; private set; }

        public void Arm(float dur)
        {
            duration = dur;
            _until = Time.time + dur;
            if (_visual == null) BuildVisual();
        }

        void BuildVisual()
        {
            _visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(_visual.GetComponent<Collider>());
            _visual.name = "MindShieldVisual";
            _visual.transform.SetParent(transform, false);
            _visual.transform.localPosition = new Vector3(0, 0.9f, 0);
            _visual.transform.localScale = Vector3.one * 2.6f;
            _visual.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.5f, 0.75f, 1f), 0.15f);
        }

        void Update()
        {
            IsActive = true;
            if (Time.time >= _until) Destroy(this);
            else if (_visual != null)
            {
                // 临近失效时护罩闪烁提示
                float rem = _until - Time.time;
                _visual.SetActive(rem > 2f || Mathf.PingPong(Time.time * 4f, 1f) > 0.5f);
            }
        }

        /// <summary>尝试用盾抵消一次心理攻击。成功返回 true 并消耗护盾。</summary>
        public bool TryConsume()
        {
            GameEvents.RaiseSubtitle("不读心盾——无法确认的事，我不把它当成事实。");
            GameAudio.Play(GameAudio.Sfx.Parry, 0.7f);
            CombatFeedback.HitSpark(transform.position + Vector3.up * 1.3f,
                new Color(0.5f, 0.8f, 1f), 6);
            Destroy(this);
            return true;
        }

        void OnDestroy()
        {
            IsActive = false;
            if (_visual != null) Destroy(_visual);
        }
    }

    /// <summary>
    /// 身份冻结减速（旧我 Boss 第二阶段）：持续期内大幅减速并流失行动力。
    /// 用「五分钟火种」立即解除——行动打破冻结。
    /// </summary>
    public class FrozenDebuff : MonoBehaviour
    {
        public float duration = 4f;
        [Range(0.1f, 1f)] public float slowTo = 0.45f;
        public float actionDrainPerSec = 4f;

        float _until;
        PlayerController _player;

        public void Arm(float dur)
        {
            duration = dur;
            _until = Time.time + dur;
        }

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _until = Time.time + duration;
        }

        void Update()
        {
            if (_player == null) { Destroy(this); return; }
            if (Time.time >= _until) { Destroy(this); return; }
            _player.MoveSpeedMultiplier = Mathf.Min(_player.MoveSpeedMultiplier, slowTo);
            _player.Stats.TakeMentalDamage(Personalization.WeaknessAxis.Procrastination,
                actionDrainPerSec * Time.deltaTime);
        }

        void OnDestroy()
        {
            if (_player != null) _player.MoveSpeedMultiplier = 1f;
        }
    }

    /// <summary>
    /// 幻影假目标（刺激放大器制造）：看似威胁、实为猜测的具象。
    /// 攻击它会消耗专注并累积反刍（把力气花在猜测上）；
    /// 「注意力回收」全场清除；「不读心盾」生效期间自动显形消散。
    /// </summary>
    public class PhantomDecoy : MonoBehaviour
    {
        public float driftSpeed = 1.6f;
        public float lifetime = 18f;

        Transform _player;
        float _dieAt;
        float _hitCd;

        void Start()
        {
            _dieAt = Time.time + lifetime;
            var p = FindObjectOfType<PlayerController>();
            if (p != null) _player = p.transform;
        }

        void Update()
        {
            if (Time.time >= _dieAt || MindShieldBuff.IsActive)
            {
                if (MindShieldBuff.IsActive)
                    GameEvents.RaiseSubtitle("不读心盾之下，幻影显形——那从来不是真的威胁。");
                Dissolve();
                return;
            }
            // 幽灵般缓慢逼近玩家（制造"是不是冲我来的"压力）
            if (_player != null)
            {
                Vector3 dir = _player.position - transform.position;
                dir.y = 0;
                if (dir.sqrMagnitude > 4f)
                    transform.position += dir.normalized * driftSpeed * Time.deltaTime;
                if (dir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(dir), 4f * Time.deltaTime);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (Time.time < _hitCd) return;
            var hb = other.GetComponent<Hitbox>();
            if (hb == null) return;
            if (other.transform.root.GetComponentInChildren<PlayerController>() == null) return;
            _hitCd = Time.time + 0.4f;

            // 打幻影 = 把力气花在猜测上：消耗专注、累积反刍
            var p = FindObjectOfType<PlayerController>();
            if (p != null)
            {
                p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.NoiseSensitivity, 6f);
                p.Stats.AddRumination(6f);
            }
            GameEvents.RaiseSubtitle("那只是幻影——别把力气花在无法确认的猜测上（用「注意力回收」清场）。");
            Dissolve();
        }

        public void Dissolve()
        {
            CombatFeedback.Debris(transform.position, new Color(0.5f, 0.55f, 0.7f), 5);
            Destroy(gameObject);
        }

        /// <summary>清除场上全部幻影，返回清除数量（注意力回收调用）。</summary>
        public static int ClearAll()
        {
            int n = 0;
            foreach (var d in FindObjectsOfType<PhantomDecoy>()) { d.Dissolve(); n++; }
            return n;
        }

        /// <summary>生成一个幻影假目标：半透明的人形轮廓 + 触发碰撞。</summary>
        public static PhantomDecoy Spawn(Vector3 pos)
        {
            var root = new GameObject("PhantomDecoy");
            root.transform.position = pos;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 0.1f, 0);
            body.transform.localScale = new Vector3(0.9f, 1.0f, 0.9f);
            body.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.45f, 0.5f, 0.65f), 0.35f);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0, 1.35f, 0);
            head.transform.localScale = Vector3.one * 0.5f;
            head.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.5f, 0.55f, 0.7f), 0.4f);

            var col = root.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            col.height = 2.2f;
            col.center = new Vector3(0, 0.6f, 0);
            col.radius = 0.6f;

            return root.AddComponent<PhantomDecoy>();
        }
    }
}
