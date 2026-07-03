using System.Collections;
using UnityEngine;
using AdversityRoad.Player;

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
                Object.DestroyImmediate(spark.GetComponent<Collider>());
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

        // ---------- 招式冲击波：组合技触发的金色扩散环 ----------

        public static void RecipeBurst(Vector3 pos, Color color)
        {
            Ensure();
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.position = pos + Vector3.up * 0.15f;
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                Mat(Color.Lerp(color, Color.white, 0.35f));
            _i.StartCoroutine(_i.RingExpand(ring));
            HitSpark(pos, color, 10);
            Shake(0.7f);
            SlowMo(0.55f, 0.12f);
        }

        IEnumerator RingExpand(GameObject ring)
        {
            float t = 0;
            while (t < 0.3f && ring != null)
            {
                t += Time.deltaTime;
                float k = t / 0.3f;
                float r = Mathf.Lerp(1.2f, 6.5f, k);
                ring.transform.localScale = new Vector3(r, 0.04f * (1f - k) + 0.01f, r);
                yield return null;
            }
            if (ring != null) Destroy(ring);
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

        // ---------- 挥击剑气 ----------

        public static void SwingArc(Transform owner, bool heavy, Color color)
        {
            Ensure();
            var arc = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(arc.GetComponent<Collider>());
            arc.transform.position = owner.position + owner.forward * 1.1f + Vector3.up * 1.1f;
            arc.transform.rotation = owner.rotation * Quaternion.Euler(0, 0, heavy ? 0 : Random.Range(-30f, 30f));
            arc.GetComponent<MeshRenderer>().sharedMaterial =
                Mat(Color.Lerp(color, Color.white, 0.5f));
            _i.StartCoroutine(_i.ArcAnim(arc, heavy));
        }

        IEnumerator ArcAnim(GameObject arc, bool heavy)
        {
            float dur = heavy ? 0.22f : 0.14f;
            float t = 0;
            Vector3 max = heavy ? new Vector3(2.6f, 0.06f, 1.6f) : new Vector3(1.9f, 0.05f, 1.1f);
            while (t < dur && arc != null)
            {
                t += Time.deltaTime;
                float k = t / dur;
                arc.transform.localScale = Vector3.Lerp(new Vector3(0.3f, 0.04f, 0.2f), max,
                    Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            if (arc != null) Destroy(arc);
        }
    }
}
