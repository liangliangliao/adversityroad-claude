using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Mobile;
using AdversityRoad.Player;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 住所里的盆栽：靠近按【用】/ E 浇水，植物随浇水次数一级一级长大。
    ///
    /// 【为什么值得做】
    /// 这间房子是玩家每次战斗之间回来的地方。它现在什么都不会变——
    /// 走一圈看到的和第一次进来时一模一样。一株会长的植物给了这里一条
    /// **只随时间与照料前进、和战斗胜负无关**的线：你打输了它也照样在长。
    /// 这与本作的主题是一致的：不是所有进展都必须靠打赢换来。
    ///
    /// 【五个阶段与它们各自的形态】
    /// 幼苗 → 长叶 → 枝繁叶茂 → 开花 → 结果。
    /// 每一级都是现搭的几何体（球/圆柱/胶囊），不引用任何美术资源，
    /// 和这个工程其余部分同一套做法。
    ///
    /// 【为什么要有冷却】
    /// 没有冷却的话，站在盆边连点十下就能一路点到结果——那不是照料，是刷按钮。
    /// 两次浇水之间要隔一段真实时间（默认 90 秒），中间再浇会被告知"土还是湿的"。
    /// 进度存本机，退出游戏再回来接着长。
    /// </summary>
    public class PottedPlant : MonoBehaviour
    {
        /// <summary>阶段名（也是浇够水之后头顶那行字）。</summary>
        public static readonly string[] StageNames =
            { "幼苗", "长叶", "枝繁叶茂", "开花", "结果" };

        /// <summary>升到下一级各需要浇几次水。越往后越慢，最后一级最久。</summary>
        static readonly int[] NeedWater = { 2, 3, 4, 5 };

        public const float WaterCooldown = 90f;
        public float interactRange = 2.6f;

        Vector3 _base;
        string _key;
        int _stage;
        int _watered;
        double _lastWaterAt = -9999;
        Transform _crown;          // 当前这一级的枝叶，换级时整个换掉
        PlayerController _player;
        float _lastHint = -99f;
        bool _prevHeld;

        // ===== 建造 =====

        /// <summary>在 at 处放一个花盆（含泥土与当前进度对应的植株）。</summary>
        public static PottedPlant Create(Vector3 at)
        {
            VillaKit.Cyl("PlantPot", at, 0.36f, 0.55f, new Color(0.52f, 0.36f, 0.28f), true);
            VillaKit.Cyl("PlantPotLip", at + new Vector3(0, 0.5f, 0), 0.4f, 0.08f,
                new Color(0.46f, 0.31f, 0.24f));
            // 盆土：浇水后会变深一点，是"刚浇过"的即时反馈
            var soil = VillaKit.Cyl("PlantSoil", at + new Vector3(0, 0.5f, 0), 0.33f, 0.05f,
                new Color(0.24f, 0.17f, 0.12f));

            var go = new GameObject("PottedPlant");
            if (VillaKit.Root != null) go.transform.SetParent(VillaKit.Root, true);
            go.transform.position = at;
            var p = go.AddComponent<PottedPlant>();
            p._base = at;
            p._soil = soil;
            p._key = "plant_" + Mathf.RoundToInt(at.x) + "_" + Mathf.RoundToInt(at.z);
            p.Load();
            p.Rebuild();
            return p;
        }

        GameObject _soil;

        void Load()
        {
            _stage = Mathf.Clamp(PlayerPrefs.GetInt(_key + "_s", 0), 0, StageNames.Length - 1);
            _watered = Mathf.Max(0, PlayerPrefs.GetInt(_key + "_w", 0));
        }

        void Save()
        {
            PlayerPrefs.SetInt(_key + "_s", _stage);
            PlayerPrefs.SetInt(_key + "_w", _watered);
            PlayerPrefs.Save();
        }

        // ===== 形态 =====

        /// <summary>按当前阶段重搭枝叶。换级时把上一级整个删掉，不留半截旧枝。</summary>
        void Rebuild()
        {
            if (_crown != null) Destroy(_crown.gameObject);
            var crown = new GameObject("Crown_" + StageNames[_stage]);
            crown.transform.SetParent(transform, false);
            crown.transform.localPosition = Vector3.zero;
            _crown = crown.transform;

            switch (_stage)
            {
                case 0: BuildSprout(crown.transform); break;
                case 1: BuildLeafy(crown.transform); break;
                case 2: BuildBushy(crown.transform); break;
                case 3: BuildFlowering(crown.transform); break;
                default: BuildFruiting(crown.transform); break;
            }
        }

        static readonly Color Stem = new Color(0.29f, 0.42f, 0.22f);
        static readonly Color LeafA = new Color(0.22f, 0.46f, 0.26f);
        static readonly Color LeafB = new Color(0.28f, 0.54f, 0.31f);
        static readonly Color LeafC = new Color(0.19f, 0.40f, 0.23f);
        static readonly Color Petal = new Color(0.94f, 0.72f, 0.80f);
        static readonly Color Core = new Color(0.96f, 0.86f, 0.42f);
        static readonly Color Fruit = new Color(0.82f, 0.26f, 0.20f);

        void Under(GameObject go) { if (go != null && _crown != null) go.transform.SetParent(_crown, true); }

        /// <summary>幼苗：一截细茎，两片小叶。刚种下的样子。</summary>
        void BuildSprout(Transform _)
        {
            Under(VillaKit.Cyl("Stem", _base + new Vector3(0, 0.55f, 0), 0.022f, 0.28f, Stem));
            for (int i = -1; i <= 1; i += 2)
                Under(VillaKit.Sph("Leaf", _base + new Vector3(i * 0.09f, 0.80f, 0), 0.16f, LeafA));
        }

        /// <summary>长叶：茎抽高，四片叶张开。</summary>
        void BuildLeafy(Transform _)
        {
            Under(VillaKit.Cyl("Stem", _base + new Vector3(0, 0.55f, 0), 0.03f, 0.62f, Stem));
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                Vector3 o = new Vector3(Mathf.Cos(a) * 0.20f, 0.92f + (i % 2) * 0.16f, Mathf.Sin(a) * 0.20f);
                Under(VillaKit.Sph("Leaf", _base + o, 0.30f, i % 2 == 0 ? LeafA : LeafB));
            }
        }

        /// <summary>枝繁叶茂：主干 + 三根分枝 + 一大丛叶。</summary>
        void BuildBushy(Transform _)
        {
            Under(VillaKit.Cyl("Stem", _base + new Vector3(0, 0.55f, 0), 0.045f, 0.95f, Stem));
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Under(VillaKit.CylAxis("Branch", _base + new Vector3(0, 1.18f, 0) + dir * 0.20f,
                    0.022f, 0.44f, Stem, new Vector3(58f, -i * 120f, 0)));
            }
            Under(VillaKit.Sph("Leaf", _base + new Vector3(0, 1.42f, 0), 0.86f, LeafA));
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f * Mathf.Deg2Rad;
                Vector3 o = new Vector3(Mathf.Cos(a) * 0.36f, 1.30f + (i % 3) * 0.17f, Mathf.Sin(a) * 0.36f);
                Under(VillaKit.Sph("Leaf", _base + o, 0.46f, i % 2 == 0 ? LeafB : LeafC));
            }
        }

        /// <summary>开花：在枝繁的基础上点上花——五瓣一芯，不是一颗糖豆。</summary>
        void BuildFlowering(Transform _)
        {
            BuildBushy(_);
            for (int f = 0; f < 4; f++)
            {
                float a = f * 90f * Mathf.Deg2Rad + 0.4f;
                Vector3 at = _base + new Vector3(Mathf.Cos(a) * 0.42f, 1.62f + (f % 2) * 0.14f,
                                                 Mathf.Sin(a) * 0.42f);
                for (int p = 0; p < 5; p++)
                {
                    float pa = p * 72f * Mathf.Deg2Rad;
                    Under(VillaKit.Sph("Petal",
                        at + new Vector3(Mathf.Cos(pa) * 0.07f, 0f, Mathf.Sin(pa) * 0.07f),
                        0.10f, Petal));
                }
                Under(VillaKit.Sph("FlowerCore", at, 0.07f, Core));
            }
        }

        /// <summary>结果：花谢了，挂果。这是最后一级。</summary>
        void BuildFruiting(Transform _)
        {
            BuildBushy(_);
            for (int f = 0; f < 5; f++)
            {
                float a = f * 72f * Mathf.Deg2Rad + 0.7f;
                Vector3 at = _base + new Vector3(Mathf.Cos(a) * 0.40f, 1.48f - (f % 2) * 0.16f,
                                                 Mathf.Sin(a) * 0.40f);
                Under(VillaKit.Cyl("FruitStalk", at + new Vector3(0, 0.06f, 0), 0.012f, 0.08f, Stem));
                Under(VillaKit.Sph("Fruit", at, 0.19f, Fruit));
            }
        }

        // ===== 交互 =====

        void Update()
        {
            if (_player == null) _player = ActorRegistry.Player;
            if (_player == null) return;
            float dist = Vector3.Distance(_base, _player.transform.position);
            if (dist > interactRange) { _prevHeld = false; return; }

            if (Time.time - _lastHint > 3.5f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle(StatusLine());
            }

            // 【不用 MobileInput.GetDown】那个接口会**消费**这次按键，谁先查到谁拿走；
            // 住所里同时还有家具、猫、目标板在查同一个键。这里自己做上升沿，不抢别人的。
            bool held = MobileInput.GetHeld("Interact");
            bool edge = held && !_prevHeld;
            _prevHeld = held;
            if (Input.GetKeyDown(KeyCode.E) || edge) Water();
        }

        string StatusLine()
        {
            if (_stage >= StageNames.Length - 1)
                return "【" + StageNames[_stage] + "】它已经长到头了。你可以就这么看着它。";
            double wait = WaterCooldown - (Time.timeAsDouble - _lastWaterAt);
            if (wait > 0)
                return "【" + StageNames[_stage] + "】土还是湿的——再等 " +
                       Mathf.CeilToInt((float)wait) + " 秒。浇太勤会涝。";
            return "【" + StageNames[_stage] + "】按【用】/ E 浇水（本级 " + _watered + "/" +
                   NeedWater[_stage] + "）";
        }

        void Water()
        {
            if (_stage >= StageNames.Length - 1)
            {
                GameEvents.RaiseSubtitle("它已经结果了。剩下的事不是浇水能加快的。");
                return;
            }
            if (Time.timeAsDouble - _lastWaterAt < WaterCooldown)
            {
                GameEvents.RaiseSubtitle("土还是湿的。浇太勤会涝——过一会儿再来。");
                return;
            }
            _lastWaterAt = Time.timeAsDouble;
            _watered++;
            GameAudio.Play(GameAudio.Sfx.Cast, 0.45f);
            Splash();

            if (_watered >= NeedWater[_stage])
            {
                _watered = 0;
                _stage++;
                Rebuild();
                GameEvents.RaiseSubtitle("它长了一节——现在是【" + StageNames[_stage] + "】。" +
                    "你没做什么特别的事，只是没有断过。");
            }
            else
            {
                GameEvents.RaiseSubtitle("浇过了（本级 " + _watered + "/" + NeedWater[_stage] +
                    "）。它不会因为今天浇了就马上变样，但它记得。");
            }
            Save();
        }

        /// <summary>浇水的即时反馈：土变深、几滴水珠落下。没有反馈的按钮会被当成没反应。</summary>
        void Splash()
        {
            if (_soil != null)
            {
                var r = _soil.GetComponent<MeshRenderer>();
                if (r != null && r.material != null) r.material.color = new Color(0.15f, 0.10f, 0.07f);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                var d = VillaKit.Sph("WaterDrop",
                    _base + new Vector3(Mathf.Cos(a) * 0.22f, 0.95f, Mathf.Sin(a) * 0.22f),
                    0.05f, new Color(0.55f, 0.78f, 0.95f));
                if (d != null) Destroy(d, 0.5f + i * 0.05f);
            }
        }
    }
}
