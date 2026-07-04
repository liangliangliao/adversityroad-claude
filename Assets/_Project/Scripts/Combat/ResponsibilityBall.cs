using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 责任球（责任转嫁法院机制 · 边界与责任线）：法官把"责任"抛向玩家，玩家要分辨真假。
    /// - 虚假责任（红）：不属于你。举起边界盾（格挡）把它挡回抛球者——这就是「责任归还」，
    ///   削法官韧性；若不挡，就会附着为「过度负责」（减速 + 边界流失 + 反刍）。
    /// - 真实责任（绿）：本就属于你。接下它反而回稳意志；用盾把真实责任推开=推卸本分，
    ///   会掉一点自尊。
    /// 全程用距离归位，不依赖物理碰撞，避免穿墙/误伤，法院大厅空旷即可稳定命中。
    /// </summary>
    public class ResponsibilityBall : MonoBehaviour
    {
        public bool isFalse = true;
        public Transform owner;          // 抛球的法官
        public float speed = 7f;
        public float boundaryDamage = 12f;
        public float postureReturn = 16f; // 挡回时对法官的削韧
        public float returnPhysical = 8f; // 挡回时对法官的少量硬伤（让伤害数字可读）

        static readonly Color FalseColor = new Color(0.85f, 0.22f, 0.2f);
        static readonly Color TrueColor = new Color(0.35f, 0.8f, 0.5f);

        Transform _player;
        PlayerCombatController _pc;
        PlayerController _pctrl;
        bool _returning;
        float _dieAt;

        public static ResponsibilityBall Spawn(Transform owner, Vector3 origin, Transform player,
            bool isFalse, Material baseMat)
        {
            var root = new GameObject(isFalse ? "ResponsibilityBall_False" : "ResponsibilityBall_True");
            root.transform.position = origin;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = Vector3.one * 0.6f;
            var r = visual.GetComponent<MeshRenderer>();
            Color c = isFalse ? FalseColor : TrueColor;
            Material m = baseMat != null ? new Material(baseMat)
                : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            r.sharedMaterial = m;

            var ball = root.AddComponent<ResponsibilityBall>();
            ball.isFalse = isFalse;
            ball.owner = owner;
            return ball;
        }

        void Start()
        {
            _dieAt = Time.time + 8f;
            var p = FindObjectOfType<PlayerController>();
            if (p != null)
            {
                _player = p.transform;
                _pc = p.GetComponent<PlayerCombatController>();
                _pctrl = p;
            }
        }

        void Update()
        {
            if (Time.time > _dieAt) { Destroy(gameObject); return; }

            if (_returning)
            {
                if (owner == null) { Destroy(gameObject); return; }
                MoveToward(owner.position + Vector3.up * 1.1f, speed * 1.4f);
                if (Reached(owner.position + Vector3.up * 1.1f, 1.3f)) ArriveOwner();
                return;
            }

            if (_player == null) return;
            Vector3 aim = _player.position + Vector3.up * 1.0f;
            MoveToward(aim, speed);
            if (Reached(aim, 1.0f)) ArrivePlayer();
        }

        void MoveToward(Vector3 target, float spd)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, spd * Time.deltaTime);
        }

        bool Reached(Vector3 target, float radius) =>
            (transform.position - target).sqrMagnitude <= radius * radius;

        void ArrivePlayer()
        {
            bool blocking = _pc != null && _pc.IsGuarding;
            Color c = isFalse ? FalseColor : TrueColor;

            if (isFalse)
            {
                if (blocking)
                {
                    // 责任归还：边界盾把不属于我的责任挡回抛球者
                    _returning = true;
                    CombatFeedback.HitSpark(transform.position, new Color(0.35f, 0.85f, 0.55f), 5);
                    GameAudio.Play(GameAudio.Sfx.Parry, 0.7f);
                    GameEvents.RaiseSubtitle("【责任归还】边界盾把不属于我的责任挡了回去。");
                }
                else
                {
                    // 过度负责：接下了本不属于自己的部分
                    if (_pctrl != null)
                    {
                        _pctrl.Stats.TakeMentalDamage(Personalization.WeaknessAxis.BoundaryConflict,
                            boundaryDamage);
                        _pctrl.Stats.AddRumination(10f);
                        OverResponsibilityDebuff.Apply(_pctrl, 3.5f, 0.55f);
                    }
                    CombatFeedback.HitSpark(transform.position, c, 4);
                    GameEvents.RaiseSubtitle("『这也是你的责任』——你接下了本不属于自己的部分。");
                    Destroy(gameObject);
                }
            }
            else
            {
                if (blocking)
                {
                    // 把真实责任推开=推卸本分
                    if (_pctrl != null)
                        _pctrl.Stats.TakeMentalDamage(Personalization.WeaknessAxis.SelfDoubt, 6f);
                    GameEvents.RaiseSubtitle("真正的责任，不必推开——那本就是我的。");
                }
                else
                {
                    // 接下属于自己的部分：认账，反而回稳
                    if (_pctrl != null) _pctrl.Stats.RestoreAxis(
                        Personalization.WeaknessAxis.WillpowerCollapse, 8f);
                    GameEvents.RaiseSubtitle("这一件确实与我有关——我认下属于我的部分。");
                }
                CombatFeedback.HitSpark(transform.position, c, 3);
                Destroy(gameObject);
            }
        }

        void ArriveOwner()
        {
            var ec = owner != null ? owner.GetComponent<EnemyController>() : null;
            if (ec != null)
                ec.TakeHit(new DamageInfo
                {
                    physicalDamage = returnPhysical,
                    postureDamage = postureReturn,
                    knockback = 1.5f,
                    sourcePosition = transform.position,
                    attackerId = "responsibility_return"
                });
            CombatFeedback.HitSpark(transform.position, new Color(0.4f, 0.85f, 0.6f), 5);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 过度负责状态：接下虚假责任后短时间减速、行动受累。到时自动解除并恢复移速。
    /// 再次命中会刷新持续时间（责任越背越重）。
    /// </summary>
    public class OverResponsibilityDebuff : MonoBehaviour
    {
        float _until;
        float _speedMult = 0.55f;
        PlayerController _player;

        public static void Apply(PlayerController player, float duration, float speedMult)
        {
            if (player == null) return;
            var d = player.GetComponent<OverResponsibilityDebuff>();
            if (d == null) d = player.gameObject.AddComponent<OverResponsibilityDebuff>();
            d._player = player;
            d._speedMult = speedMult;
            d._until = Mathf.Max(d._until, Time.time + duration);
        }

        void Update()
        {
            if (_player == null) { Destroy(this); return; }
            if (Time.time >= _until)
            {
                _player.MoveSpeedMultiplier = 1f;
                Destroy(this);
                return;
            }
            _player.MoveSpeedMultiplier = _speedMult;
        }

        void OnDestroy()
        {
            if (_player != null) _player.MoveSpeedMultiplier = 1f;
        }
    }
}
