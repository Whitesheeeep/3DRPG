using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Process = System.Diagnostics.Process;

namespace WS_Modules
{
    /// <summary>
    /// 通过项目级请求文件协调 Codex 批量编辑期间的 Unity 资源导入暂停与集中恢复。
    /// </summary>
    [InitializeOnLoad]
    internal static class CodexUnityAssetEditingGate
    {
        #region 常量与字段

        // 外部控制脚本与 Unity Editor 之间使用用户目录下的控制文件通信，避免污染项目资源目录。
        private const string GateDirectoryName = "CodexUnityAssetEditing";
        private const string StatusActive = "active";
        private const string StatusIdle = "idle";
        private const string StatusRefreshing = "refreshing";
        private const string StatusError = "error";
        private const string StatusInterrupted = "interrupted";
        private const double PollIntervalSeconds = 0.25d;
        private const double StatusHeartbeatSeconds = 1d;
        private const int RequiredStableIdleUpdates = 2;
        private const string SessionStateSuffix = "CodexUnityAssetEditing.";

        // Unity Editor API 依赖：AssetDatabase 负责导入队列，EditorApplication 负责生命周期和空闲检测。
        private static readonly string ProjectRoot;
        private static readonly string GateDirectory;
        private static readonly string RequestPath;
        private static readonly string ExitMarkerPath;
        private static readonly string StatusPath;
        private static readonly string SessionStatePrefix;
        private static readonly string SessionStartedKey;
        private static readonly string RequestIdKey;
        private static readonly string StageKey;
        private static readonly string OwnsAssetEditingKey;
        private static readonly string RefreshIssuedKey;
        private static readonly string CompileErrorCountKey;
        private static readonly string LastCompileErrorKey;

        // 状态字段：SessionState 在同一个 Unity Editor 进程内跨程序集重载保留这些进度。
        private static DateTime _editorSessionStartedUtc;
        private static E_CodexUnityAssetEditingStage _stage;
        private static bool _ownsAssetEditing;
        private static bool _refreshIssued;
        private static int _compileErrorCount;
        private static int _stableIdleUpdateCount;
        private static double _nextPollTime;
        private static double _nextStatusHeartbeatTime;
        private static string _activeRequestId = string.Empty;
        private static string _lastCompileError = string.Empty;
        private static string _detail = "idle";
        private static bool _statusWriteWarningLogged;

        #endregion

        #region 生命周期

        /// <summary>
        /// 初始化项目级导入闸门，并注册 Editor 更新与退出回调。
        /// </summary>
        static CodexUnityAssetEditingGate()
        {
            ProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            GateDirectory = Path.Combine(Path.GetTempPath(), GateDirectoryName);
            var projectKey = CreateProjectKey(ProjectRoot);
            RequestPath = Path.Combine(GateDirectory, projectKey + ".request");
            ExitMarkerPath = Path.Combine(GateDirectory, projectKey + ".exit");
            StatusPath = Path.Combine(GateDirectory, projectKey + ".status");
            SessionStatePrefix = SessionStateSuffix + projectKey + ".";
            SessionStartedKey = SessionStatePrefix + "editorStartedUtc";
            RequestIdKey = SessionStatePrefix + "requestId";
            StageKey = SessionStatePrefix + "stage";
            OwnsAssetEditingKey = SessionStatePrefix + "ownsAssetEditing";
            RefreshIssuedKey = SessionStatePrefix + "refreshIssued";
            CompileErrorCountKey = SessionStatePrefix + "compileErrorCount";
            LastCompileErrorKey = SessionStatePrefix + "lastCompileError";

            _editorSessionStartedUtc = GetEditorSessionStartedUtc();
            RestoreSessionState();
            RestoreLegacyRefreshState();

            EditorApplication.update -= ProcessEditorUpdate;
            EditorApplication.update += ProcessEditorUpdate;
            EditorApplication.quitting += HandleEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;
            CompilationPipeline.assemblyCompilationFinished += HandleAssemblyCompilationFinished;
            Debug.Log($"[CodexUnityAssetEditingGate] 初始化，stage={_stage}，requestId={_activeRequestId}。");
        }

        /// <summary>
        /// 轮询外部请求文件，并在请求进入或退出时切换 Unity 导入闸门。
        /// </summary>
        private static void ProcessEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextPollTime)
            {
                return;
            }

            _nextPollTime = EditorApplication.timeSinceStartup + PollIntervalSeconds;

            if (_stage == E_CodexUnityAssetEditingStage.BatchEditing)
            {
                PollBatchRequest();
            }
            else if (_stage == E_CodexUnityAssetEditingStage.AwaitingRefresh)
            {
                AdvanceRefreshRequest();
            }
            else if (_stage == E_CodexUnityAssetEditingStage.WaitingForUnity)
            {
                CompleteWhenUnityIsIdle();
            }
            else
            {
                PollForNewRequest();
            }

            WriteStatusHeartbeat();
        }

        /// <summary>
        /// 在 Unity Editor 退出前释放本桥接持有的导入暂停状态并记录中断信息。
        /// </summary>
        private static void HandleEditorQuitting()
        {
            DeleteRequestFile();
            if (_ownsAssetEditing)
            {
                try
                {
                    AssetDatabase.StopAssetEditing();
                    _ownsAssetEditing = false;
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[CodexUnityAssetEditingGate] Editor 退出时释放导入暂停失败：{exception.Message}");
                }
            }

            string interruptedRequestId = _activeRequestId;
            _stage = E_CodexUnityAssetEditingStage.Idle;
            _detail = "Unity Editor is quitting before refresh completion";
            SaveSessionState();
            WriteStatus(StatusInterrupted, _detail, interruptedRequestId, _stage);
            Debug.LogWarning($"[CodexUnityAssetEditingGate] Editor 退出，中断 requestId={interruptedRequestId}。");
        }

        /// <summary>
        /// 在程序集重载前释放导入暂停计数，避免域重载后遗留不可恢复的 AssetDatabase 状态。
        /// </summary>
        private static void HandleBeforeAssemblyReload()
        {
            if (_ownsAssetEditing)
            {
                // 活跃批处理遇到非预期域重载时必须先释放暂停，再由新域接管 Refresh。
                DeleteRequestFile();
                BeginRefreshCycle("assembly reload released the active batch");
                return;
            }

            SaveSessionState();
        }

        /// <summary>累计当前 Refresh 周期中 Unity 编译器报告的错误。</summary>
        /// <param name="assemblyPath">刚完成编译的程序集路径。</param>
        /// <param name="messages">该程序集的编译诊断。</param>
        private static void HandleAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (_stage != E_CodexUnityAssetEditingStage.AwaitingRefresh &&
                _stage != E_CodexUnityAssetEditingStage.WaitingForUnity)
            {
                return;
            }

            for (int index = 0; index < messages.Length; index++)
            {
                CompilerMessage message = messages[index];
                if (message.type != CompilerMessageType.Error)
                {
                    continue;
                }

                _compileErrorCount++;
                _lastCompileError = $"{Path.GetFileName(assemblyPath)}: {message.message}";
            }

            SaveSessionState();
        }

        #endregion

        #region 菜单操作

        /// <summary>
        /// 强制恢复 Unity 资源导入，供 Codex 中断或控制脚本异常时人工兜底。
        /// </summary>
        [MenuItem("Tools/Codex/Force Resume Imports", priority = 2000)]
        private static void ForceResumeImports()
        {
            if (!DeleteRequestFile())
                return;

            if (_stage == E_CodexUnityAssetEditingStage.AwaitingRefresh ||
                _stage == E_CodexUnityAssetEditingStage.WaitingForUnity)
            {
                Debug.Log($"[CodexUnityAssetEditingGate] 恢复请求已在推进，stage={_stage}，requestId={_activeRequestId}。");
                return;
            }

            if (string.IsNullOrWhiteSpace(_activeRequestId))
            {
                _activeRequestId = Guid.NewGuid().ToString("N");
            }

            if (_ownsAssetEditing)
                BeginRefreshCycle("manual force resume requested");
            else
                BeginRecoveryRefresh("manual force resume requested");
        }

        #endregion

        #region 导入状态

        /// <summary>
        /// 开始一次由指定请求拥有的批量导入暂停。
        /// </summary>
        /// <param name="requestId">外部控制脚本生成的请求标识。</param>
        private static void BeginAssetEditing(string requestId)
        {
            try
            {
                AssetDatabase.StartAssetEditing();
                _activeRequestId = requestId;
                _ownsAssetEditing = true;
                _refreshIssued = false;
                _compileErrorCount = 0;
                _lastCompileError = string.Empty;
                SetStage(E_CodexUnityAssetEditingStage.BatchEditing, "batch asset editing active");
            }
            catch (Exception exception)
            {
                _activeRequestId = requestId;
                Fail($"StartAssetEditing failed: {exception.Message}");
            }
        }

        /// <summary>
        /// 在外部删除请求后释放批量编辑，并把 Refresh 流程交给主更新循环。
        /// </summary>
        private static void PollBatchRequest()
        {
            if (TryReadRequest(out string requestId, out _))
            {
                if (requestId != _activeRequestId)
                    WriteStatus(StatusError, "another batch request is already active", requestId, _stage);
                return;
            }

            if (File.Exists(RequestPath))
                return;

            BeginRefreshCycle("batch request closed; stopping asset editing");
        }

        /// <summary>读取新请求并验证其属于当前 Unity Editor 进程。</summary>
        private static void PollForNewRequest()
        {
            if (!TryReadRequest(out string requestId, out DateTime createdUtc))
                return;

            // 已完成或失败请求的残留文件不能在心跳时重新打开新的 StartAssetEditing 计数。
            if (string.Equals(requestId, _activeRequestId, StringComparison.Ordinal))
            {
                DeleteRequestFile();
                return;
            }

            if (_stage == E_CodexUnityAssetEditingStage.Failed && !_ownsAssetEditing)
                SetStage(E_CodexUnityAssetEditingStage.Idle, "new request started after a prior failure");

            if (_stage != E_CodexUnityAssetEditingStage.Idle)
            {
                WriteStatus(StatusError, $"gate is busy at stage {_stage}", requestId, _stage);
                return;
            }

            if (createdUtc < _editorSessionStartedUtc)
            {
                _activeRequestId = requestId;
                DeleteRequestFile();
                Fail("request was created before the current Unity Editor process started");
                return;
            }

            BeginAssetEditing(requestId);
        }

        /// <summary>释放本桥接持有的导入暂停并记录可跨域重载恢复的阶段。</summary>
        /// <param name="detail">本次结束批量编辑的原因。</param>
        private static void BeginRefreshCycle(string detail)
        {
            SetStage(E_CodexUnityAssetEditingStage.AwaitingRefresh, detail);

            if (!_ownsAssetEditing)
                return;

            try
            {
                AssetDatabase.StopAssetEditing();
                _ownsAssetEditing = false;
                SaveSessionState();
                Debug.Log($"[CodexUnityAssetEditingGate] 已停止批量导入暂停，requestId={_activeRequestId}。");
            }
            catch (Exception exception)
            {
                Fail($"StopAssetEditing failed: {exception.Message}");
            }
        }

        /// <summary>从空闲或失败状态建立一次人工恢复 Refresh。</summary>
        /// <param name="detail">人工恢复原因。</param>
        private static void BeginRecoveryRefresh(string detail)
        {
            _ownsAssetEditing = false;
            _refreshIssued = false;
            _compileErrorCount = 0;
            _lastCompileError = string.Empty;
            SetStage(E_CodexUnityAssetEditingStage.AwaitingRefresh, detail);
        }

        /// <summary>在 Unity 安静时保存资产并且只发出一次主动 Refresh。</summary>
        private static void AdvanceRefreshRequest()
        {
            if (EditorApplication.isUpdating || EditorApplication.isCompiling)
            {
                _stableIdleUpdateCount = 0;
                return;
            }

            if (_refreshIssued)
            {
                SetStage(E_CodexUnityAssetEditingStage.WaitingForUnity, "refresh was already issued before domain reload");
                return;
            }

            // 在调用 Unity API 前持久化，避免 Refresh 引发域重载后重复发出 Refresh。
            _refreshIssued = true;
            _stableIdleUpdateCount = 0;
            SetStage(E_CodexUnityAssetEditingStage.WaitingForUnity, "issuing SaveAssets and Refresh");
            try
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                _detail = "Refresh issued; waiting for Unity import and compilation";
                SaveSessionState();
                WriteCurrentStatus();
            }
            catch (Exception exception)
            {
                Fail($"SaveAssets or Refresh failed: {exception.Message}");
            }
        }

        /// <summary>
        /// 在 Refresh 请求发出后继续等待 Unity 的导入与编译状态稳定为空闲。
        /// </summary>
        private static void CompleteWhenUnityIsIdle()
        {
            if (EditorApplication.isUpdating || EditorApplication.isCompiling)
            {
                _stableIdleUpdateCount = 0;
                return;
            }

            _stableIdleUpdateCount++;
            if (_stableIdleUpdateCount < RequiredStableIdleUpdates)
                return;

            if (_compileErrorCount > 0)
            {
                Fail($"Unity compilation finished with {_compileErrorCount} error(s); last error: {_lastCompileError}");
                return;
            }

            _ownsAssetEditing = false;
            _refreshIssued = false;
            SetStage(E_CodexUnityAssetEditingStage.Idle, "refresh completed; Unity is idle");
        }

        #endregion

        #region 控制文件

        /// <summary>
        /// 读取当前项目的外部请求并解析请求创建时间。
        /// </summary>
        /// <param name="requestId">解析出的请求标识。</param>
        /// <param name="createdUtc">请求创建时间，统一转换为 UTC。</param>
        /// <returns>请求存在且格式有效时返回 true。</returns>
        private static bool TryReadRequest(out string requestId, out DateTime createdUtc)
        {
            requestId = string.Empty;
            createdUtc = default;

            if (!File.Exists(RequestPath))
            {
                return false;
            }

            try
            {
                var fields = File.ReadAllText(RequestPath, Encoding.UTF8).Split('|');
                if (fields.Length < 3 || string.IsNullOrWhiteSpace(fields[0]) ||
                    !DateTime.TryParse(fields[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedUtc))
                {
                    string invalidRequestId = fields.Length > 0 ? fields[0] : string.Empty;
                    WriteStatus(StatusError, "invalid request format", invalidRequestId, _stage);
                    DeleteRequestFile();
                    return false;
                }

                requestId = fields[0];
                createdUtc = parsedUtc.ToUniversalTime();
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                WriteStatus(StatusError, $"cannot read request: {exception.Message}", requestId, _stage);
                return false;
            }
        }

        /// <summary>
        /// 写入当前项目的控制状态，并保留旧客户端使用的前四个字段。
        /// </summary>
        /// <param name="status">状态名称。</param>
        /// <param name="detail">可选的诊断信息。</param>
        /// <param name="requestId">此状态所属的外部请求标识。</param>
        /// <param name="stage">此状态对应的持久阶段。</param>
        private static void WriteStatus(string status, string detail, string requestId, E_CodexUnityAssetEditingStage stage)
        {
            string temporaryPath = StatusPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(GateDirectory);
                string content = string.Join("|",
                    status,
                    DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    ProjectRoot,
                    SanitizeStatusField(detail),
                    requestId ?? string.Empty,
                    stage.ToString(),
                    EditorApplication.isUpdating.ToString(),
                    EditorApplication.isCompiling.ToString());

                // 临时文件与状态文件位于同一目录，替换过程避免读者看到半截状态记录。
                File.WriteAllText(temporaryPath, content, Encoding.UTF8);
                if (File.Exists(StatusPath))
                    File.Replace(temporaryPath, StatusPath, null);
                else
                    File.Move(temporaryPath, StatusPath);

                _statusWriteWarningLogged = false;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                if (!_statusWriteWarningLogged)
                {
                    Debug.LogWarning($"[CodexUnityAssetEditingGate] 状态文件写入失败，stage={stage}：{exception.Message}");
                    _statusWriteWarningLogged = true;
                }

                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                        // 临时状态文件不影响 Unity 导入流程，后续心跳会再次尝试写入。
                    }
                }
            }
        }

        /// <summary>
        /// 删除当前项目的请求文件，并将文件系统失败反馈给 Gate 状态机。
        /// </summary>
        /// <returns>请求文件不存在或删除成功时返回 true。</returns>
        private static bool DeleteRequestFile()
        {
            if (!File.Exists(RequestPath))
                return true;

            try
            {
                File.Delete(RequestPath);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Fail($"cannot remove request file: {exception.Message}");
                return false;
            }
        }

        /// <summary>读取当前进程启动时间，域重载期间始终使用同一份会话时间。</summary>
        /// <returns>当前 Unity Editor 进程的 UTC 启动时间。</returns>
        private static DateTime GetEditorSessionStartedUtc()
        {
            using Process currentProcess = Process.GetCurrentProcess();
            int currentProcessId = currentProcess.Id;
            string processIdKey = SessionStatePrefix + "editorProcessId";
            int storedProcessId = SessionState.GetInt(processIdKey, 0);
            string storedStart = SessionState.GetString(SessionStartedKey, string.Empty);
            if (storedProcessId == currentProcessId &&
                DateTime.TryParse(storedStart, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime sessionStartUtc))
            {
                return sessionStartUtc.ToUniversalTime();
            }

            DateTime processStartUtc = currentProcess.StartTime.ToUniversalTime();
            SessionState.SetInt(processIdKey, currentProcessId);
            SessionState.SetString(SessionStartedKey, processStartUtc.ToString("O", CultureInfo.InvariantCulture));
            return processStartUtc;
        }

        /// <summary>从 SessionState 恢复批处理阶段和 Refresh 的幂等标记。</summary>
        private static void RestoreSessionState()
        {
            string savedStage = SessionState.GetString(StageKey, string.Empty);
            if (!Enum.TryParse(savedStage, out _stage))
                _stage = E_CodexUnityAssetEditingStage.Idle;

            _activeRequestId = SessionState.GetString(RequestIdKey, string.Empty);
            _ownsAssetEditing = SessionState.GetBool(OwnsAssetEditingKey, false);
            _refreshIssued = SessionState.GetBool(RefreshIssuedKey, false);
            _compileErrorCount = SessionState.GetInt(CompileErrorCountKey, 0);
            _lastCompileError = SessionState.GetString(LastCompileErrorKey, string.Empty);
            _detail = "restored from Unity SessionState";

            if (_stage == E_CodexUnityAssetEditingStage.BatchEditing && !_ownsAssetEditing)
                Fail("restored batch state has no matching AssetDatabase ownership");
        }

        /// <summary>升级期间接管旧桥接留下的 Refresh 等待状态。</summary>
        private static void RestoreLegacyRefreshState()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(StageKey, string.Empty)) || File.Exists(RequestPath))
                return;

            if (!TryReadStatusFields(out string[] fields) || fields.Length < 4 || fields[0] != StatusRefreshing)
                return;

            if (!string.Equals(Path.GetFullPath(fields[2]), ProjectRoot, StringComparison.OrdinalIgnoreCase))
                return;

            // 旧版状态文件只有四列；Exit 脚本在关闭请求前写入一次性标记，以便此次升级仍能按 ID 确认完成。
            string legacyRequestId = fields.Length >= 5 ? fields[4] : ReadLegacyExitMarker();
            if (_stage == E_CodexUnityAssetEditingStage.Failed)
                return;

            _activeRequestId = legacyRequestId;
            _ownsAssetEditing = false;
            // 旧桥接会在 Refresh 已调用后把这段诊断写入前四列；接管时据此等待而不是重复 Refresh。
            _refreshIssued = fields[3].IndexOf("refresh requested", StringComparison.OrdinalIgnoreCase) >= 0;
            _compileErrorCount = 0;
            _lastCompileError = string.Empty;
            E_CodexUnityAssetEditingStage resumedStage = _refreshIssued
                ? E_CodexUnityAssetEditingStage.WaitingForUnity
                : E_CodexUnityAssetEditingStage.AwaitingRefresh;
            SetStage(resumedStage, "resuming a legacy Refresh state after gate upgrade");
            DeleteLegacyExitMarker();
        }

        /// <summary>读取旧状态协议迁移用的一次性请求标识。</summary>
        /// <returns>状态文件未包含 RequestId 时 Exit 脚本保存的标识。</returns>
        private static string ReadLegacyExitMarker()
        {
            if (!File.Exists(ExitMarkerPath))
                return string.Empty;

            try
            {
                return File.ReadAllText(ExitMarkerPath, Encoding.UTF8).Trim();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Fail($"cannot read legacy Exit marker: {exception.Message}");
                return string.Empty;
            }
        }

        /// <summary>删除已迁移到 SessionState 的旧协议请求标记。</summary>
        private static void DeleteLegacyExitMarker()
        {
            if (!File.Exists(ExitMarkerPath))
                return;

            try
            {
                File.Delete(ExitMarkerPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[CodexUnityAssetEditingGate] 删除旧版 Exit 标记失败，requestId={_activeRequestId}：{exception.Message}");
            }
        }

        /// <summary>读取状态文件并解析兼容新旧版本的分隔字段。</summary>
        /// <param name="fields">解析出的字段列表。</param>
        /// <returns>状态文件存在且完整读取时返回 true。</returns>
        private static bool TryReadStatusFields(out string[] fields)
        {
            fields = Array.Empty<string>();
            if (!File.Exists(StatusPath))
                return false;

            try
            {
                fields = File.ReadAllText(StatusPath, Encoding.UTF8).Split('|');
                return fields.Length >= 4;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>将阶段、诊断和当前请求持久化，并只在阶段切换时写一条 Unity 日志。</summary>
        /// <param name="stage">新的 Gate 阶段。</param>
        /// <param name="detail">阶段切换原因或可读状态。</param>
        private static void SetStage(E_CodexUnityAssetEditingStage stage, string detail)
        {
            bool changed = _stage != stage;
            _stage = stage;
            _detail = detail ?? string.Empty;
            SaveSessionState();
            WriteCurrentStatus();

            if (changed)
                Debug.Log($"[CodexUnityAssetEditingGate] 阶段切换为 {stage}，requestId={_activeRequestId}，detail={_detail}。");
        }

        /// <summary>记录失败阶段，向等待中的控制脚本提供失败原因。</summary>
        /// <param name="detail">失败步骤及诊断信息。</param>
        private static void Fail(string detail)
        {
            _stage = E_CodexUnityAssetEditingStage.Failed;
            _detail = detail ?? "unknown gate failure";
            SaveSessionState();
            WriteCurrentStatus();
            Debug.LogError($"[CodexUnityAssetEditingGate] { _detail }，requestId={_activeRequestId}，ownsAssetEditing={_ownsAssetEditing}。");
        }

        /// <summary>保存跨程序集重载所需的请求、阶段、所有权和编译诊断快照。</summary>
        private static void SaveSessionState()
        {
            SessionState.SetString(RequestIdKey, _activeRequestId ?? string.Empty);
            SessionState.SetString(StageKey, _stage.ToString());
            SessionState.SetBool(OwnsAssetEditingKey, _ownsAssetEditing);
            SessionState.SetBool(RefreshIssuedKey, _refreshIssued);
            SessionState.SetInt(CompileErrorCountKey, _compileErrorCount);
            SessionState.SetString(LastCompileErrorKey, _lastCompileError ?? string.Empty);
        }

        /// <summary>刷新状态文件中的更新时间与 Unity 当前导入、编译标志。</summary>
        private static void WriteStatusHeartbeat()
        {
            if (EditorApplication.timeSinceStartup < _nextStatusHeartbeatTime)
                return;

            _nextStatusHeartbeatTime = EditorApplication.timeSinceStartup + StatusHeartbeatSeconds;
            WriteCurrentStatus();
        }

        /// <summary>根据当前阶段写入与旧状态格式兼容的完整状态记录。</summary>
        private static void WriteCurrentStatus()
        {
            string status = _stage switch
            {
                E_CodexUnityAssetEditingStage.Idle => StatusIdle,
                E_CodexUnityAssetEditingStage.BatchEditing => StatusActive,
                E_CodexUnityAssetEditingStage.AwaitingRefresh => StatusRefreshing,
                E_CodexUnityAssetEditingStage.WaitingForUnity => StatusRefreshing,
                E_CodexUnityAssetEditingStage.Failed => StatusError,
                _ => StatusError
            };

            WriteStatus(status, _detail, _activeRequestId, _stage);
        }

        /// <summary>将状态文本压平，保证分隔符不会破坏控制文件结构。</summary>
        /// <param name="value">待写入的状态文本。</param>
        /// <returns>无换行、无字段分隔符的文本。</returns>
        private static string SanitizeStatusField(string value)
        {
            return (value ?? string.Empty).Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
        }

        /// <summary>
        /// 根据规范化项目根目录生成跨进程稳定的 Gate ID。
        /// </summary>
        /// <param name="projectRoot">Unity 项目根目录。</param>
        /// <returns>小写十六进制 SHA-256 标识。</returns>
        private static string CreateProjectKey(string projectRoot)
        {
            var normalizedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
            using (var sha256 = SHA256.Create())
            {
                var digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedRoot));
                var builder = new StringBuilder(digest.Length * 2);
                foreach (var value in digest)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        #endregion

        #region 阶段类型与辅助类型

        /// <summary>描述批量导入桥接在 Editor 会话中的可恢复阶段。</summary>
        private enum E_CodexUnityAssetEditingStage
        {
            /// <summary>没有待处理的批量编辑或 Refresh。</summary>
            Idle,
            /// <summary>外部请求持有 AssetDatabase 导入暂停。</summary>
            BatchEditing,
            /// <summary>等待 Unity 空闲后发出唯一一次 Refresh。</summary>
            AwaitingRefresh,
            /// <summary>Refresh 已发出，等待导入和编译稳定完成。</summary>
            WaitingForUnity,
            /// <summary>当前操作失败，状态文件保留诊断供控制脚本读取。</summary>
            Failed
        }

        #endregion
    }
}
