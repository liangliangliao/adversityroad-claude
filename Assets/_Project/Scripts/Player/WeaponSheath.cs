using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 剑鞘·拔刀/收刀控制器（带剑鞘的成套武器，如 scene「Highland 剑」）。
    ///
    /// 收刀（默认/脱战）：剑身插在剑鞘里，整套（剑+鞘）挂在【左手】。
    /// 拔刀（临战）：把【剑身】从鞘中抽出移到【右手】，按与其它剑一致的握位夹住剑柄；
    ///   剑鞘留在左手。由 PlayerController 按「临战」信号驱动 SetDrawn(true/false)。
    ///
    /// 只做刚性换手（即时/无动画）。装配、剑身识别、插鞘对齐、右手握持对齐都在
    /// PlayerAppearance 里完成，这里只负责在两种状态间搬剑身并回调握持/收拢。
    /// </summary>
    public class WeaponSheath : MonoBehaviour
    {
        Transform _blade;              // 可从鞘中抽出的剑身子树
        Transform _sheathParent;       // 收刀时剑身的父节点（鞘内挂点）
        Vector3 _lp, _ls; Quaternion _lr;   // 收刀时剑身的本地 TRS（插鞘对齐后的姿态）
        Transform _rightHand;
        System.Action<Transform, Transform> _drawFit;   // 拔刀：把剑身按握位对齐到右手
        System.Action<Transform> _onSheath;             // 收刀：撤掉右手的握拳组件
        bool _drawn;

        public bool IsDrawn => _drawn;

        public void Setup(Transform blade, Transform sheathParent, Vector3 lp, Quaternion lr, Vector3 ls,
            Transform rightHand, System.Action<Transform, Transform> drawFit, System.Action<Transform> onSheath)
        {
            _blade = blade; _sheathParent = sheathParent; _lp = lp; _lr = lr; _ls = ls;
            _rightHand = rightHand; _drawFit = drawFit; _onSheath = onSheath; _drawn = false;
        }

        public void SetDrawn(bool drawn)
        {
            if (_blade == null || _sheathParent == null || _rightHand == null || drawn == _drawn) return;
            _drawn = drawn;
            if (drawn)
            {
                _blade.SetParent(_rightHand, false);
                _drawFit?.Invoke(_blade, _rightHand);          // 右手握持对齐（与其它剑一致）
            }
            else
            {
                _onSheath?.Invoke(_rightHand);                  // 撤右手握拳
                _blade.SetParent(_sheathParent, false);        // 剑身回鞘
                _blade.localPosition = _lp;
                _blade.localRotation = _lr;
                _blade.localScale = _ls;
            }
        }
    }
}
