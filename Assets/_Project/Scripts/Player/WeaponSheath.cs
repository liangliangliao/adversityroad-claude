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
        bool _seatCapture;                                  // 收刀：入鞘那一帧的位姿已捕获
        Vector3 _capLP, _capLS; Quaternion _capLR;          // 捕获到的剑位姿（鞘本地）

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
        const float SheatheAim = 0.30f;     // 收刀：右手开始转腕，把剑尖对准鞘口
        const float SheatheSlide = 0.68f;   // 收刀：剑尖抵到鞘口，交给鞘
        const float SheatheSeat = 0.92f;    // 收刀：沿鞘轴推到底（入座）

        /// <summary>转腕对准的最大角度：超过这个量就不是"稍微弯一下手腕"，
        /// 而是把剑拧到人做不出来的姿势了。</summary>
        const float MaxWristBend = 60f;

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

        void LateUpdate()
        {
            if (_blade == null || _scab == null) return;

            if (_anim)
            {
                _t += Time.deltaTime / _dur;
                float t = Mathf.Clamp01(_t);
                // 拔刀沿用原来的横置呈鞘；收刀改走「左手把鞘迎向剑身」（见 SheatheCarry）
                if (_toDrawn) AimScabbard();
                else SheatheCarry(t);

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
                    // 【收刀新增的一段：右手转腕，把剑尖对准鞘口】
                    // 之前收刀是"手里举着剑 → 到点剑突然出现在鞘口"，中间少的就是这个动作。
                    // 注意仍然【不做世界轨迹插值】（那正是上一版剑乱飞的原因）：
                    // 剑始终挂在右手上、握位一点不动，只绕【握把点】旋转——
                    // 看上去就是右手稍微弯一下、把剑尖调过去，跟真人收刀的顺序一致。
                    // 注意顺序：上面 SheatheCarry 已经把鞘朝剑挪过一步，这里剑再朝
                    // 新的鞘口挪一步——两边相向靠拢，比单方向去够近得多。
                    if (!_toDrawn && t >= SheatheAim)
                        AimBladeAtMouth(Mathf.InverseLerp(SheatheAim, SheatheSlide, t));
                }
                else if (!_toDrawn && t < SheatheSeat)
                {
                    // 收刀第二段：沿鞘推进去。
                    //
                    // 【为什么用"捕获"而不是固定的鞘口位姿】
                    // 上一版这里是从一个【固定的鞘口位姿】插到座位姿。
                    // 但交接那一瞬间剑实际在哪儿，是由右手动画决定的——跟那个固定位姿
                    // 对不上。于是玩家看到的就是：剑明明朝着别的方向，突然"啪"地
                    // 出现在鞘口，然后才滑进去。这正是"剑被任意位置插入、后来自动
                    // 补回鞘里"的观感来源。
                    // 现在改成：交接时【保持世界位姿】换父级，把剑当时真实的位姿
                    // 原封不动记下来（_capLP/_capLR/_capLS），再从这个位姿滑到座位姿。
                    // 交接帧世界位姿完全不变＝零跳变；而上一段已经把鞘转到与剑身共线，
                    // 所以这段插值走的就是沿鞘轴推进的那条直线。
                    if (!_seatCapture)
                    {
                        _removeGrip?.Invoke(_rightHand);
                        _blade.SetParent(_scab, true);   // 世界位姿保持不变
                        _capLP = _blade.localPosition;
                        _capLR = _blade.localRotation;
                        _capLS = _blade.localScale;
                        _seatCapture = true;
                    }
                    float e = Mathf.InverseLerp(SheatheSlide, SheatheSeat, t);
                    e = e * e * (3f - 2f * e);
                    _blade.localPosition = Vector3.Lerp(_capLP, _sLP, e);
                    _blade.localRotation = Quaternion.Slerp(_capLR, _sLR, e);
                    _blade.localScale = Vector3.Lerp(_capLS, _sLS, e);
                }
                else
                {
                    // 剑归鞘：静静待在鞘中（拔刀前半程 / 收刀入座后）
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

        /// <summary>左手能把鞘伸出去多远（米）：超过这个距离就不再硬迎，
        /// 免得鞘脱手飞到剑边上去。</summary>
        const float MaxReach = 0.42f;

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>
        /// 收刀时的持鞘：【左手主动把鞘迎向剑身】。
        ///
        /// 玩家的观察一针见血——之前是让右手拿着剑去够鞘口，可右手的位置完全由
        /// `Sheathing Sword` 这段动画决定，程序改不了；剑尖怎么调都对不准，
        /// 于是只能在最后一刻把剑硬塞回鞘里，看上去就是"剑往别的方向插，
        /// 后来又自己飞回鞘中"。
        ///
        /// 真实的納刀本来就是**左手的活**：右手保持刀势不动，左手把鞘口迎上刀身，
        /// 找到鞘口后再推进去。所以正确的做法是反过来——让鞘去找剑：
        ///   ① 把鞘转到与剑身【共线】（鞘口朝着剑尖那一侧）；
        ///   ② 把【鞘口】平移到【剑尖】上；
        ///   ③ 左手够不到就不硬伸（MaxReach 夹住），剩下的差距由剑那一侧的
        ///      转腕对准（AimBladeAtMouth）和入鞘段的插值吸收。
        /// 两边同时相向靠拢，一帧一点，在对准窗口内自然会合。
        ///
        /// 交接之后（剑已成为鞘的子物体）e 归零、回到自然竖提——此时不能再拿剑当
        /// 参照物：剑跟着鞘走，"把鞘口移到剑尖"会变成每帧平移一个鞘长的自激反馈。
        /// </summary>
        void SheatheCarry(float t)
        {
            if (_set == null || _lhand == null || _scab == null || _blade == null) return;

            Vector3 palmW = _lhand.TransformPoint(_palmL);
            Vector3 axisW = _scab.TransformPoint(_mouthPt) - _scab.TransformPoint(_botPt);
            if (axisW.sqrMagnitude < 1e-10f) return;

            // 自然竖提朝向（e=0 时的目标，与 CarryScabbard 一致）
            Vector3 fwd = _visual != null
                ? Vector3.ProjectOnPlane(_visual.forward, Vector3.up) : Vector3.forward;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            Vector3 carryWant = (Vector3.up * 0.92f + fwd.normalized * 0.39f).normalized;

            // 只有剑还在右手里时，才拿它当对准参照
            bool bladeInHand = _blade.parent == _rightHand;
            float e = bladeInHand && t >= SheatheAim
                ? Smooth(Mathf.InverseLerp(SheatheAim, SheatheSlide, t)) : 0f;

            Vector3 gripW = _blade.TransformPoint(_gripL);
            Vector3 tipW = _blade.TransformPoint(_tipL);
            Vector3 bladeDir = tipW - gripW;

            Vector3 want = carryWant;
            if (e > 0f && bladeDir.sqrMagnitude > 1e-8f)
            {
                // 鞘轴（鞘底→鞘口）要与刀身反向：鞘口朝着剑尖，剑才插得进去
                want = Vector3.Slerp(carryWant, -bladeDir.normalized, e).normalized;
            }
            Quaternion target = Quaternion.FromToRotation(axisW.normalized, want) * _set.rotation;
            _set.rotation = Quaternion.Slerp(_set.rotation, target,
                1f - Mathf.Exp(-18f * Time.deltaTime));

            // 位置：e=0 鞘中点贴掌心；e=1 鞘口贴剑尖
            Vector3 byPalm = palmW - _scab.TransformPoint(_midPt);
            Vector3 byMouth = tipW - _scab.TransformPoint(_mouthPt);
            _set.position += e > 0f ? Vector3.Lerp(byPalm, byMouth, e) : byPalm;

            // 够不到就收着点：鞘中点离掌心不超过 MaxReach
            Vector3 off = _scab.TransformPoint(_midPt) - palmW;
            float d = off.magnitude;
            if (d > MaxReach) _set.position -= off / d * (d - MaxReach);
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

        /// <summary>
        /// 收刀对准段：绕【握把点】旋转剑身，让剑尖指向鞘口——读作"右手转腕对准"。
        ///
        /// e=0 时完全是动画给的握姿（不干预），e 越大越把剑尖摆向鞘口。
        /// 与 SheatheCarry（鞘迎向剑）配对：一个管指向、一个管共线与贴合，
        /// 两边相向靠拢，在对准窗口内会合。
        /// 整体旋转量被 MaxWristBend 夹住：宁可对不太准，也不要拧出一个人做不出的手腕。
        /// </summary>
        void AimBladeAtMouth(float e)
        {
            e = Mathf.Clamp01(e);
            e = e * e * (3f - 2f * e);                       // 缓入缓出，别在起点猛地一拧
            Vector3 gripW = _blade.TransformPoint(_gripL);
            Vector3 tipW = _blade.TransformPoint(_tipL);
            Vector3 cur = tipW - gripW;
            if (cur.sqrMagnitude < 1e-8f) return;

            Vector3 mouthW = _scab.TransformPoint(_mouthPt);
            Vector3 toMouth = mouthW - gripW;                // 剑尖指向鞘口
            if (toMouth.sqrMagnitude < 1e-8f) return;

            // 目标只有一个：把剑尖指向鞘口。
            // 「与鞘轴共线」这件事已经交给 SheatheCarry 那一侧去做了（鞘转到与剑身反向），
            // 这里再去掺一个共线目标就会与它互相抵消——两边都以为对方会动，
            // 结果谁都不怎么动。分工要单一：剑管指向，鞘管共线与贴合。
            Vector3 want = toMouth.normalized;
            Quaternion full = Quaternion.FromToRotation(cur.normalized, want);
            Quaternion capped = Quaternion.RotateTowards(Quaternion.identity, full, MaxWristBend);
            Quaternion delta = Quaternion.Slerp(Quaternion.identity, capped, e);

            _blade.rotation = delta * _blade.rotation;
            _blade.position += gripW - _blade.TransformPoint(_gripL);   // 握把点钉在手里不动
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
