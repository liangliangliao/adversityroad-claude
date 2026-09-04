using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.AI;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 威胁指示器：把【看不见的攻击】画到屏幕边缘。
    ///
    /// 【要解决的问题】玩家反馈"突然被敌人攻击，感觉毫无理由、毫无征兆受到伤害、
    /// 看不清敌人的状况"。敌人本来就有前摇警示（头顶「！/危」+ 脚下红圈），
    /// 但那套东西有一个致命前提——**敌人得在画面里**。三人称镜头只有约 60° 视野，
    /// 围攻时至少一半敌人在画面外或在你背后，它们的警示玩家一眼都看不到。
    /// 于是从玩家视角看，伤害确实是"凭空"来的。
    ///
    /// 大型动作游戏对这件事有一套稳定的解法，本类照做：
    ///   ① 【前摇方向标】：任何敌人进入攻击前摇，就在屏幕对应方向的边缘画一个箭头
    ///      （红＝可格挡，橙＝不可格挡的危险攻击，且更大更快闪）。
    ///      箭头在**判定框开启之前**就出现，所以它是"预告"而不是"事后报告"。
    ///   ② 【受击方向标】：真挨打了，在来袭方向短暂亮一道更粗的弧——
    ///      让玩家至少知道该往哪转身，而不是茫然。
    ///   ③ 屏幕内的敌人不画箭头（它自己的「！」已经够了），只画画面外与背后的，
    ///      避免正面 1v1 时糊一屏幕的记号。
    ///
    /// 由此形成一条玩家可以学会的固定规则：
    ///   **屏幕边缘出现红色箭头 = 0.5 秒后那个方向会挨一下；橙色 = 只能闪不能挡。**
    /// 配合 EnemyController.MinWindup（起手前摇下限 0.5s）与 ComboWindup（连击段 0.34s），
    /// "提前预测并闪避"才是一件做得到的事，而不是靠运气。
    /// </summary>
    public class ThreatIndicator : MonoBehaviour
    {
        const int Pool = 8;                 // 同屏最多画几个方向标（围攻上限足够）
        // 【在场标】与前摇标是两件事：前摇标回答"哪儿要挨打"，在场标回答
        // "敌人在哪儿"。玩家报的"敌人在视野盲区、镜头拍不到"要的是后者——
        // 它得一直在，而不是等对方抬手才出现。
        const int SoftPool = 6;
        const float SoftRange = 32f;        // 米：这个距离外的追兵不占标位
        const float Radius = 0.34f;         // 箭头落在屏幕短边 34% 半径的圆环上
        const float HitFlashTime = 0.7f;    // 受击方向标停留时长

        class Mark
        {
            public RectTransform rt;
            public Image img;
            public Text glyph;
        }

        readonly List<Mark> _marks = new List<Mark>();
        readonly List<Mark> _soft = new List<Mark>();
        LockOnSystem _lock;
        RectTransform _canvasRt;
        Transform _player;
        Camera _cam;

        // 受击方向标（独立于前摇标，来袭方向 + 剩余时间）
        Vector3 _hitDir;
        float _hitT;

        static readonly Color Normal = new Color(1f, 0.25f, 0.2f, 0.9f);
        static readonly Color Perilous = new Color(1f, 0.55f, 0.05f, 0.95f);
        static readonly Color Damage = new Color(1f, 0.85f, 0.35f, 0.95f);
        // 在场标要"看得见但不抢戏"：前摇标是红/橙的警报，在场标是灰白的方位提示。
        static readonly Color Presence = new Color(0.86f, 0.88f, 0.95f, 0.55f);
        static readonly Color Locked = new Color(1f, 0.82f, 0.30f, 0.95f);

        public static ThreatIndicator Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<ThreatIndicator>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _canvasRt = canvas as RectTransform;
            for (int i = 0; i < Pool + 1; i++)   // 最后一个专用于受击方向标
            {
                var go = new GameObject("Threat" + i, typeof(Image));
                go.transform.SetParent(canvas, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(64, 64);
                var img = go.GetComponent<Image>();
                img.color = new Color(0, 0, 0, 0);
                img.raycastTarget = false;
                // 用文本字形当箭头：不依赖任何贴图资源，且随缩放清晰
                var glyph = UiUtil.MakeText(go.transform, "Glyph", "▲", 44,
                    TextAnchor.MiddleCenter, Normal);
                var grt = glyph.GetComponent<RectTransform>();
                grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
                grt.offsetMin = Vector2.zero; grt.offsetMax = Vector2.zero;
                go.SetActive(false);
                _marks.Add(new Mark { rt = rt, img = img, glyph = glyph });
            }
            for (int i = 0; i < SoftPool; i++)
            {
                var go = new GameObject("Foe" + i, typeof(Image));
                go.transform.SetParent(canvas, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(52, 52);
                var img = go.GetComponent<Image>();
                img.color = new Color(0, 0, 0, 0);
                img.raycastTarget = false;
                var glyph = UiUtil.MakeText(go.transform, "Glyph", "▲", 34,
                    TextAnchor.MiddleCenter, Presence);
                var grt = glyph.GetComponent<RectTransform>();
                grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
                grt.offsetMin = Vector2.zero; grt.offsetMax = Vector2.zero;
                go.SetActive(false);
                _soft.Add(new Mark { rt = rt, img = img, glyph = glyph });
            }
        }

        void OnEnable() => GameEvents.OnPlayerHurtFrom += OnHurtFrom;
        void OnDisable() => GameEvents.OnPlayerHurtFrom -= OnHurtFrom;

        void OnHurtFrom(Vector3 attackerPos)
        {
            if (_player == null) return;
            Vector3 d = attackerPos - _player.position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return;
            _hitDir = d.normalized;
            _hitT = HitFlashTime;
        }

        void LateUpdate()
        {
            if (_player == null)
            {
                var pc = AdversityRoad.Core.ActorRegistry.Player;
                if (pc == null) { HideAll(); return; }
                _player = pc.transform;
            }
            if (_cam == null) _cam = Camera.main;
            if (_cam == null || _canvasRt == null) { HideAll(); return; }

            float ring = Mathf.Min(_canvasRt.rect.width, _canvasRt.rect.height) * Radius;
            int used = 0;

            foreach (var e in AdversityRoad.Core.ActorRegistry.Enemies)
            {
                if (used >= Pool) break;
                if (e == null || !e.Telegraphing || e.State == EnemyState.Dead) continue;

                // 屏幕内且在镜头正面的敌人不画标记——它头顶的「！」已经在画面里了，
                // 再叠一个箭头只会让围攻时满屏都是记号，反而看不清哪个才是威胁。
                Vector3 sp = _cam.WorldToViewportPoint(e.transform.position + Vector3.up * 1.2f);
                bool onScreen = sp.z > 0f && sp.x > 0.06f && sp.x < 0.94f && sp.y > 0.06f && sp.y < 0.94f;
                if (onScreen) continue;

                Place(_marks[used], e.transform.position,
                    e.PerilousTelegraph ? Perilous : Normal,
                    e.PerilousTelegraph ? "危" : "！", ring,
                    e.PerilousTelegraph ? 1.25f : 1f);
                used++;
            }

            for (int i = used; i < Pool; i++)
                if (_marks[i].rt.gameObject.activeSelf) _marks[i].rt.gameObject.SetActive(false);

            // ---- 在场标：正在追你/打你、却不在画面里的敌人 ----
            //
            // 【为什么用记号而不是转镜头】移动是镜头相对的（H = C + θ）：
            // 玩家按着摇杆时镜头每自动转 1°，角色的世界朝向就被带转 1°。
            // 所以"敌人跑出画面就把镜头转回去"这件事，代价是玩家被迫绕着敌人画弧
            //（他报的"像被磁铁吸住"）。这条路在无锁定状态下不能走。
            //
            // 成熟动作游戏对"看不见敌人"的通行解法本来就不是转镜头，而是
            // 屏幕边缘的方位记号（鬼泣/猎天使/怪猎/战神都是这套）+ 锁定。
            // 记号不动镜头，所以它不可能改变玩家的移动方向。
            if (_lock == null)
            {
                var pc0 = AdversityRoad.Core.ActorRegistry.Player;
                if (pc0 != null) _lock = pc0.GetComponent<LockOnSystem>();
            }
            Transform locked = _lock != null ? _lock.CurrentTarget : null;
            int soft = 0;
            foreach (var e in AdversityRoad.Core.ActorRegistry.Enemies)
            {
                if (soft >= SoftPool) break;
                if (e == null || e.State == EnemyState.Dead) continue;
                // 只标"已经动起来"的：待机/巡逻的还没盯上你，标出来只是噪音
                if (e.State != EnemyState.Chase && e.State != EnemyState.Attack &&
                    e.State != EnemyState.MentalAttack) continue;
                if (e.Telegraphing) continue;      // 它已经有一枚更醒目的前摇标了
                float d = Vector3.Distance(e.transform.position, _player.position);
                if (d > SoftRange) continue;
                Vector3 sp = _cam.WorldToViewportPoint(e.transform.position + Vector3.up * 1.2f);
                if (sp.z > 0f && sp.x > 0.06f && sp.x < 0.94f &&
                    sp.y > 0.06f && sp.y < 0.94f) continue;   // 画面里就不用标
                bool isLocked = locked != null && locked == e.transform;
                // 越近越大越实：远处的追兵只是"知道在那边"，贴脸的那个要一眼看见
                float near = 1f - Mathf.Clamp01(d / SoftRange);
                var c = isLocked ? Locked : Presence;
                c.a *= isLocked ? 1f : Mathf.Lerp(0.55f, 1f, near);
                Place(_soft[soft], e.transform.position, c,
                      isLocked ? "◆" : "▲", ring * 0.86f,
                      (isLocked ? 1.15f : 0.85f) + near * 0.25f);
                soft++;
            }
            for (int i = soft; i < SoftPool; i++)
                if (_soft[i].rt.gameObject.activeSelf) _soft[i].rt.gameObject.SetActive(false);

            // ---- 受击方向标 ----
            var hitMark = _marks[Pool];
            if (_hitT > 0f)
            {
                _hitT -= Time.unscaledDeltaTime;
                var c = Damage;
                c.a *= Mathf.Clamp01(_hitT / HitFlashTime);
                Place(hitMark, _player.position + _hitDir * 5f, c, "▲", ring * 1.12f, 1.35f);
            }
            else if (hitMark.rt.gameObject.activeSelf) hitMark.rt.gameObject.SetActive(false);
        }

        /// <summary>把世界方向映射到屏幕中心的方向环上，并让字形朝外。</summary>
        void Place(Mark m, Vector3 worldPos, Color color, string glyph, float ring, float scale)
        {
            Vector3 d = worldPos - _player.position;
            // 用【镜头】而不是角色朝向做基准：玩家判断左右是照着画面判断的
            Vector3 fwd = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.ProjectOnPlane(_cam.transform.right, Vector3.up);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.up, fwd);
            right.Normalize();
            float x = Vector3.Dot(d, right);
            float y = Vector3.Dot(d, fwd);
            if (Mathf.Abs(x) < 1e-5f && Mathf.Abs(y) < 1e-5f) return;
            Vector2 dir = new Vector2(x, y).normalized;

            m.rt.gameObject.SetActive(true);
            m.rt.anchoredPosition = dir * ring;
            m.rt.localScale = Vector3.one * scale;
            // ▲ 默认朝上（+Y）；转到指向该方向
            m.rt.localRotation = Quaternion.Euler(0, 0,
                Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            m.glyph.text = glyph;
            m.glyph.color = color;
            // 「！」「危」是字，跟着箭头一起转会读不出来——把字形转回水平
            var grt = m.glyph.GetComponent<RectTransform>();
            grt.localRotation = (glyph == "▲" || glyph == "◆") ? Quaternion.identity
                : Quaternion.Inverse(m.rt.localRotation);
        }

        void HideAll()
        {
            foreach (var m in _marks)
                if (m.rt.gameObject.activeSelf) m.rt.gameObject.SetActive(false);
            foreach (var m in _soft)
                if (m.rt.gameObject.activeSelf) m.rt.gameObject.SetActive(false);
        }
    }
}
