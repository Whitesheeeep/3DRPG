#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RPG.DialogueSystemModule.Editor.Tests
{
    /// <summary>
    /// 驱动 Dialogue 编辑器集成测试的 Editor 入口。测试在真实 Play Mode 中运行，结果保存到 Library。
    /// </summary>
    [InitializeOnLoad]
    public static class DialogueEditorTestRunner
    {
        #region 常量与静态状态

        private const string TestScenePath = "Assets/Scripts/InteractionSystem/Test/TestScene/TestInteractableScene.unity";
        private const string RunningKey = "RPG.DialogueEditorTests.Running";
        private const string RunIdKey = "RPG.DialogueEditorTests.RunId";
        private const string PhaseKey = "RPG.DialogueEditorTests.Phase";
        private const string OriginalScenesKey = "RPG.DialogueEditorTests.OriginalScenes";
        private const string ActiveSceneIndexKey = "RPG.DialogueEditorTests.ActiveSceneIndex";
        private const string StartTimeKey = "RPG.DialogueEditorTests.StartTime";
        private const string ReportJsonKey = "RPG.DialogueEditorTests.ReportJson";
        private const double StartupTimeoutSeconds = 60d;
        private const double TotalTimeoutSeconds = 180d;

        private static DialogueEditorTestReport report;
        private static DialogueEditorTestFixture fixture;
        private static IEnumerator testRoutine;
        private static double runStartedAt;
        private static bool cleanupRequested;
        private static bool finalizing;

        #endregion

        #region 初始化与菜单

        /// <summary>注册 Editor 更新回调，并在脚本域重载后恢复尚未完成的测试。</summary>
        static DialogueEditorTestRunner()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            if (SessionState.GetBool(RunningKey, false))
            {
                report = LoadReport();
                double.TryParse(SessionState.GetString(StartTimeKey, "0"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out runStartedAt);
            }
        }

        /// <summary>从 Unity 菜单启动完整对话测试。</summary>
        [MenuItem("DialogueSystem/Tests/运行完整测试")]
        public static void StartFromMenu() => StartRun();

        /// <summary>从 Unity 菜单请求取消测试。</summary>
        [MenuItem("DialogueSystem/Tests/取消测试")]
        public static void CancelFromMenu() => CancelRun(SessionState.GetString(RunIdKey, string.Empty));

        /// <summary>
        /// 创建一次跨 Play Mode 域重载可恢复的测试运行。
        /// </summary>
        /// <returns>运行 ID；环境不满足时返回空字符串。</returns>
        public static string StartRun()
        {
            if (SessionState.GetBool(RunningKey, false))
                return string.Empty;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                return string.Empty;

            List<string> originalScenes = CaptureOriginalScenes();
            if (originalScenes.Count == 0 || HasDirtyScene())
                return string.Empty;
            if (!File.Exists(TestScenePath))
                return string.Empty;

            string runId = Guid.NewGuid().ToString("N");
            report = new DialogueEditorTestReport(runId, TestScenePath);
            report.Phase = "OpeningTestScene";
            runStartedAt = EditorApplication.timeSinceStartup;
            cleanupRequested = false;
            finalizing = false;

            SessionState.SetBool(RunningKey, true);
            SessionState.SetString(RunIdKey, runId);
            SessionState.SetString(StartTimeKey, runStartedAt.ToString(CultureInfo.InvariantCulture));
            SessionState.SetString(PhaseKey, report.Phase);
            SessionState.SetString(OriginalScenesKey, string.Join("\n", originalScenes));
            SessionState.SetString(ActiveSceneIndexKey, SceneManager.GetActiveScene().path);
            PersistReport();

            EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
            report.Phase = "WaitingForPlayMode";
            SessionState.SetString(PhaseKey, report.Phase);
            PersistReport();
            EditorApplication.isPlaying = true;
            return runId;
        }

        /// <summary>返回指定运行的当前 JSON 状态；未知 ID 返回空字符串。</summary>
        /// <param name="runId">StartRun 返回的运行 ID。</param>
        /// <returns>可供 MCP 读取的状态 JSON。</returns>
        public static string GetStatusJson(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId) || !string.Equals(runId, SessionState.GetString(RunIdKey, string.Empty), StringComparison.Ordinal))
                return string.Empty;
            if (report != null) return JsonUtility.ToJson(report, true);
            string saved = SessionState.GetString(ReportJsonKey, string.Empty);
            return string.IsNullOrEmpty(saved) ? string.Empty : saved;
        }

        /// <summary>请求当前运行停止，后续仍会执行临时对象清理和场景恢复。</summary>
        /// <param name="runId">需要取消的运行 ID。</param>
        public static void CancelRun(string runId)
        {
            if (!SessionState.GetBool(RunningKey, false) ||
                !string.Equals(runId, SessionState.GetString(RunIdKey, string.Empty), StringComparison.Ordinal))
                return;
            report ??= LoadReport();
            report.Phase = "Canceled";
            report.FailureMessage = "用户请求取消测试。";
            cleanupRequested = true;
        }

        #endregion

        #region Editor 驱动循环

        /// <summary>每个 Editor 更新推进一个测试步骤，不占用 Unity 主线程。</summary>
        private static void Tick()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            report ??= LoadReport();
            if (report == null) return;

            if (EditorApplication.timeSinceStartup - runStartedAt > TotalTimeoutSeconds && !finalizing)
            {
                report.Phase = "TimedOut";
                report.FailureMessage = "整套对话测试超过 180 秒。";
                cleanupRequested = true;
            }

            if (cleanupRequested)
            {
                DriveCleanup();
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                // 测试启动前和场景切换期间等待 Play Mode；测试中途被外部停止则视为中断。
                if (report.Phase == "WaitingForPlayMode" || report.Phase == "OpeningTestScene") return;
                report.Phase = "Interrupted";
                report.FailureMessage = "Play Mode 被外部停止。";
                cleanupRequested = true;
                return;
            }

            if (fixture == null)
            {
                if (EditorApplication.timeSinceStartup - runStartedAt > StartupTimeoutSeconds)
                {
                    report.Phase = "TimedOut";
                    report.FailureMessage = "等待 GameArchitecture、UIManager 或 DialogueWindow 超时。";
                    cleanupRequested = true;
                    return;
                }

                if (!TryCreateFixture(out DialogueEditorTestFixture createdFixture)) return;
                fixture = createdFixture;
                report.Phase = "Running";
                SessionState.SetString(PhaseKey, report.Phase);
                testRoutine = DialogueEditorTestCases.RunAll(fixture, report);
            }

            try
            {
                if (!testRoutine.MoveNext())
                {
                    report.Phase = report.Cases.Count == 0 || report.FailedCount != 0 ? "Failed" : "Passed";
                    if (report.Cases.Count == 0)
                        report.FailureMessage = "测试枚举器没有执行任何用例。";
                    cleanupRequested = true;
                }
            }
            catch (Exception exception)
            {
                report.Phase = "Failed";
                report.FailureMessage = exception.ToString();
                cleanupRequested = true;
            }
            PersistReport();
        }

        /// <summary>检查架构和窗口预加载是否完成，准备真实运行时测试夹具。</summary>
        /// <param name="createdFixture">创建完成的夹具。</param>
        /// <returns>本帧是否已创建夹具。</returns>
        private static bool TryCreateFixture(out DialogueEditorTestFixture createdFixture)
        {
            createdFixture = null;
            try
            {
                // 旧版 Architecture 没有公开 IsInitialized；GetSystem 成功代表启动层已完成注册。
                RPG.Game.GameArchitecture.Interface.GetSystem<DialogueSystem>();
            }
            catch
            {
                return false;
            }
            if (!WS_Modules.UIModule.UIManager.Instance.IsInitialized) return false;
            if (!RPG.Game.UI.GameWindowPreloadService.Instance.IsPreloaded) return false;
            if (!WS_Modules.UIModule.UIManager.Instance.TryGetWindow<WS_Modules.UIModule.DialogueWindow>(out WS_Modules.UIModule.DialogueWindow window))
                return false;
            if (window.GameObject == null || window.GameObject.GetComponent<WS_Modules.UIModule.DialogueWindowDataComponent>() == null)
                return false;

            createdFixture = new DialogueEditorTestFixture(window);
            return true;
        }

        /// <summary>按顺序释放测试对象、退出 Play Mode 并恢复原场景。</summary>
        private static void DriveCleanup()
        {
            if (!finalizing)
            {
                finalizing = true;
                fixture?.Dispose();
                fixture = null;
                testRoutine = null;
                PersistReport();
                if (EditorApplication.isPlaying)
                {
                    report.Phase = report.Phase == "Passed" || report.Phase == "Failed" || report.Phase == "Canceled" || report.Phase == "TimedOut"
                        ? report.Phase
                        : "Interrupted";
                    EditorApplication.isPlaying = false;
                    return;
                }
            }

            if (EditorApplication.isPlaying) return;
            RestoreOriginalScenes();
            report.Phase = report.FailureMessage == null && report.FailedCount == 0 ? "Passed" :
                (report.Phase == "Canceled" ? "Canceled" : "Failed");
            PersistReport();
            WriteFiles();
            SessionState.SetBool(RunningKey, false);
            SessionState.SetString(PhaseKey, report.Phase);
            cleanupRequested = false;
            finalizing = false;
        }

        #endregion

        #region 场景与报告

        /// <summary>取得当前打开场景路径，供结束后恢复多场景布局。</summary>
        private static List<string> CaptureOriginalScenes()
        {
            var paths = new List<string>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                string path = SceneManager.GetSceneAt(index).path;
                if (string.IsNullOrEmpty(path)) return new List<string>();
                paths.Add(path);
            }
            return paths;
        }

        /// <summary>判断任一打开场景是否有未保存修改，避免测试覆盖用户工作。</summary>
        private static bool HasDirtyScene()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
                if (SceneManager.GetSceneAt(index).isDirty) return true;
            return false;
        }

        /// <summary>恢复启动测试前的场景集合；测试场景本身不保存。</summary>
        private static void RestoreOriginalScenes()
        {
            string saved = SessionState.GetString(OriginalScenesKey, string.Empty);
            string[] paths = saved.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (paths.Length == 0) return;
            EditorSceneManager.OpenScene(paths[0], OpenSceneMode.Single);
            for (int index = 1; index < paths.Length; index++)
                EditorSceneManager.OpenScene(paths[index], OpenSceneMode.Additive);
            string activePath = SessionState.GetString(ActiveSceneIndexKey, string.Empty);
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.path == activePath)
                {
                    SceneManager.SetActiveScene(scene);
                    break;
                }
            }
        }

        /// <summary>从域重载期间保存的 JSON 恢复报告对象。</summary>
        private static DialogueEditorTestReport LoadReport()
        {
            string saved = SessionState.GetString(ReportJsonKey, string.Empty);
            return string.IsNullOrEmpty(saved) ? null : JsonUtility.FromJson<DialogueEditorTestReport>(saved);
        }

        /// <summary>增量保存报告，使测试中断后仍能查看已完成用例。</summary>
        private static void PersistReport()
        {
            if (report != null) SessionState.SetString(ReportJsonKey, JsonUtility.ToJson(report));
        }

        /// <summary>将最终报告写入 Library 下的 JSON 和 Markdown 文件。</summary>
        private static void WriteFiles()
        {
            if (report == null) return;
            string directory = Path.Combine("Library", "DialogueTests", report.RunId);
            Directory.CreateDirectory(directory);
            report.ReportDirectory = directory.Replace('\\', '/');
            string jsonPath = Path.Combine(directory, "report.json");
            string markdownPath = Path.Combine(directory, "report.md");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true), Encoding.UTF8);

            var markdown = new StringBuilder();
            markdown.AppendLine($"# Dialogue Editor Test {report.RunId}");
            markdown.AppendLine();
            markdown.AppendLine($"- Phase: `{report.Phase}`");
            markdown.AppendLine($"- Passed: `{report.PassedCount}`");
            markdown.AppendLine($"- Failed: `{report.FailedCount}`");
            markdown.AppendLine();
            for (int index = 0; index < report.Cases.Count; index++)
            {
                DialogueEditorTestCaseResult item = report.Cases[index];
                markdown.AppendLine($"## {item.Id} — {item.Status}");
                markdown.AppendLine();
                markdown.AppendLine(item.Message ?? string.Empty);
                markdown.AppendLine($"- Expected: {item.Expected ?? string.Empty}");
                markdown.AppendLine($"- Actual: {item.Actual ?? string.Empty}");
                markdown.AppendLine($"- SessionId: `{item.SessionId ?? string.Empty}`");
                markdown.AppendLine($"- NodeId: `{item.NodeId ?? string.Empty}`");
                markdown.AppendLine($"- WindowVisible: `{item.WindowVisible}`");
                markdown.AppendLine($"- SelectedObject: `{item.SelectedObject ?? string.Empty}`");
                markdown.AppendLine($"- TypeWriterState: `{item.TypeWriterState ?? string.Empty}`");
                markdown.AppendLine($"- ChoiceVisible: `{item.ChoiceVisible}`");
                if (!string.IsNullOrEmpty(item.Exception)) markdown.AppendLine($"\n```text\n{item.Exception}\n```");
                markdown.AppendLine();
            }
            File.WriteAllText(markdownPath, markdown.ToString(), Encoding.UTF8);
            report.JsonPath = jsonPath.Replace('\\', '/');
            report.MarkdownPath = markdownPath.Replace('\\', '/');
            File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true), Encoding.UTF8);
        }

        #endregion
    }

    /// <summary>一套测试运行的可序列化状态。</summary>
    [Serializable]
    public sealed class DialogueEditorTestReport
    {
        /// <summary>创建测试报告。</summary>
        /// <param name="runId">运行 ID。</param>
        /// <param name="scenePath">测试场景路径。</param>
        public DialogueEditorTestReport(string runId, string scenePath)
        {
            RunId = runId;
            ScenePath = scenePath;
            Cases = new List<DialogueEditorTestCaseResult>();
        }

        /// <summary>供 Unity JsonUtility 创建对象的无参构造函数。</summary>
        public DialogueEditorTestReport() => Cases = new List<DialogueEditorTestCaseResult>();

        /// <summary>运行 ID。</summary>
        public string RunId;
        /// <summary>测试场景路径。</summary>
        public string ScenePath;
        /// <summary>当前运行阶段。</summary>
        public string Phase;
        /// <summary>当前正在执行的用例标识；清理或完成后为空。</summary>
        public string CurrentCaseId;
        /// <summary>整套测试的失败说明。</summary>
        public string FailureMessage;
        /// <summary>报告目录。</summary>
        public string ReportDirectory;
        /// <summary>JSON 报告路径。</summary>
        public string JsonPath;
        /// <summary>Markdown 报告路径。</summary>
        public string MarkdownPath;
        /// <summary>各用例结果。</summary>
        public List<DialogueEditorTestCaseResult> Cases;
        /// <summary>增量保存的通过数量。</summary>
        [SerializeField] private int passedCount;
        /// <summary>增量保存的失败数量。</summary>
        [SerializeField] private int failedCount;
        /// <summary>通过数量。</summary>
        public int PassedCount => passedCount;
        /// <summary>失败数量。</summary>
        public int FailedCount => failedCount;

        /// <summary>追加一个用例结果。</summary>
        /// <param name="result">用例结果。</param>
        public void Add(DialogueEditorTestCaseResult result)
        {
            Cases.Add(result);
            if (result == null) return;
            if (string.Equals(result.Status, "Passed", StringComparison.Ordinal)) passedCount++;
            if (string.Equals(result.Status, "Failed", StringComparison.Ordinal)) failedCount++;
        }
    }

    /// <summary>单个对话测试用例的结果和诊断信息。</summary>
    [Serializable]
    public sealed class DialogueEditorTestCaseResult
    {
        /// <summary>用例标识。</summary>
        public string Id;
        /// <summary>Passed 或 Failed。</summary>
        public string Status;
        /// <summary>断言与上下文说明。</summary>
        public string Message;
        /// <summary>用例预期结果。</summary>
        public string Expected;
        /// <summary>用例实际结果或失败原因。</summary>
        public string Actual;
        /// <summary>异常文本。</summary>
        public string Exception;
        /// <summary>用例耗时秒数。</summary>
        public float DurationSeconds;
        /// <summary>失败或完成时关联的会话标识。</summary>
        public string SessionId;
        /// <summary>失败或完成时所在的 SpeechNode 标识。</summary>
        public string NodeId;
        /// <summary>观测时 DialogueWindow 是否可见。</summary>
        public bool WindowVisible;
        /// <summary>观测时 EventSystem 当前焦点对象名称。</summary>
        public string SelectedObject;
        /// <summary>观测时 TypeWriter 状态。</summary>
        public string TypeWriterState;
        /// <summary>观测时 Choice 区域是否可见。</summary>
        public bool ChoiceVisible;
    }
}
#endif
