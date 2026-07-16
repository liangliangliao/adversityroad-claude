using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 剑鞘·拔刀/收刀控制器（武术行家口径）。
    ///
    /// 收刀（默认）：剑在鞘中（挂鞘节点下、每帧复位，不可能分离）；左手竖提剑鞘
    /// （柄朝上微前倾），任何动作下都是自然携持。
    ///
    /// 拔/收刀过渡（与 Draw/Sheathing 动作时长同步）：剑【全程握在右手】，绝不脱手
    /// 悬空；剑鞘转为【横向呈鞘】——鞘口套在剑身上、沿剑轴滑动：
    ///   拔刀 = 鞘自剑身上退开直至剑尖脱鞘；收刀 = 鞘口自剑尖吞入直至柄底入座。
    /// 呈鞘姿态有 0.3 段的柔和渐入（从携持姿态摆到横向），收刀末端把鞘的相对姿态
    /// 并到装配姿态（含 roll），入座零跳变。
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
        Vector3 _setStartP; Quaternion _setStartR;         // 呈鞘渐入起点（世界）

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
            if (_set != null) { _setStartP = _set.position; _setStartR = _set.rotation; }
            if (_toDrawn)
            {
                // 拔刀从第一帧起就握柄——手不离柄；鞘随后被"呈鞘"逻辑套在剑上滑退
                _blade.SetParent(_rightHand, false);
                _blade.localPosition = _dLP; _blade.localRotation = _dLR; _blade.localScale = _dLS;
                _addGrip?.Invoke(_rightHand);
            }
            // 收刀：剑本就在右手，保持握持直到完全入鞘
        }

        void LateUpdate()
        {
            if (_blade == null || _scab == null) return;

            if (_anim)
            {
                _t += Time.deltaTime / _dur;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));
                PresentScabbard(e);
                if (_t >= 1f)
                {
                    _anim = false;
                    _drawn = _toDrawn;
                    if (!_drawn)
                    {
                        // 入座：撤右手握拳，剑落回鞘下的装配姿态（呈鞘末端已对齐，零跳变）
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

        /// <summary>呈鞘：鞘口套在剑身上、沿剑轴滑动（k=入鞘深度）。前 0.3 段自携持
        /// 姿态柔和摆到横向；收刀末端把鞘姿态并到装配相对姿态（roll 吻合，入座零跳变）。</summary>
        void PresentScabbard(float e)
        {
            if (_set == null) return;
            float k = _toDrawn ? 1f - e : e;
            Vector3 gripW = _blade.TransformPoint(_gripL);
            Vector3 tipW = _blade.TransformPoint(_tipL);
            Vector3 a = tipW - gripW;
            if (a.sqrMagnitude < 1e-10f) return;
            float bladeLenW = a.magnitude; a /= bladeLenW;

            Vector3 mouthW = _scab.TransformPoint(_mouthPt);
            Vector3 botW = _scab.TransformPoint(_botPt);
            Vector3 cur = botW - mouthW;
            float scabLenW = cur.magnitude;
            if (scabLenW < 1e-6f) return;

            // 鞘轴(口→底)对齐剑轴(柄→尖)
            Quaternion target = Quaternion.FromToRotation(cur / scabLenW, a) * _set.rotation;
            if (!_toDrawn)
            {
                // 收刀末端并入装配相对姿态：scab.rotation → blade.rotation * inv(sLR)
                Quaternion scabSeat = _blade.rotation * Quaternion.Inverse(_sLR);
                Quaternion rRel = Quaternion.Inverse(_set.rotation) * _scab.rotation;
                target = Quaternion.Slerp(target, scabSeat * Quaternion.Inverse(rRel), e);
            }
            float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / 0.3f));
            _set.rotation = Quaternion.Slerp(_setStartR, target, ramp);

            // 鞘口落在剑上的插入点（自剑尖回退 insert；k=1 时与入座位置精确一致）
            float insert = k * Mathf.Min(scabLenW * 0.98f, bladeLenW * 0.95f);
            Vector3 mouthTarget = tipW - a * insert;
            Vector3 posTarget = _set.position + (mouthTarget - _scab.TransformPoint(_mouthPt));
            _set.position = Vector3.Lerp(_setStartP, posTarget, ramp);
        }

        /// <summary>每帧自然携持：鞘轴(鞘底→鞘口)对齐"竖直微前倾"、鞘中点贴左手掌心。
        /// 只做最小旋转修正，绕竖轴的朝向仍随手转动（转身时剑鞘自然跟随）。</summary>
        void CarryScabbard()
        {
            if (_set == null || _lhand == null) return;
            Vector3 axisW = _scab.TransformPoint(_mouthPt) - _scab.TransformPoint(_botPt);
            if (axisW.sqrMagnitude < 1e-10f) return;
            Vector3 fwd = _visual != null
                ? Vector3.ProjectOnPlane(_visual.forward, Vector3.up) : Vector3.forward;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            Vector3 want = (Vector3.up * 0.92f + fwd.normalized * 0.39f).normalized;
            _set.rotation = Quaternion.FromToRotation(axisW.normalized, want) * _set.rotation;
            _set.position += _lhand.TransformPoint(_palmL) - _scab.TransformPoint(_midPt);
        }
    }
}
