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

        static readonly Color Bark = new Color(0.34f, 0.26f, 0.17f);
        static readonly Color Stem = new Color(0.31f, 0.44f, 0.23f);
        static readonly Color LeafA = new Color(0.20f, 0.44f, 0.23f);
        static readonly Color LeafB = new Color(0.29f, 0.56f, 0.30f);
        static readonly Color LeafC = new Color(0.16f, 0.36f, 0.20f);
        static readonly Color Petal = new Color(0.96f, 0.78f, 0.84f);
        static readonly Color Core = new Color(0.97f, 0.87f, 0.42f);
        static readonly Color Fruit = new Color(0.80f, 0.22f, 0.17f);

        void Under(GameObject go) { if (go != null && _crown != null) go.transform.SetParent(_crown, true); }

        /// <summary>
        /// 一片叶子：压扁的椭球 + 一根叶柄，按给定的朝向与俯仰张开。
        ///
        /// 【为什么不能用球】上一版每片叶子是一颗正球——球没有朝向，一堆球堆在一起
        /// 只能读成"一坨绿色的泡泡"，这就是"一点也不真实"最直接的来源。
        /// 叶子必须是**扁的、有长短轴、并且朝某个方向张开**，一眼就能看出哪是叶面。
        /// </summary>
        void Leaf(Vector3 at, float yawDeg, float pitchDeg, float len, Color c)
        {
            var rot = new Vector3(pitchDeg, yawDeg, 0f);
            Vector3 dir = Quaternion.Euler(rot) * Vector3.forward;
            // 叶柄：从枝上伸出去半片叶长
            Under(VillaKit.CylAxis("LeafStalk", at + dir * (len * 0.28f), 0.012f, len * 0.55f,
                Stem, new Vector3(pitchDeg + 90f, yawDeg, 0f)));
            // 叶面：长 len、宽 0.55len、厚 0.12len 的扁椭球
            var blade = VillaKit.Sph("LeafBlade", at + dir * (len * 0.72f), 1f, c);
            if (blade != null)
            {
                blade.transform.localScale = new Vector3(len * 0.55f, len * 0.12f, len);
                blade.transform.rotation = Quaternion.Euler(rot);
                Under(blade);
            }
        }

        /// <summary>渐细的主干：分几段，每一段比上一段细。等粗的圆柱一看就是根管子。</summary>
        void Trunk(float bottomY, float topY, float r0, float r1, Color c)
        {
            const int Seg = 4;
            for (int i = 0; i < Seg; i++)
            {
                float t0 = i / (float)Seg, t1 = (i + 1) / (float)Seg;
                float y0 = Mathf.Lerp(bottomY, topY, t0), y1 = Mathf.Lerp(bottomY, topY, t1);
                float r = Mathf.Lerp(r0, r1, (t0 + t1) * 0.5f);
                // 每一节稍微歪一点：笔直的杆子不像长出来的
                float lean = Mathf.Sin(i * 1.7f) * 0.018f;
                Under(VillaKit.Cyl("Trunk", _base + new Vector3(lean * i, y0, lean * i * 0.6f),
                    r, y1 - y0, c));
            }
        }

        /// <summary>幼苗：一截细芽，两片子叶朝相反方向张开。</summary>
        void BuildSprout(Transform _)
        {
            Trunk(0.55f, 0.82f, 0.020f, 0.014f, Stem);
            Leaf(_base + new Vector3(0, 0.80f, 0), 0f, -28f, 0.20f, LeafB);
            Leaf(_base + new Vector3(0, 0.78f, 0), 180f, -28f, 0.18f, LeafA);
        }

        /// <summary>长叶：茎抽高，六片叶沿螺旋排开（自然界的叶序是螺旋，不是十字）。</summary>
        void BuildLeafy(Transform _)
        {
            Trunk(0.55f, 1.28f, 0.028f, 0.018f, Stem);
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                Leaf(_base + new Vector3(0, 0.78f + t * 0.46f, 0),
                     i * 137.5f, -34f + t * 12f, 0.26f + t * 0.06f, i % 2 == 0 ? LeafA : LeafB);
            }
        }

        /// <summary>枝繁叶茂：木质化的主干 + 六根斜向分枝，每根分枝末端一簇叶。</summary>
        void BuildBushy(Transform _)
        {
            Trunk(0.55f, 1.45f, 0.055f, 0.030f, Bark);
            for (int i = 0; i < 6; i++)
            {
                float yaw = i * 60f + 12f;
                float tier = i < 3 ? 0f : 1f;
                float y = 1.05f + tier * 0.30f;
                float len = 0.52f - tier * 0.10f;
                Vector3 from = _base + new Vector3(0, y, 0);
                Vector3 dir = Quaternion.Euler(-38f, yaw, 0f) * Vector3.forward;

                Under(VillaKit.CylAxis("Branch", from + dir * (len * 0.5f), 0.018f, len, Bark,
                    new Vector3(-38f + 90f, yaw, 0f)));

                Vector3 tip = from + dir * len;
                for (int l = 0; l < 4; l++)
                    Leaf(tip, yaw + l * 90f, -20f - l * 8f, 0.30f,
                         l % 2 == 0 ? LeafA : (l == 1 ? LeafB : LeafC));
            }
            // 顶芽：树冠顶上收一个尖，轮廓才不是一个球
            Leaf(_base + new Vector3(0, 1.46f, 0), 40f, -70f, 0.24f, LeafB);
            Leaf(_base + new Vector3(0, 1.44f, 0), 220f, -70f, 0.22f, LeafA);
        }

        /// <summary>开花：枝繁的基础上挂花——五片扁花瓣围一个花芯，不是一颗糖豆。</summary>
        void BuildFlowering(Transform _)
        {
            BuildBushy(_);
            for (int f = 0; f < 5; f++)
            {
                float yaw = f * 72f + 30f;
                Vector3 at = _base + new Vector3(0, 1.16f + (f % 2) * 0.22f, 0)
                           + Quaternion.Euler(-30f, yaw, 0f) * Vector3.forward * 0.56f;
                for (int p = 0; p < 5; p++)
                {
                    var petal = VillaKit.Sph("Petal", at, 1f, Petal);
                    if (petal == null) continue;
                    float pa = p * 72f;
                    petal.transform.localScale = new Vector3(0.055f, 0.018f, 0.10f);
                    petal.transform.rotation = Quaternion.Euler(-20f, pa, 0f);
                    petal.transform.position = at + Quaternion.Euler(-20f, pa, 0f) * Vector3.forward * 0.055f;
                    Under(petal);
                }
                Under(VillaKit.Sph("FlowerCore", at, 0.055f, Core));
            }
        }

        /// <summary>结果：花谢挂果——果柄弯下来，果子有点垂感，不是贴在枝上的球。</summary>
        void BuildFruiting(Transform _)
        {
            BuildBushy(_);
            for (int f = 0; f < 5; f++)
            {
                float yaw = f * 72f + 18f;
                Vector3 hang = _base + new Vector3(0, 1.10f + (f % 2) * 0.20f, 0)
                             + Quaternion.Euler(-26f, yaw, 0f) * Vector3.forward * 0.52f;
                Under(VillaKit.Cyl("FruitStalk", hang + new Vector3(0, -0.02f, 0), 0.010f, 0.10f, Stem));
                var fr = VillaKit.Sph("Fruit", hang + new Vector3(0, -0.14f, 0), 1f, Fruit);
                if (fr != null)
                {
                    // 略扁略长 = 果实的重量感；正球读起来像塑料珠子
                    fr.transform.localScale = new Vector3(0.17f, 0.20f, 0.17f);
                    Under(fr);
                }
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
            // 浇水是蹲下去摆弄一盆花：Fixing_Kneeling 正是这个动作；
            // 拿不到就退回通用的 Interact（站着伸手）。
            // 直接用本类已有的 _player（Update 里已取好）。
            // 注意别写成 Core.ActorRegistry——这个类里有个叫 Core 的 Color 字段
            //（花芯的颜色），那样会被解析成"在 Color 上找 ActorRegistry"。
            var ha = _player != null
                ? _player.GetComponentInChildren<AdversityRoad.Combat.HumanoidAnimator>() : null;
            if (ha != null) ha.PlayFirstClip(1f, 0.25f, "fixing_kneeling", "interact");
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
