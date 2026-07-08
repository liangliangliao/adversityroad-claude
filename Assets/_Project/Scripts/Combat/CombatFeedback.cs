using System.Collections;
using UnityEngine;
using AdversityRoad.Player;
using AdversityRoad.Core;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 战斗反馈特效（全程序化，零外部资产）：
    /// 伤害数字 / 受击闪红 / 碎块飞溅 / 顿帧 / 震屏 / 挥击剑气。
    /// </summary>
    public class CombatFeedback : MonoBehaviour
    {
        static CombatFeedback _i;
        static Material _base;
        static bool _hitStopping;

        public static void Init(Material baseMaterial)
        {
            _base = baseMaterial;
            Ensure();
        }

        static void Ensure()
        {
            if (_i != null) return;
            _i = new GameObject("CombatFX").AddComponent<CombatFeedback>();
        }

        static Material Mat(Color c)
        {
            Material m;
            if (_base != null) m = new Material(_base);
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                m = new Material(sh);
            }
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        /// <summary>特效材质：半透明加色（发光感），用于刀光/冲击环等——避免实心大色块。</summary>
        static Material MatFX(Color c, float alpha)
        {
            var m = Mat(c);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);            // URP 透明
            if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); // 加色=能量光
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            Color cc = c; cc.a = alpha;
            m.color = cc;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", cc);
            return m;
        }

        /// <summary>去掉特效基元的碰撞体：立即禁用（任何上下文都安全）再延迟销毁。
        /// 不能用 DestroyImmediate——特效常在物理命中回调里生成，DestroyImmediate 会报错。</summary>
        static void StripCol(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) { c.enabled = false; Object.Destroy(c); }
        }

        static void FadeAlpha(GameObject go, float mul)
        {
            var r = go != null ? go.GetComponent<MeshRenderer>() : null;
            if (r == null) return;
            var m = r.sharedMaterial;
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            c.a *= mul;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        // ---------- 伤害数字 ----------

        public static void DamageNumber(Vector3 pos, string text, Color color) =>
            DamageNumber(pos, text, color, 1f);

        public static void DamageNumber(Vector3 pos, string text, Color color, float scale)
        {
            Ensure();
            var go = new GameObject("DmgNum");
            go.transform.position = pos + Vector3.up * 2.2f + Random.insideUnitSphere * 0.3f;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = 46;
            tm.characterSize = 0.06f * scale;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = color;
            var r = go.GetComponent<MeshRenderer>();
            if (tm.font != null) r.material = tm.font.material;
            _i.StartCoroutine(_i.FloatUp(go, tm));
        }

        IEnumerator FloatUp(GameObject go, TextMesh tm)
        {
            float t = 0;
            while (t < 0.9f && go != null)
            {
                t += Time.deltaTime;
                go.transform.position += Vector3.up * 1.6f * Time.deltaTime;
                if (Camera.main != null)
                    go.transform.rotation = Quaternion.LookRotation(
                        go.transform.position - Camera.main.transform.position);
                var c = tm.color; c.a = 1f - t / 0.9f; tm.color = c;
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        // ---------- 受击闪红 ----------

        public static void HitFlash(GameObject target)
        {
            Ensure();
            _i.StartCoroutine(_i.Flash(target));
        }

        IEnumerator Flash(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<MeshRenderer>();
            var originals = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || !renderers[i].enabled) continue;
                var m = renderers[i].material;
                originals[i] = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                SetColor(m, Color.Lerp(originals[i], new Color(1f, 0.25f, 0.2f), 0.85f));
            }
            yield return new WaitForSecondsRealtime(0.1f);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                SetColor(renderers[i].material, originals[i]);
            }
        }

        static void SetColor(Material m, Color c)
        {
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        // ---------- 碎块飞溅 ----------

        public static void Debris(Vector3 pos, Color color, int count)
        {
            Ensure();
            for (int i = 0; i < count; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = pos + Vector3.up * 1.2f;
                cube.transform.localScale = Vector3.one * Random.Range(0.08f, 0.2f);
                cube.GetComponent<MeshRenderer>().sharedMaterial = Mat(color);
                var rb = cube.AddComponent<Rigidbody>();
                rb.linearVelocity = new Vector3(
                    Random.Range(-2.5f, 2.5f), Random.Range(2.5f, 5.5f), Random.Range(-2.5f, 2.5f));
                _i.StartCoroutine(_i.ShrinkAndKill(cube, 0.8f));
            }
        }

        IEnumerator ShrinkAndKill(GameObject go, float life)
        {
            yield return new WaitForSeconds(life * 0.5f);
            float t = 0;
            Vector3 start = go != null ? go.transform.localScale : Vector3.one;
            while (t < life * 0.5f && go != null)
            {
                t += Time.deltaTime;
                go.transform.localScale = Vector3.Lerp(start, Vector3.zero, t / (life * 0.5f));
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        // ---------- 顿帧 ----------

        public static void HitStop(float duration = 0.05f)
        {
            Ensure();
            if (_hitStopping || Time.timeScale < 0.9f) return; // 不与暂停/面板冲突
            _i.StartCoroutine(_i.DoHitStop(duration));
        }

        IEnumerator DoHitStop(float duration)
        {
            _hitStopping = true;
            float prev = Time.timeScale;
            Time.timeScale = 0.08f;
            yield return new WaitForSecondsRealtime(duration);
            if (Time.timeScale < 0.9f) Time.timeScale = prev; // 期间未被面板改动才恢复
            _hitStopping = false;
        }

        // ---------- 命中火花：放射状光条爆开（打击感核心） ----------

        public static void HitSpark(Vector3 pos, Color color, int count = 7)
        {
            Ensure();
            Vector3 center = pos + Vector3.up * 1.2f;
            for (int i = 0; i < count; i++)
            {
                var spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                StripCol(spark);
                spark.transform.position = center;
                spark.transform.rotation = Random.rotation;
                spark.transform.localScale = new Vector3(0.05f, 0.05f, Random.Range(0.4f, 0.9f));
                spark.GetComponent<MeshRenderer>().sharedMaterial =
                    Mat(Color.Lerp(color, Color.white, 0.6f));
                _i.StartCoroutine(_i.SparkFly(spark));
            }
        }

        IEnumerator SparkFly(GameObject spark)
        {
            Vector3 dir = spark.transform.forward;
            float t = 0;
            while (t < 0.16f && spark != null)
            {
                t += Time.deltaTime;
                spark.transform.position += dir * 9f * Time.deltaTime;
                spark.transform.localScale = Vector3.Lerp(spark.transform.localScale,
                    Vector3.zero, t / 0.16f);
                yield return null;
            }
            if (spark != null) Destroy(spark);
        }

        // ---------- 能量爆发（组合技/大招）：借鉴动作大作的"光爆"而非大色块 ----------
        // 组合：强闪光球 + 密集放射光条 + 上升火星 + 细亮冲击环 + 顿帧/时缓/震屏。
        // 用加色半透明的暖橙-白光，读作"能量光"而非涂了一片米黄色实体。

        public static void RecipeBurst(Vector3 pos, Color color) => EnergyBurst(pos, color, 0.85f);

        /// <summary>能量光爆。power≈0.85 组合技，≈1.6 大招。core=能量主色（暖色更"燃"）。</summary>
        public static void EnergyBurst(Vector3 pos, Color core, float power)
        {
            Ensure();
            // 提亮加饱和，暖色再朝橙偏一点，读作"能量光"而非平铺的米黄实体
            Color hot = Color.Lerp(core, new Color(1f, 0.6f, 0.2f), core.b < core.r ? 0.4f : 0.15f);
            Vector3 c = pos + Vector3.up * 1.05f;

            // 强闪光球：偏侧上生成 + 幅度收敛，避免糊住角色本体（看得清动作是第一位）
            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            StripCol(flash);
            flash.transform.position = c + Vector3.up * 0.2f;
            flash.GetComponent<MeshRenderer>().sharedMaterial =
                MatFX(Color.Lerp(hot, Color.white, 0.7f), 0.45f);      // 更透，不刺眼
            _i.StartCoroutine(_i.FlashPop(flash, 0.35f + power * 0.5f));

            // 放射光条 + 上升火星：数量收敛，点到为止（能量感在，但不遮挡招式）
            HitSpark(pos, Color.Lerp(hot, Color.white, 0.4f), Mathf.RoundToInt(8 + power * 5));
            _i.StartCoroutine(_i.EmberRise(c, hot, Mathf.RoundToInt(5 + power * 4)));

            Shake(0.3f + power * 0.15f);
            SlowMo(0.7f, 0.06f + power * 0.04f);
        }

        IEnumerator FlashPop(GameObject go, float maxR)
        {
            float t = 0, dur = 0.16f;
            while (t < dur && go != null)
            {
                t += Time.deltaTime;
                float k = t / dur;
                go.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, maxR, Mathf.Sqrt(k));
                FadeAlpha(go, 1f - Time.deltaTime / dur * 1.7f);
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        IEnumerator EmberRise(Vector3 center, Color color, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Cube);
                StripCol(e);
                e.transform.position = center + Random.insideUnitSphere * 0.35f;
                e.transform.localScale = Vector3.one * Random.Range(0.05f, 0.12f);
                e.GetComponent<MeshRenderer>().sharedMaterial =
                    MatFX(Color.Lerp(color, Color.white, 0.3f), 0.95f);
                _i.StartCoroutine(_i.EmberFly(e));
            }
            yield break;
        }

        IEnumerator EmberFly(GameObject e)
        {
            Vector3 v = new Vector3(Random.Range(-1.3f, 1.3f), Random.Range(1.8f, 3.6f),
                Random.Range(-1.3f, 1.3f));
            float t = 0, life = Random.Range(0.4f, 0.8f);
            while (t < life && e != null)
            {
                t += Time.deltaTime;
                v.y -= 5f * Time.deltaTime;                      // 受"重力"回落，像迸溅火星
                e.transform.position += v * Time.deltaTime;
                FadeAlpha(e, 1f - Time.deltaTime / life);
                yield return null;
            }
            if (e != null) Destroy(e);
        }

        // ---------- 时缓（完美闪避） ----------

        public static void SlowMo(float scale = 0.3f, float realDuration = 0.35f)
        {
            Ensure();
            if (_hitStopping || Time.timeScale < 0.9f) return;
            _i.StartCoroutine(_i.DoSlowMo(scale, realDuration));
        }

        IEnumerator DoSlowMo(float scale, float realDuration)
        {
            _hitStopping = true;
            Time.timeScale = scale;
            yield return new WaitForSecondsRealtime(realDuration);
            if (Time.timeScale < 0.9f) Time.timeScale = 1f;
            _hitStopping = false;
        }

        // ---------- 震屏 ----------

        public static void Shake(float strength = 0.6f)
        {
            var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            if (cam != null) cam.Kick(strength);
        }

        /// <summary>大招镜头：短暂拉近取景（仅大招调用，普通攻击/移动不触发）。</summary>
        public static void UltimateShot(float duration)
        {
            var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            if (cam != null) cam.UltimateShot(duration);
        }

        // ---------- 挥击剑气 ----------

        public static void SwingArc(Transform owner, bool heavy, Color color)
        {
            Ensure();
            GameAudio.Play(GameAudio.Sfx.Swing, heavy ? 0.9f : 0.6f);
            var arc = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(arc.GetComponent<Collider>());
            // 一道细窄的斜向刀光，出现在身前偏上（不再是横铺一大片色块）
            arc.transform.position = owner.position + owner.forward * 0.95f + Vector3.up * 1.25f;
            float tilt = (heavy ? Random.Range(-14f, 14f) : Random.Range(-42f, 42f)) + 58f;
            arc.transform.rotation = owner.rotation * Quaternion.Euler(0, 0, tilt);
            arc.GetComponent<MeshRenderer>().sharedMaterial =
                MatFX(Color.Lerp(color, Color.white, 0.35f), heavy ? 0.38f : 0.28f);   // 更透，不盖住招式
            _i.StartCoroutine(_i.ArcAnim(arc, heavy));
        }

        IEnumerator ArcAnim(GameObject arc, bool heavy)
        {
            float dur = heavy ? 0.16f : 0.12f;
            float t = 0;
            // 细长弧刃：长度方向拉长、厚度极薄（Z≈0.03），像一道光而非实体板
            Vector3 max = heavy ? new Vector3(2.0f, 0.14f, 0.03f) : new Vector3(1.45f, 0.1f, 0.03f);
            while (t < dur && arc != null)
            {
                t += Time.deltaTime;
                float k = t / dur;
                arc.transform.localScale = Vector3.Lerp(new Vector3(0.35f, 0.05f, 0.03f), max,
                    Mathf.Sin(k * Mathf.PI));
                FadeAlpha(arc, 1f - Time.deltaTime / dur * 1.3f);   // 快速淡出
                yield return null;
            }
            if (arc != null) Destroy(arc);
        }
    }
}
