using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>小题大做审判庭的全局小状态（证据是否已读、已击碎标签数）。</summary>
    public static class JudgmentState
    {
        public static bool EvidenceRead;
        public static int LabelsBroken;

        public static void Reset() { EvidenceRead = false; LabelsBroken = 0; }
    }

    /// <summary>
    /// 浮动标签（"太敏感/小题大做/你也有问题/不值得计较"）：悬浮在审判庭里的否定之词。
    /// 用兵器（事实之刃）击碎——已读证据桌后一击即碎；击碎回补自尊、降低反刍。
    /// </summary>
    public class FloatingLabel : MonoBehaviour
    {
        public string labelText = "太敏感";
        public int hitsToBreak = 2;

        int _hits;
        float _hitCd;
        bool _broken;
        float _bob;
        Vector3 _basePos;

        void Start() => _basePos = transform.position;

        void Update()
        {
            if (_broken) return;
            // 幽浮上下漂动
            _bob += Time.deltaTime;
            transform.position = _basePos + Vector3.up * Mathf.Sin(_bob * 1.4f) * 0.25f;
            transform.Rotate(0, 18f * Time.deltaTime, 0);
        }

        void OnTriggerEnter(Collider other)
        {
            if (_broken || Time.time < _hitCd) return;
            var hb = other.GetComponent<Hitbox>();
            if (hb == null) return;
            if (other.transform.root.GetComponentInChildren<PlayerController>() == null) return;

            _hitCd = Time.time + 0.25f;
            _hits += JudgmentState.EvidenceRead ? hitsToBreak : 1;   // 读过证据 = 一击碎标签
            CombatFeedback.HitSpark(transform.position, new Color(0.95f, 0.8f, 0.4f), 4);
            GameAudio.Play(GameAudio.Sfx.Hit, 0.6f);
            if (_hits >= hitsToBreak) Break();
        }

        void Break()
        {
            _broken = true;
            JudgmentState.LabelsBroken++;
            CombatFeedback.Debris(transform.position, new Color(0.9f, 0.75f, 0.4f), 7);
            GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.7f);

            var p = FindObjectOfType<PlayerController>();
            if (p != null)
            {
                p.Stats.RestoreAxis(Personalization.WeaknessAxis.SelfDoubt, 14f);
                p.Stats.ReduceRumination(8f);
            }
            GameEvents.RaiseSubtitle("标签「" + labelText + "」被事实击碎（已碎 " +
                JudgmentState.LabelsBroken + "）——感受强烈，不等于没有理由。");
            Destroy(gameObject, 0.1f);
        }
    }

    /// <summary>
    /// 证据桌（可交互）：走近即"看清事实"——此后浮动标签一击即碎，回补心理属性。
    /// 象征"先说清发生了什么，再回应评价"。一次性。
    /// </summary>
    public class EvidenceTable : MonoBehaviour
    {
        public float interactRange = 3f;

        bool _read;

        void Update()
        {
            if (_read) return;
            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            if (Vector3.Distance(transform.position, p.transform.position) > interactRange) return;

            _read = true;
            JudgmentState.EvidenceRead = true;
            p.Stats.RestoreMental(16f);
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.FairnessSensitivity, 15f);
            CombatFeedback.HitSpark(transform.position + Vector3.up, new Color(0.95f, 0.92f, 0.7f), 6);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.7f);
            GameEvents.RaiseSubtitle("证据桌：事实先于评价。——「事实之刃」淬火，浮动标签此后一击即碎。");
        }
    }

    /// <summary>破碎镜子（可交互）：走近一次，看清镜中被扭曲的自己——大幅回补自尊。一次性。</summary>
    public class BrokenMirror : MonoBehaviour
    {
        public float interactRange = 3f;

        bool _used;

        void Update()
        {
            if (_used) return;
            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            if (Vector3.Distance(transform.position, p.transform.position) > interactRange) return;

            _used = true;
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.SelfDoubt, 32f);
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.Shame, 20f);
            CombatFeedback.HitSpark(transform.position + Vector3.up, new Color(0.8f, 0.85f, 1f), 8);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.8f);
            GameEvents.RaiseSubtitle("破碎的镜子：里面那个\"不行的你\"是被砸碎的倒影，不是你。——自尊恢复。");
        }
    }
}
