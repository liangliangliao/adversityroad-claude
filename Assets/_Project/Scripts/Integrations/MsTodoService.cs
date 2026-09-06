using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AdversityRoad.Integrations
{
    /// <summary>
    /// Microsoft To Do（Graph API）的登录与同步。
    ///
    /// 【为什么用设备码流程，而不是把系统浏览器拉起来回跳】
    /// 手机游戏做 OAuth 最麻烦的是回跳：授权码流程要注册自定义 scheme、改 Manifest、
    /// 在 Activity 里拦 Intent，还要处理"用户按了返回键"这类中断。
    /// 设备码流程（RFC 8628）把这些全省了——应用向微软要一个短码，玩家在**任何一台设备**
    /// 的浏览器里打开 microsoft.com/devicelogin 输入这个码，应用这边轮询拿令牌。
    /// 全程不需要重定向 URI，也就不需要包名与签名哈希。
    /// 配置里仍然提供那两项，是因为玩家在 Azure 门户注册 Android 平台时要填，
    /// 而且将来若改走授权码流程会直接用上（见 MsTodoConfig.RedirectUri）。
    ///
    /// 【令牌存哪】access/refresh token 存本机 PlayerPrefs，不上传任何地方。
    /// 这个游戏对隐私的口径一贯是"本地优先"，账号令牌更没有例外。
    ///
    /// 【"后台同步"到此为止的边界，必须说清楚】
    /// Android 会在应用退到后台后很快挂起进程，Unity 的协程随之停摆。
    /// 真正意义上的 OS 级后台同步需要一个 Android 前台服务或 WorkManager 任务，
    /// 那是 Java/Kotlin 侧的工作，不是纯 C# 能做到的。
    /// 这里实现的是**在应用运行期间按周期自动同步 + 从后台回到前台时补一次**，
    /// 并把这条边界写在同步面板上，不含糊其辞。
    /// </summary>
    public class MsTodoService : MonoBehaviour
    {
        public static MsTodoService Instance { get; private set; }

        public static MsTodoService Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("MsTodoService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<MsTodoService>();
            return Instance;
        }

        // ---- 状态 ----
        public enum State { SignedOut, WaitingForCode, SignedIn, Syncing }
        public State Status { get; private set; } = State.SignedOut;

        /// <summary>登录中要显示给玩家的东西（短码与验证网址）。</summary>
        public string UserCode { get; private set; } = "";
        public string VerificationUrl { get; private set; } = "";
        public string LastError { get; private set; } = "";
        public string Account { get; private set; } = "";
        public TodoSnapshot Data { get; private set; } = new TodoSnapshot();

        /// <summary>状态或数据变化时触发，界面据此刷新（不必每帧轮询）。</summary>
        public static event Action Changed;
        static void Raise() { try { Changed?.Invoke(); } catch { } }

        const string KeyAccess = "mstodo_at";
        const string KeyRefresh = "mstodo_rt";
        const string KeyExpiry = "mstodo_exp";
        const string KeyCache = "mstodo_cache";

        string _access, _refresh;
        double _expiresAt;
        float _nextAutoSync;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _access = PlayerPrefs.GetString(KeyAccess, "");
            _refresh = PlayerPrefs.GetString(KeyRefresh, "");
            double.TryParse(PlayerPrefs.GetString(KeyExpiry, "0"), out _expiresAt);
            string cache = PlayerPrefs.GetString(KeyCache, "");
            if (!string.IsNullOrEmpty(cache))
            {
                try { Data = JsonUtility.FromJson<TodoSnapshot>(cache) ?? new TodoSnapshot(); }
                catch { Data = new TodoSnapshot(); }
                Account = Data.account;
            }
            if (!string.IsNullOrEmpty(_refresh)) Status = State.SignedIn;
        }

        void Update()
        {
            var cfg = MsTodoConfig.Current;
            if (!cfg.autoSync || Status != State.SignedIn || !cfg.Usable) return;
            if (Time.unscaledTime < _nextAutoSync) return;
            _nextAutoSync = Time.unscaledTime + Mathf.Max(60, cfg.syncMinutes * 60);
            StartCoroutine(SyncRoutine());
        }

        /// <summary>从后台回到前台补一次同步：进程挂起期间协程是停的，回来要对一次账。</summary>
        void OnApplicationPause(bool paused)
        {
            if (paused || Status != State.SignedIn) return;
            if (!MsTodoConfig.Current.autoSync) return;
            _nextAutoSync = 0f;
        }

        // ================= 登录 =================

        public void SignIn()
        {
            if (Status == State.WaitingForCode) return;
            var cfg = MsTodoConfig.Current;
            if (!cfg.Usable)
            {
                LastError = "还没填 Client ID / Tenant——先在上面填好并保存。";
                Raise();
                return;
            }
            StartCoroutine(DeviceCodeRoutine());
        }

        public void SignOut()
        {
            _access = _refresh = "";
            _expiresAt = 0;
            Account = "";
            Status = State.SignedOut;
            UserCode = VerificationUrl = "";
            PlayerPrefs.DeleteKey(KeyAccess);
            PlayerPrefs.DeleteKey(KeyRefresh);
            PlayerPrefs.DeleteKey(KeyExpiry);
            PlayerPrefs.Save();
            Raise();
        }

        IEnumerator DeviceCodeRoutine()
        {
            var cfg = MsTodoConfig.Current;
            LastError = "";
            Status = State.WaitingForCode;
            Raise();

            // 租户要换着试。原因：/common 只对"多租户 + 个人账户"的应用注册有效，
            // 注册时账户类型选成"仅个人 Microsoft 账户"要用 consumers，
            // 选成"仅此组织目录"要用组织的租户 ID——而玩家没有义务知道自己当初选了哪一项，
            // 更没法从报错 AADSTS50059 反推。所以先用配置里的，失败且是"租户解析不了"这一类
            // 错误时，依次换 consumers / organizations / common 再试。
            // 试通的那个要存回配置：后面取令牌、刷新令牌必须走同一个租户端点。
            var tries = new List<string> { cfg.tenant };
            foreach (var alt in new[] { "consumers", "organizations", "common" })
                if (!tries.Contains(alt)) tries.Add(alt);

            GDeviceCode dc = null;
            string useTenant = cfg.tenant;
            string lastErr = "";
            foreach (string t in tries)
            {
                var form = new WWWForm();
                form.AddField("client_id", cfg.clientId);
                form.AddField("scope", cfg.scopes);
                using (var req = UnityWebRequest.Post(cfg.DeviceCodeUrlFor(t), form))
                {
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        dc = Parse<GDeviceCode>(req.downloadHandler.text);
                        if (dc != null && !string.IsNullOrEmpty(dc.device_code))
                        {
                            useTenant = t;
                            break;
                        }
                        lastErr = "返回内容看不懂（租户 " + t + "）。";
                        continue;
                    }
                    lastErr = Describe(req) + "\n请求地址：" + cfg.DeviceCodeUrlFor(t);
                    dc = null;
                    // 只有"认不出租户"这一类才值得换个租户重试；
                    // 没开公共客户端流、Client ID 不存在这些换几次都是一样的结果。
                    if (!TenantIssue(lastErr)) break;
                }
            }

            if (dc == null || string.IsNullOrEmpty(dc.device_code))
            {
                Fail("取设备码失败：" + lastErr);
                yield break;
            }

            if (useTenant != cfg.tenant)
            {
                cfg.tenant = useTenant;
                cfg.Save();
            }

            UserCode = dc.user_code;
            VerificationUrl = string.IsNullOrEmpty(dc.verification_uri)
                ? "https://microsoft.com/devicelogin" : dc.verification_uri;
            Raise();

            // 轮询取令牌。interval 是微软给的最小间隔，抢着问会被 slow_down 惩罚。
            float interval = Mathf.Max(3, dc.interval);
            float deadline = Time.unscaledTime + Mathf.Max(60, dc.expires_in);
            while (Time.unscaledTime < deadline)
            {
                yield return new WaitForSecondsRealtime(interval);
                if (Status != State.WaitingForCode) yield break;   // 玩家中途取消

                var tf = new WWWForm();
                tf.AddField("grant_type", "urn:ietf:params:oauth:grant-type:device_code");
                tf.AddField("client_id", cfg.clientId);
                tf.AddField("device_code", dc.device_code);

                using (var req = UnityWebRequest.Post(cfg.TokenUrlFor(useTenant), tf))
                {
                    yield return req.SendWebRequest();
                    var tok = Parse<GToken>(req.downloadHandler != null
                        ? req.downloadHandler.text : "");
                    if (tok == null) continue;

                    if (!string.IsNullOrEmpty(tok.access_token))
                    {
                        StoreToken(tok);
                        Status = State.SignedIn;
                        UserCode = VerificationUrl = "";
                        Raise();
                        yield return WhoAmI();
                        yield return SyncRoutine();
                        yield break;
                    }
                    // authorization_pending = 玩家还没在浏览器里输码，继续等，这不是错误
                    if (tok.error == "authorization_pending") continue;
                    if (tok.error == "slow_down") { interval += 5f; continue; }
                    if (!string.IsNullOrEmpty(tok.error))
                    {
                        Fail("登录失败：" + tok.error + " " + tok.error_description);
                        yield break;
                    }
                }
            }
            Fail("登录超时——短码过期了，重新点一次登录。");
        }

        void StoreToken(GToken tok)
        {
            _access = tok.access_token;
            if (!string.IsNullOrEmpty(tok.refresh_token)) _refresh = tok.refresh_token;
            // 提前 120 秒当作过期：正好卡在边界上发请求会拿到 401
            _expiresAt = CurrentUnix() + Mathf.Max(60, tok.expires_in) - 120;
            PlayerPrefs.SetString(KeyAccess, _access);
            PlayerPrefs.SetString(KeyRefresh, _refresh);
            PlayerPrefs.SetString(KeyExpiry, _expiresAt.ToString("F0"));
            PlayerPrefs.Save();
        }

        IEnumerator EnsureToken()
        {
            if (!string.IsNullOrEmpty(_access) && CurrentUnix() < _expiresAt) yield break;
            if (string.IsNullOrEmpty(_refresh)) yield break;

            var cfg = MsTodoConfig.Current;
            var form = new WWWForm();
            form.AddField("grant_type", "refresh_token");
            form.AddField("client_id", cfg.clientId);
            form.AddField("refresh_token", _refresh);
            form.AddField("scope", cfg.scopes);
            using (var req = UnityWebRequest.Post(cfg.TokenUrl, form))
            {
                yield return req.SendWebRequest();
                var tok = Parse<GToken>(req.downloadHandler != null ? req.downloadHandler.text : "");
                if (tok != null && !string.IsNullOrEmpty(tok.access_token)) StoreToken(tok);
                else
                {
                    // 刷新令牌也失效了（改密码、撤销授权、过期）——退回未登录，让玩家重来
                    SignOut();
                    LastError = "登录已过期，需要重新登录。";
                    Raise();
                }
            }
        }

        IEnumerator WhoAmI()
        {
            var cfg = MsTodoConfig.Current;
            using (var req = Get(cfg.graphBase.TrimEnd('/') + "/me"))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) yield break;
                var me = Parse<GMe>(req.downloadHandler.text);
                if (me != null)
                    Account = !string.IsNullOrEmpty(me.userPrincipalName)
                        ? me.userPrincipalName : me.displayName;
            }
            Raise();
        }

        // ================= 同步 =================

        public void SyncNow()
        {
            if (Status == State.Syncing) return;
            if (Status != State.SignedIn) { LastError = "还没登录。"; Raise(); return; }
            StartCoroutine(SyncRoutine());
        }

        IEnumerator SyncRoutine()
        {
            if (Status == State.Syncing) yield break;
            Status = State.Syncing;
            LastError = "";
            Raise();

            yield return EnsureToken();
            if (string.IsNullOrEmpty(_access)) { Status = State.SignedOut; Raise(); yield break; }

            var cfg = MsTodoConfig.Current;
            var snap = new TodoSnapshot { account = Account };

            // 一层：清单
            using (var req = Get(cfg.ListsUrl))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Fail("拉取清单失败：" + Describe(req), State.SignedIn);
                    yield break;
                }
                var gl = Parse<GLists>(req.downloadHandler.text);
                if (gl != null)
                    foreach (var l in gl.value)
                        snap.lists.Add(new TodoList
                        {
                            id = l.id, displayName = l.displayName,
                            isOwner = l.isOwner, isShared = l.isShared
                        });
            }

            // 二层：任务；三层：步骤
            foreach (var list in snap.lists)
            {
                string tasksUrl = cfg.ListsUrl + "/" + list.id + "/tasks?$top=100";
                using (var req = Get(tasksUrl))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) continue;
                    var gt = Parse<GTasks>(req.downloadHandler.text);
                    if (gt == null) continue;
                    foreach (var t in gt.value)
                        list.tasks.Add(new TodoTask
                        {
                            id = t.id,
                            title = t.title,
                            status = t.status,
                            importance = t.importance,
                            dueDate = ShortDate(t.dueDateTime != null ? t.dueDateTime.dateTime : null),
                            bodyPreview = FirstLine(t.body != null ? t.body.content : null)
                        });
                }

                // 步骤只对"还没完成"的任务拉：已完成任务的步骤没人看，
                // 而每个任务一次请求，清单大了会把同步拖成几十次往返。
                foreach (var task in list.tasks)
                {
                    if (task.Done) continue;
                    string stepUrl = cfg.ListsUrl + "/" + list.id + "/tasks/" + task.id + "/checklistItems";
                    using (var req = Get(stepUrl))
                    {
                        yield return req.SendWebRequest();
                        if (req.result != UnityWebRequest.Result.Success) continue;
                        var gc = Parse<GChecks>(req.downloadHandler.text);
                        if (gc == null) continue;
                        foreach (var c in gc.value)
                            task.steps.Add(new TodoStep
                            {
                                id = c.id, displayName = c.displayName, isChecked = c.isChecked
                            });
                    }
                }
            }

            snap.syncedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Data = snap;
            PlayerPrefs.SetString(KeyCache, JsonUtility.ToJson(snap));
            PlayerPrefs.Save();
            Status = State.SignedIn;
            _nextAutoSync = Time.unscaledTime + Mathf.Max(60, cfg.syncMinutes * 60);
            Raise();
        }

        // ================= 小工具 =================

        UnityWebRequest Get(string url)
        {
            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Authorization", "Bearer " + _access);
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = 20;
            return req;
        }

        void Fail(string msg, State back = State.SignedOut)
        {
            LastError = msg + Hint(msg);
            Status = back;
            UserCode = VerificationUrl = "";
            Raise();
        }

        /// <summary>
        /// 把微软的 AADSTS 码翻成"到哪一页点哪一下"。
        /// 原始英文照留——它带 Trace ID，真去提工单时有用；但玩家先看到的应该是能照做的那一句。
        /// 这些码全都指向 Azure 应用注册的配置，没有一个是游戏这边能改掉的，
        /// 所以提示必须说清楚是去门户改，而不是让人反复点「登录」。
        /// </summary>
        /// <summary>这条报错是不是"认不出租户"——只有这一类换个租户重试才有意义。</summary>
        static bool TenantIssue(string raw) =>
            !string.IsNullOrEmpty(raw) &&
            (raw.Contains("AADSTS50059") || raw.Contains("AADSTS90002") ||
             raw.Contains("AADSTS900023") || raw.Contains("AADSTS700016") ||
             raw.Contains("AADSTS50194") || raw.Contains("tenant"));

        static string Hint(string raw)
        {
            if (!string.IsNullOrEmpty(raw) && raw.Contains("AADSTS50059"))
                return "\n\n→ consumers / organizations / common 都试过了，都不认这个 Client ID。" +
                       "这说明该应用注册是【仅此组织目录】的单租户应用——单租户只认它自己的租户地址。\n" +
                       "最省事：重新注册一个应用，在\"新注册\"那一页把账户类型直接选成" +
                       "\"任何组织目录 + 个人 Microsoft 账户\"（新建时能选，建完再改会被门户挡下来，" +
                       "报 requestedAccessTokenVersion is invalid），把新的 Client ID 填回上面。\n" +
                       "要救现有的那个：门户 → 你的应用 → \"清单\"，先把 requestedAccessTokenVersion " +
                       "（旧版清单里叫 accessTokenAcceptedVersion）改成 2 并保存，再改账户类型。\n" +
                       "另一条路：把上面的 Tenant 换成你的\"目录(租户) ID\"（概述页可复制），" +
                       "单租户应用配自己的租户地址是能登的——但个人 Microsoft 账号的待办数据不在组织租户里，" +
                       "登进去可能看不到清单，所以还是推荐重新注册。";
            if (string.IsNullOrEmpty(raw)) return "";
            if (raw.Contains("AADSTS70002") || raw.Contains("AADSTS7000218") ||
                raw.Contains("must be marked as"))
                return "\n\n→ 这个应用没被当成\"公共客户端\"。去 portal.azure.com → 应用注册 → " +
                       "选中你的应用 → 左侧\"身份验证\" → 拉到最下面\"高级设置 / 允许公共客户端流\" " +
                       "选【是】→ 保存。改完等一两分钟再点登录。\n" +
                       "顺带检查：平台那一栏只能加\"移动和桌面应用程序\"，" +
                       "加成\"Web\"或\"单页应用程序\"同样会被拒。";
            if (raw.Contains("AADSTS700016") || raw.Contains("was not found in the directory"))
                return "\n\n→ 这个 Client ID 在该目录里不存在。检查两件事：" +
                       "Client ID 有没有粘错（是概述页的\"应用程序(客户端) ID\"，不是对象 ID）；" +
                       "个人 Microsoft 账号请把 Tenant 填 common。";
            if (raw.Contains("AADSTS900023") || raw.Contains("Specified tenant identifier"))
                return "\n\n→ Tenant 填错了。个人账号填 common；企业账号填租户 ID 或域名。";
            if (raw.Contains("AADSTS50194") || raw.Contains("AADSTS50020"))
                return "\n\n→ 应用注册时账户类型选窄了。改成\"任何组织目录中的账户 + 个人 Microsoft 账户\"，" +
                       "或者把 Tenant 改成你自己的租户 ID。";
            if (raw.Contains("AADSTS65001") || raw.Contains("consent"))
                return "\n\n→ 权限没同意。去 API 权限页确认已添加 Microsoft Graph 的委托权限 " +
                       "Tasks.ReadWrite、User.Read、offline_access，然后重新登录并在浏览器里点同意。";
            if (raw.Contains("AADSTS650053") || raw.Contains("invalid_scope"))
                return "\n\n→ Scopes 写错了。保持默认的 offline_access Tasks.ReadWrite User.Read，空格分隔。";
            return "";
        }

        static string Describe(UnityWebRequest req) =>
            req.responseCode + " " + req.error + " " +
            (req.downloadHandler != null ? Trim(req.downloadHandler.text, 180) : "");

        static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");

        static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); } catch { return null; }
        }

        static double CurrentUnix() =>
            (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        static string ShortDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            return DateTime.TryParse(iso, out var d) ? d.ToString("MM-dd") : "";
        }

        static string FirstLine(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            string s = body.Replace("\r", "").Trim();
            int nl = s.IndexOf('\n');
            if (nl > 0) s = s.Substring(0, nl);
            return Trim(s, 60);
        }
    }
}
