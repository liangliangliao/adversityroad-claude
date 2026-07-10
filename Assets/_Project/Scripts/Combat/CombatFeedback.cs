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

        /// <summary>对外提供能量光材质（半透明加色）：护体屏障等可视化不再用实心色块。</summary>
        public static Material EnergyMaterial(Color c, float alpha) => MatFX(c, alpha);

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

        /// <summary>部位命中标签：直接贴在身体接触点旁（不加头顶偏移），
        /// 一眼看清「打中了哪里」；连击同一部位就连续弹出。</summary>
        public static void HitPartLabel(Vector3 contact, string text, Color color)
        {
            Ensure();
            var go = new GameObject("HitPart");
            go.transform.position = contact + Vector3.up * 0.12f + Random.insideUnitSphere * 0.06f;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = 46;
            tm.characterSize = 0.048f;
            tm.anchor = TextAnchor.MiddleLeft;
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
            // 必须用 Renderer 基类：动捕模型是 SkinnedMeshRenderer——此前只找
            // MeshRenderer，切动捕模型后受击闪红从未生效（"看不出被击中"的根因）
            var renderers = target.GetComponentsInChildren<Renderer>();
            var originals = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled || r is TrailRenderer || r is LineRenderer) continue;
                var m = r.material;
                originals[i] = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                SetColor(m, Color.Lerp(originals[i], new Color(1f, 0.22f, 0.18f), 0.9f));
            }
            yield return new WaitForSecondsRealtime(0.12f);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || r is TrailRenderer || r is LineRenderer) continue;
                SetColor(r.material, originals[i]);
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
            count = Mathf.Min(count, 6);   // 碎屑克制：小而透，绝不出现遮视野的大方块
            for (int i = 0; i < count; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = pos + Vector3.up * 1.2f;
                cube.transform.localScale = Vector3.one * Random.Range(0.04f, 0.1f);
                cube.GetComponent<MeshRenderer>().sharedMaterial = MatFX(color, 0.85f);
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
                spark.transform.localScale = new Vector3(0.04f, 0.04f, Random.Range(0.35f, 0.8f));
                spark.GetComponent<MeshRenderer>().sharedMaterial =
                    MatFX(Color.Lerp(color, Color.white, 0.6f), 0.9f);   // 加色光条而非实体块
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

        // ---------- 命中冲击（打击清晰化：命中点火花+白闪盘+顿帧+震屏+重击拉近） ----------
        // 参考格斗游戏：在【实际接触点】爆一簇火花与一枚朝镜头的白色冲击盘，让玩家
        // 一眼看清「击中了哪里」；重击叠加顿帧、震屏与短促拉近特写，读作"实打实的碰撞"。

        static int _combo;
        static float _lastComboT;

        /// <summary>在世界坐标 contact 处打出命中冲击。heavy=重击（更强反馈+特写）。
        /// countCombo=玩家打中敌人才计连击（被打不计）。</summary>
        public static void HitImpact(Vector3 contact, Color color, bool heavy, bool countCombo = true)
        {
            Ensure();
            SparksAt(contact, Color.Lerp(color, Color.white, 0.55f), heavy ? 16 : 10);
            // 命中点亮闪核（一枚朝镜头的高亮小球，瞬现瞬灭）：一眼锁定"打中了这里"
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            StripCol(core);
            core.transform.position = contact;
            core.transform.localScale = Vector3.one * (heavy ? 0.5f : 0.34f);
            core.GetComponent<MeshRenderer>().sharedMaterial =
                MatFX(Color.Lerp(color, Color.white, 0.85f), 0.9f);
            _i.StartCoroutine(_i.FlashPop(core, heavy ? 0.7f : 0.45f));

            // 格斗游戏式连击计数：2.2 秒内连续命中累计。
            // 计数改为屏幕固定位置的 HUD 计数器（格斗游戏惯例）——之前跟着接触点
            // 满场乱飞的"N 连击"浮字与伤害数字/部位标签挤成一团，战斗可读性差。
            if (countCombo)
            {
                if (Time.unscaledTime - _lastComboT > 2.2f) _combo = 0;
                _combo++;
                _lastComboT = Time.unscaledTime;
                if (_combo >= 2) GameEvents.RaiseComboCount(_combo);
            }

            // 命中点小型冲击盘：只标出命中位置，尺寸克制不遮视野。
            // 按用户要求：普通拳脚命中【不震屏、不切/拉镜头、不闪屏】——
            // 打击感交给受击方的倒地/击飞/踉跄反应与短促卡肉。
            var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
            StripCol(disc);
            disc.transform.position = contact;
            disc.GetComponent<MeshRenderer>().sharedMaterial =
                MatFX(Color.Lerp(color, Color.white, 0.75f), 0.5f);
            _i.StartCoroutine(_i.ImpactDisc(disc, heavy ? 0.55f : 0.38f));

            HitStop(heavy ? 0.07f : 0.035f);   // 短促卡肉（非晃屏）
        }

        /// <summary>在指定世界点爆一簇放射火花（不加内部高度偏移，用于精确命中点）。</summary>
        static void SparksAt(Vector3 center, Color color, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                StripCol(spark);
                spark.transform.position = center;
                spark.transform.rotation = Random.rotation;
                spark.transform.localScale = new Vector3(0.04f, 0.04f, Random.Range(0.3f, 0.75f));
                spark.GetComponent<MeshRenderer>().sharedMaterial = MatFX(color, 0.9f);
                _i.StartCoroutine(_i.SparkFly(spark));
            }
        }

        /// <summary>兵器相撞（格挡/对攻）：接触点爆出密集金白火花 + 金属声 + 短促卡肉，
        /// 读作"刀剑撞在一起"。</summary>
        public static void WeaponClash(Vector3 contact)
        {
            Ensure();
            SparksAt(contact, new Color(1f, 0.92f, 0.6f), 14);
            SparksAt(contact, Color.white, 6);
            var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
            StripCol(disc);
            disc.transform.position = contact;
            disc.GetComponent<MeshRenderer>().sharedMaterial =
                MatFX(new Color(1f, 0.95f, 0.75f), 0.55f);
            _i.StartCoroutine(_i.ImpactDisc(disc, 0.5f));
            GameAudio.Play(GameAudio.Sfx.Block, 0.9f);
            HitStop(0.055f);
        }

        /// <summary>血花：兵器/拳脚击中血肉的飞溅——深红细条从命中点向受击方
        /// 外侧喷出并受重力回落（数量克制，不遮视野）。</summary>
        public static void BloodSpray(Vector3 contact, Vector3 outDir)
        {
            Ensure();
            if (outDir.sqrMagnitude < 0.01f) outDir = Vector3.up;
            outDir = outDir.normalized;
            for (int i = 0; i < 9; i++)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
                StripCol(d);
                d.transform.position = contact;
                d.transform.localScale = new Vector3(0.03f, 0.03f, Random.Range(0.1f, 0.28f));
                Color c = Color.Lerp(new Color(0.62f, 0.05f, 0.05f), new Color(0.85f, 0.12f, 0.1f),
                    Random.value);
                d.GetComponent<MeshRenderer>().sharedMaterial = MatFX(c, 0.9f);
                Vector3 v = (outDir + Random.insideUnitSphere * 0.55f).normalized
                            * Random.Range(1.6f, 3.4f) + Vector3.up * Random.Range(0.6f, 1.6f);
                d.transform.rotation = Quaternion.LookRotation(v);
                _i.StartCoroutine(_i.BloodFly(d, v));
            }
        }

        IEnumerator BloodFly(GameObject d, Vector3 v)
        {
            float t = 0, life = Random.Range(0.28f, 0.45f);
            while (t < life && d != null)
            {
                float dt = Time.deltaTime;
                t += dt;
                v.y -= 12f * dt;                                  // 重力回落
                d.transform.position += v * dt;
                if (v.sqrMagnitude > 0.01f) d.transform.rotation = Quaternion.LookRotation(v);
                FadeAlpha(d, 1f - dt / life);
                yield return null;
            }
            if (d != null) Destroy(d);
        }

        /// <summary>招式名浮字：出招瞬间在角色头顶弹出招式名（玩家金、敌人红），
        /// 一眼看清双方"正在用什么招"。</summary>
        public static void MoveName(Vector3 pos, string name, bool enemy)
        {
            DamageNumber(pos + Vector3.up * 0.5f, "「" + name + "」",
                enemy ? new Color(1f, 0.4f, 0.35f) : new Color(1f, 0.85f, 0.35f), 1.15f);
        }

        /// <summary>地面冲击环：贴地快速扩散的能量圆环（重击落点/绝招终结，悟空式震地感）。</summary>
        public static void ShockRing(Vector3 pos, Color color, float maxR = 3f)
        {
            Ensure();
            var ring = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            StripCol(ring);
            ring.transform.position = pos + Vector3.up * 0.06f;
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                MatFX(Color.Lerp(color, Color.white, 0.5f), 0.6f);
            _i.StartCoroutine(_i.RingExpand(ring, maxR));
        }

        IEnumerator RingExpand(GameObject ring, float maxR)
        {
            float t = 0, dur = 0.28f;
            while (t < dur && ring != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Sqrt(t / dur);
                ring.transform.localScale = new Vector3(maxR * k, 0.05f, maxR * k);
                FadeAlpha(ring, 0.6f * (1f - k));
                yield return null;
            }
            if (ring != null) Destroy(ring);
        }

        IEnumerator ImpactDisc(GameObject disc, float maxR)
        {
            var cam = Camera.main;
            float t = 0, dur = 0.14f;
            while (t < dur && disc != null)
            {
                t += Time.deltaTime;
                float k = t / dur;
                if (cam != null) disc.transform.rotation = cam.transform.rotation;
                disc.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, maxR, Mathf.Sqrt(k));
                FadeAlpha(disc, 1f - k * 1.4f);
                yield return null;
            }
            if (disc != null) Destroy(disc);
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

        // ---------- 震屏（已全面停用） ----------
        // 按用户要求：任何击中（拳/腿/重击/大招）都不晃动屏幕、不闪屏、不掉转镜头。
        // 打击感完全交给：受击方的受击/击倒/击飞动作 + 短促卡肉(HitStop) + 命中点特效。
        public static void Shake(float strength = 0.6f) { }

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
