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

            _redirect = Note("", ref y);
            UiUtil.MakeButton(_content.transform, "保存参数", new Vector2(0.5f, 1f),
                new Vector2(-300, y - 34), new Vector2(340, 62),
                new Color(0.24f, 0.42f, 0.56f, 0.95f), SaveConfig, 24);
            UiUtil.MakeButton(_content.transform, "恢复默认端点", new Vector2(0.5f, 1f),
                new Vector2(60, y - 34), new Vector2(340, 62),
                new Color(0.3f, 0.3f, 0.36f, 0.95f), ResetEndpoints, 24);
            y -= 86f;

            y = Section("② 授权与登录", y);
            _status = Note("", ref y);
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
            _body = Note("", ref y, 22);
            _body.rectTransform.sizeDelta = new Vector2(1130, 1000);
            _body.rectTransform.anchoredPosition = new Vector2(0, y - 500);

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

        Text Note(string text, ref float y, int size = 21)
        {
            var t = UiUtil.MakeText(_content.transform, "N", text, size,
                TextAnchor.UpperLeft, new Color(0.78f, 0.80f, 0.86f));
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            UiUtil.SetRect(t, new Vector2(0.5f, 1f), new Vector2(0, y - 70), new Vector2(1130, 150));
            y -= 160f;
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
            if (_status != null) _status.text = sb.ToString();
            if (_signBtn != null)
            {
                var lab = _signBtn.GetComponentInChildren<Text>();
                if (lab != null)
                    lab.text = svc != null && svc.Status == MsTodoService.State.WaitingForCode
                        ? "等待授权中…" : "登录";
            }
            if (_body != null) _body.text = Render(svc);
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
