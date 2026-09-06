using System;
using UnityEngine;

namespace AdversityRoad.Integrations
{
    /// <summary>
    /// Microsoft To Do 同步的连接参数。全部存在本机 PlayerPrefs，不上传任何地方。
    ///
    /// 【为什么把这些参数摊开给玩家填，而不是写死在代码里】
    /// 这个工程是开源仓库，写死 client id 等于把一个注册应用公开出去让所有人共用；
    /// 而 Azure 里注册一个"移动与桌面应用"是免费的、五分钟的事。
    /// 摊开填还有一个好处：企业租户的用户可以填自己公司的 tenant，不必走 common。
    ///
    /// 【哪些参数用于哪条流程】
    /// · 设备码登录（本作默认，见 MsTodoService）：只需要 Tenant + ClientId + Scopes。
    ///   它不需要重定向 URI，因此也不需要包名与签名哈希——这正是它适合手机游戏的原因：
    ///   不用拦截浏览器回调，不用配 Activity。
    /// · 授权码 + PKCE（用系统浏览器回跳应用）：额外需要 PackageName 与 SignatureHashBase64，
    ///   它们拼成 Android 的重定向 URI msauth://{包名}/{URL 编码后的签名哈希}。
    ///   玩家在 Azure 门户注册 Android 平台时填的就是这两项，所以这里一并提供，
    ///   并把拼好的 URI 显示出来供他复制粘贴。
    /// </summary>
    [Serializable]
    public class MsTodoConfig
    {
        // ---- 端点 ----
        public string authority = "https://login.microsoftonline.com";
        public string tenant = "common";
        public string graphBase = "https://graph.microsoft.com/v1.0";

        // ---- 应用注册 ----
        public string clientId = "";
        public string packageName = "";   // 空则取 Application.identifier，见 Load()
        /// <summary>Android 签名哈希（Base64）。Azure 门户注册 Android 平台时要填的那一串。</summary>
        public string signatureHashBase64 = "";

        // ---- 权限与同步 ----
        /// <summary>Tasks.ReadWrite 读写待办；offline_access 换取刷新令牌（否则一小时后要重登）。</summary>
        public string scopes = "offline_access Tasks.ReadWrite User.Read";
        public bool autoSync = true;
        public int syncMinutes = 15;

        // ---- 派生 ----
        public string DeviceCodeUrl => $"{authority.TrimEnd('/')}/{tenant}/oauth2/v2.0/devicecode";
        public string TokenUrl => $"{authority.TrimEnd('/')}/{tenant}/oauth2/v2.0/token";
        public string ListsUrl => $"{graphBase.TrimEnd('/')}/me/todo/lists";

        /// <summary>Android 重定向 URI（授权码流程用；设备码流程不需要）。</summary>
        public string RedirectUri =>
            string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(signatureHashBase64)
                ? "（填了包名与签名哈希后自动生成）"
                : "msauth://" + packageName + "/" + Uri.EscapeDataString(signatureHashBase64);

        public bool Usable => !string.IsNullOrWhiteSpace(clientId) &&
                              !string.IsNullOrWhiteSpace(tenant) &&
                              !string.IsNullOrWhiteSpace(authority);

        // ---- 持久化 ----
        const string Key = "mstodo_cfg";

        static MsTodoConfig _cur;
        public static MsTodoConfig Current => _cur ?? (_cur = Load());

        static MsTodoConfig Load()
        {
            string json = PlayerPrefs.GetString(Key, "");
            MsTodoConfig c;
            if (string.IsNullOrEmpty(json)) c = new MsTodoConfig();
            else
            {
                try { c = JsonUtility.FromJson<MsTodoConfig>(json) ?? new MsTodoConfig(); }
                catch { c = new MsTodoConfig(); }
            }
            // 包名不该靠人手打——Unity 自己就知道，写错了在 Azure 那边是查不出来的哑错。
            if (string.IsNullOrWhiteSpace(c.packageName)) c.packageName = Application.identifier;
            return c;
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
            _cur = this;
        }
    }
}
