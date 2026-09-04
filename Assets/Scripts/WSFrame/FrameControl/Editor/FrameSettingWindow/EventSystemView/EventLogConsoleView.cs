using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace WS_Modules
{
    /// <summary>
    /// EventSystem 面板中的日志 Console，负责控件绑定、主线程刷新和详情展示。
    /// </summary>
    internal sealed class EventLogConsoleView : IDisposable
    {
        #region 依赖字段

        // 依赖字段：UXML 提供布局容器，本类只创建交互控件和日志行。
        private readonly VisualElement _root;
        private readonly VisualElement _toolbar;
        private readonly ScrollView _list;
        private readonly ScrollView _details;
        private readonly EventLogCaptureService _captureService;
        private readonly List<EventLogEntry> _displayedEntries = new List<EventLogEntry>();

        private Label _countLabel;
        private Label _statusLabel;
        private Toggle _captureToggle;
        private Toggle _autoScrollToggle;
        private TextField _tokenField;
        private int _renderedVersion = -1;
        private bool _disposed;

        #endregion

        #region 生命周期

        /// <summary>创建日志 Console 并开始监听 Editor 更新。</summary>
        /// <param name="root">UXML 中的日志 Console 根元素。</param>
        /// <param name="captureService">由 EventSystemView 持有、用于跨模块切换保留状态的日志服务。</param>
        public EventLogConsoleView(VisualElement root, EventLogCaptureService captureService)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
            _toolbar = _root.Q<VisualElement>("EventLogToolbar");
            _list = _root.Q<ScrollView>("EventLogList");
            _details = _root.Q<ScrollView>("EventLogDetails");

            BuildToolbar();
            ShowEmptyDetails();
            UpdateStatus();
            _root.RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
            EditorApplication.update += OnEditorUpdate;
            if (_captureService.CaptureEnabled && string.IsNullOrEmpty(_captureService.FilterError))
            {
                _captureService.Start();
            }
        }

        /// <summary>解除 Editor 更新和 Unity 日志订阅。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            EditorApplication.update -= OnEditorUpdate;
            // 面板切换时保留缓存和开关状态，只停止当前面板的日志接收。
            _captureService.Stop();
        }

        #endregion

        #region 控件构建

        /// <summary>创建捕获开关、筛选表达式、清空和自动滚动控件。</summary>
        private void BuildToolbar()
        {
            if (_toolbar == null)
            {
                return;
            }

            _captureToggle = new Toggle("捕获事件日志")
            {
                value = _captureService.CaptureEnabled,
                tooltip = "只接收开启期间产生的新日志，不读取 Unity Console 历史记录。",
            };
            _captureToggle.AddToClassList("event-log-toggle");
            _captureToggle.RegisterValueChangedCallback(evt =>
            {
                _captureService.SetCaptureEnabled(evt.newValue);
                if (evt.newValue && string.IsNullOrEmpty(_captureService.FilterError))
                {
                    _captureService.Start();
                }
                else
                {
                    _captureService.Stop();
                }

                UpdateStatus();
            });

            _tokenField = new TextField("筛选表达式")
            {
                value = EventLogCaptureService.DefaultToken,
                isDelayed = true,
                tooltip = "按正文区分大小写匹配。支持括号、&（同时包含）和 |（包含任一项），优先级为括号 > & > |。示例：[WSFrame.Event] & (背包 | 任务)。",
            };
            _tokenField.value = _captureService.Token;
            _tokenField.AddToClassList("event-log-token-field");
            _tokenField.RegisterValueChangedCallback(evt =>
            {
                _captureService.SetToken(evt.newValue);
                if (_captureService.CaptureEnabled && string.IsNullOrEmpty(_captureService.FilterError))
                {
                    _captureService.Start();
                }
                else
                {
                    _captureService.Stop();
                }
                UpdateStatus();
            });

            var clearButton = new Button(ClearEntries)
            {
                text = "清空",
                tooltip = "清空此面板已经捕获的日志，不影响 Unity Console。",
            };
            clearButton.AddToClassList("event-button");
            clearButton.AddToClassList("event-button-neutral");

            _autoScrollToggle = new Toggle("自动滚动")
            {
                value = true,
                tooltip = "有新日志时滚动到最新一条记录。",
            };
            _autoScrollToggle.AddToClassList("event-log-toggle");

            _countLabel = new Label("记录: 0");
            _countLabel.AddToClassList("event-log-count");
            _statusLabel = new Label("已关闭");
            _statusLabel.AddToClassList("event-log-status");

            _toolbar.Add(_captureToggle);
            _toolbar.Add(_tokenField);
            _toolbar.Add(clearButton);
            _toolbar.Add(_autoScrollToggle);
            _toolbar.Add(_countLabel);
            _toolbar.Add(_statusLabel);
        }

        #endregion

        #region 状态刷新

        /// <summary>在 Editor 主线程消费日志缓存版本并刷新列表。</summary>
        private void OnEditorUpdate()
        {
            if (_disposed)
            {
                return;
            }

            if (_captureService.Version == _renderedVersion)
            {
                return;
            }

            var snapshot = _captureService.GetSnapshot();
            _renderedVersion = snapshot.Version;
            _displayedEntries.Clear();
            _displayedEntries.AddRange(snapshot.Entries);
            RedrawEntries();
            UpdateStatus();
        }

        /// <summary>把缓存记录转换为可点击的日志行。</summary>
        private void RedrawEntries()
        {
            if (_list == null)
            {
                return;
            }

            _list.Clear();
            for (var i = 0; i < _displayedEntries.Count; i++)
            {
                var entry = _displayedEntries[i];
                var index = i;
                var row = new Button(() => ShowDetails(index))
                {
                    text = BuildRowText(entry),
                };
                row.AddToClassList("event-log-row");
                row.AddToClassList(GetSeverityClass(entry.Severity));
                _list.Add(row);
            }

            if (_autoScrollToggle != null && _autoScrollToggle.value && _list.childCount > 0)
            {
                var latest = _list[_list.childCount - 1];
                _list.schedule.Execute(() => _list.ScrollTo(latest));
            }
        }

        /// <summary>刷新捕获状态和记录数量提示。</summary>
        private void UpdateStatus()
        {
            if (_countLabel != null)
            {
                _countLabel.text = $"记录: {_displayedEntries.Count}/{EventLogCaptureService.MaxEntryCount}";
            }

            if (_statusLabel == null)
            {
                return;
            }

            _statusLabel.tooltip = string.Empty;

            if (!_captureService.CaptureEnabled)
            {
                _statusLabel.text = "已关闭";
            }
            else if (!string.IsNullOrEmpty(_captureService.FilterError))
            {
                var position = _captureService.FilterErrorPosition + 1;
                _statusLabel.text = position > 0
                    ? $"表达式错误 ({position})"
                    : "表达式错误，暂停捕获";
                _statusLabel.tooltip = _captureService.FilterError;
            }
            else if (string.IsNullOrWhiteSpace(_captureService.Token))
            {
                _statusLabel.text = "表达式为空，暂停捕获";
                _statusLabel.tooltip = "请输入关键词或组合表达式。";
            }
            else
            {
                _statusLabel.text = "捕获中";
            }
        }

        #endregion

        #region 详情与内部辅助

        /// <summary>清空日志并隐藏旧的详情内容。</summary>
        private void ClearEntries()
        {
            _captureService.Clear();
            _displayedEntries.Clear();
            _renderedVersion = -1;
            RedrawEntries();
            ShowEmptyDetails();
            UpdateStatus();
        }

        /// <summary>显示所选日志的完整正文和堆栈。</summary>
        /// <param name="index">显示列表中的记录索引。</param>
        private void ShowDetails(int index)
        {
            if (_details == null || index < 0 || index >= _displayedEntries.Count)
            {
                return;
            }

            _details.Clear();
            var entry = _displayedEntries[index];
            var detailField = new TextField
            {
                value = BuildEntryDetails(entry),
                multiline = true,
                isReadOnly = true,
                tooltip = "可选择并复制完整日志正文和堆栈。",
            };
            detailField.AddToClassList("event-log-detail-field");
            _details.Add(detailField);
        }

        /// <summary>在没有选中记录时显示使用提示。</summary>
        private void ShowEmptyDetails()
        {
            if (_details == null)
            {
                return;
            }

            _details.Clear();
            var label = new Label("选择一条日志查看完整正文和堆栈");
            label.AddToClassList("event-log-details-placeholder");
            _details.Add(label);
        }

        /// <summary>构造日志列表中的单行摘要。</summary>
        /// <param name="entry">日志记录。</param><returns>单行摘要。</returns>
        private static string BuildRowText(EventLogEntry entry)
        {
            var message = entry.Message.Replace("\r", " ").Replace("\n", " ");
            return $"{entry.Timestamp:HH:mm:ss.fff}  [{entry.Severity}]  {message}";
        }

        /// <summary>构造详情区可复制的完整日志文本。</summary>
        /// <param name="entry">日志记录。</param><returns>完整日志文本。</returns>
        private static string BuildEntryDetails(EventLogEntry entry)
        {
            return $"时间: {entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}\n级别: {entry.Severity}\n\n{entry.Message}\n\n堆栈:\n{entry.StackTrace}";
        }

        /// <summary>取得日志级别对应的 USS 样式类。</summary>
        /// <param name="severity">日志级别。</param><returns>样式类名。</returns>
        private static string GetSeverityClass(EventLogSeverity severity)
        {
            switch (severity)
            {
                case EventLogSeverity.Warning:
                    return "event-log-warning";
                case EventLogSeverity.Error:
                case EventLogSeverity.Assert:
                case EventLogSeverity.Exception:
                    return "event-log-error";
                default:
                    return "event-log-normal";
            }
        }

        #endregion
    }
}
