using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 剑鞘·拔刀/收刀控制器（武术行家口径）。
    ///
    /// 收刀（默认）：剑在鞘中（挂鞘节点下、每帧复位，不可能分离）；左手竖提剑鞘
    /// （柄朝上微前倾），任何动作下都是自然携持。
    ///
    /// 拔/收刀过渡（与 Draw/Sheathing 动作时长同步）：剑鞘【始终钉在左手掌心】
    /// （绝不跟着剑跑到右手），仅把鞘口迎向剑柄方向——左手横托呈鞘：
    ///   拔刀 = 前 35% 剑留鞘中（右手动画伸手来取），交接后剑随右手抽离；
    ///   收刀 = 右手持剑送回，末段 35% 剑的世界位姿平滑并入左手鞘中的装配姿态
    ///   （目标实时取自鞘，e=1 与装配完全一致，入座零跳变）。
    /// </summary>
    [DefaultExecutionOrder(5000)]   // 在所有动画/骨骼驱动之后跑，复位与呈鞘不被覆盖
    public class WeaponSheath : MonoBehaviour
    {
        Transform _blade, _scab, _rightHand;
        Vector3 _sLP, _sLS; Quaternion _sLR;      // 收刀本地姿态（相对剑鞘节点）
        Vector3 _dLP, _dLS; Quaternion _dLR;      // 拔刀本地姿态（相对右手）
        Vector3 _gripL, _tipL;                     // 剑柄端/剑尖（剑组本地，精修中轴）
        System.Action<Transform> _addGrip, _removeGrip;   // 右手握拳/枢轴 挂/撤
        bool _drawn;

        // 过渡
        bool _anim; float _t, _dur; bool _toDrawn;
        bool _transferred;                                  // 拔刀：剑已交接到右手
        bool _seatCapture;                                  // （保留位：分段边界一次性动作标记）
        Quaternion _setStartR;                              // 呈鞘渐入起点（世界）

        // 自然携持（每帧强制）：左手提鞘、鞘身竖直微前倾、鞘中点贴掌心
        Transform _set, _lhand, _visual;
        Vector3 _mouthPt, _botPt, _midPt;          // 鞘口/鞘底/鞘中点（鞘本地）
        Vector3 _palmL;                            // 掌心（左手本地）

        public bool IsDrawn => _drawn;

        public void Setup(Transform blade, Transform scab,
            Vector3 sLP, Quaternion sLR, Vector3 sLS,
            Transform rightHand, Vector3 dLP, Quaternion dLR, Vector3 dLS,
            Vector3 gripLocal, Vector3 tipLocal,
            System.Action<Transform> addGrip, System.Action<Transform> removeGrip)
        {
            _blade = blade; _scab = scab;
            _sLP = sLP; _sLR = sLR; _sLS = sLS;
            _rightHand = rightHand; _dLP = dLP; _dLR = dLR; _dLS = dLS;
            _gripL = gripLocal; _tipL = tipLocal;
            _addGrip = addGrip; _removeGrip = removeGrip;
            _drawn = false; _anim = false;
        }

        /// <summary>配置自然携持：整套(set)挂在左手，每帧把鞘轴摆竖直（刀柄朝上微前倾）、
        /// 鞘中点贴掌心——与绑定姿势/手骨朝向无关。</summary>
        public void SetCarry(Transform set, Transform lhand, Transform visual,
            Vector3 mouthPt, Vector3 botPt, Vector3 midPt, Vector3 palmLocal)
        {
            _set = set; _lhand = lhand; _visual = visual;
            _mouthPt = mouthPt; _botPt = botPt; _midPt = midPt; _palmL = palmLocal;
        }

        // 过渡分段（占总时长比例）——所有分段两端都取【活】目标（跟手或锚定鞘口），
        // 剑没有任何"自由飘移"段，也不可能斜穿鞘壁：
        //   拔刀: [0,0.30]剑留鞘中 → [0.30,0.55]沿鞘轴滑出到鞘口 → [0.55,0.80]鞘口→手(双活端) → 交接右手
        //   收刀: [0,0.50]随手 → [0.50,0.75]手(活)→鞘口(活)对口 → [0.75,1]沿鞘轴滑入到座
        const float DrawSlide = 0.30f, DrawHand = 0.55f, DrawGrab = 0.80f;
        const float SheatheAim = 0.50f, SheatheSlide = 0.75f;

        /// <summary>在拔刀/收刀之间切换，用 dur 秒过渡（与动画时长同步）。</summary>
        public void Toggle(float dur)
        {
            if (_blade == null || _scab == null || _rightHand == null) return;
            if (_anim) return;                      // 过渡中不重复触发
            _toDrawn = !_drawn;
            _dur = Mathf.Max(0.25f, dur);
            _t = 0f; _anim = true;
            _transferred = false; _seatCapture = false;
            if (_set != null) { _setStartR = _set.rotation; }
        }

        /// <summary>鞘口姿态（鞘本地）：座位姿态沿鞘轴外移一个鞘长——剑尖恰在鞘口。</summary>
        Vector3 MouthLP()
        {
            Vector3 outL = _mouthPt - _botPt;
            float len = outL.magnitude;
            return len < 1e-6f ? _sLP : _sLP + outL / len * (len * 0.98f);
        }

        void LateUpdate()
        {
            if (_blade == null || _scab == null) return;

            if (_anim)
            {
                _t += Time.deltaTime / _dur;
                float t = Mathf.Clamp01(_t);
                AimScabbard();   // 鞘始终钉在左手掌心，鞘口迎向剑柄（横向呈鞘）

                if (_toDrawn)
                {
                    if (t < DrawSlide)
                    {
                        if (_blade.parent == _scab)
                        { _blade.localPosition = _sLP; _blade.localRotation = _sLR; _blade.localScale = _sLS; }
                    }
                    else if (t < DrawHand)
                    {
                        // 沿鞘轴滑出：座位→鞘口（纯轴向，剑始终在鞘管里）
                        float e = Mathf.SmoothStep(0f, 1f, (t - DrawSlide) / (DrawHand - DrawSlide));
                        _blade.localRotation = _sLR; _blade.localScale = _sLS;
                        _blade.localPosition = Vector3.Lerp(_sLP, MouthLP(), e);
                    }
                    else if (!_transferred)
                    {
                        // 鞘口(活)→右手握位(活)：两端点都实时跟随，t=DrawGrab 时与手位
                        // 完全一致，交接零跳变
                        float e = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, (t - DrawHand) / (DrawGrab - DrawHand)));
                        WorldPose(_scab, MouthLP(), _sLR, _sLS, out Vector3 mP, out Quaternion mR, out Vector3 mS);
                        WorldPose(_rightHand, _dLP, _dLR, _dLS, out Vector3 hP, out Quaternion hR, out Vector3 hS);
                        SetBladeWorld(Vector3.Lerp(mP, hP, e), Quaternion.Slerp(mR, hR, e), Vector3.Lerp(mS, hS, e));
                        if (t >= DrawGrab)
                        {
                            _transferred = true;
                            _blade.SetParent(_rightHand, false);
                            _blade.localPosition = _dLP; _blade.localRotation = _dLR; _blade.localScale = _dLS;
                            _addGrip?.Invoke(_rightHand);
                        }
                    }
                }
                else if (t >= SheatheSlide)
                {
                    // 末段：沿鞘轴滑入——剑尖已在鞘口，纯轴向推到座位（真实入鞘轨迹）
                    if (_blade.parent != _scab)
                    {
                        _removeGrip?.Invoke(_rightHand);    // 手放开，剑顺鞘管滑到底
                        _blade.SetParent(_scab, true);
                    }
                    float e = Mathf.SmoothStep(0f, 1f, (t - SheatheSlide) / (1f - SheatheSlide));
                    _blade.localRotation = _sLR; _blade.localScale = _sLS;
                    _blade.localPosition = Vector3.Lerp(MouthLP(), _sLP, e);
                }
                else if (t >= SheatheAim)
                {
                    // 对口：右手握位(活)→鞘口(活)——剑尖被引导到鞘口、剑轴对齐鞘轴，
                    // 两端点都实时跟随（跟手/跟鞘），e=1 时剑尖恰好落在鞘口
                    float e = Mathf.SmoothStep(0f, 1f, (t - SheatheAim) / (SheatheSlide - SheatheAim));
                    WorldPose(_rightHand, _dLP, _dLR, _dLS, out Vector3 hP, out Quaternion hR, out Vector3 hS);
                    WorldPose(_scab, MouthLP(), _sLR, _sLS, out Vector3 mP, out Quaternion mR, out Vector3 mS);
                    SetBladeWorld(Vector3.Lerp(hP, mP, e), Quaternion.Slerp(hR, mR, e), Vector3.Lerp(hS, mS, e));
                }

                if (_t >= 1f)
                {
                    _anim = false;
                    _drawn = _toDrawn;
                    _transferred = false; _seatCapture = false;
                    if (!_drawn)
                    {
                        if (_blade.parent != _scab)
                        {
                            _removeGrip?.Invoke(_rightHand);
                            _blade.SetParent(_scab, false);
                        }
                        _blade.localPosition = _sLP; _blade.localRotation = _sLR; _blade.localScale = _sLS;
                    }
                    else if (!_transferred)
                    {
                        // 极短时长下可能没走到交接段：直接落到右手握位
                        _blade.SetParent(_rightHand, false);
                        _blade.localPosition = _dLP; _blade.localRotation = _dLR; _blade.localScale = _dLS;
                        _addGrip?.Invoke(_rightHand);
                    }
                }
                return;
            }

            CarryScabbard();
            // 收刀静止态：每帧复位鞘本地姿态——任何外部改写都被纠正
            if (!_drawn && _blade.parent == _scab)
            {
                _blade.localPosition = _sLP;
                _blade.localRotation = _sLR;
                _blade.localScale = _sLS;
            }
        }

        /// <summary>呈鞘（过渡期间）：鞘中点【始终钉在左手掌心】（鞘绝不离开左手、
        /// 绝不跟着剑跑），仅把鞘口方向迎向剑柄（剑未出鞘时迎向来取剑的右手）——
        /// 左手横托剑鞘、右手精准拔/收，鞘口与剑的对准由双手动画自然完成。</summary>
        void AimScabbard()
        {
            if (_set == null || _lhand == null) return;
            Vector3 palmW = _lhand.TransformPoint(_palmL);
            bool bladeOut = !_toDrawn || _transferred;
            Vector3 aimPt = bladeOut ? _blade.TransformPoint(_gripL) : _rightHand.position;
            Vector3 aim = aimPt - palmW;
            Vector3 axisW = _scab.TransformPoint(_mouthPt) - _scab.TransformPoint(_botPt);   // 底→口
            if (aim.sqrMagnitude < 1e-8f || axisW.sqrMagnitude < 1e-10f) return;
            Quaternion target = Quaternion.FromToRotation(axisW.normalized, aim.normalized) * _set.rotation;
            float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / 0.3f));
            _set.rotation = Quaternion.Slerp(_setStartR, target, ramp);
            _set.position += palmW - _scab.TransformPoint(_midPt);   // 鞘中点钉掌心
        }

        static void WorldPose(Transform parent, Vector3 lp, Quaternion lr, Vector3 ls,
            out Vector3 pos, out Quaternion rot, out Vector3 scl)
        {
            pos = parent.TransformPoint(lp);
            rot = parent.rotation * lr;
            scl = Vector3.Scale(parent.lossyScale, ls);
        }

        void SetBladeWorld(Vector3 pos, Quaternion rot, Vector3 worldScale)
        {
            _blade.SetPositionAndRotation(pos, rot);
            Vector3 ps = _blade.parent != null ? _blade.parent.lossyScale : Vector3.one;
            _blade.localScale = new Vector3(
                Mathf.Approximately(ps.x, 0f) ? worldScale.x : worldScale.x / ps.x,
                Mathf.Approximately(ps.y, 0f) ? worldScale.y : worldScale.y / ps.y,
                Mathf.Approximately(ps.z, 0f) ? worldScale.z : worldScale.z / ps.z);
        }

        /// <summary>每帧自然携持：鞘轴(鞘底→鞘口)对齐"竖直微前倾"、鞘中点贴左手掌心。
        /// 旋转带平滑（呈鞘结束后柔和转回竖提）；绕竖轴朝向仍随手转动。</summary>
        void CarryScabbard()
        {
            if (_set == null || _lhand == null) return;
            Vector3 axisW = _scab.TransformPoint(_mouthPt) - _scab.TransformPoint(_botPt);
            if (axisW.sqrMagnitude < 1e-10f) return;
            Vector3 fwd = _visual != null
                ? Vector3.ProjectOnPlane(_visual.forward, Vector3.up) : Vector3.forward;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            Vector3 want = (Vector3.up * 0.92f + fwd.normalized * 0.39f).normalized;
            Quaternion target = Quaternion.FromToRotation(axisW.normalized, want) * _set.rotation;
            _set.rotation = Quaternion.Slerp(_set.rotation, target,
                1f - Mathf.Exp(-12f * Time.deltaTime));
            _set.position += _lhand.TransformPoint(_palmL) - _scab.TransformPoint(_midPt);
        }
    }
}
