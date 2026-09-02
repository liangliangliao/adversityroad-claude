using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 三层世界（方案 6.4 / 6.5 / 验收标准 21）：
    ///
    /// Reality Layer  — 可信的社会物理环境：住宅、街道、行人、车辆、商店、天气、昼夜。
    /// Adversity Layer— 同一地点被"逆境化"：现实结构仍在，但灯光、声音、NPC 行为、
    ///                   敌人、封锁与心理视效改变。玩家是在现实环境上作战，
    ///                   而不是突然被传送到一间抽象空房。
    /// Inner World    — 内部冲突到阈值时，现实地点出现心理裂隙，连接拖延沼泽、
    ///                   旧事回声馆等象征空间；完成挑战后回到原地点，世界状态可见变化。
    ///
    /// 本控制器负责：按压力/障碍状态推动 Reality→Adversity 的连续过渡，
    /// 到阈值时在裂隙锚点长出入口，并在返回时结算世界变化。
    /// </summary>
    public class WorldLayerController : MonoBehaviour
    {
        public static WorldLayerController Instance { get; private set; }

        /// <summary>玩家当前所处的层。</summary>
        public static WorldLayer CurrentLayer { get; private set; } = WorldLayer.Reality;

        /// <summary>进入内心世界之前的落脚点（完成后回到"原地点"，不是回菜单）。</summary>
        static Vector3 _returnPoint;
        static string _returnDistrict = "";
        static bool _hasReturn;

        readonly Dictionary<string, GameObject> _overlays = new Dictionary<string, GameObject>();
        readonly Dictionary<string, PsychicRift> _rifts = new Dictionary<string, PsychicRift>();

        PlayerController _player;
        float _nextTick;
        string _lastDistrictId = "";

        public static WorldLayerController Ensure(Material baseMat)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("WorldLayerController");
            Instance = go.AddComponent<WorldLayerController>();
            return Instance;
        }

        void Update()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 0.75f;

            if (_player == null)
            {
                _player = AdversityRoad.Core.ActorRegistry.Player;
                if (_player == null) return;
            }
            if (!DistrictCatalog.Ready) return;

            var d = DistrictCatalog.NearestTo(_player.transform.position, 90f);
            if (d == null) return;

            if (d.id != _lastDistrictId)
            {
                _lastDistrictId = d.id;
                AnnounceDistrict(d);
            }

            float adversity = WorldState.AdversityOf(d.id);
            ApplyOverlay(d, adversity);

            // 逆境层足够深 → 现实地点上长出心理裂隙
            var state = WorldState.Get(d.id);
            if (state.riftOpen && state.riftZoneIndex >= 0) EnsureRift(d, state);

            CurrentLayer = adversity >= 0.35f ? WorldLayer.Adversity : WorldLayer.Reality;
        }

        void AnnounceDistrict(DistrictRuntime d)
        {
            float a = WorldState.AdversityOf(d.id);
            string mood = a >= 0.7f ? "——这里的空气已经变了形。"
                : a >= 0.35f ? "——街上的东西看起来都不太对劲。"
                : "";
            GameEvents.RaiseSubtitle("〔" + d.name + "〕" + mood);
        }

        // ================= 逆境覆盖层 =================

        /// <summary>
        /// 在现实结构之上叠一层可读的"逆境化"：压低亮度、加冷色雾、在街面浮出压迫感的视觉物。
        /// 强度连续变化——玩家能看出这个地方正在被什么东西侵占，而不是突然换场景。
        /// </summary>
        void ApplyOverlay(DistrictRuntime d, float adversity)
        {
            if (!_overlays.TryGetValue(d.id, out var root) || root == null)
            {
                root = new GameObject("AdversityOverlay_" + d.id);
                root.transform.position = d.center;
                BuildOverlayVisuals(root.transform, d);
                _overlays[d.id] = root;
            }

            bool on = adversity > 0.08f;
            if (root.activeSelf != on) root.SetActive(on);
            if (!on) return;

            float s = Mathf.Clamp01(adversity);
            root.transform.localScale = Vector3.one * (0.7f + s * 0.6f);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                var c = r.sharedMaterial != null ? r.sharedMaterial.color : Color.white;
                c.a = Mathf.Lerp(0.05f, 0.42f, s);
                if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", c);
                r.material.color = c;
            }
        }

        void BuildOverlayVisuals(Transform parent, DistrictRuntime d)
        {
            // 藤蔓/泥化/锁链的抽象化身：从地面长出的暗色片体（Psychological Kit 的最小集合）
            var rng = new System.Random(d.id.GetHashCode());
            for (int i = 0; i < 10; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "AdversityVeil";
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.SetParent(parent, false);
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rad = 8f + (float)rng.NextDouble() * 22f;
                q.transform.localPosition = new Vector3(Mathf.Cos(ang) * rad, 1.8f, Mathf.Sin(ang) * rad);
                q.transform.localRotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                q.transform.localScale = new Vector3(3f + (float)rng.NextDouble() * 4f, 3.6f, 1f);
                q.GetComponent<MeshRenderer>().sharedMaterial =
                    Combat.CombatFeedback.EnergyMaterial(new Color(0.22f, 0.12f, 0.3f), 0.25f);
            }
        }

        // ================= 心理裂隙 =================

        void EnsureRift(DistrictRuntime d, DistrictState state)
        {
            if (_rifts.TryGetValue(d.id, out var existing) && existing != null) return;
            var rift = PsychicRift.Create(d.riftAnchor, state.riftZoneIndex, state.riftLabel, d.id);
            _rifts[d.id] = rift;
        }

        /// <summary>进入内心世界前记住返回点（象征空间完成后回到原地点）。</summary>
        public static void MarkReturnPoint(Vector3 pos, string districtId)
        {
            _returnPoint = pos;
            _returnDistrict = districtId;
            _hasReturn = true;
            CurrentLayer = WorldLayer.Inner;
        }

        public static bool HasReturnPoint => _hasReturn;

        /// <summary>
        /// 从内心世界返回：回到进入前的现实地点，并结算世界状态的可见变化
        /// （逆境层退去一层、裂隙闭合、路线重开）。
        /// </summary>
        public static void ReturnToReality()
        {
            if (!_hasReturn) return;
            var player = AdversityRoad.Core.ActorRegistry.Player;
            if (player != null)
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.position = _returnPoint;
                if (cc != null) cc.enabled = true;
                // 传送必须通知控制器：它会清掉速度、土狼时间和最近的站稳点。
                // 少了这一句，嵌墙兜底会拿上一张地图的安全点当参照。
                // Portal / SiteGate / ShameLine / LevelSelect 四处早就这么做了，这里漏了。
                player.NotifyTeleported();
            }
            if (OpenWorldBuilder.CityZoneIndex >= 0)
                ZoneBuilder.CurrentZoneId = ZoneBuilder.ZoneIdOf(OpenWorldBuilder.CityZoneIndex);

            if (!string.IsNullOrEmpty(_returnDistrict))
            {
                WorldState.AddAdversity(_returnDistrict, -0.4f);
                WorldState.CloseRift(_returnDistrict);
                WorldState.ReopenRoute(_returnDistrict);
            }
            _hasReturn = false;
            CurrentLayer = WorldLayer.Reality;
            GameEvents.RaiseSubtitle("你从裂隙里走回原地——这条街还是这条街，但它压在身上的重量变轻了。");
        }
    }

    /// <summary>
    /// 心理裂隙（方案 6.2 最后一行 / 6.5）：现实地点上生成的入口。
    /// 它不是传送门菜单：只有当现实层的压力真的累积到阈值，这道缝才会出现在你走过的街角。
    /// </summary>
    public class PsychicRift : MonoBehaviour
    {
        public int targetZoneIndex = -1;
        public string label = "";
        public string districtId = "";
        public float range = 3f;

        PlayerController _player;
        float _lastHint = -99f;

        public static PsychicRift Create(Vector3 pos, int zoneIndex, string label, string districtId)
        {
            var root = new GameObject("PsychicRift_" + label);
            root.transform.position = pos;

            // 竖直裂缝：一道暗紫色的能量薄片 + 边缘辉光
            var slit = GameObject.CreatePrimitive(PrimitiveType.Quad);
            slit.name = "RiftSlit";
            Object.DestroyImmediate(slit.GetComponent<Collider>());
            slit.transform.SetParent(root.transform, false);
            slit.transform.localPosition = new Vector3(0, 2.2f, 0);
            slit.transform.localScale = new Vector3(1.4f, 4.4f, 1f);
            slit.GetComponent<MeshRenderer>().sharedMaterial =
                Combat.CombatFeedback.EnergyMaterial(new Color(0.55f, 0.3f, 0.95f), 0.7f);
            slit.AddComponent<FaceCamera>();

            var glow = new GameObject("RiftLight");
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0, 2.2f, 0);
            var l = glow.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 12f;
            l.intensity = 2.2f;
            l.color = new Color(0.6f, 0.35f, 1f);

            OpenWorldBuilder.HomeSign(pos + new Vector3(0, 5f, 0), "裂隙 · " + label);

            var rift = root.AddComponent<PsychicRift>();
            rift.targetZoneIndex = zoneIndex;
            rift.label = label;
            rift.districtId = districtId;
            return rift;
        }

        void Update()
        {
            if (_player == null)
            {
                _player = AdversityRoad.Core.ActorRegistry.Player;
                if (_player == null) return;
            }
            if (Vector3.Distance(transform.position, _player.transform.position) > range) return;

            if (Time.time - _lastHint > 8f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("裂隙里传来熟悉的回声——【" + label + "】。走进去（按 E）面对它。");
            }
            if (!Input.GetKeyDown(KeyCode.E) && !Mobile.MobileInput.GetDown("Interact")) return;
            if (targetZoneIndex < 0) return;

            WorldLayerController.MarkReturnPoint(_player.transform.position, districtId);
            var cc = _player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _player.transform.position = ZoneBuilder.PlayerSpawnOf(targetZoneIndex);
            if (cc != null) cc.enabled = true;
            _player.NotifyTeleported();   // 同上：跨场景传送后必须清掉旧的安全点
            ZoneBuilder.CurrentZoneId = ZoneBuilder.ZoneIdOf(targetZoneIndex);
            GameEvents.RaiseSubtitle("—— 进入内心世界 · " + ZoneBuilder.ZoneNameOf(targetZoneIndex) +
                " ——（完成后会回到你出发的那条街）");
        }
    }
}
