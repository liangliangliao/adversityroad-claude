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
        bool _seatCapture;                                  // 收刀：末段入座起点已快照
        Vector3 _seatStartP, _seatStartS; Quaternion _seatStartR;
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
            // 拔刀：前 35% 剑仍留鞘中（左手呈鞘、右手动画伸手来取），到点才交接到右手；
            // 收刀：剑本就在右手，保持握持，末段送入左手中的鞘
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
                    if (!_transferred && t >= 0.35f)
                    {
                        // 交接：右手从鞘口接过剑柄，此后剑随右手（绝不悬空）
                        _transferred = true;
                        _blade.SetParent(_rightHand, false);
                        _blade.localPosition = _dLP; _blade.localRotation = _dLR; _blade.localScale = _dLS;
                        _addGrip?.Invoke(_rightHand);
                    }
                    if (!_transferred && _blade.parent == _scab)
                    {
                        _blade.localPosition = _sLP; _blade.localRotation = _sLR; _blade.localScale = _sLS;
                    }
                }
                else if (t >= 0.65f)
                {
                    // 末段：右手把剑送回左手中的鞘——世界位姿平滑并入"入座"（目标实时
                    // 取自左手中的鞘，鞘不动、剑входит；e=1 与装配姿态完全一致，零跳变）
                    if (!_seatCapture)
                    {
                        _seatCapture = true;
                        _seatStartP = _blade.position; _seatStartR = _blade.rotation; _seatStartS = _blade.lossyScale;
                    }
                    float e2 = Mathf.SmoothStep(0f, 1f, (t - 0.65f) / 0.35f);
                    Vector3 pT = _scab.TransformPoint(_sLP);
                    Quaternion rT = _scab.rotation * _sLR;
                    Vector3 sT = Vector3.Scale(_scab.lossyScale, _sLS);
                    _blade.SetPositionAndRotation(
                        Vector3.Lerp(_seatStartP, pT, e2),
                        Quaternion.Slerp(_seatStartR, rT, e2));
                    Vector3 ws = Vector3.Lerp(_seatStartS, sT, e2);
                    Vector3 ps = _blade.parent != null ? _blade.parent.lossyScale : Vector3.one;
                    _blade.localScale = new Vector3(
                        Mathf.Approximately(ps.x, 0f) ? ws.x : ws.x / ps.x,
                        Mathf.Approximately(ps.y, 0f) ? ws.y : ws.y / ps.y,
                        Mathf.Approximately(ps.z, 0f) ? ws.z : ws.z / ps.z);
                }

                if (_t >= 1f)
                {
                    _anim = false;
                    _drawn = _toDrawn;
                    _transferred = false; _seatCapture = false;
                    if (!_drawn)
                    {
                        // 入座：撤右手握拳，剑落回鞘下的装配姿态（末段已对齐，零跳变）
                        _removeGrip?.Invoke(_rightHand);
                        _blade.SetParent(_scab, false);
                        _blade.localPosition = _sLP; _blade.localRotation = _sLR; _blade.localScale = _sLS;
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
