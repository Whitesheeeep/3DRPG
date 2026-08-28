using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

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
        private const string StatusInterrupted = "interrupted";
        private const string StatusError = "error";
        private const double PollIntervalSeconds = 0.25d;

        // Unity Editor API 依赖：AssetDatabase 负责导入队列，EditorApplication 负责生命周期和空闲检测。
        private static readonly string ProjectRoot;
        private static readonly string GateDirectory;
        private static readonly string RequestPath;
        private static readonly string StatusPath;
        private static readonly DateTime EditorSessionStartedUtc = DateTime.UtcNow;

        private static bool _isAssetEditing;
        private static bool _refreshPending;
        private static double _nextPollTime;
        private static string _activeRequestId;

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
            StatusPath = Path.Combine(GateDirectory, projectKey + ".status");

            EditorApplication.update += PollRequest;
            EditorApplication.quitting += HandleEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;
        }

        /// <summary>
        /// 轮询外部请求文件，并在请求进入或退出时切换 Unity 导入闸门。
        /// </summary>
        private static void PollRequest()
        {
            if (EditorApplication.timeSinceStartup < _nextPollTime || _refreshPending)
            {
                return;
            }

            _nextPollTime = EditorApplication.timeSinceStartup + PollIntervalSeconds;

            if (TryReadRequest(out var requestId, out var createdUtc))
            {
                if (_isAssetEditing)
                {
                    return;
                }

                if (createdUtc <= EditorSessionStartedUtc)
                {
                    WriteStatus(StatusError, "stale request from before the current Unity Editor session");
                    return;
                }

                BeginAssetEditing(requestId);
                return;
            }

            if (_isAssetEditing)
            {
                EndAssetEditing();
            }
        }

        /// <summary>
        /// 在 Unity Editor 退出前释放本桥接持有的导入暂停状态并记录中断信息。
        /// </summary>
        private static void HandleEditorQuitting()
        {
            if (!_isAssetEditing)
            {
                return;
            }

            try
            {
                AssetDatabase.StopAssetEditing();
            }
            finally
            {
                _isAssetEditing = false;
                WriteStatus(StatusInterrupted, "Unity Editor is quitting before the batch completed");
            }
        }

        /// <summary>
        /// 在程序集重载前释放导入暂停计数，避免域重载后遗留不可恢复的 AssetDatabase 状态。
        /// </summary>
        private static void HandleBeforeAssemblyReload()
        {
            if (!_isAssetEditing)
            {
                return;
            }

            try
            {
                AssetDatabase.StopAssetEditing();
            }
            finally
            {
                _isAssetEditing = false;
                WriteStatus(StatusInterrupted, "assembly reload interrupted the batch");
            }
        }

        #endregion

        #region 菜单操作

        /// <summary>
        /// 强制恢复 Unity 资源导入，供 Codex 中断或控制脚本异常时人工兜底。
        /// </summary>
        [MenuItem("Tools/Codex/Force Resume Imports", priority = 2000)]
        private static void ForceResumeImports()
        {
            DeleteRequestFile();

            if (_isAssetEditing)
            {
                EndAssetEditing();
            }
            else
            {
                _refreshPending = true;
                WriteStatus(StatusRefreshing, "manual force resume requested");
                EditorApplication.delayCall += CompleteRefresh;
            }
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
                _isAssetEditing = true;
                WriteStatus(StatusActive, requestId);
            }
            catch (Exception exception)
            {
                WriteStatus(StatusError, exception.Message);
            }
        }

        /// <summary>
        /// 停止批量导入暂停，保存资源并安排一次集中 Refresh。
        /// </summary>
        private static void EndAssetEditing()
        {
            Exception stopException = null;

            try
            {
                AssetDatabase.StopAssetEditing();
            }
            catch (Exception exception)
            {
                stopException = exception;
            }
            finally
            {
                _isAssetEditing = false;
                _activeRequestId = null;
                _refreshPending = true;
                WriteStatus(StatusRefreshing, stopException?.Message ?? "stopping asset editing");
                EditorApplication.delayCall += CompleteRefresh;
            }
        }

        /// <summary>
        /// 等待 Unity 完成 Refresh、导入和编译后写入空闲状态。
        /// </summary>
        private static void CompleteRefresh()
        {
            if (EditorApplication.isUpdating || EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += CompleteRefresh;
                return;
            }

            try
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                WriteStatus(StatusRefreshing, "refresh requested; waiting for Unity to become idle");
                EditorApplication.delayCall += WaitForRefreshIdle;
            }
            catch (Exception exception)
            {
                WriteStatus(StatusError, exception.Message);
                _refreshPending = false;
            }
        }

        /// <summary>
        /// 在 Refresh 请求发出后继续等待 Unity 的导入与编译状态稳定为空闲。
        /// </summary>
        private static void WaitForRefreshIdle()
        {
            if (EditorApplication.isUpdating || EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += WaitForRefreshIdle;
                return;
            }

            WriteStatus(StatusIdle, "refresh completed");
            _refreshPending = false;
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
                if (fields.Length < 3 || !DateTime.TryParse(fields[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedUtc))
                {
                    WriteStatus(StatusError, "invalid request format");
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
        }

        /// <summary>
        /// 写入当前项目的控制状态，供外部脚本等待状态转换。
        /// </summary>
        /// <param name="status">状态名称。</param>
        /// <param name="detail">可选的诊断信息。</param>
        private static void WriteStatus(string status, string detail)
        {
            try
            {
                Directory.CreateDirectory(GateDirectory);
                var content = string.Join("|", status, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), ProjectRoot, detail ?? string.Empty);
                File.WriteAllText(StatusPath, content, Encoding.UTF8);
            }
            catch (IOException)
            {
                // 状态文件只用于外部观测，写入失败不能阻止 Unity 继续执行自身的导入流程。
            }
        }

        /// <summary>
        /// 删除当前项目的请求文件，并限制操作范围在桥接控制目录内。
        /// </summary>
        private static void DeleteRequestFile()
        {
            if (File.Exists(RequestPath))
            {
                File.Delete(RequestPath);
            }
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
    }
}
