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
        // 拔刀: [0,0.28]左手横置呈鞘（剑仍在鞘中，柄朝右手）
        //       [0.28,0.50]剑沿鞘轴滑到鞘口（柄完全送出，正对右手）
        //       [0.50,0.72]鞘口→右手握位，双端实时跟随 → 交接，右手合指握柄
        //       [0.72,1]   剑随右手抽离
        // 收刀: [0,0.42]右手持剑（左手同时把鞘横置到位）
        //       [0.42,0.72]剑先转到与鞘轴共线、再把剑尖送到鞘口
        //       [0.72,1]   沿鞘轴推进到底
        // 交接点（占总时长比例）：踩在动画里手真正碰到柄 / 剑尖进鞘口的那一拍。
        // 只剩这两个数需要对——不再有任何轨迹插值要调。
        const float DrawGrab = 0.42f;       // 拔刀：手抓住柄
        const float SheatheSlide = 0.68f;   // 收刀：剑尖入鞘口

        /// <summary>在拔刀/收刀之间切换，用 dur 秒过渡（与动画时长同步）。</summary>
        public void Toggle(float dur)
        {
            if (_blade == null || _scab == null || _rightHand == null) return;
            if (_anim) return;                      // 过渡中不重复触发
            _toDrawn = !_drawn;
            _dur = Mathf.Max(0.25f, dur);
            _t = 0f; _anim = true;
            _transferred = false; _seatCapture = false;
            // （_setStartR 已弃用：过渡期间不再对鞘做任何"转过去对准"的插值）
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

                // ================= 过渡：让【动画】主导，程序只做一次交接 =================
                //
                // 动作库里本来就有 `Draw Sword 2` 与 `Sheathing Sword` 两段真实拔/收刀
                // 动画——手臂怎么伸向剑柄、怎么送回鞘口，全都是作者做好的。
                // 之前的实现却在同一时间【另外算了一条剑的轨迹】：把剑从"鞘口位姿"
                // 插值到"右手握位"。两套东西各算各的，从不一致，于是：
                //   · 剑沿着一条谁也没设计过的弧线飞出去（插值出来的中间位姿）；
                //   · 手明明还没到，剑已经在动 → 读作"没握住就拔出来了"；
                //   · 插值终点与手的真实位置对不上，最后一帧只能瞬移过去 → "突然飞到鞘口"。
                //
                // 所以正确做法是**减法**：不再插值任何轨迹，剑在交接点之前老老实实待在
                // 原属主（鞘里 / 右手里）身上，到点一次性换属主。中间那段"拔出/送入"的
                // 观感由动画本身承担——它本来就画好了。
                bool grabbed = _toDrawn ? t >= DrawGrab : t < SheatheSlide;
                if (grabbed)
                {
                    // 剑归右手：跟着手走（拔刀后半程 / 收刀前半程）
                    if (_blade.parent != _rightHand)
                    {
                        _blade.SetParent(_rightHand, false);
                        _addGrip?.Invoke(_rightHand);     // 五指合拢握柄
                    }
                    _blade.localPosition = _dLP; _blade.localRotation = _dLR; _blade.localScale = _dLS;
                }
                else
                {
                    // 剑归鞘：静静待在鞘中（拔刀前半程 / 收刀后半程）
                    if (_blade.parent != _scab)
                    {
                        _removeGrip?.Invoke(_rightHand);
                        _blade.SetParent(_scab, false);
                    }
                    _blade.localPosition = _sLP; _blade.localRotation = _sLR; _blade.localScale = _sLS;
                }
                _transferred = _toDrawn && grabbed;

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

        /// <summary>
        /// 过渡期间的持鞘：**鞘不去追剑**。
        ///
        /// 真实的拔/收刀是「鞘稳在胯侧不动，剑去找鞘」，而不是「鞘甩过去接剑」。
        /// 旧实现做的恰恰是后者：`FromToRotation(鞘轴, 指向剑尖)` 把整套鞘转到
        /// 「从左掌指向剑尖」的方向上——而收刀时剑在右手、举在身前，
        /// 于是鞘被甩成**水平横在腰前**，鞘与剑首尾相接连成一条三倍身宽的长线
        /// （实机截图正是如此）。把鞘口迎向剑柄还是剑尖只是在错误里换了个方向，
        /// 两种都不对：**鞘根本就不该转**。
        ///
        /// 现在过渡期间沿用自然携持姿态（竖提于左胯、鞘口朝上偏前），只把收敛速度
        /// 调快一点以免摇晃；对准完全交给剑那一侧——剑先转到与鞘轴共线、
        /// 再把剑尖送到鞘口、最后沿轴推进去。这也正是人真实收刀的顺序。
        /// </summary>
        void AimScabbard()
        {
            if (_set == null || _lhand == null || _visual == null) return;
            Vector3 palmW = _lhand.TransformPoint(_palmL);

            // 【横置呈鞘】——固定姿势，不追剑。
            // 上一版让鞘去"指向剑尖/剑柄"，结果鞘被甩成任意角度（实机：横在腰前
            // 连成一条长线）。真实的拔/收刀是：左手先把鞘**横到身前**，
            // 鞘口朝右手那一侧；右手在这条固定的轴上握柄、拔出或插入。
            // 关键是这个姿势【与剑的位置无关】，所以稳定、可预期，
            // 剑那一侧的对准也就有了一个不动的靶子。
            Vector3 want = (_visual.right * 0.86f + _visual.forward * 0.40f
                            + Vector3.up * 0.26f).normalized;
            Vector3 axisW = _scab.TransformPoint(_mouthPt) - _scab.TransformPoint(_botPt);
            if (axisW.sqrMagnitude < 1e-10f) return;
            Quaternion target = Quaternion.FromToRotation(axisW.normalized, want) * _set.rotation;
            _set.rotation = Quaternion.Slerp(_set.rotation, target,
                1f - Mathf.Exp(-18f * Time.deltaTime));
            _set.position += palmW - _scab.TransformPoint(_midPt);   // 鞘中点钉左掌
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
        void CarryScabbard(float rate = 12f)
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
                1f - Mathf.Exp(-rate * Time.deltaTime));
            _set.position += _lhand.TransformPoint(_palmL) - _scab.TransformPoint(_midPt);
        }
    }
}
