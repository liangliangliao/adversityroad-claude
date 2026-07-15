using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 背包跟随绑定。
    ///
    /// 【为什么需要它】背包若只在装备瞬间按世界方向摆一次姿态，会把"装备时刻的骨骼姿势"
    /// 烘进本地变换——不同模型的绑定姿势与播放姿势差异（尤其 glb 角色）会让背包在运行时
    /// 横倒、沉进躯干。本组件改为【每帧】从活动骨骼推算：
    ///   竖直 = 髋→颈方向（跟随躯干前倾），朝向 = 角色面向（肩带面朝身体、鼓面朝身后），
    ///   座位 = 上胸骨骼后方【躯干半厚 + 0.38 包厚】处（背板贴背、肩带环套肩），包顶到肩线。
    ///
    /// 肩带：直接用背包模型自带的双肩带环（FitBackpack 的 qFix 已把肩带面转向身体），
    /// 双肩从环中穿过，视觉上即"背上了"——不再叠加程序化肩带。
    /// </summary>
    [DefaultExecutionOrder(5100)]   // 在动画与骨骼驱动之后跑，姿态不被覆盖
    public class BackpackRig : MonoBehaviour
    {
        Transform _visual, _hips, _neck, _chest;
        Quaternion _qFix;                  // 模型轴修正：肩带面→+Z(朝身体)、高轴→+Y
        Vector3 _lbCenter;                 // 包围盒中心（包本地）
        float _backOff, _liftOff;          // 后移量（躯干半厚+0.38包厚）、抬升量（包顶到肩线）

        public void Setup(Transform visual, Transform hips, Transform neck, Transform chest,
            Quaternion qFix, Vector3 lbCenter, float backOff, float liftOff)
        {
            _visual = visual; _hips = hips; _neck = neck; _chest = chest;
            _qFix = qFix; _lbCenter = lbCenter;
            _backOff = backOff; _liftOff = liftOff;
            Apply();
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            if (_visual == null || _chest == null) return;

            // 竖直跟随躯干（髋→颈），朝向跟随角色面向——与绑定姿势/骨轴约定无关
            Vector3 up = (_neck != null && _hips != null)
                ? (_neck.position - _hips.position) : Vector3.up;
            if (up.sqrMagnitude < 1e-6f || float.IsNaN(up.x)) up = Vector3.up;
            up.Normalize();
            Vector3 fwd = Vector3.ProjectOnPlane(_visual.forward, up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = _visual.forward;
            fwd.Normalize();

            // 跟随系：+Z=角色前方（qFix 把肩带面转到 +Z=朝身体），+Y=躯干竖直
            transform.rotation = Quaternion.LookRotation(fwd, up) * _qFix;
            Vector3 seat = _chest.position - fwd * _backOff + up * _liftOff;
            transform.position += seat - transform.TransformPoint(_lbCenter);
        }
    }
}
