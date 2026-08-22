using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 低语链的一节（后排低语者 → 侧目者 → 放大镜围观者）。
    /// 挂在敌人身上；被击倒即视为断链，由 WhisperChainSystem 在别处重建。
    /// </summary>
    public class WhisperNode : MonoBehaviour
    {
        /// <summary>在链上的序位：0 起点 / 1 中继 / 2 放大端。</summary>
        public int rank;

        EnemyController _ec;

        void Start()
        {
            _ec = GetComponent<EnemyController>();
            WhisperChainSystem.Ensure().Register(this);
        }

        void OnDestroy()
        {
            if (WhisperChainSystem.Instance != null) WhisperChainSystem.Instance.Unregister(this);
        }

        public bool Alive => _ec == null || _ec.State != EnemyState.Dead;
    }

    /// <summary>
    /// 低语链（方案 8.6.1 / 8.13.1 WhisperChainSystem）。
    ///
    /// 【它存在是为了证明一件事：让所有人闭嘴是做不到的】
    /// 打断任意一节，8 秒后链条从**另一处**重建。本关不存在"彻底消音"的隐藏解法，
    /// 这一条必须在关卡、UI 与复盘文案里保持一致（验收第 40 条）。
    ///
    /// 【所以它不能造成伤害】
    /// 一个无法消除的骚扰源如果还持续掉血，就只剩挫败（8.15 风险表）。
    /// 链条完整时只做一件事：抬高 Exposure 的增速。压力来自被看着，不来自被扣血。
    /// </summary>
    public class WhisperChainSystem : MonoBehaviour
    {
        public static WhisperChainSystem Instance { get; private set; }

        /// <summary>断链后的重建时间：8 秒（验收第 40 条）。</summary>
        public const float RebuildDelay = 8f;

        /// <summary>链条完整时额外施加的 Exposure 每秒增量。</summary>
        public const float ChainExposureRate = 4.5f;

        readonly List<WhisperNode> _nodes = new List<WhisperNode>();
        readonly List<WhisperNode> _chain = new List<WhisperNode>();
        readonly List<LineRenderer> _links = new List<LineRenderer>();

        float _rebuildAt = -1f;
        bool _enabledForLevel;
        bool _fieldWide;

        public bool ChainComplete => _chain.Count >= 3;
        public bool Rebuilding => _rebuildAt > 0f && Time.time < _rebuildAt;

        public static WhisperChainSystem Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("WhisperChainSystem");
            Instance = go.AddComponent<WhisperChainSystem>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Register(WhisperNode n)
        {
            if (n != null && !_nodes.Contains(n)) _nodes.Add(n);
            if (_enabledForLevel && !ChainComplete && !Rebuilding) TryForm();
        }

        public void Unregister(WhisperNode n)
        {
            _nodes.Remove(n);
            if (_chain.Contains(n)) Break("链上的一节倒下了");
        }

        /// <summary>本关启用低语链（8-2 进入时调用）。</summary>
        public void EnableForLevel(bool on)
        {
            _enabledForLevel = on;
            if (!on) { ClearLinks(); _chain.Clear(); return; }
            TryForm();
        }

        /// <summary>Boss 阶段三「扩散」：全场重建并锁定。</summary>
        public void FieldWideRebuild()
        {
            _fieldWide = true;
            _rebuildAt = -1f;
            TryForm();
            GameEvents.RaiseSubtitle("【扩散】低语在全场重新连起来了——这一次不用把它断掉，" +
                "在它活着的时候把最后一件事做完。");
        }

        void Update()
        {
            if (!_enabledForLevel) return;

            // 链上任何一节死了/被移走，就算断链
            for (int i = _chain.Count - 1; i >= 0; i--)
                if (_chain[i] == null || !_chain[i].Alive) { Break("一节被打断了"); break; }

            if (!ChainComplete && !Rebuilding) TryForm();
            if (Rebuilding && Time.time >= _rebuildAt) { _rebuildAt = -1f; TryForm(); }

            if (ChainComplete)
            {
                UpdateLinks();
                var ex = ExposureSystem.Instance;
                // 只在玩家真的被注视时叠加：链条是"传播"，不是全天候的隐形扣分
                var gaze = GazeConeSystem.Instance;
                bool watched = gaze != null && gaze.ConesOnPlayer() > 0;
                if (ex != null && (watched || _fieldWide))
                    ex.Add(ChainExposureRate * (_fieldWide ? 1.4f : 1f) * Time.deltaTime, null);
            }
        }

        void TryForm()
        {
            _chain.Clear();
            ClearLinks();
            var alive = new List<WhisperNode>();
            foreach (var n in _nodes) if (n != null && n.Alive) alive.Add(n);
            if (alive.Count < 3) return;

            // 从"离玩家最远的一端"起链：重建总是从另一处开始，而不是原地复活
            var player = FindObjectOfType<PlayerController>();
            if (player != null)
                alive.Sort((a, b) =>
                    (b.transform.position - player.transform.position).sqrMagnitude
                    .CompareTo((a.transform.position - player.transform.position).sqrMagnitude));

            for (int i = 0; i < 3 && i < alive.Count; i++) _chain.Add(alive[i]);
            BuildLinks();
            GameEvents.RaiseSubtitle("低语又连起来了——从另一处。让所有人闭嘴不是这一关的通关条件。");
        }

        void Break(string why)
        {
            if (_chain.Count == 0) return;
            _chain.Clear();
            ClearLinks();
            _rebuildAt = Time.time + RebuildDelay;
            GameEvents.RaiseSubtitle(why + "——8 秒后它会从别处接上。破链只能拖延，不能解决。");
        }

        // ================= 可视化：一条看得见的传播线 =================

        void BuildLinks()
        {
            for (int i = 0; i + 1 < _chain.Count; i++)
            {
                var go = new GameObject("WhisperLink");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = 0.05f;
                lr.endWidth = 0.05f;
                lr.useWorldSpace = true;
                lr.material = GazeConeSystem.ConeMaterial();
                lr.startColor = new Color(0.85f, 0.8f, 0.95f, 0.55f);
                lr.endColor = new Color(0.65f, 0.6f, 0.85f, 0.35f);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _links.Add(lr);
            }
        }

        void UpdateLinks()
        {
            for (int i = 0; i < _links.Count && i + 1 < _chain.Count; i++)
            {
                if (_links[i] == null || _chain[i] == null || _chain[i + 1] == null) continue;
                _links[i].SetPosition(0, _chain[i].transform.position + Vector3.up * 1.6f);
                _links[i].SetPosition(1, _chain[i + 1].transform.position + Vector3.up * 1.6f);
            }
        }

        void ClearLinks()
        {
            foreach (var l in _links) if (l != null) Destroy(l.gameObject);
            _links.Clear();
        }

        /// <summary>玩家在场时链条能否完整成形——逆袭判定「宿敌降级」读它。</summary>
        public bool FormsWithPlayerPresent() => ChainComplete;
    }
}
