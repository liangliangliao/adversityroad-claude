using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 目标看板旁边的兵器架：可以把手里的兵器**真的放下**，也可以再拿起来。
    ///
    /// 为什么值得做：这栋房子是全游戏唯一没有敌人的地方，而角色一直攥着一把剑
    /// 坐沙发、躺床、跑跑步机——那不像回家，像随时准备开打。放下兵器这个动作
    /// 本身就是"到家了"的表达，也和目标看板放在一起：出门前从这里取剑，
    /// 回家先把剑放下。
    ///
    /// 动作用 Anims/Putting Down（弯身放下→站起）：放与取共用同一段，
    /// 在动作最低点（弯到底那一刻）交换"手里的剑"与"架上的剑"，
    /// 于是放下和拿起看着都是一次完整的弯身动作，而不是道具凭空闪现。
    /// </summary>
    public class WeaponRack : MonoBehaviour
    {
        /// <summary>兵器躺在架上的位置（世界坐标）。</summary>
        public Vector3 cradleAt;
        /// <summary>玩家站在架前的朝向（架子面朝哪）。</summary>
        public Vector3 faceDir = Vector3.forward;
        public float range = 3.4f;

        /// <summary>动作里"弯到最低点"的相对位置：在这一刻交换手上/架上的剑。</summary>
        const float SwapAt = 0.46f;
        /// <summary>只播动作中间那段（前后各有一段站着不动的余量），并略微提速。</summary>
        const float ClipStart = 0.22f, ClipEnd = 0.62f, ClipSpeed = 2f;

        static readonly string[] PutDownKeys = { "putting down", "put down", "picking up" };

        Transform _prop;          // 架上那把剑（替身几何）
        bool _stored;             // 兵器现在在架上
        float _swapAt, _endAt;    // 本次动作的交换时刻 / 结束时刻
        bool _swapped;
        PlayerController _pc;
        PlayerAppearance _appearance;
        Combat.HumanoidAnimator _anim;
        float _lastHint = -99f;

        /// <summary>在 at 处建一个兵器架（面朝 faceDir）。</summary>
        public static WeaponRack Build(WorldContext ctx, Vector3 at, Vector3 faceDir)
        {
            var wood = new Color(0.36f, 0.25f, 0.17f);
            var metal = new Color(0.62f, 0.64f, 0.68f);
            faceDir = faceDir.sqrMagnitude < 0.01f ? Vector3.forward : faceDir.normalized;
            float yaw = Quaternion.LookRotation(faceDir).eulerAngles.y;
            Vector3 right = Quaternion.Euler(0, yaw, 0) * Vector3.right;

            // 底座 + 两根立柱 + 两道托架：一眼能看出是"放兵器的地方"
            var body = VillaKit.Box("WeaponRack", at + new Vector3(0, 0.09f, 0),
                new Vector3(1.9f, 0.18f, 0.8f), wood, new Vector3(0, yaw, 0));
            for (int s = -1; s <= 1; s += 2)
            {
                VillaKit.Cyl("RackPost", at + right * (s * 0.7f), 0.055f, 1.35f, wood, true);
                VillaKit.Metal(VillaKit.CylAxis("RackCradle",
                    at + right * (s * 0.7f) + new Vector3(0, 1.18f, 0), 0.05f, 0.34f, metal,
                    new Vector3(90f, yaw, 0)), metal);
            }
            VillaKit.Metal(VillaKit.CylAxis("RackBar", at + new Vector3(0, 0.72f, 0),
                0.035f, 1.5f, metal, new Vector3(0, yaw, 90f)), metal);
            VillaKit.WallSign(at + new Vector3(0, 1.75f, 0), "兵 器 架", faceDir, 1.7f);

            var rack = body.AddComponent<WeaponRack>();
            rack.cradleAt = at + new Vector3(0, 1.22f, 0);
            rack.faceDir = faceDir;

            // 架上的替身剑：放下时显示（玩家手里那把的视觉替身）
            var prop = new GameObject("RackedSword");
            prop.transform.SetParent(VillaKit.Root, true);
            prop.transform.position = rack.cradleAt;
            prop.transform.rotation = Quaternion.Euler(0, yaw, 90f);
            var blade = VillaKit.CylAxis("PropBlade", rack.cradleAt + new Vector3(0, 0.03f, 0),
                0.035f, 1.35f, new Color(0.78f, 0.82f, 0.88f), new Vector3(0, yaw, 90f));
            VillaKit.Metal(blade, new Color(0.80f, 0.84f, 0.90f));
            blade.transform.SetParent(prop.transform, true);
            var guard = VillaKit.Box("PropGuard", rack.cradleAt + right * -0.62f + new Vector3(0, 0.03f, 0),
                new Vector3(0.08f, 0.1f, 0.30f), new Color(0.55f, 0.44f, 0.22f), new Vector3(0, yaw, 0));
            guard.transform.SetParent(prop.transform, true);
            var grip = VillaKit.CylAxis("PropGrip", rack.cradleAt + right * -0.78f + new Vector3(0, 0.03f, 0),
                0.03f, 0.28f, new Color(0.24f, 0.18f, 0.14f), new Vector3(0, yaw, 90f));
            grip.transform.SetParent(prop.transform, true);
            prop.SetActive(false);
            rack._prop = prop.transform;
            return rack;
        }

        void Update()
        {
            // 动作进行中：到最低点交换，播完把控制权还给玩家
            if (_endAt > 0f)
            {
                if (!_swapped && Time.time >= _swapAt) DoSwap();
                if (Time.time >= _endAt)
                {
                    _endAt = 0f;
                    if (_pc != null) _pc.enabled = true;
                }
                return;
            }

            if (SitController.Busy) return;
            if (_pc == null)
            {
                _pc = AdversityRoad.Core.ActorRegistry.Player;
                if (_pc == null) return;
                _appearance = _pc.GetComponent<PlayerAppearance>();
                _anim = _pc.GetComponent<Combat.HumanoidAnimator>();
            }
            if (Vector3.Distance(transform.position, _pc.transform.position) > range) return;

            if (Time.time - _lastHint > 8f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("【兵器架】" + Mobile.MobileInput.UseHint +
                    (_stored ? "取回兵器。" : "把兵器放下。"));
            }
            if (!(Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact"))) return;

            if (!_stored && (_appearance == null || !_appearance.HasWeaponInHand()))
            {
                GameEvents.RaiseSubtitle("手里没有兵器。");
                return;
            }
            Begin();
        }

        void Begin()
        {
            // 面朝架子弯身：不转过来的话，人会背对着架子把剑放到身后
            Vector3 to = transform.position - _pc.transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.04f)
                _pc.transform.rotation = Quaternion.LookRotation(to.normalized);

            float dur = 0f;
            if (_anim != null)
            {
                _anim.SetLocomotion(0f, false, true, 0f);
                dur = _anim.PlayRestClip(FirstAvailable(), false, false, ClipSpeed, 0.2f,
                    ClipStart, ClipEnd);
            }
            if (dur <= 0.05f) dur = 0.5f;      // 没有片段也要能放下（只是没有过程动画）
            _swapAt = Time.time + dur * SwapAt;
            _endAt = Time.time + dur;
            _swapped = false;
            _pc.enabled = false;               // 动作期间站定，别一边走一边放
        }

        string FirstAvailable()
        {
            if (_anim == null) return PutDownKeys[0];
            foreach (var k in PutDownKeys)
                if (_anim.HasClip(k)) return k;
            return PutDownKeys[0];
        }

        void DoSwap()
        {
            _swapped = true;
            _stored = !_stored;
            if (_prop != null) _prop.gameObject.SetActive(_stored);
            if (_appearance != null) _appearance.SetWeaponVisible(!_stored);
            GameEvents.RaiseSubtitle(_stored
                ? "兵器放在架上了——在家不必带着它。"
                : "取回了兵器。");
            GameAudio.Play(GameAudio.Sfx.Cast, 0.25f);
        }

        /// <summary>世界重建时兜底：兵器不能"留在上一个世界的架子上"。</summary>
        void OnDisable()
        {
            if (_endAt > 0f && _pc != null) _pc.enabled = true;
            _endAt = 0f;
            if (_stored && _appearance != null)
            {
                _appearance.SetWeaponVisible(true);
                _stored = false;
            }
        }
    }
}
