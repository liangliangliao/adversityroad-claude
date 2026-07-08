using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>责任转嫁法院的全局小状态（是否已在责任天平判断归属 → 解锁责任归还；已收集事实证据数）。</summary>
    public static class CourtState
    {
        public static bool AttributionJudged;
        public static int EvidenceCollected;

        public static void Reset() { AttributionJudged = false; EvidenceCollected = 0; }
    }

    public enum CourtPropKind { FileMountain, Chain }

    /// <summary>
    /// 可击碎的法院物件：
    /// - 文件山（FileMountain）：用兵器（事实之刃）击碎，露出「事实证据」拾取物；
    /// - 锁链（Chain）：束缚玩家（配合 ChainBindZone 减速），击碎即「破念掌」解除束缚。
    /// 通过玩家攻击判定框（AttackHitbox）命中累计，达到阈值即破碎。
    /// </summary>
    public class BreakableProp : MonoBehaviour
    {
        public CourtPropKind kind = CourtPropKind.FileMountain;
        public int hitsToBreak = 3;
        public ChainBindZone linkedChain;   // Chain 专用：破碎后解除对应束缚区

        int _hits;
        float _hitCd;
        bool _broken;

        void OnTriggerEnter(Collider other)
        {
            if (_broken || Time.time < _hitCd) return;
            var hb = other.GetComponent<Hitbox>();
            if (hb == null) return;
            // 只认玩家的攻击判定框（避免敌人挥击误破物件）
            if (other.transform.root.GetComponentInChildren<PlayerController>() == null) return;

            _hitCd = Time.time + 0.25f;
            _hits++;
            CombatFeedback.HitSpark(transform.position + Vector3.up * 0.8f,
                kind == CourtPropKind.Chain ? new Color(0.6f, 0.7f, 0.9f) : new Color(0.85f, 0.8f, 0.6f), 4);
            GameAudio.Play(GameAudio.Sfx.Hit, 0.6f);
            if (_hits >= hitsToBreak) Break();
        }

        void Break()
        {
            _broken = true;
            CombatFeedback.Debris(transform.position, kind == CourtPropKind.Chain
                ? new Color(0.5f, 0.55f, 0.7f) : new Color(0.75f, 0.7f, 0.55f), 8);
            GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.7f);

            if (kind == CourtPropKind.Chain)
            {
                GameEvents.RaiseSubtitle("破念——锁链断开，束缚解除。");
                if (linkedChain != null) linkedChain.Release();
            }
            else
            {
                CourtState.EvidenceCollected++;
                GameEvents.RaiseSubtitle("事实之刃劈开文件山——露出一份事实证据（" +
                    CourtState.EvidenceCollected + "）。");
                FactEvidence.Spawn(transform.position + Vector3.up * 0.6f);
            }

            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            Destroy(gameObject, 0.2f);
        }
    }

    /// <summary>锁链束缚区：链未断时站入其中大幅减速；链被击碎后失效。</summary>
    [RequireComponent(typeof(Collider))]
    public class ChainBindZone : MonoBehaviour
    {
        [Range(0.1f, 1f)] public float boundSpeed = 0.4f;
        bool _released;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        public void Release() { _released = true; }

        void OnTriggerStay(Collider other)
        {
            if (_released) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            p.MoveSpeedMultiplier = boundSpeed;
        }

        void OnTriggerExit(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p != null) p.MoveSpeedMultiplier = 1f;
        }
    }

    /// <summary>事实证据拾取物：走近即拾取，恢复心理属性并计入证据（象征"用事实回应"）。</summary>
    public class FactEvidence : MonoBehaviour
    {
        float _spin;

        public static FactEvidence Spawn(Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "FactEvidence";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = pos;
            go.transform.localScale = new Vector3(0.5f, 0.65f, 0.06f);
            var r = go.GetComponent<MeshRenderer>();
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            var c = new Color(0.95f, 0.92f, 0.7f);
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            r.sharedMaterial = m;
            return go.AddComponent<FactEvidence>();
        }

        void Update()
        {
            _spin += Time.deltaTime * 120f;
            transform.rotation = Quaternion.Euler(0, _spin, 0);
            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            if (Vector3.Distance(transform.position, p.transform.position) < 1.4f)
            {
                p.Stats.RestoreMental(14f);
                p.Stats.RestoreAxis(Personalization.WeaknessAxis.FairnessSensitivity, 12f);
                GameEvents.RaiseSubtitle("拾取事实证据：先说清发生了什么，比争对错更有力。");
                CombatFeedback.HitSpark(transform.position, new Color(0.95f, 0.92f, 0.7f), 5);
                Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// 责任天平（可交互）：走近即"判断责任归属"——天平从倾斜回正，解锁/确认「责任归还」，
    /// 并回补边界。象征"分清哪部分是你的、哪部分不是"。一次性。
    /// </summary>
    public class ResponsibilityScale : MonoBehaviour
    {
        public Transform panLeft;    // 倾斜的两个托盘（回正动画）
        public Transform panRight;
        public float interactRange = 3.2f;

        bool _judged;
        float _leftY0 = 2.1f, _rightY0 = 2.4f; // 初始一高一低（失衡）
        float _t;

        void Start()
        {
            if (panLeft != null) _leftY0 = panLeft.localPosition.y;
            if (panRight != null) _rightY0 = panRight.localPosition.y;
        }

        void Update()
        {
            if (!_judged)
            {
                var p = FindObjectOfType<PlayerController>();
                if (p != null &&
                    Vector3.Distance(transform.position, p.transform.position) < interactRange)
                    Judge(p);
                return;
            }
            // 回正动画：两托盘缓缓归到同一高度
            _t = Mathf.Min(1f, _t + Time.deltaTime * 0.8f);
            float mid = (_leftY0 + _rightY0) * 0.5f;
            if (panLeft != null)
                panLeft.localPosition = new Vector3(panLeft.localPosition.x,
                    Mathf.Lerp(_leftY0, mid, _t), panLeft.localPosition.z);
            if (panRight != null)
                panRight.localPosition = new Vector3(panRight.localPosition.x,
                    Mathf.Lerp(_rightY0, mid, _t), panRight.localPosition.z);
        }

        void Judge(PlayerController p)
        {
            _judged = true;
            CourtState.AttributionJudged = true;
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.BoundaryConflict, 25f);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.6f);
            GameEvents.RaiseSubtitle(
                "责任天平：分清哪部分是我的、哪部分不是。——「责任归还」已就绪（技能键3 / 触屏『还』）。");
        }
    }
}
