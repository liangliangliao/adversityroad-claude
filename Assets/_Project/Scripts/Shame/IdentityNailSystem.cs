using System;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Shame
{
    /// <summary>身份钉锁住的那一条能力。</summary>
    public enum NailLock
    {
        FactBlade,    // 事实之刃：说不出"发生了什么"
        LockOn,       // 锁定：看不住对面
        Sprint,       // 冲刺：跑不起来
        GoalBeacon,   // 目标灯塔显示：忘了自己要去哪
    }

    /// <summary>
    /// 身份钉（方案 8.4.2 / 8.13.1 IdentityNailSystem）。
    ///
    /// 【它要处理的是"行为被转译为存在"】
    /// 「我做了一件事」滑向「我是一种人」——这个滑动在别的章节只是台词，
    /// 在这里被做成一个**挂在角色身上、看得见、能亲手拔掉的物件**。
    ///
    /// 【为什么只有认领不终审能拔】
    /// 恢复点治不好它，道具治不好它，时间过去也治不好它（验收第 42 条）。
    /// 因为钉进去的不是伤口，是一句判词；判词只能由"我承认事实、但判词不终审"
    /// 这一个动作作废，别的动作都在绕开它。
    /// </summary>
    public class IdentityNailSystem : MonoBehaviour
    {
        public static IdentityNailSystem Instance { get; private set; }

        public const int MaxNails = 3;

        class Nail
        {
            public NailLock kind;
            public string claimTag;
            public GameObject visual;
        }

        readonly List<Nail> _nails = new List<Nail>();
        PlayerController _player;
        LockOnSystem _lockOn;
        float _nailedSince = -1f;
        bool _autoOwnUsed;

        public int Count => _nails.Count;
        public bool Full => _nails.Count >= MaxNails;

        /// <summary>「事实之刃暂时不可用」——钉住的，或 SelfWorth 归零的羞耻状态（8.10.2）。</summary>
        public static bool FactBladeLocked =>
            (Instance != null && Instance.HasLock(NailLock.FactBlade)) || ShameBreakdown.FactBladeSuppressed;

        public static IdentityNailSystem Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("IdentityNailSystem");
            Instance = go.AddComponent<IdentityNailSystem>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public bool HasLock(NailLock kind)
        {
            foreach (var n in _nails) if (n.kind == kind) return true;
            return false;
        }

        PlayerController Player()
        {
            if (_player == null) _player = AdversityRoad.Core.ActorRegistry.Player;
            return _player;
        }

        /// <summary>
        /// 挂钉。指认招式命中后调用，最多叠三枚。
        ///
        /// 安全条款（8.7.1）：连续两次挂钉之间必须至少给出一个认领不终审窗口——
        /// 这条不在这里判，而在 OwnNotFinalSystem 开窗时判；这里只负责挂。
        /// </summary>
        public void Mount(ClaimData claim)
        {
            // 自述套（8.8.3）：首次被挂钉时自动触发一次认领不终审。
            // 它替玩家接住的是**第一次**——之后每一次都得自己按。
            if (!_autoOwnUsed && _nails.Count == 0 &&
                GrowthSystem.EquippedSet == "set_statement" && claim != null && claim.truthTag)
            {
                _autoOwnUsed = true;
                ClaimRegistry.MarkOwned(claim);
                var ex0 = ExposureSystem.Instance;
                if (ex0 != null) ex0.Add(-25f, "自述套替你接住了第一枚钉");
                GameEvents.RaiseSubtitle("「这件事我做了。」——第一次由套装替你说出口，" +
                    "接下来的每一次都得你自己按。");
                return;
            }

            if (Full)
            {
                GameEvents.RaiseSubtitle("钉子已经满了——再多一句判词也钉不进去。用「认领不终审」一次拔掉全部。");
                return;
            }

            var free = new List<NailLock>();
            foreach (NailLock k in Enum.GetValues(typeof(NailLock)))
                if (!HasLock(k)) free.Add(k);
            if (free.Count == 0) return;

            var nail = new Nail
            {
                kind = free[UnityEngine.Random.Range(0, free.Count)],
                claimTag = claim != null ? claim.claimTag : "",
            };
            _nails.Add(nail);
            nail.visual = BuildNailVisual(_nails.Count - 1);

            var d = ShameLine.Data;
            d.nailCount = _nails.Count;
            if (string.IsNullOrEmpty(d.firstNailAt))
            {
                d.firstNailAt = DateTime.UtcNow.ToString("o");
                d.firstNailTag = nail.claimTag;
            }
            ShameLine.Persist();

            if (_nailedSince < 0) _nailedSince = Time.time;
            ApplyLocks();

            GameEvents.RaiseSubtitle("【身份钉 " + _nails.Count + "/" + MaxNails + "】" +
                LockLabel(nail.kind) + "被钉住了。" +
                "恢复点和道具都拔不掉它——只有认领不终审可以。");
            GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.85f);
            var p = Player();
            if (p != null)
                CombatFeedback.HitImpact(p.transform.position + Vector3.up * 1.2f,
                    new Color(0.7f, 0.65f, 0.6f), true);
        }

        /// <summary>认领不终审：一次拔掉全部（恢复点/道具无效）。</summary>
        public void ClearAll(string reason)
        {
            if (_nails.Count == 0) return;
            foreach (var n in _nails)
                if (n.visual != null) Destroy(n.visual);
            int had = _nails.Count;
            _nails.Clear();
            ShameLine.Data.nailCount = 0;

            // 「指认后恢复速度」：被钉住之后多久重新进入有效行动——本章的优势边之一
            if (_nailedSince > 0)
            {
                var d = ShameLine.Data;
                d.nailedRecoverySamples++;
                d.nailedRecoveryTotal += Time.time - _nailedSince;
                _nailedSince = -1f;
                Adversity.AdversityProfile.ObserveStrength("指认后恢复速度", ShameLine.CurrentLevelId);
            }
            ShameLine.Persist();

            ApplyLocks();
            GameEvents.RaiseSubtitle("钉子全部脱落（" + had + " 枚）——" + reason);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.9f);
            var p = Player();
            if (p != null)
                CombatFeedback.ShockRing(p.transform.position, new Color(0.95f, 0.9f, 0.6f), 3.4f);
        }

        /// <summary>离开本章时清干净（钉子是章节内的东西，不带出章外）。</summary>
        public void ResetForExit()
        {
            _autoOwnUsed = false;
            foreach (var n in _nails) if (n.visual != null) Destroy(n.visual);
            _nails.Clear();
            ShameLine.Data.nailCount = 0;
            _nailedSince = -1f;
            ApplyLocks();
        }

        /// <summary>普通结算带 1 枚钉进 8-2（8.5.5）。</summary>
        public void KeepOneForNextLevel()
        {
            while (_nails.Count > 1)
            {
                var last = _nails[_nails.Count - 1];
                if (last.visual != null) Destroy(last.visual);
                _nails.RemoveAt(_nails.Count - 1);
            }
            ShameLine.Data.nailCount = _nails.Count;
            ShameLine.Persist();
            ApplyLocks();
        }

        public static string LockLabel(NailLock k)
        {
            switch (k)
            {
                case NailLock.FactBlade: return "「事实之刃」";
                case NailLock.LockOn: return "「锁定」";
                case NailLock.Sprint: return "「冲刺」";
                default: return "「目标灯塔」";
            }
        }

        /// <summary>当前被钉住的能力清单（HUD 与复盘页读它）。</summary>
        public string LockSummary()
        {
            if (_nails.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _nails.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(LockLabel(_nails[i].kind));
            }
            return sb.ToString();
        }

        void ApplyLocks()
        {
            var p = Player();
            if (p == null) return;

            // 冲刺：跑不起来（只走）。钉子拔掉即恢复
            p.WalkOnly = HasLock(NailLock.Sprint);

            // 锁定：看不住对面
            if (_lockOn == null) _lockOn = p.GetComponent<LockOnSystem>();
            if (_lockOn != null)
            {
                if (HasLock(NailLock.LockOn))
                {
                    _lockOn.Release();
                    _lockOn.enabled = false;
                }
                else _lockOn.enabled = true;
            }

            // 目标灯塔：忘了自己要去哪
            var beacon = OpenWorld.GoalBeaconController.Instance;
            if (beacon != null) beacon.gameObject.SetActive(!HasLock(NailLock.GoalBeacon));

            // 钉子越多，动作越重（8.4.2 视觉）。登记成一条**具名减益**，
            // 拔钉时只撤自己这一条——别的来源（泥沼、冻结）不受影响。
            if (_nails.Count == 0) p.ClearSlow(this);
            else p.SetSlow(this, Mathf.Clamp(1f - 0.08f * _nails.Count, 0.7f, 1f));
        }

        GameObject BuildNailVisual(int index)
        {
            var p = Player();
            if (p == null) return null;

            var root = new GameObject("IdentityNail_" + index);
            root.transform.SetParent(p.transform, false);
            // 钉在肩背一线：玩家自己在第三人称视角下能看见它
            root.transform.localPosition = new Vector3(-0.28f + index * 0.28f, 1.42f, -0.22f);
            root.transform.localRotation = Quaternion.Euler(20f, 0f, 12f * (index - 1));

            var pin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pin.name = "NailPin";
            DestroyImmediate(pin.GetComponent<Collider>());
            pin.transform.SetParent(root.transform, false);
            pin.transform.localScale = new Vector3(0.045f, 0.045f, 0.34f);
            Tint(pin, new Color(0.62f, 0.6f, 0.58f));

            // 短锁链：三节，垂在钉子下面
            for (int i = 0; i < 3; i++)
            {
                var link = GameObject.CreatePrimitive(PrimitiveType.Cube);
                link.name = "NailChain";
                DestroyImmediate(link.GetComponent<Collider>());
                link.transform.SetParent(root.transform, false);
                link.transform.localPosition = new Vector3(0, -0.11f - i * 0.1f, -0.12f);
                link.transform.localScale = new Vector3(0.05f, 0.075f, 0.03f);
                Tint(link, new Color(0.42f, 0.41f, 0.44f));
            }
            return root;
        }

        static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) return;
            var m = new Material(sh);
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            r.sharedMaterial = m;
        }
    }
}
