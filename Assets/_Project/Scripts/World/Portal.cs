using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.World
{
    /// <summary>门的角色：每个关卡只有两扇门——回上一关、去下一关。</summary>
    public enum PortalRole { Back, Forward }

    /// <summary>
    /// 区域传送门。
    ///
    /// 【目标由章节顺序解析，不再写死】
    /// 以前每扇门都硬编码一个目标区域编号，结果是训练武馆同时开着通往噪声街区、
    /// 两元赌桌、独居小屋的三扇门，其中大半根本不是当前关卡的相邻关——
    /// 玩家既进不去（剧情锁），显示出来也没有意义。
    /// 现在门只记两件事：**它在哪个区（homeZone）**、**它是回头门还是向前门（role）**；
    /// 具体通向哪一关，在玩家踏进来的那一刻按 StoryManager 的章节顺序算。
    /// 好处有三：
    ///   ① 门永远只指向章节序列里的相邻关，不会再冒出无意义的门；
    ///   ② 同一个区被多个章节复用时（噪声街区就出现两次），
    ///      门会按玩家当前进度指向正确的那一对邻居；
    ///   ③ 以后调整章节顺序不用再回来改二十几处坐标。
    ///
    /// 【为什么不是"碰到就走"】
    /// 战斗中玩家的位移不完全受自己控制：翻滚 10m/s、绝招突进、被击退……
    /// 任何一次擦过门框都会被判成"我要换区域"。所以加了三道门槛：
    ///   ① 站进门里连续 DwellTime 秒才生效（0.2 秒，走进去几乎无感）；
    ///   ② 高速穿过（翻滚/突进/被击飞）期间计时不累积；
    ///   ③ 附近还有咬着你的活敌人时不放行，并说明原因。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Portal : MonoBehaviour
    {
        /// <summary>这扇门所在的区域编号（由 ZoneBuilder 建门时写入）。</summary>
        public int homeZone;

        /// <summary>回头门 / 向前门。</summary>
        public PortalRole role = PortalRole.Forward;

        /// <summary>
        /// 显式目标区号（&lt;0 表示按章节顺序解析，即默认行为）。
        ///
        /// 开放城区与 AI 现场生成的临时站点都不在章节序列里——前者常开常在，
        /// 后者压根没有章节号——它们算不出"上一关/下一关"，只能直接指定去处。
        /// </summary>
        public int explicitZone = -1;

        /// <summary>显式目标的门牌文字（留空则取区域名）。</summary>
        public string explicitLabel;

        /// <summary>门顶标牌（随解析结果刷新，玩家总能看清这扇门通向哪一关）。</summary>
        public TextMesh sign;

        /// <summary>门框发光片（无效门会被隐藏，不留一扇进不去的空门）。</summary>
        public GameObject glow;

        const float DwellTime = 0.2f;        // 停留时间：只滤掉"擦过去那一帧"
        const float WalkThroughSpeed = 7.5f; // 超过这个速度就不是在正常走路
        const float CombatLockRadius = 22f;  // 这个范围内有交战敌人就不放行

        static float _lastTeleport = -10f;

        bool _armed = true;
        float _dwell;
        Vector3 _lastPlayerPos;
        bool _tracking;
        float _lastHint = -99f;
        int _signZone = -99;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        /// <summary>
        /// 按章节顺序解析这扇门的目标。
        /// 同一个区可能在章节序列里出现多次（噪声街区出现两次），
        /// 取【离玩家当前进度最近的那一次出场】作为基准，邻居才对得上玩家的实际处境。
        /// </summary>
        bool Resolve(out int zone, out string label)
        {
            zone = -1; label = "";

            // 显式目标门（开放城区 / 临时站点）：直接给答案，不查章节表
            if (explicitZone >= 0)
            {
                zone = explicitZone;
                label = string.IsNullOrEmpty(explicitLabel)
                    ? ZoneBuilder.ZoneNameOf(zone) : explicitLabel;
                return true;
            }

            var chapters = StoryManager.Chapters;
            if (chapters == null || chapters.Length == 0) return false;

            var story = StoryManager.Instance;
            int cur = story != null ? Mathf.Clamp(story.Chapter, 0, chapters.Length - 1) : 0;

            int best = -1, bestDist = int.MaxValue;
            for (int i = 0; i < chapters.Length; i++)
            {
                if (chapters[i].zoneIndex != homeZone) continue;
                int d = Mathf.Abs(i - cur);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            if (best < 0) return false;

            int target = role == PortalRole.Back ? best - 1 : best + 1;
            if (target < 0 || target >= chapters.Length) return false;

            zone = chapters[target].zoneIndex;
            label = ZoneBuilder.ZoneNameOf(zone);
            return true;
        }

        float _nextSignCheck;

        void Update()
        {
            // 标牌只在解析结果变化时重写（章节推进后门的去向会变）。
            // 全世界有四十多扇门，没必要每帧都去遍历章节表——每半秒查一次足够。
            if (sign == null || Time.unscaledTime < _nextSignCheck) return;
            _nextSignCheck = Time.unscaledTime + 0.5f;
            bool ok = Resolve(out int z, out string name);
            if (z == _signZone) return;
            _signZone = z;
            if (!ok)
            {
                sign.text = role == PortalRole.Back ? "起点" : "尽头";
                sign.color = new Color(0.55f, 0.55f, 0.6f);
                if (glow != null) glow.SetActive(false);
                return;
            }
            sign.text = (role == PortalRole.Back ? "← " : "→ ") + name;
            sign.color = role == PortalRole.Back
                ? new Color(0.85f, 0.85f, 0.7f) : new Color(0.7f, 0.95f, 1f);
            if (glow != null) glow.SetActive(true);
        }

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            _armed = true;
            _dwell = 0f;
            _tracking = false;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            // 刚被传送过来就压在门里 → 本扇门先失效，等玩家自己走出去再说
            if (Time.time - _lastTeleport < 1.5f) _armed = false;
            _dwell = 0f;
            _tracking = false;
        }

        void OnTriggerStay(Collider other)
        {
            if (!_armed) return;
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;

            if (!Resolve(out int targetZone, out string targetName)) return;   // 尽头门：不响应

            // ---- ② 高速穿过不算停留 ----
            Vector3 pos = player.transform.position;
            if (!_tracking) { _lastPlayerPos = pos; _tracking = true; }
            float speed = Time.deltaTime > 1e-4f
                ? Vector3.Distance(pos, _lastPlayerPos) / Time.deltaTime : 0f;
            _lastPlayerPos = pos;
            if (speed > WalkThroughSpeed || player.IsDodging) { _dwell = 0f; return; }

            // ---- ③ 战斗中不放行 ----
            if (EnemyEngaged(pos))
            {
                _dwell = 0f;
                Hint("战斗未了——先解决眼前的敌人，才能离开这片区域。");
                return;
            }

            // ---- 剧情锁 ----
            var story = StoryManager.Instance;
            if (story != null && !story.ZoneUnlocked(targetZone))
            {
                _dwell = 0f;
                Hint("此路通往【" + targetName + "】，现在被心魔封锁——先打完当前这一关。");
                return;
            }

            // ---- ① 停留计时（0.2 秒内不刷任何提示：走进门应当就是走过去就到了）----
            _dwell += Time.deltaTime;
            if (_dwell < DwellTime) return;

            Teleport(player, targetZone, targetName);
        }

        /// <summary>附近是否还有活着且已经进入战斗状态的敌人。</summary>
        static bool EnemyEngaged(Vector3 pos)
        {
            foreach (var e in Object.FindObjectsByType<AI.EnemyController>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                // 待机/巡逻的敌人还没发现你，不该锁门；只有真正咬上来的才算交战
                if (e == null || e.State == AI.EnemyState.Dead ||
                    e.State == AI.EnemyState.Idle || e.State == AI.EnemyState.Patrol)
                    continue;
                if (Vector3.SqrMagnitude(e.transform.position - pos)
                    <= CombatLockRadius * CombatLockRadius) return true;
            }
            return false;
        }

        void Hint(string msg, float interval = 1.5f)
        {
            if (Time.time - _lastHint < interval) return;
            _lastHint = Time.time;
            GameEvents.RaiseSubtitle(msg);
        }

        void Teleport(PlayerController player, int targetZone, string targetName)
        {
            _lastTeleport = Time.time;
            _armed = false;
            _dwell = 0f;
            _tracking = false;
            // 落点统一取该区的玩家出生点——那里由 ZoneBuilder 保证脚下有实地
            // （见 EnsureSpawnPads），不会再出现"进下一关掉进深渊"。
            Vector3 dest = ZoneBuilder.PlayerSpawnOf(targetZone);
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = dest;
            if (cc != null) cc.enabled = true;
            player.NotifyTeleported();   // 清掉上一关的"最近站稳点"，免得刚落地就被拽回去
            ZoneBuilder.CurrentZoneId = ZoneBuilder.ZoneIdOf(targetZone);
            GameEvents.RaiseSubtitle("—— 进入 " + targetName + " ——");
        }
    }
}
