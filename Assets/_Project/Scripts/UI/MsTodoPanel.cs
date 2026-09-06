using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Integrations;

namespace AdversityRoad.UI
{
    /// <summary>
    /// Microsoft To Do 同步面板：参数配置 → 登录 → 同步 → 按 To Do 原结构浏览。
    ///
    /// 面板做成可滚动的（和设置面板同一个结构）：参数一共八项，加上登录区与内容区，
    /// 一屏放不下；这个工程之前吃过亏——面板比屏幕高又不能滚，下半截永远够不到。
    /// </summary>
    public class MsTodoPanel : MonoBehaviour
    {
        GameObject _frame, _content;
        InputField _authority, _tenant, _clientId, _package, _sha1, _graph, _scopes, _interval;
        Toggle _autoSync;
        Text _redirect, _status, _body;
        float _bodyTop, _bodyShift, _statusTop, _statusReserve;
        RectTransform _contentRt;
        // 状态那块的文字长度是微软的报错决定的，短则一行、长则十几行。
        // 它一长，下面的按钮和说明就被压住——所以记下它下面每个控件的原始 Y，
        // 状态每次变化时按实际高度把它们整体推一推，而不是赌一个够大的预留值。
        readonly List<RectTransform> _below = new List<RectTransform>();
        readonly List<float> _belowY = new List<float>();
        Button _signBtn;

        public static MsTodoPanel Create(Transform canvas)
        {
            var c = canvas.gameObject.AddComponent<MsTodoPanel>();
            c.Build(canvas);
            return c;
        }

        void OnEnable() { MsTodoService.Changed += Refresh; }
        void OnDisable() { MsTodoService.Changed -= Refresh; }

        void Build(Transform canvas)
        {
            _frame = UiUtil.MakePanel(canvas, "MsTodoFrame", new Vector2(1240, 880),
                new Color(0.06f, 0.07f, 0.10f, 0.98f));

            var viewGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewGo.transform.SetParent(_frame.transform, false);
            var viewRt = viewGo.GetComponent<RectTransform>();
            viewRt.anchorMin = Vector2.zero; viewRt.anchorMax = Vector2.one;
            viewRt.offsetMin = new Vector2(8, 8); viewRt.offsetMax = new Vector2(-8, -78);

            _content = UiUtil.MakePanel(viewGo.transform, "MsTodoContent", new Vector2(1200, 2280),
                new Color(0.08f, 0.09f, 0.13f, 0.97f));
            var crt = _content.GetComponent<RectTransform>();
            _contentRt = crt;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(1200, 2280);

            var scroll = _frame.AddComponent<ScrollRect>();
            scroll.content = crt; scroll.viewport = viewRt;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 44f;

            var title = UiUtil.MakeText(_frame.transform, "Title", "Microsoft To Do 同步", 34,
                TextAnchor.MiddleLeft, new Color(0.55f, 0.82f, 1f));
            UiUtil.SetRect(title, new Vector2(0f, 1f), new Vector2(250, -40), new Vector2(560, 46));
            UiUtil.MakeButton(_frame.transform, "关闭", new Vector2(1f, 1f), new Vector2(-90, -40),
                new Vector2(140, 60), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 24);

            float y = -20f;
            y = Section("① 连接参数（存在本机，不上传）", y);
            _authority = Field("Authority（登录端点）", MsTodoConfig.Current.authority, ref y);
            _tenant = Field("Tenant（个人账号填 common；企业填租户 ID）", MsTodoConfig.Current.tenant, ref y);
            _clientId = Field("Client ID（Azure 应用注册里的应用 ID）", MsTodoConfig.Current.clientId, ref y);
            _graph = Field("Graph Base URL", MsTodoConfig.Current.graphBase, ref y);
            _scopes = Field("Scopes（权限，空格分隔）", MsTodoConfig.Current.scopes, ref y);
            _package = Field("Package Name（Android 包名）", MsTodoConfig.Current.packageName, ref y);
            _sha1 = Field("Signature Hash（Base64 的签名哈希）", MsTodoConfig.Current.signatureHashBase64, ref y);
            _interval = Field("自动同步间隔（分钟）", MsTodoConfig.Current.syncMinutes.ToString(), ref y);

            _autoSync = Check("自动同步（应用运行期间按间隔同步，回到前台补一次）",
                MsTodoConfig.Current.autoSync, ref y);

            _redirect = Note("", ref y, 21, 3);
            UiUtil.MakeButton(_content.transform, "保存参数", new Vector2(0.5f, 1f),
                new Vector2(-300, y - 34), new Vector2(340, 62),
                new Color(0.24f, 0.42f, 0.56f, 0.95f), SaveConfig, 24);
            UiUtil.MakeButton(_content.transform, "恢复默认端点", new Vector2(0.5f, 1f),
                new Vector2(60, y - 34), new Vector2(340, 62),
                new Color(0.3f, 0.3f, 0.36f, 0.95f), ResetEndpoints, 24);
            y -= 86f;

            y = Section("② 授权与登录", y);
            _statusTop = y;
            _status = Note("", ref y, 21, 4);
            _statusReserve = _status.rectTransform.sizeDelta.y;
            _signBtn = UiUtil.MakeButton(_content.transform, "登录", new Vector2(0.5f, 1f),
                new Vector2(-300, y - 34), new Vector2(340, 62),
                new Color(0.22f, 0.5f, 0.34f, 0.95f), OnSign, 24);
            UiUtil.MakeButton(_content.transform, "立即同步", new Vector2(0.5f, 1f),
                new Vector2(60, y - 34), new Vector2(340, 62),
                new Color(0.24f, 0.42f, 0.56f, 0.95f), () => MsTodoService.Ensure().SyncNow(), 24);
            UiUtil.MakeButton(_content.transform, "退出登录", new Vector2(0.5f, 1f),
                new Vector2(420, y - 34), new Vector2(260, 62),
                new Color(0.45f, 0.26f, 0.24f, 0.95f), () => MsTodoService.Ensure().SignOut(), 24);
            y -= 86f;

            var howto = Note(
                "【Azure 里要先注册一个应用，五分钟，免费】\n" +
                "1. portal.azure.com → 应用注册 → 新注册，账户类型选\"任何组织目录 + 个人 Microsoft 账户\"。\n" +
                "2. 概述页的\"应用程序(客户端) ID\"填到上面的 Client ID。\n" +
                "3. 身份验证页最下方，把\"允许公共客户端流\"打开——**这一步不做登录必失败**，\n" +
                "   微软会回 AADSTS70002 或 AADSTS7000218：它默认把应用当成带密钥的服务端应用，\n" +
                "   而手机应用不能存密钥。同一页的\"平台\"只加\"移动和桌面应用程序\"，\n" +
                "   加成 Web 或单页应用程序照样会被拒。\n" +
                "4. API 权限页添加 Microsoft Graph 委托权限：Tasks.ReadWrite、User.Read、offline_access。\n" +
                "※ 账户类型必须和 Tenant 对上：多租户+个人账户用 common，仅个人账户用 consumers，\n" +
                "   仅本组织用你的租户 ID。对不上会回 AADSTS50059——登录时会自动换着试一遍，\n" +
                "   试通了就把 Tenant 存下来，通常不用你操心。\n\n" +
                "登录用的是设备码流程：点「登录」后这里会显示一串短码，\n" +
                "在任意一台设备的浏览器打开 microsoft.com/devicelogin 输入它即可。\n" +
                "这条流程不需要重定向 URI，因此包名与签名哈希不是登录的必填项——\n" +
                "它们是你在 Azure 门户注册 Android 平台时要填的，拼出来的 URI 见上方。\n" +
                "关于「后台同步」：Android 会在应用退到后台后挂起进程，Unity 协程随之停摆。\n" +
                "真正的 OS 级后台同步需要 Android 侧的前台服务或 WorkManager，纯 C# 做不到。\n" +
                "这里做到的是：应用运行期间按间隔自动同步，且从后台回到前台立刻补一次。",
                ref y, 20);
            y -= 8f;

            y = Section("③ 同步内容（按 To Do 原结构：清单 → 任务 → 步骤）", y);
            _bodyTop = y;
            _body = Note("", ref y, 22);
            // 同步内容有多长取决于玩家的清单，这里先给个底，真实高度在 Refresh 里按行数定，
            // 内容区高度也跟着一起长——否则清单一多就顶出容器，滚也滚不到底。
            LayoutBody(600f);

            // 记下状态框下面的所有控件（_body 除外，它由 LayoutBody 单独定位）
            int si = _status.transform.GetSiblingIndex();
            for (int i = si + 1; i < _content.transform.childCount; i++)
            {
                var rt = _content.transform.GetChild(i) as RectTransform;
                if (rt == null || rt == _body.rectTransform) continue;
                _below.Add(rt);
                _belowY.Add(rt.anchoredPosition.y);
            }

            _frame.SetActive(false);
            Refresh();
        }

        // ---- 构件 ----

        float Section(string text, float y)
        {
            var t = UiUtil.MakeText(_content.transform, "Sec", text, 26,
                TextAnchor.MiddleLeft, new Color(0.62f, 0.86f, 1f));
            UiUtil.SetRect(t, new Vector2(0.5f, 1f), new Vector2(-20, y - 26), new Vector2(1120, 36));
            return y - 62f;
        }

        InputField Field(string label, string value, ref float y)
        {
            var t = UiUtil.MakeText(_content.transform, "L", label, 21,
                TextAnchor.MiddleLeft, new Color(0.82f, 0.84f, 0.9f));
            UiUtil.SetRect(t, new Vector2(0.5f, 1f), new Vector2(-292, y - 22), new Vector2(560, 32));
            var f = UiUtil.MakeInput(_content.transform, label, new Vector2(0.5f, 1f),
                new Vector2(320, y - 22), new Vector2(520, 50), false);
            f.text = value ?? "";
            y -= 60f;
            return f;
        }

        Toggle Check(string label, bool on, ref float y)
        {
            var go = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(_content.transform, false);
            UiUtil.SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 1f),
                new Vector2(0, y - 24), new Vector2(1120, 44));
            var box = new GameObject("Box", typeof(Image));
            box.transform.SetParent(go.transform, false);
            UiUtil.SetRect(box.GetComponent<Image>(), new Vector2(0f, 0.5f),
                new Vector2(30, 0), new Vector2(34, 34));
            var tg = go.GetComponent<Toggle>();
            tg.targetGraphic = box.GetComponent<Image>();
            tg.isOn = on;
            var mark = new GameObject("Mark", typeof(Image));
            mark.transform.SetParent(box.transform, false);
            UiUtil.SetRect(mark.GetComponent<Image>(), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(20, 20));
            mark.GetComponent<Image>().color = new Color(0.4f, 0.85f, 0.55f);
            tg.graphic = mark.GetComponent<Image>();
            var t = UiUtil.MakeText(go.transform, "L", label, 21,
                TextAnchor.MiddleLeft, new Color(0.82f, 0.84f, 0.9f));
            UiUtil.SetRect(t, new Vector2(0f, 0.5f), new Vector2(600, 0), new Vector2(1020, 34));
            y -= 54f;
            return tg;
        }

        /// <summary>
        /// 说明文字。高度按**行数**算，不写死。
        /// 写死高度的后果是文字溢出到下一节标题上——面板越写越长，溢出越攒越多，
        /// 最后就是玩家看到的"字压在字上"。reserveLines 给那些创建时还是空、
        /// 之后才填内容的说明（状态、重定向 URI）预留位置：它们的行数在这里还不知道。
        /// </summary>
        Text Note(string text, ref float y, int size = 21, int reserveLines = 0)
        {
            var t = UiUtil.MakeText(_content.transform, "N", text, size,
                TextAnchor.UpperLeft, new Color(0.78f, 0.80f, 0.86f));
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            int lines = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
            if (reserveLines > lines) lines = reserveLines;
            float h = lines * (size + 8f) + 14f;
            UiUtil.SetRect(t, new Vector2(0.5f, 1f), new Vector2(0, y - h * 0.5f), new Vector2(1130, h));
            y -= h + 16f;
            return t;
        }

        // ---- 行为 ----

        void SaveConfig()
        {
            var c = MsTodoConfig.Current;
            c.authority = _authority.text.Trim();
            c.tenant = _tenant.text.Trim();
            c.clientId = _clientId.text.Trim();
            c.graphBase = _graph.text.Trim();
            c.scopes = _scopes.text.Trim();
            c.packageName = _package.text.Trim();
            c.signatureHashBase64 = _sha1.text.Trim();
            c.autoSync = _autoSync.isOn;
            if (int.TryParse(_interval.text, out int m)) c.syncMinutes = Mathf.Clamp(m, 1, 720);
            c.Save();
            GameEvents.RaiseSubtitle("Microsoft To Do 参数已保存到本机。");
            Refresh();
        }

        void ResetEndpoints()
        {
            var d = new MsTodoConfig();
            _authority.text = d.authority;
            _tenant.text = d.tenant;
            _graph.text = d.graphBase;
            _scopes.text = d.scopes;
            Refresh();
        }

        void OnSign()
        {
            SaveConfig();
            MsTodoService.Ensure().SignIn();
        }

        /// <summary>同步内容那块的高度与内容区总高一起定，让滚动条真的能滚到最后一行。</summary>
        void LayoutBody(float h)
        {
            if (_body == null) return;
            float top = _bodyTop - _bodyShift;
            _body.rectTransform.sizeDelta = new Vector2(1130, h);
            _body.rectTransform.anchoredPosition = new Vector2(0, top - h * 0.5f);
            if (_contentRt != null)
                _contentRt.sizeDelta = new Vector2(1200, Mathf.Max(1200f, -top + h + 40f));
        }

        /// <summary>
        /// 按状态文字的实际排版高度重排。preferredHeight 是 Text 在当前宽度下折行后的真实高度，
        /// 比数换行符准——微软的报错是一整行长英文，换行符一个都没有，全靠折行。
        /// </summary>
        void LayoutStatus()
        {
            if (_status == null) return;
            float h = Mathf.Max(40f, _status.preferredHeight + 10f);
            _status.rectTransform.sizeDelta = new Vector2(1130, h);
            _status.rectTransform.anchoredPosition = new Vector2(0, _statusTop - h * 0.5f);
            _bodyShift = h - _statusReserve;
            for (int i = 0; i < _below.Count; i++)
                _below[i].anchoredPosition =
                    new Vector2(_below[i].anchoredPosition.x, _belowY[i] - _bodyShift);
        }

        void Refresh()
        {
            if (_frame == null) return;
            var cfg = MsTodoConfig.Current;
            if (_redirect != null)
                _redirect.text = "拼出来的 Android 重定向 URI（Azure 门户注册 Android 平台时填这个）：\n" +
                                 cfg.RedirectUri;

            var svc = MsTodoService.Instance;
            var sb = new StringBuilder();
            if (svc == null) sb.Append("未启动。点「登录」即开始。");
            else
            {
                switch (svc.Status)
                {
                    case MsTodoService.State.SignedOut: sb.Append("状态：未登录"); break;
                    case MsTodoService.State.WaitingForCode:
                        sb.Append("状态：等待授权\n\n在浏览器打开 ").Append(svc.VerificationUrl)
                          .Append("\n输入这个短码： ").Append(svc.UserCode);
                        break;
                    case MsTodoService.State.SignedIn:
                        sb.Append("状态：已登录").Append(string.IsNullOrEmpty(svc.Account)
                            ? "" : "（" + svc.Account + "）");
                        break;
                    case MsTodoService.State.Syncing: sb.Append("状态：同步中…"); break;
                }
                if (!string.IsNullOrEmpty(svc.Data.syncedAt))
                    sb.Append("\n上次同步：").Append(svc.Data.syncedAt)
                      .Append("　清单 ").Append(svc.Data.lists.Count)
                      .Append("　任务 ").Append(svc.Data.TotalTasks)
                      .Append("（未完成 ").Append(svc.Data.OpenTasks).Append("）");
                if (!string.IsNullOrEmpty(svc.LastError))
                    sb.Append("\n\n").Append(svc.LastError);
            }
            if (_status != null) { _status.text = sb.ToString(); LayoutStatus(); }
            if (_signBtn != null)
            {
                var lab = _signBtn.GetComponentInChildren<Text>();
                if (lab != null)
                    lab.text = svc != null && svc.Status == MsTodoService.State.WaitingForCode
                        ? "等待授权中…" : "登录";
            }
            if (_body != null)
            {
                string body = Render(svc);
                _body.text = body;
                int lines = 1;
                for (int i = 0; i < body.Length; i++) if (body[i] == '\n') lines++;
                LayoutBody(Mathf.Max(600f, lines * 30f + 24f));
            }
        }

        /// <summary>按 To Do 的原结构铺开：清单 → 任务 → 步骤，完成状态与截止日期都带上。</summary>
        public static string Render(MsTodoService svc)
        {
            if (svc == null || svc.Data == null || svc.Data.lists.Count == 0)
                return "（还没有同步内容。填好 Client ID → 登录 → 立即同步。）";
            var sb = new StringBuilder();
            foreach (var l in svc.Data.lists)
            {
                sb.Append("▍").Append(l.displayName);
                if (l.isShared) sb.Append("　[共享]");
                sb.Append("　（").Append(l.OpenCount).Append('/').Append(l.tasks.Count).Append("）\n");
                if (l.tasks.Count == 0) sb.Append("    （空清单）\n");
                foreach (var t in l.tasks)
                {
                    sb.Append("    ").Append(t.Done ? "☑ " : "☐ ").Append(t.title);
                    if (t.importance == "high") sb.Append("　★");
                    if (!string.IsNullOrEmpty(t.dueDate)) sb.Append("　⏳").Append(t.dueDate);
                    sb.Append('\n');
                    if (!string.IsNullOrEmpty(t.bodyPreview))
                        sb.Append("        · ").Append(t.bodyPreview).Append('\n');
                    foreach (var s in t.steps)
                        sb.Append("        ").Append(s.isChecked ? "▪ " : "▫ ")
                          .Append(s.displayName).Append('\n');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public void Toggle()
        {
            if (_frame.activeSelf) { Hide(); return; }
            MsTodoService.Ensure();
            Refresh();
            _frame.SetActive(true);
            _frame.transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        void Hide()
        {
            _frame.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
