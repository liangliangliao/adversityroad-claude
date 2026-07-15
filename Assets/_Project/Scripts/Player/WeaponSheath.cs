using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 剑鞘·拔刀/收刀控制器（带剑鞘的成套武器，如 scene「Highland 剑」）。
    ///
    /// 收刀（默认）：剑身以【鞘本地姿态】直接挂在剑鞘节点下（纯本地 TRS、解析求出，
    /// 与骨骼缩放/世界姿态无关），且每帧复位——任何外部改写都会被纠正，剑与鞘不可能分离。
    ///
    /// 拔/收刀是【两段式动画】（与 Draw/Sheathing 动作片段时长同步）：
    ///   收刀：先把剑移到【鞘口】、剑尖对准鞘口且剑轴对齐鞘轴 → 再沿鞘轴缓缓滑入鞘底；
    ///   拔刀：先沿鞘轴把剑从鞘口缓缓抽出 → 再挥到右手握持位。
    /// 鞘口/鞘底两端位姿实时跟随移动中的手（鞘挂左手），所以"对准鞘口插入"始终成立。
    /// </summary>
    [DefaultExecutionOrder(5000)]   // 在所有动画/骨骼驱动之后跑，复位与过渡不被覆盖
    public class WeaponSheath : MonoBehaviour
    {
        Transform _blade, _scab, _rightHand;
        Vector3 _sLP, _sLS; Quaternion _sLR;      // 收刀本地姿态（相对剑鞘节点）
        Vector3 _mouthDir; float _slide;           // 鞘口方向（鞘本地、指向鞘外）与抽出距离
        Vector3 _dLP, _dLS; Quaternion _dLR;      // 拔刀本地姿态（相对右手）
        System.Action<Transform> _addGrip, _removeGrip;   // 右手握拳/枢轴 挂/撤
        bool _drawn;

        // 过渡
        bool _anim; float _t, _dur; bool _toDrawn;
        Vector3 _startP, _startS; Quaternion _startR;   // 收刀起点（世界，Toggle 时快照）

        public bool IsDrawn => _drawn;

        public void Setup(Transform blade, Transform scab,
            Vector3 sLP, Quaternion sLR, Vector3 sLS,
            Vector3 mouthDirLocal, float slideDist,
            Transform rightHand, Vector3 dLP, Quaternion dLR, Vector3 dLS,
            System.Action<Transform> addGrip, System.Action<Transform> removeGrip)
        {
            _blade = blade; _scab = scab;
            _sLP = sLP; _sLR = sLR; _sLS = sLS;
            _mouthDir = mouthDirLocal; _slide = slideDist;
            _rightHand = rightHand; _dLP = dLP; _dLR = dLR; _dLS = dLS;
            _addGrip = addGrip; _removeGrip = removeGrip;
            _drawn = false; _anim = false;
        }

        /// <summary>在拔刀/收刀之间切换，用 dur 秒两段式过渡（与动画时长同步）。</summary>
        public void Toggle(float dur)
        {
            if (_blade == null || _scab == null || _rightHand == null) return;
            if (_anim) return;                      // 过渡中不重复触发
            _toDrawn = !_drawn;
            _dur = Mathf.Max(0.2f, dur);
            _t = 0f; _anim = true;
            _removeGrip?.Invoke(_rightHand);        // 撤右手握拳/枢轴（拔刀开始时本就没有，无害）
            if (_blade.parent != _scab)
                _blade.SetParent(_scab, true);      // 过渡全程挂鞘下，世界姿态不跳变
            _startP = _blade.position; _startR = _blade.rotation; _startS = _blade.lossyScale;
        }

        void LateUpdate()
        {
            if (_blade == null || _scab == null) return;

            if (!_anim)
            {
                // 收刀静止态：每帧复位鞘本地姿态——耍花/动画/任何外部改写都被纠正
                if (!_drawn && _blade.parent == _scab)
                {
                    _blade.localPosition = _sLP;
                    _blade.localRotation = _sLR;
                    _blade.localScale = _sLS;
                }
                return;
            }

            _t += Time.deltaTime / _dur;
            float t = Mathf.Clamp01(_t);

            if (_toDrawn)
            {
                // 拔刀：0~0.4 沿鞘轴滑出到鞘口，0.4~1 挥到右手握位
                if (t < 0.4f)
                {
                    float e = Mathf.SmoothStep(0f, 1f, t / 0.4f);
                    _blade.localRotation = _sLR; _blade.localScale = _sLS;
                    _blade.localPosition = _sLP + _mouthDir * (_slide * e);
                }
                else
                {
                    float e = Mathf.SmoothStep(0f, 1f, (t - 0.4f) / 0.6f);
                    WorldPose(_scab, _sLP + _mouthDir * _slide, _sLR, _sLS,
                        out Vector3 mP, out Quaternion mR, out Vector3 mS);
                    WorldPose(_rightHand, _dLP, _dLR, _dLS,
                        out Vector3 dP, out Quaternion dR, out Vector3 dS);
                    SetWorld(Vector3.Lerp(mP, dP, e), Quaternion.Slerp(mR, dR, e), Vector3.Lerp(mS, dS, e));
                }
            }
            else
            {
                // 收刀：0~0.55 把剑移到鞘口、剑轴对齐鞘轴（对准入口），0.55~1 沿鞘轴缓缓插入
                if (t < 0.55f)
                {
                    float e = Mathf.SmoothStep(0f, 1f, t / 0.55f);
                    WorldPose(_scab, _sLP + _mouthDir * _slide, _sLR, _sLS,
                        out Vector3 mP, out Quaternion mR, out Vector3 mS);
                    SetWorld(Vector3.Lerp(_startP, mP, e), Quaternion.Slerp(_startR, mR, e), Vector3.Lerp(_startS, mS, e));
                }
                else
                {
                    float e = Mathf.SmoothStep(0f, 1f, (t - 0.55f) / 0.45f);
                    _blade.localRotation = _sLR; _blade.localScale = _sLS;
                    _blade.localPosition = _sLP + _mouthDir * (_slide * (1f - e));
                }
            }

            if (_t >= 1f)
            {
                _anim = false;
                _drawn = _toDrawn;
                if (_drawn)
                {
                    _blade.SetParent(_rightHand, false);
                    _blade.localPosition = _dLP; _blade.localRotation = _dLR; _blade.localScale = _dLS;
                    _addGrip?.Invoke(_rightHand);
                }
                else
                {
                    _blade.localPosition = _sLP; _blade.localRotation = _sLR; _blade.localScale = _sLS;
                }
            }
        }

        static void WorldPose(Transform parent, Vector3 lp, Quaternion lr, Vector3 ls,
            out Vector3 pos, out Quaternion rot, out Vector3 scl)
        {
            pos = parent.TransformPoint(lp);
            rot = parent.rotation * lr;
            scl = Vector3.Scale(parent.lossyScale, ls);
        }

        void SetWorld(Vector3 pos, Quaternion rot, Vector3 worldScale)
        {
            _blade.SetPositionAndRotation(pos, rot);
            Vector3 ps = _blade.parent != null ? _blade.parent.lossyScale : Vector3.one;
            _blade.localScale = new Vector3(
                Mathf.Approximately(ps.x, 0f) ? worldScale.x : worldScale.x / ps.x,
                Mathf.Approximately(ps.y, 0f) ? worldScale.y : worldScale.y / ps.y,
                Mathf.Approximately(ps.z, 0f) ? worldScale.z : worldScale.z / ps.z);
        }
    }
}
