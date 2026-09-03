using System.Collections;
using System.Collections.Generic;
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

        /// <summary>拖尾（刀光）走的着色器名，供 CI 诊断核对——挑到哪一条不该靠猜。</summary>
        public static string TrailShaderName { get; private set; } = "（尚未创建）";

        /// <summary>
        /// 刀光拖尾专用材质。
        ///
        /// 【为什么不能沿用 EnergyMaterial】那条路是 URP/Lit 改混合模式：
        ///   1) URP/Lit **不读顶点色**。而 TrailRenderer 的"越靠尾端越透明"
        ///      正是写在顶点色里的——用 Lit 画出来的是一条**通体等亮**的实心带子，
        ///      根本不会淡出。
        ///   2) 它是加色混合（SrcAlpha + One）且**受光照**。白天场景本来就亮，
        ///      一条 0.28 米宽、0.75 不透明度的加色带叠上去直接饱和到纯白——
        ///      这就是玩家在截图里看到的"剑上明显的白色痕迹"，
        ///      而且每把兵器都走这一条，所以**所有武器都有**。
        ///
        /// 改用 Sprites/Default 一类的**无光照 + 顺顶点色 + 普通 alpha 混合**着色器：
        /// 不受光照就不会被照亮到过曝，普通 alpha 混合数学上不可能叠出白，
        /// 顶点色生效之后刃尖到尾端的淡出才真的画得出来。
        /// 这几条着色器都在"始终包含"清单里（GraphicsSettings），不会被打包裁掉。
        /// </summary>
        public static Material TrailMaterial(Color c, float alpha)
        {
            Shader sh = Shader.Find("Sprites/Default")
                     ?? Shader.Find("UI/Default")
                     ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return MatFX(c, alpha * 0.5f);   // 兜底：至少把亮度砍一半
            TrailShaderName = sh.name;
            var m = new Material(sh);
            Color cc = c; cc.a = alpha;
            m.color = cc;
            if (m.HasProperty("_Color")) m.SetColor("_Color", cc);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", cc);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
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
            tm.characterSize = 0.058f;   // 放大一档：部位反馈是打击确认的一部分，要读得到
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
        // 原始色缓存（每个材质首次闪红前记录真实底色）+ 闪红版本号（每目标）。
        // 根治"连续快速被击中后肤色卡在红色"：重叠的闪红协程若各自"记录当前色再
        // 恢复"，第二次会把【已经变红的色】当成原色记下来恢复→永久红。改为：
        // 原色只在首次记录（恒为真实底色），且只有最新一次闪红负责恢复。
        static readonly Dictionary<Material, Color> _origColor = new Dictionary<Material, Color>();
        static readonly Dictionary<GameObject, int> _flashVer = new Dictionary<GameObject, int>();

        public static void HitFlash(GameObject target)
        {
            Ensure();
            _i.StartCoroutine(_i.Flash(target));
        }

        IEnumerator Flash(GameObject target)
        {
            if (target == null) yield break;
            int ver = (_flashVer.TryGetValue(target, out int v) ? v : 0) + 1;
            _flashVer[target] = ver;

            // 必须用 Renderer 基类：动捕模型是 SkinnedMeshRenderer
            var renderers = target.GetComponentsInChildren<Renderer>();
            var mats = new List<Material>();
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || r is TrailRenderer || r is LineRenderer) continue;
                if (r.GetComponent<TextMesh>() != null) continue;
                var m = r.material;
                // 原色只在首次记录（此时一定是真实底色，绝不会是红闪色）
                if (!_origColor.ContainsKey(m))
                    _origColor[m] = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                mats.Add(m);
                SetColor(m, Color.Lerp(_origColor[m], new Color(1f, 0.22f, 0.18f), 0.85f));
            }

            yield return new WaitForSecondsRealtime(0.12f);

            // 仅最新一次闪红负责恢复（更早的重叠闪红不再覆盖，避免恢复成红色）
            if (_flashVer.TryGetValue(target, out int cur) && cur != ver) yield break;
            foreach (var m in mats)
                if (m != null && _origColor.TryGetValue(m, out Color oc)) SetColor(m, oc);
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

        // ---------- 顿帧（打击"分量"的第一来源）----------
        // 卡肉是动作游戏表达力道最有效的手段：刀砍进身体的那一瞬间，画面短暂停住，
        // 玩家才会觉得"这一下砸实了"。两处关键修正：
        // ① 强度分级——轻拳 0.035s、连段末段 0.06s、重击 0.09s、处决 0.13s，
        //    此前重击与轻击几乎一样，于是所有攻击都读作"挠痒痒"；
        //    上限从 0.115 收到 0.10、冻结深度从 0.05 放宽到 0.07：一记 0.30 秒的
        //    轻击里塞 0.115 秒的近乎全停，占了三分之一时长，读出来就是"打着打着
        //    人不动了"。顿帧要短到只当作"撞击的那一下"，不能长到像掉帧；
        // ② 可续期——此前"正在顿帧中"会直接丢掉新的顿帧请求，而群战/连段里
        //    命中往往密集发生，重击顿帧常被前一记轻击的顿帧吃掉。改为取更晚的
        //    截止时刻续期：每一下都吃得到与自己力度相称的顿挫。

        static float _hitStopUntil;
        static bool _slowMoing;

        public static void HitStop(float duration = 0.05f)
        {
            Ensure();
            if (_hitStopping)
            {
                if (_slowMoing) return;   // 慢镜演出优先，不被顿帧打断
                _hitStopUntil = Mathf.Max(_hitStopUntil, Time.unscaledTime + duration);
                return;
            }
            if (Time.timeScale < 0.9f) return;   // 不与暂停/面板冲突
            _hitStopUntil = Time.unscaledTime + duration;
            _i.StartCoroutine(_i.DoHitStop());
        }

        IEnumerator DoHitStop()
        {
            _hitStopping = true;
            float prev = Time.timeScale;
            Time.timeScale = 0.07f;
            while (Time.unscaledTime < _hitStopUntil) yield return null;
            if (Time.timeScale < 0.9f) Time.timeScale = prev; // 期间未被面板改动才恢复
            _hitStopping = false;
        }

        /// <summary>按打击力度给顿帧（power01：0=轻拳，1=终结技级）。</summary>
        public static void HitStopByPower(float power01) =>
            HitStop(Mathf.Lerp(0.035f, 0.10f, Mathf.Clamp01(power01)));

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
        /// countCombo=玩家打中敌人才计连击（被打不计）。
        /// power01≥0 时按打击力度连续分级火花密度/闪核大小/顿帧时长——
        /// 一记轻拳与一记终结斩的反馈从此不再长得一样（"攻击力显得很弱"的直接来源）。</summary>
        public static void HitImpact(Vector3 contact, Color color, bool heavy,
            bool countCombo = true, float power01 = -1f)
        {
            Ensure();
            float p = power01 >= 0f ? Mathf.Clamp01(power01) : (heavy ? 0.85f : 0.25f);
            SparksAt(contact, Color.Lerp(color, Color.white, 0.55f),
                Mathf.RoundToInt(Mathf.Lerp(9f, 22f, p)));
            // 命中点亮闪核（一枚朝镜头的高亮小球，瞬现瞬灭）：一眼锁定"打中了这里"
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            StripCol(core);
            core.transform.position = contact;
            core.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 0.62f, p);
            core.GetComponent<MeshRenderer>().sharedMaterial =
                MatFX(Color.Lerp(color, Color.white, 0.85f), 0.9f);
            _i.StartCoroutine(_i.FlashPop(core, Mathf.Lerp(0.4f, 0.85f, p)));

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

            // 命中点圆盘已按用户要求停用（不再渲染圆圈）——命中位置由火花/闪核标出。
            HitStopByPower(p);   // 短促卡肉（非晃屏），力度越大顿得越久
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
            // 冲击圆盘已停用（不再渲染圆圈）——相撞感由火花+金属声+卡肉表达
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

        /// <summary>地面冲击环（已按用户要求停用）：招式/绝招不再渲染贴地扩散的圆环。
        /// 打击感交给受击反应/卡肉/火花，不再有满地圆圈。</summary>
        public static void ShockRing(Vector3 pos, Color color, float maxR = 3f)
        {
            // no-op：不再生成地面圆环
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

        // ---------- 蓄力气场（狂风环流） ----------

        /// <summary>蓄力气场：环身狂风——细长风弧绕角色高速环绕上升 + 地面气浪环，
        /// charge01 越大风势越强。读作"强大气流护体，敌人无法近身"。</summary>
        public static void ChargeGale(Vector3 pos, float charge01)
        {
            Ensure();
            int n = 2 + Mathf.RoundToInt(charge01 * 3f);
            for (int i = 0; i < n; i++) _i.StartCoroutine(_i.GaleArc(pos, charge01));
            ShockRing(pos, new Color(0.72f, 0.9f, 1f), 2.4f + charge01 * 1.6f);
        }

        IEnumerator GaleArc(Vector3 center, float charge01)
        {
            var arc = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCol(arc);
            arc.transform.localScale = new Vector3(0.05f, 0.07f, Random.Range(1.0f, 1.8f));
            arc.GetComponent<MeshRenderer>().sharedMaterial =
                MatFX(Color.Lerp(new Color(0.75f, 0.92f, 1f), Color.white, 0.4f), 0.65f);
            float ang = Random.Range(0f, 360f);
            float h = Random.Range(0.1f, 0.7f);
            float spd = Random.Range(460f, 640f) * (0.8f + 0.5f * charge01);   // 高速环绕=强气流
            float t = 0, dur = 0.5f;
            while (t < dur && arc != null)
            {
                float dt = Time.deltaTime;
                t += dt;
                ang += spd * dt;
                h += 1.7f * dt;                                    // 螺旋上升
                float r = 1.4f + t * 0.9f;                          // 逐渐外扩
                float rad = ang * Mathf.Deg2Rad;
                Vector3 p = center + new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad)) * r
                            + Vector3.up * h;
                // 朝切线方向（风沿环流方向拉长）
                Vector3 tangent = new Vector3(-Mathf.Sin(rad), 0.12f, Mathf.Cos(rad));
                arc.transform.position = p;
                arc.transform.rotation = Quaternion.LookRotation(tangent);
                FadeAlpha(arc, 1f - dt / dur * 1.2f);
                yield return null;
            }
            if (arc != null) Destroy(arc);
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
            _slowMoing = true;
            Time.timeScale = scale;
            yield return new WaitForSecondsRealtime(realDuration);
            if (Time.timeScale < 0.9f) Time.timeScale = 1f;
            _slowMoing = false;
            _hitStopping = false;
        }

        // ---------- 震屏（已全面停用） ----------
        // 按用户要求：任何击中（拳/腿/重击/大招）都不晃动屏幕、不闪屏、不掉转镜头。
        // 打击感完全交给：受击方的受击/击倒/击飞动作 + 短促卡肉(HitStop) + 命中点特效。
        public static void Shake(float strength = 0.6f) { }

        /// <summary>大招镜头：短暂拉近取景（仅大招调用，普通攻击/移动不触发）。</summary>
        /// <summary>推近特写（分级）：strength 1=大招满推，0.5~0.6=击杀/处决轻推。
        /// 节流与群战抑制由镜头侧统一裁决——调用方只负责"这一刻值不值得一个特写"。</summary>
        public static void CloseUp(float duration, float strength)
        {
            var cam = Camera.main != null
                ? Camera.main.GetComponent<Player.ThirdPersonCamera>() : null;
            if (cam != null) cam.CloseUp(duration, strength);
        }

        public static void UltimateShot(float duration)
        {
            var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            if (cam != null) cam.UltimateShot(duration);
        }

        /// <summary>
        /// Boss 绝招特写（各 Boss 的机制技共用这一个入口）。
        /// 名字前自动冠上 Boss 的称号，于是横幅永远是"谁 · 什么招 — 怎么应对"。
        /// </summary>
        public static void BossUltimate(AI.EnemyController ec, string moveName, string answer,
            float duration = 1.1f)
        {
            if (ec == null || ec.State == AI.EnemyState.Dead) return;
            string who = ec.profile != null ? ec.profile.displayName : "";
            EnemyCastShot(ec.transform, duration,
                string.IsNullOrEmpty(who) ? moveName : who + " · " + moveName, answer);
        }

        /// <summary>
        /// 敌方绝招特写：把施展者框进画面 + 报出招名与应对答案。
        ///
        /// 这是给玩家的**防守信息**，不是给敌人的排场：所以
        ///   · 不锁操作、不停时间（只在起手一瞬给一格极短的时缓作为"注意"提示）；
        ///   · 横幅里带上应对答案（挡 / 侧移 / 跳 / 闪），看见的同时就知道该做什么；
        ///   · 时长不超过前摇本身——特写结束的那一刻正好是它出手的那一刻。
        /// </summary>
        static float _lastCastShot = -99f;

        public static void EnemyCastShot(Transform caster, float duration,
            string moveName, string answer = null)
        {
            // 全局节流：多个 Boss 机制技/精英红光挤在一起时，连续推镜会把战场推没了。
            // 5 秒一次已经足够"每个大招都被看见"，又不会让镜头一直在演出。
            if (Time.unscaledTime - _lastCastShot < 5f) return;
            _lastCastShot = Time.unscaledTime;

            var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            if (cam != null) cam.FocusOn(caster, duration);
            if (!string.IsNullOrEmpty(moveName))
                Core.GameEvents.RaiseSkillBanner(moveName +
                    (string.IsNullOrEmpty(answer) ? "" : "　—　" + answer));
            SlowMo(0.62f, 0.16f);   // 一格"注意"：短到不影响操作，长到能被看见
            Core.GameAudio.Play(Core.GameAudio.Sfx.Alert, 0.9f, 0.3f);
        }

        // ---------- 挥击剑气 ----------

        /// <summary>剑气（弧月刃气）：程序化新月弧网格，双层（亮芯+柔光晕）叠加加色发光，
        /// 沿挥击方向飞出、边飞边舒展淡出——替代旧的"发光方块拉长"式刀光。
        /// 轻击=窄弧快飞、随机斜角；重击/绝招=宽弧更亮、飞更远。不用任何圆圈。</summary>
        public static void SwingArc(Transform owner, bool heavy, Color color)
        {
            Ensure();
            GameAudio.Play(GameAudio.Sfx.Swing, heavy ? 0.9f : 0.6f);
            float tilt = (heavy ? Random.Range(-12f, 12f) : Random.Range(-46f, 46f)) + 58f;
            Vector3 origin = owner.position + owner.forward * 1.0f + Vector3.up * 1.25f;
            Quaternion rot = owner.rotation * Quaternion.Euler(0, 0, tilt);
            // 双层：内层亮芯（窄、白热）+ 外层光晕（宽、本色），同步飞行
            _i.StartCoroutine(_i.QiAnim(SpawnCrescent(heavy, Color.Lerp(color, Color.white, 0.55f),
                heavy ? 0.5f : 0.4f, 0.8f), origin, rot, owner.forward, heavy, 0f));
            _i.StartCoroutine(_i.QiAnim(SpawnCrescent(heavy, color,
                heavy ? 0.3f : 0.22f, 1.25f), origin, rot, owner.forward, heavy, 0.02f));
        }

        static Mesh _qiMeshLight, _qiMeshHeavy;

        /// <summary>新月弧网格（本地 XY 面）：外缘随角度收细成月牙尖，顶点色 alpha
        /// 中段亮、两尖渐隐——一张网格即呈现"剑气"的月牙形与羽化边。</summary>
        static Mesh CrescentMesh(bool heavy)
        {
            Mesh cache = heavy ? _qiMeshHeavy : _qiMeshLight;
            if (cache != null) return cache;
            float arcDeg = heavy ? 130f : 100f;
            float r0 = heavy ? 0.85f : 0.7f;
            float belly = heavy ? 0.42f : 0.3f;      // 月牙最厚处
            const int Seg = 24;
            var v = new Vector3[(Seg + 1) * 2];
            var col = new Color[(Seg + 1) * 2];
            var tris = new int[Seg * 6];
            float half = arcDeg * Mathf.Deg2Rad * 0.5f;
            for (int i = 0; i <= Seg; i++)
            {
                float u = (float)i / Seg;                     // 0..1 along the arc
                float a = -half + u * half * 2f;
                float thick = belly * Mathf.Pow(Mathf.Cos((u - 0.5f) * Mathf.PI), 0.75f);
                Vector3 dir = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                v[i * 2] = dir * r0;
                v[i * 2 + 1] = dir * (r0 + thick);
                float fade = Mathf.Pow(Mathf.Cos((u - 0.5f) * Mathf.PI), 1.5f);   // 两尖渐隐
                col[i * 2] = new Color(1, 1, 1, fade);
                col[i * 2 + 1] = new Color(1, 1, 1, fade * 0.15f);                // 外缘羽化
            }
            for (int i = 0; i < Seg; i++)
            {
                int a0 = i * 2, b0 = i * 2 + 1, a1 = (i + 1) * 2, b1 = (i + 1) * 2 + 1;
                tris[i * 6] = a0; tris[i * 6 + 1] = a1; tris[i * 6 + 2] = b0;
                tris[i * 6 + 3] = b0; tris[i * 6 + 4] = a1; tris[i * 6 + 5] = b1;
            }
            cache = new Mesh { vertices = v, colors = col, triangles = tris };
            cache.RecalculateNormals();
            cache.RecalculateBounds();
            if (heavy) _qiMeshHeavy = cache; else _qiMeshLight = cache;
            return cache;
        }

        static GameObject SpawnCrescent(bool heavy, Color c, float alpha, float widthMul)
        {
            var go = new GameObject("SwordQi");
            go.AddComponent<MeshFilter>().sharedMesh = CrescentMesh(heavy);
            var mr = go.AddComponent<MeshRenderer>();
            var m = MatFX(c, alpha);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // 双面（背面也可见）
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = new Vector3(widthMul, widthMul, 1f);
            return go;
        }

        /// <summary>剑气飞行：沿挥向飞出、逐渐舒展放大并淡出；重击飞更快更远。</summary>
        IEnumerator QiAnim(GameObject qi, Vector3 origin, Quaternion rot, Vector3 fly, bool heavy, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (qi == null) yield break;
            qi.transform.SetPositionAndRotation(origin, rot);
            float dur = heavy ? 0.42f : 0.3f;
            float speed = heavy ? 9f : 6.5f;
            Vector3 s0 = qi.transform.localScale;
            float t = 0;
            while (t < dur && qi != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                qi.transform.position += fly * (speed * (1f - k * 0.45f) * Time.deltaTime);
                qi.transform.localScale = s0 * (1f + k * (heavy ? 1.1f : 0.7f));
                FadeAlpha(qi, 1f - Time.deltaTime / dur * 1.15f);   // 渐隐
                yield return null;
            }
            if (qi != null) Destroy(qi);
        }
    }
}
