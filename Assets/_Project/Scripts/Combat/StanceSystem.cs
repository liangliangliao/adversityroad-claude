using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Mobile;
using AdversityRoad.Personalization;

namespace AdversityRoad.Combat
{
    public enum StanceKind { Startup, Boundary, Focus, Fact, Will }

    /// <summary>
    /// 战斗姿态系统（方案「五种姿态」）：起步 / 边界 / 定心 / 事实 / 意志。
    /// 每种姿态对应一类困境——把姿态切到与当前威胁匹配的一档，可大幅削减该弱点轴的
    /// 心理伤害、并在言语攻防里获得契合加成；进攻型姿态则略微提升物理输出。
    /// 姿态由玩家主动切换（Tab/F 循环，或 HUD 姿态条点选），体现"我先稳住/我先行动"的选择。
    /// </summary>
    public class StanceSystem : MonoBehaviour
    {
        public struct StanceDef
        {
            public StanceKind kind;
            public string name;
            public string mantra;         // 心法核心句
            public float outgoingMult;    // 物理输出倍率
            public WeaknessAxis[] covered; // 该姿态克制（大幅减伤 + 言语契合）的弱点轴
        }

        // 顺序与 HUD 姿态条一致
        public static readonly StanceDef[] Defs =
        {
            new StanceDef {
                kind = StanceKind.Startup, name = "起步", mantra = "不要想太多，先动起来。",
                outgoingMult = 1.06f,
                covered = new[] { WeaknessAxis.Procrastination, WeaknessAxis.JobAnxiety, WeaknessAxis.LowConfidence } },
            new StanceDef {
                kind = StanceKind.Boundary, name = "边界", mantra = "我守住我自己。",
                outgoingMult = 1.0f,
                covered = new[] { WeaknessAxis.BoundaryConflict, WeaknessAxis.FairnessSensitivity } },
            new StanceDef {
                kind = StanceKind.Focus, name = "定心", mantra = "外界可以存在，但不能接管我。",
                outgoingMult = 1.0f,
                covered = new[] { WeaknessAxis.NoiseSensitivity } },
            new StanceDef {
                kind = StanceKind.Fact, name = "事实", mantra = "发生了什么，先说清楚。",
                outgoingMult = 1.08f,
                covered = new[] { WeaknessAxis.FairnessSensitivity, WeaknessAxis.JobAnxiety } },
            new StanceDef {
                kind = StanceKind.Will, name = "意志", mantra = "我害怕，但我继续向前。",
                outgoingMult = 1.10f,
                covered = new[] { WeaknessAxis.WillpowerCollapse, WeaknessAxis.Shame,
                                  WeaknessAxis.SelfDoubt, WeaknessAxis.FailureFear } },
        };

        int _index;
        bool _announcedOnce;

        public int Index => _index;
        public StanceDef CurrentDef => Defs[_index];

        void Start() => Announce(); // 首帧同步 HUD（不喊心法字幕）

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.F) ||
                MobileInput.GetDown("Stance"))
                Cycle();
        }

        public void Cycle() { _index = (_index + 1) % Defs.Length; Announce(); }

        public void SetStance(int i)
        {
            if (i < 0 || i >= Defs.Length || i == _index) return;
            _index = i;
            Announce();
        }

        void Announce()
        {
            var d = Defs[_index];
            GameEvents.RaiseStanceChanged(_index, d.name, d.mantra);
            if (_announcedOnce)
                GameEvents.RaiseSubtitle("姿态 · " + d.name + "：" + d.mantra);
            _announcedOnce = true;
        }

        public float OutgoingPhysicalMult() => Defs[_index].outgoingMult;

        public bool CoversAxis(WeaknessAxis axis)
        {
            var cov = Defs[_index].covered;
            for (int i = 0; i < cov.Length; i++) if (cov[i] == axis) return true;
            return false;
        }

        /// <summary>被匹配轴命中时心理伤害 ×0.4（减 60%），否则不变。</summary>
        public float IncomingMentalMult(WeaknessAxis axis) => CoversAxis(axis) ? 0.4f : 1f;
    }
}
