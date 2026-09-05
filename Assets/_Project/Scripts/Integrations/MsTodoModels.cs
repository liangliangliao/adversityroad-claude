using System;
using System.Collections.Generic;

namespace AdversityRoad.Integrations
{
    /// <summary>
    /// Microsoft To Do 的数据结构，字段名与 Graph 的 JSON 一一对应，
    /// 好让 JsonUtility 直接反序列化（这个工程没有引第三方 JSON 库）。
    ///
    /// 【保持 To Do 的原结构】玩家要求"同步出来的全部内容按照 todo 原结构显示"，
    /// 所以这里不做任何扁平化：清单 → 任务 → 步骤三层原样保留，
    /// 完成状态、重要性、截止日期、备注也一并带过来。
    /// </summary>
    [Serializable]
    public class TodoStep
    {
        public string id;
        public string displayName;
        public bool isChecked;
    }

    [Serializable]
    public class TodoTask
    {
        public string id;
        public string title;
        /// <summary>notStarted / inProgress / completed / waitingOnOthers / deferred</summary>
        public string status;
        /// <summary>low / normal / high</summary>
        public string importance;
        public string dueDate;         // 已格式化成 yyyy-MM-dd，空 = 无截止
        public string bodyPreview;     // 备注首行
        public List<TodoStep> steps = new List<TodoStep>();

        public bool Done => status == "completed";
    }

    [Serializable]
    public class TodoList
    {
        public string id;
        public string displayName;
        public bool isOwner;
        public bool isShared;
        public List<TodoTask> tasks = new List<TodoTask>();

        public int OpenCount
        {
            get { int n = 0; foreach (var t in tasks) if (!t.Done) n++; return n; }
        }
    }

    /// <summary>一次同步的完整结果（也是本地缓存的格式）。</summary>
    [Serializable]
    public class TodoSnapshot
    {
        public string account = "";
        public string syncedAt = "";
        public List<TodoList> lists = new List<TodoList>();

        public int TotalTasks
        {
            get { int n = 0; foreach (var l in lists) n += l.tasks.Count; return n; }
        }
        public int OpenTasks
        {
            get { int n = 0; foreach (var l in lists) n += l.OpenCount; return n; }
        }
    }

    // ===== Graph 返回体的最小 DTO（只取用得上的字段）=====

    [Serializable] class GLists { public List<GList> value = new List<GList>(); }
    [Serializable] class GList { public string id; public string displayName; public bool isOwner; public bool isShared; }

    [Serializable] class GTasks { public List<GTask> value = new List<GTask>(); }
    [Serializable]
    class GTask
    {
        public string id; public string title; public string status; public string importance;
        public GBody body; public GDate dueDateTime;
    }
    [Serializable] class GBody { public string content; }
    [Serializable] class GDate { public string dateTime; }

    [Serializable] class GChecks { public List<GCheck> value = new List<GCheck>(); }
    [Serializable] class GCheck { public string id; public string displayName; public bool isChecked; }

    [Serializable]
    class GDeviceCode
    {
        public string device_code; public string user_code; public string verification_uri;
        public int expires_in; public int interval; public string message;
    }

    [Serializable]
    class GToken
    {
        public string access_token; public string refresh_token; public string token_type;
        public int expires_in; public string error; public string error_description;
    }

    [Serializable] class GMe { public string userPrincipalName; public string displayName; }
}
