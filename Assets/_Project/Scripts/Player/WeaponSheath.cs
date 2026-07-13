using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 剑鞘·拔刀/收刀控制器（带剑鞘的成套武器，如 scene「Highland 剑」）。
    ///
    /// 收刀（默认）：剑身插在鞘中，整套（剑+鞘）挂左手。
    /// 拔刀：把剑身抽到右手（与其它剑一致握位）；剑鞘留左手。
    ///
    /// 拔/收刀是【平滑动画】：过渡期间把剑身挂到中立父节点，用【世界位姿】在
    /// 「右手握持位」与「插入鞘中位」之间缓动插值——两端位姿实时跟随移动中的手/鞘，
    /// 故收刀时剑身对准鞘口缓慢滑入、拔刀时缓慢抽出，且与同时播放的 Draw/Sheathing
    /// 动画时长同步。到位后落回对应父节点并挂/撤右手握拳。
    /// </summary>
    public class WeaponSheath : MonoBehaviour
    {
        Transform _blade, _neutral, _sheathParent, _rightHand;
        Vector3 _sLP, _sLS; Quaternion _sLR;     // 收刀本地姿态（相对鞘挂点）
        Vector3 _dLP, _dLS; Quaternion _dLR;     // 拔刀本地姿态（相对右手）
        System.Action<Transform> _addGrip, _removeGrip;   // 右手握拳 挂/撤
        bool _drawn;

        // 过渡
        bool _anim; float _t, _dur; bool _toDrawn;

        public bool IsDrawn => _drawn;

        public void Setup(Transform blade, Transform neutral,
            Transform sheathParent, Vector3 sLP, Quaternion sLR, Vector3 sLS,
            Transform rightHand, Vector3 dLP, Quaternion dLR, Vector3 dLS,
            System.Action<Transform> addGrip, System.Action<Transform> removeGrip)
        {
            _blade = blade; _neutral = neutral;
            _sheathParent = sheathParent; _sLP = sLP; _sLR = sLR; _sLS = sLS;
            _rightHand = rightHand; _dLP = dLP; _dLR = dLR; _dLS = dLS;
            _addGrip = addGrip; _removeGrip = removeGrip;
            _drawn = false; _anim = false;
        }

        /// <summary>在拔刀/收刀之间切换，用 dur 秒平滑过渡（与动画时长同步）。</summary>
        public void Toggle(float dur)
        {
            if (_blade == null || _neutral == null || _sheathParent == null || _rightHand == null) return;
            if (_anim) return;                      // 过渡中不重复触发
            _toDrawn = !_drawn;
            _dur = Mathf.Max(0.2f, dur);
            _t = 0f; _anim = true;
            _removeGrip?.Invoke(_rightHand);        // 过渡期间剑身在中立父节点，不需要右手握拳
            _blade.SetParent(_neutral, false);      // 挂中立父节点，用世界位姿驱动
        }

        void LateUpdate()
        {
            if (!_anim || _blade == null) return;
            _t += Time.deltaTime / _dur;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));

            WorldPose(_sheathParent, _sLP, _sLR, _sLS, out Vector3 sP, out Quaternion sR, out Vector3 sS);
            WorldPose(_rightHand, _dLP, _dLR, _dLS, out Vector3 dP, out Quaternion dR, out Vector3 dS);
            // 收刀: 右手(0) → 鞘中(1)；拔刀: 鞘中(0) → 右手(1)
            Vector3 fromP = _toDrawn ? sP : dP, toP = _toDrawn ? dP : sP;
            Quaternion fromR = _toDrawn ? sR : dR, toR = _toDrawn ? dR : sR;
            Vector3 fromS = _toDrawn ? sS : dS, toS = _toDrawn ? dS : sS;

            SetWorld(Vector3.Lerp(fromP, toP, e), Quaternion.Slerp(fromR, toR, e), Vector3.Lerp(fromS, toS, e));

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
                    _blade.SetParent(_sheathParent, false);
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
            Vector3 ps = _neutral.lossyScale;
            _blade.localScale = new Vector3(
                Mathf.Approximately(ps.x, 0f) ? worldScale.x : worldScale.x / ps.x,
                Mathf.Approximately(ps.y, 0f) ? worldScale.y : worldScale.y / ps.y,
                Mathf.Approximately(ps.z, 0f) ? worldScale.z : worldScale.z / ps.z);
        }
    }
}
