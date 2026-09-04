using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.Utilities;

namespace WS_Modules
{
    /// <summary>
    /// 事件面板可以显示的 Unity 日志级别。
    /// </summary>
    internal enum EventLogSeverity
    {
        Log,
        Warning,
        Error,
        Assert,
        Exception,
    }

    /// <summary>
    /// 一条已经通过筛选表达式的日志，保存正文和 Unity 提供的堆栈。
    /// </summary>
    internal sealed class EventLogEntry
    {
        #region 数据

        /// <summary>创建日志记录。</summary>
        /// <param name="timestamp">接收日志的本地时间。</param>
        /// <param name="severity">Unity 日志级别。</param>
        /// <param name="message">日志正文。</param>
        /// <param name="stackTrace">Unity 日志堆栈。</param>
        public EventLogEntry(DateTime timestamp, EventLogSeverity severity, string message, string stackTrace)
        {
            Timestamp = timestamp;
            Severity = severity;
            Message = message ?? string.Empty;
            StackTrace = stackTrace ?? string.Empty;
        }

        /// <summary>日志接收时间。</summary>
        public DateTime Timestamp { get; }

        /// <summary>日志级别。</summary>
        public EventLogSeverity Severity { get; }

        /// <summary>日志正文。</summary>
        public string Message { get; }

        /// <summary>Unity 提供的调用堆栈。</summary>
        public string StackTrace { get; }

        #endregion
    }

    /// <summary>
    /// 日志快照及其版本号，用于在 Editor 主线程判断是否需要重绘列表。
    /// </summary>
    internal sealed class EventLogSnapshot
    {
        #region 数据

        /// <summary>创建快照。</summary>
        /// <param name="version">快照版本号。</param>
        /// <param name="entries">快照中的日志记录。</param>
        public EventLogSnapshot(int version, IReadOnlyList<EventLogEntry> entries)
        {
            Version = version;
            Entries = entries;
        }

        /// <summary>日志缓存版本号。</summary>
        public int Version { get; }

        /// <summary>当前缓存的日志记录。</summary>
        public IReadOnlyList<EventLogEntry> Entries { get; }

        #endregion
    }

    /// <summary>
    /// 订阅 Unity 全局日志通知并按正文筛选表达式保存事件面板需要的日志。
    /// </summary>
    internal sealed class EventLogCaptureService : IDisposable
    {
        #region 常量与依赖字段

        /// <summary>默认的事件日志标记。</summary>
        public const string DefaultToken = "[WSFrame.Event]";

        /// <summary>日志缓存最多保留的记录数量。</summary>
        public const int MaxEntryCount = 500;

        // 依赖字段：日志回调可能在任意线程执行，所有状态通过同一把锁保护。
        private readonly object _syncRoot = new object();
        private readonly List<EventLogEntry> _entries = new List<EventLogEntry>();

        private bool _captureEnabled;
        private string _token = DefaultToken;
        private TextFilterExpression.TextMatcher _matcher = TextFilterExpression.Parse(DefaultToken).Matcher;
        private int _version;
        private bool _disposed;
        private string _filterError = string.Empty;
        private int _filterErrorPosition = -1;

        #endregion

        #region 属性

        /// <summary>当前是否请求捕获日志；空或无效表达式时回调仍会跳过日志。</summary>
        public bool CaptureEnabled
        {
            get
            {
                lock (_syncRoot)
                {
                    return _captureEnabled;
                }
            }
        }

        /// <summary>当前用于正文匹配的筛选表达式。</summary>
        public string Token
        {
            get
            {
                lock (_syncRoot)
                {
                    return _token;
                }
            }
        }

        /// <summary>当前已解析的筛选表达式错误；有效表达式时为空。</summary>
        public string FilterError
        {
            get
            {
                lock (_syncRoot)
                {
                    return _filterError;
                }
            }
        }

        /// <summary>当前筛选表达式错误所在位置；有效表达式时为 -1。</summary>
        public int FilterErrorPosition
        {
            get
            {
                lock (_syncRoot)
                {
                    return _filterErrorPosition;
                }
            }
        }

        /// <summary>当前缓存版本，用于避免 Editor 主线程无变化时复制日志列表。</summary>
        public int Version
        {
            get
            {
                lock (_syncRoot)
                {
                    return _version;
                }
            }
        }

        #endregion

        #region 公开操作

        /// <summary>设置是否接收后续日志；设置不会清理已有记录。</summary>
        /// <param name="enabled">是否开启捕获。</param>
        public void SetCaptureEnabled(bool enabled)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                _captureEnabled = enabled;
            }
        }

        /// <summary>解析并设置后续日志的正文筛选表达式。</summary>
        /// <param name="token">支持括号、&amp; 和 | 的区分大小写表达式。</param>
        public void SetToken(string token)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                _token = token ?? string.Empty;
                var result = TextFilterExpression.Parse(_token);
                _matcher = result.IsValid ? result.Matcher : null;
                _filterError = result.ErrorMessage;
                _filterErrorPosition = result.ErrorPosition;
            }
        }

        /// <summary>取得线程安全的日志快照。</summary>
        /// <returns>包含当前版本和记录副本的快照。</returns>
        public EventLogSnapshot GetSnapshot()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                return new EventLogSnapshot(_version, new List<EventLogEntry>(_entries));
            }
        }

        /// <summary>清空已捕获的记录，并使界面下一次更新重新绘制为空列表。</summary>
        public void Clear()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                _entries.Clear();
                _version++;
            }
        }

        /// <summary>订阅 Unity 日志通知。</summary>
        public void Start()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                if (!_captureEnabled) return;

                Application.logMessageReceivedThreaded -= OnLogMessageReceived;
                Application.logMessageReceivedThreaded += OnLogMessageReceived;
            }
        }

        /// <summary>停止接收新的 Unity 日志，但保留当前缓存和服务状态。</summary>
        public void Stop()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                Application.logMessageReceivedThreaded -= OnLogMessageReceived;
            }
        }

        /// <summary>解除 Unity 日志通知订阅并释放缓存服务。</summary>
        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                Application.logMessageReceivedThreaded -= OnLogMessageReceived;
                _disposed = true;
            }
        }

        #endregion

        #region 日志回调与内部校验

        /// <summary>接收 Unity 日志；此方法可能在后台线程执行，不触碰任何 UI 或 Editor API。</summary>
        /// <param name="condition">日志正文。</param>
        /// <param name="stackTrace">Unity 提供的堆栈文本。</param>
        /// <param name="type">Unity 日志级别。</param>
        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            lock (_syncRoot)
            {
                if (_disposed || !_captureEnabled || _matcher == null)
                {
                    return;
                }

                if (string.IsNullOrEmpty(condition) || !_matcher.IsMatch(condition))
                {
                    return;
                }

                _entries.Add(new EventLogEntry(DateTime.Now, ConvertSeverity(type), condition, stackTrace));
                if (_entries.Count > MaxEntryCount)
                {
                    _entries.RemoveAt(0);
                }

                _version++;
            }
        }

        /// <summary>把 Unity 日志级别映射为面板显示级别。</summary>
        /// <param name="type">Unity 日志级别。</param>
        /// <returns>面板日志级别。</returns>
        private static EventLogSeverity ConvertSeverity(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return EventLogSeverity.Warning;
                case LogType.Error:
                    return EventLogSeverity.Error;
                case LogType.Assert:
                    return EventLogSeverity.Assert;
                case LogType.Exception:
                    return EventLogSeverity.Exception;
                default:
                    return EventLogSeverity.Log;
            }
        }

        /// <summary>在服务已释放后尽早暴露错误使用。</summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EventLogCaptureService));
            }
        }

        #endregion
    }
}
