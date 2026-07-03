using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 投射物：远程攻击（玩家剑气 / 敌人心念弹）。
    /// 命中受击框结算伤害，命中墙体地面消散，超时自毁。
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public DamageInfo damage;
        public float speed = 14f;
        public float lifeTime = 3.5f;
        public Transform ownerRoot;
        public Color color = Color.white;

        float _dieAt;

        public static Projectile Launch(Transform owner, Vector3 origin, Vector3 dir,
            DamageInfo damage, float speed, Color color, Material baseMat)
        {
            var root = new GameObject("Projectile");
            root.transform.position = origin;
            root.transform.rotation = Quaternion.LookRotation(dir.normalized);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(0.25f, 0.25f, 1.1f);
            var r = visual.GetComponent<MeshRenderer>();
            Material m;
            if (baseMat != null) m = new Material(baseMat);
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                m = new Material(sh);
            }
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            r.sharedMaterial = m;

            var col = root.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.3f;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var p = root.AddComponent<Projectile>();
            p.damage = damage;
            p.damage.sourcePosition = origin;
            p.speed = speed;
            p.ownerRoot = owner;
            p.color = color;
            return p;
        }

        void Start() => _dieAt = Time.time + lifeTime;

        void Update()
        {
            transform.position += transform.forward * speed * Time.deltaTime;
            if (Time.time > _dieAt) Pop(false);
        }

        void OnTriggerEnter(Collider other)
        {
            if (ownerRoot != null && other.transform.root == ownerRoot.root) return;

            var hurt = other.GetComponent<Hurtbox>();
            if (hurt != null)
            {
                if (hurt.OwnerRoot == (ownerRoot != null ? ownerRoot.root : null)) return;
                hurt.ReceiveHit(damage);
                Pop(true);
                return;
            }

            // 命中墙体/地面等实体消散；忽略其它触发器（泥潭、传送门等）
            if (!other.isTrigger) Pop(false);
        }

        void Pop(bool hit)
        {
            CombatFeedback.Debris(transform.position - Vector3.up * 1f, color, hit ? 4 : 2);
            Destroy(gameObject);
        }
    }
}
