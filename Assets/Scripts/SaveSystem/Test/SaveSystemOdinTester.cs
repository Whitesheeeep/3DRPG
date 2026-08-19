#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using RPG.SaveSystem;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using WS_Modules.BusinessArchitecture;

namespace RPG.SaveSystem.Tests
{
    /// <summary>
    /// 通过 Odin Inspector 手动驱动 SaveManager 的 JSON、本地容器和模块恢复测试组件。
    /// </summary>
    public sealed class SaveSystemOdinTester : MonoBehaviour
    {
        #region 测试常量与输入

        private const string TestSlotIdText = "fffffffffffffffffffffffffffffffe";
        private const string TestModuleIdText = "save-test";
        private const int TestModuleVersion = 1;

        private static readonly SaveSlotId TestSlotId = SaveSlotId.Parse(TestSlotIdText);
        private static readonly SaveModuleId TestModuleId = new SaveModuleId(TestModuleIdText);

        [Title("测试快照")]
        [SerializeField] private string testLabel = "Odin Save Test";
        [SerializeField, MinValue(0)] private int testScore = 42;
        [SerializeField] private List<string> testCompletedTaskIds = new List<string>
        {
            "task.first",
            "task.second"
        };

        #endregion

        #region 测试状态

        [Title("测试状态")]
        [ShowInInspector, ReadOnly] private bool isRunning;
        [ShowInInspector, ReadOnly] private string lastStatus = "Idle";
        [ShowInInspector, ReadOnly] private string lastTestFilePath = string.Empty;
        [ShowInInspector, ReadOnly] private long lastPayloadLength;

        private LocalFileSaveStorage storage;
        private IArchitecture testArchitecture;
        private SaveTestModule testModule;

        /// <summary>
        /// 获取本次测试使用的正式存档目录。
        /// </summary>
        private string SaveDirectoryPath =>
            Path.Combine(Application.persistentDataPath, SaveStorageDefaults.LocalDirectoryName);

        #endregion

        #region 生命周期

        /// <summary>
        /// 测试组件销毁时注销测试架构，使 Manager/System 按 BusinessArchitecture 生命周期清理。
        /// </summary>
        private void OnDestroy()
        {
            if (TestArchitecture.IsInitialized)
            {
                TestArchitecture.DeinitArchitecture();
            }

            testArchitecture = null;
            storage = null;
            testModule = null;
        }

        #endregion

        #region Odin 操作

        /// <summary>
        /// 执行一次通过 SaveManager 完成的 JSON、容器、本地文件和模块恢复往返测试。
        /// </summary>
        [Button("运行基础往返测试", ButtonSizes.Large)]
        public void RunBasicRoundTrip()
        {
            if (!TryBeginOperation("基础往返测试"))
            {
                return;
            }

            RunBasicRoundTripAsync().Forget();
        }

        /// <summary>
        /// 只读列出正式 Saves 目录中的可用和损坏槽位，不反序列化业务 Payload。
        /// </summary>
        [Button("列出正式存档")]
        public void ListOfficialSaves()
        {
            if (!TryBeginOperation("列出正式存档"))
            {
                return;
            }

            ListOfficialSavesAsync().Forget();
        }

        /// <summary>
        /// 删除固定保留测试槽位，不触碰其他正式存档。
        /// </summary>
        [Button("删除保留测试槽位")]
        public void DeleteReservedTestSlot()
        {
            if (!TryBeginOperation("删除保留测试槽位"))
            {
                return;
            }

            DeleteReservedTestSlotAsync().Forget();
        }

        /// <summary>
        /// 在文件浏览器中打开正式 Saves 目录，便于检查 .save 文件。
        /// </summary>
        [Button("打开正式存档目录")]
        public void RevealOfficialSaveDirectory()
        {
            Directory.CreateDirectory(SaveDirectoryPath);
            EditorUtility.RevealInFinder(SaveDirectoryPath);
            Debug.Log($"[SaveSystemTest] 已打开正式存档目录：{SaveDirectoryPath}", this);
        }

        /// <summary>
        /// 输出正式目录、保留测试槽位、序列化格式和测试模块版本。
        /// </summary>
        [Button("打印测试配置")]
        public void PrintTestConfiguration()
        {
            Debug.Log(
                $"[SaveSystemTest] directory={SaveDirectoryPath}, " +
                $"slot={TestSlotId}, format={NewtonsoftJsonSaveSerializer.JsonFormatId}, " +
                $"module={TestModuleId} v{TestModuleVersion}",
                this);
        }

        #endregion

        #region 异步测试流程

        /// <summary>
        /// 使用 SaveManager 执行保存、列举、原始 JSON 检查、加载和字段比较。
        /// </summary>
        private async UniTask RunBasicRoundTripAsync()
        {
            string phase = "初始化测试上下文";
            try
            {
                lastStatus = "Running: 基础往返测试";
                SaveManager manager = GetSaveManager();
                SaveTestSnapshot expectedSnapshot = CreateTestSnapshot();
                testModule.SetState(expectedSnapshot);

                phase = "通过 SaveManager 保存测试槽位";
                SaveResult saveResult = await manager.SaveAsync(
                    new SaveRequest(TestSlotId, "[SaveTest] Odin RoundTrip"));
                EnsureSuccess(saveResult, phase);

                phase = "通过 SaveManager 列举并校验测试槽位";
                SaveResult<IReadOnlyList<SaveStorageEntry>> listResult =
                    await manager.ListSlotsAsync();
                EnsureSuccess(listResult, phase);
                SaveStorageEntry entry = FindEntry(listResult.Value, TestSlotId, phase);
                Ensure(entry.IsAvailable, phase, $"测试槽位状态为 {entry.State}，错误码={entry.ErrorCode}");

                phase = "读取并检查 JSON Payload";
                byte[] jsonBytes = ReadPayloadBytes(TestSlotId, phase);
                string json = ValidateJsonPayload(jsonBytes, phase);

                // 修改运行时状态后再加载，确保 Manager 的 RestoreSnapshot 确实覆盖了当前状态。
                testModule.SetState(new SaveTestSnapshot
                {
                    Label = "State before load",
                    Score = -1,
                    CompletedTaskIds = new List<string> { "changed.before.load" }
                });

                phase = "通过 SaveManager 加载并恢复测试模块";
                SaveResult<SaveLoadResult> loadResult = await manager.LoadAsync(TestSlotId);
                EnsureSuccess(loadResult, phase);
                Ensure(loadResult.Value.Summary.SlotId == TestSlotId, phase, "加载摘要 SlotId 不一致。");
                Ensure(loadResult.Value.Summary.CharacterName == "[SaveTest] Odin RoundTrip",
                    phase,
                    "加载摘要 CharacterName 不一致。");
                Ensure(loadResult.Value.Summary.FormatVersion == 1,
                    phase,
                    "加载摘要 FormatVersion 不一致。");
                Ensure(loadResult.Value.MigratedModules.Count == 0,
                    phase,
                    "测试模块不应产生迁移记录。");

                phase = "比较恢复后的测试快照字段";
                CompareSnapshot(expectedSnapshot, testModule.CurrentState, phase);

                phase = "验证 Handle 释放底层流";
                Stream payloadStreamAfterDispose = OpenAndDisposePayload(TestSlotId, phase);
                EnsureDisposedStream(payloadStreamAfterDispose, phase);

                lastPayloadLength = entry.PayloadLength;
                lastTestFilePath = Path.Combine(
                    SaveDirectoryPath,
                    SaveContainerFormat.GetFileName(TestSlotId));
                lastStatus = $"Passed: Payload={lastPayloadLength} bytes, JSON={json.Length} chars";
                Debug.Log(
                    $"[SaveSystemTest] 基础往返测试通过：slot={TestSlotId}, " +
                    $"file={lastTestFilePath}, payload={lastPayloadLength} bytes",
                    this);
            }
            catch (OperationCanceledException)
            {
                lastStatus = $"Canceled: {phase}";
                Debug.LogWarning($"[SaveSystemTest] 测试已取消：{phase}", this);
            }
            catch (Exception exception)
            {
                lastStatus = $"Failed: {phase}";
                Debug.LogError($"[SaveSystemTest] 测试失败：phase={phase}", this);
                Debug.LogException(exception, this);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// 通过 SaveManager 列出正式存档目录并输出每个容器的状态和头部元数据。
        /// </summary>
        private async UniTask ListOfficialSavesAsync()
        {
            try
            {
                lastStatus = "Running: 列出正式存档";
                SaveResult<IReadOnlyList<SaveStorageEntry>> result =
                    await GetSaveManager().ListSlotsAsync();
                EnsureSuccess(result, "列出正式存档");

                for (int index = 0; index < result.Value.Count; index++)
                {
                    SaveStorageEntry entry = result.Value[index];
                    Debug.Log(
                        $"[SaveSystemTest] entry[{index}] slot={entry.SlotId}, state={entry.State}, " +
                        $"format={entry.FormatId}, version={entry.ContainerVersion}, " +
                        $"payload={entry.PayloadLength}, error={entry.ErrorCode}, message={entry.Message}",
                        this);
                }

                lastStatus = $"Passed: 列出 {result.Value.Count} 个槽位";
            }
            catch (OperationCanceledException)
            {
                lastStatus = "Canceled: 列出正式存档";
                Debug.LogWarning("[SaveSystemTest] 列出正式存档已取消。", this);
            }
            catch (Exception exception)
            {
                lastStatus = "Failed: 列出正式存档";
                Debug.LogException(exception, this);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// 通过 SaveManager 删除保留测试槽位，并把不存在视为已经清理完成。
        /// </summary>
        private async UniTask DeleteReservedTestSlotAsync()
        {
            try
            {
                lastStatus = "Running: 删除保留测试槽位";
                SaveResult result = await GetSaveManager().DeleteAsync(TestSlotId);
                if (!result.IsSuccess && result.ErrorCode != SaveErrorCode.SlotNotFound)
                {
                    EnsureSuccess(result, "删除保留测试槽位");
                }

                lastStatus = result.IsSuccess ? "Passed: 已删除测试槽位" : "Passed: 测试槽位已不存在";
                Debug.Log($"[SaveSystemTest] 删除测试槽位结果：{lastStatus}", this);
            }
            catch (OperationCanceledException)
            {
                lastStatus = "Canceled: 删除保留测试槽位";
                Debug.LogWarning("[SaveSystemTest] 删除保留测试槽位已取消。", this);
            }
            catch (Exception exception)
            {
                lastStatus = "Failed: 删除保留测试槽位";
                Debug.LogException(exception, this);
            }
            finally
            {
                EndOperation();
            }
        }

        #endregion

        #region 管理器组装与测试数据

        /// <summary>
        /// 创建本测试使用的 BusinessArchitecture，模拟游戏启动层显式组装依赖。
        /// </summary>
        /// <returns>可复用的测试 Manager。</returns>
        private SaveManager GetSaveManager()
        {
            if (testArchitecture != null)
            {
                return testArchitecture.GetManager<SaveManager>();
            }

            TestArchitecture.Configure(SaveDirectoryPath);
            testArchitecture = TestArchitecture.Interface;
            storage = TestArchitecture.Storage;
            testModule = TestArchitecture.TestModule;
            return testArchitecture.GetManager<SaveManager>();
        }

        /// <summary>
        /// 构建当前 Inspector 输入对应的测试快照。
        /// </summary>
        /// <returns>独立的测试快照副本。</returns>
        private SaveTestSnapshot CreateTestSnapshot()
        {
            if (testCompletedTaskIds == null)
            {
                throw new InvalidOperationException("测试快照的 CompletedTaskIds 不能为 null。");
            }

            return new SaveTestSnapshot
            {
                Label = testLabel,
                Score = testScore,
                CompletedTaskIds = new List<string>(testCompletedTaskIds)
            };
        }

        /// <summary>
        /// 从存储层读取指定槽位的原始 Payload，用于验证 JSON 可视化内容。
        /// </summary>
        /// <param name="slotId">待读取槽位。</param>
        /// <param name="phase">断言阶段。</param>
        /// <returns>Payload 原始字节。</returns>
        private byte[] ReadPayloadBytes(SaveSlotId slotId, string phase)
        {
            SaveResult<ISaveReadHandle> openResult = storage.OpenReadAsync(
                slotId,
                default).GetAwaiter().GetResult();
            EnsureSuccess(openResult, phase);
            using (ISaveReadHandle handle = openResult.Value)
            using (var payload = new MemoryStream())
            {
                handle.Content.CopyTo(payload);
                return payload.ToArray();
            }
        }

        /// <summary>
        /// 打开并释放一次读取句柄，返回释放后的 Payload 流供生命周期断言使用。
        /// </summary>
        /// <param name="slotId">待读取槽位。</param>
        /// <param name="phase">断言阶段。</param>
        /// <returns>已经随 Handle 释放的 Payload 流。</returns>
        private Stream OpenAndDisposePayload(SaveSlotId slotId, string phase)
        {
            SaveResult<ISaveReadHandle> openResult = storage.OpenReadAsync(
                slotId,
                default).GetAwaiter().GetResult();
            EnsureSuccess(openResult, phase);
            ISaveReadHandle handle = openResult.Value;
            Stream stream = handle.Content;
            handle.Dispose();
            return stream;
        }

        #endregion

        #region 测试断言与操作生命周期

        /// <summary>
        /// 验证 JSON 使用 UTF-8 无 BOM、对象 snapshot 和无运行时类型元数据。
        /// </summary>
        /// <param name="jsonBytes">Payload 原始字节。</param>
        /// <param name="phase">断言阶段。</param>
        /// <returns>用于日志的 JSON 文本。</returns>
        private static string ValidateJsonPayload(byte[] jsonBytes, string phase)
        {
            Ensure(jsonBytes.Length < 3 || jsonBytes[0] != 0xEF || jsonBytes[1] != 0xBB || jsonBytes[2] != 0xBF,
                phase,
                "JSON Payload 不应包含 UTF-8 BOM。");
            string json = Encoding.UTF8.GetString(jsonBytes);
            Ensure(json.IndexOf("$type", StringComparison.Ordinal) < 0, phase, "JSON Payload 不应包含 $type。");
            Ensure(json.IndexOf("\"snapshot\": {", StringComparison.Ordinal) >= 0,
                phase,
                "JSON Payload 的 snapshot 必须是对象，不能是 Base64 字符串。");
            return json;
        }

        /// <summary>
        /// 比较保存前与恢复后的测试快照字段。
        /// </summary>
        /// <param name="expected">保存前快照。</param>
        /// <param name="actual">模块恢复后的快照。</param>
        /// <param name="phase">断言阶段。</param>
        private static void CompareSnapshot(
            SaveTestSnapshot expected,
            SaveTestSnapshot actual,
            string phase)
        {
            Ensure(actual != null, phase, "恢复后的测试快照不能为 null。");
            Ensure(expected.Label == actual.Label, phase, "快照 Label 不一致。");
            Ensure(expected.Score == actual.Score, phase, "快照 Score 不一致。");
            Ensure(actual.CompletedTaskIds != null &&
                   expected.CompletedTaskIds.Count == actual.CompletedTaskIds.Count,
                phase,
                "CompletedTaskIds 数量不一致。");
            for (int index = 0; index < expected.CompletedTaskIds.Count; index++)
            {
                Ensure(expected.CompletedTaskIds[index] == actual.CompletedTaskIds[index],
                    phase,
                    $"CompletedTaskIds[{index}] 不一致。");
            }
        }

        /// <summary>
        /// 验证释放 Handle 后 Payload Stream 已拒绝继续访问。
        /// </summary>
        /// <param name="payloadStream">Handle 释放前取得的 Payload Stream。</param>
        /// <param name="phase">断言阶段。</param>
        private static void EnsureDisposedStream(Stream payloadStream, string phase)
        {
            try
            {
                payloadStream.ReadByte();
                throw new InvalidOperationException($"{phase}：释放 Handle 后 Payload Stream 仍可读取。");
            }
            catch (ObjectDisposedException)
            {
                // 预期结果：Handle 释放同时关闭底层 Payload Stream。
            }
        }

        /// <summary>
        /// 从列举结果中查找固定测试槽位。
        /// </summary>
        /// <param name="entries">存储列举结果。</param>
        /// <param name="slotId">目标槽位。</param>
        /// <param name="phase">断言阶段。</param>
        /// <returns>匹配的槽位条目。</returns>
        private static SaveStorageEntry FindEntry(
            IReadOnlyList<SaveStorageEntry> entries,
            SaveSlotId slotId,
            string phase)
        {
            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index].SlotId == slotId)
                {
                    return entries[index];
                }
            }

            throw new InvalidOperationException($"{phase}：没有找到测试槽位 {slotId}。");
        }

        /// <summary>
        /// 检查非泛型存档结果，并把稳定错误码带入测试异常。
        /// </summary>
        /// <param name="result">待检查结果。</param>
        /// <param name="phase">断言阶段。</param>
        private static void EnsureSuccess(SaveResult result, string phase)
        {
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"{phase}：error={result.ErrorCode}, message={result.Message}",
                    result.Exception);
            }
        }

        /// <summary>
        /// 检查泛型存档结果，并把稳定错误码带入测试异常。
        /// </summary>
        /// <typeparam name="T">结果携带值类型。</typeparam>
        /// <param name="result">待检查结果。</param>
        /// <param name="phase">断言阶段。</param>
        private static void EnsureSuccess<T>(SaveResult<T> result, string phase)
        {
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"{phase}：error={result.ErrorCode}, message={result.Message}",
                    result.Exception);
            }
        }

        /// <summary>
        /// 抛出带阶段信息的手动测试断言异常。
        /// </summary>
        /// <param name="condition">断言条件。</param>
        /// <param name="phase">断言所属阶段。</param>
        /// <param name="message">失败原因。</param>
        private static void Ensure(bool condition, string phase, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"{phase}：{message}");
            }
        }

        /// <summary>
        /// 阻止多个 Odin 按钮同时操作同一个测试槽位。
        /// </summary>
        /// <param name="operationName">日志中的操作名称。</param>
        /// <returns>没有其他操作运行时返回 true。</returns>
        private bool TryBeginOperation(string operationName)
        {
            if (isRunning)
            {
                Debug.LogWarning($"[SaveSystemTest] 已有操作运行中，忽略请求：{operationName}", this);
                return false;
            }

            isRunning = true;
            return true;
        }

        /// <summary>
        /// 结束当前 Odin 测试操作。
        /// </summary>
        private void EndOperation()
        {
            isRunning = false;
        }

        #endregion

        #region 测试模块与快照

        /// <summary>
        /// 为 Odin 测试提供独立的 BusinessArchitecture 和 SaveManager 组装环境。
        /// </summary>
        private sealed class TestArchitecture : Architecture<TestArchitecture>
        {
            private static string testDirectory;
            /// <summary>
            /// 获取测试架构创建的本地存储实例。
            /// </summary>
            internal static LocalFileSaveStorage Storage { get; private set; }

            /// <summary>
            /// 获取测试架构创建的测试模块实例。
            /// </summary>
            internal static SaveTestModule TestModule { get; private set; }

            /// <summary>
            /// 设置测试架构使用的本地存档目录。
            /// </summary>
            /// <param name="directory">测试存档目录。</param>
            internal static void Configure(string directory)
            {
                if (IsInitialized)
                {
                    return;
                }

                testDirectory = directory;
            }

            /// <summary>
            /// 注册测试用 SaveManager 和模块注册 System。
            /// </summary>
            protected override void Init()
            {
                if (string.IsNullOrWhiteSpace(testDirectory))
                {
                    throw new InvalidOperationException("测试架构必须先配置存档目录。");
                }

                Storage = new LocalFileSaveStorage(testDirectory);
                var serializer = new NewtonsoftJsonSaveSerializer();
                var serializerRegistry = new SaveSerializerRegistry(
                    new ISaveSerializer[] { serializer });
                var snapshotTypeRegistry = new SaveSnapshotTypeRegistry();
                TestModule = new SaveTestModule(TestModuleId, TestModuleVersion);

                RegisterManager(new SaveManager(
                    new SaveManagerOptions(serializer.FormatId, 1),
                    Storage,
                    serializerRegistry,
                    snapshotTypeRegistry));
                RegisterSystem(new TestSaveModuleRegistrationSystem());
            }

            /// <summary>
            /// 清理测试架构保存的临时引用，避免下一次按钮测试复用旧模块实例。
            /// </summary>
            protected override void OnDeinit()
            {
                Storage = null;
                TestModule = null;
                testDirectory = null;
            }
        }

        /// <summary>
        /// 在所有测试 Manager 初始化后，把测试模块注入 SaveManager。
        /// </summary>
        private sealed class TestSaveModuleRegistrationSystem : AbstractSystem
        {
            /// <summary>
            /// 注册测试模块，使 Odin 测试走与正式业务相同的 System 组装路径。
            /// </summary>
            protected override void OnInit()
            {
                this.GetManager<SaveManager>().RegisterModule(TestArchitecture.TestModule);
            }
        }

        /// <summary>
        /// 用于验证 JSON 基础类型、有序列表和强类型恢复的编辑器测试快照。
        /// </summary>
        [Serializable]
        public sealed class SaveTestSnapshot : ISaveModuleSnapshot
        {
            /// <summary>获取或设置测试标签。</summary>
            public string Label { get; set; }

            /// <summary>获取或设置测试分数。</summary>
            public int Score { get; set; }

            /// <summary>获取或设置需要保持顺序的已完成任务 ID 列表。</summary>
            public List<string> CompletedTaskIds { get; set; } = new List<string>();
        }

        /// <summary>
        /// 将测试快照接入 SaveModule 契约并保存可观察的恢复状态。
        /// </summary>
        private sealed class SaveTestModule : SaveModule<SaveTestSnapshot>
        {
            private SaveTestSnapshot state = new SaveTestSnapshot();

            /// <summary>
            /// 创建测试存档模块。
            /// </summary>
            /// <param name="moduleId">测试模块标识。</param>
            /// <param name="version">测试模块版本。</param>
            public SaveTestModule(SaveModuleId moduleId, int version)
                : base(moduleId, version, SaveMissingModulePolicy.Required)
            {
            }

            /// <summary>
            /// 获取模块当前状态的独立副本。
            /// </summary>
            public SaveTestSnapshot CurrentState => Clone(state);

            /// <summary>
            /// 设置测试模块当前运行状态。
            /// </summary>
            /// <param name="snapshot">新的测试状态。</param>
            public void SetState(SaveTestSnapshot snapshot)
            {
                state = Clone(snapshot);
            }

            /// <summary>
            /// 采集当前测试状态。
            /// </summary>
            /// <returns>独立的测试快照。</returns>
            protected override SaveTestSnapshot CaptureTypedSnapshot() => Clone(state);

            /// <summary>
            /// 创建测试模块默认状态。
            /// </summary>
            /// <returns>空测试快照。</returns>
            protected override SaveTestSnapshot CreateDefaultTypedSnapshot() => new SaveTestSnapshot();

            /// <summary>
            /// 校验测试快照字段。
            /// </summary>
            /// <param name="snapshot">待校验快照。</param>
            protected override void ValidateTypedSnapshot(SaveTestSnapshot snapshot)
            {
                if (snapshot.CompletedTaskIds == null)
                {
                    throw new InvalidOperationException("测试快照的 CompletedTaskIds 不能为 null。");
                }
            }

            /// <summary>
            /// 将测试快照恢复为模块当前状态。
            /// </summary>
            /// <param name="snapshot">已校验测试快照。</param>
            protected override void RestoreTypedSnapshot(SaveTestSnapshot snapshot)
            {
                state = Clone(snapshot);
            }

            /// <summary>
            /// 复制测试快照及其有序列表，隔离运行时状态和序列化对象。
            /// </summary>
            /// <param name="snapshot">源快照。</param>
            /// <returns>独立副本。</returns>
            private static SaveTestSnapshot Clone(SaveTestSnapshot snapshot)
            {
                if (snapshot == null)
                {
                    throw new ArgumentNullException(nameof(snapshot));
                }

                return new SaveTestSnapshot
                {
                    Label = snapshot.Label,
                    Score = snapshot.Score,
                    CompletedTaskIds = snapshot.CompletedTaskIds == null
                        ? null
                        : new List<string>(snapshot.CompletedTaskIds)
                };
            }
        }

        #endregion
    }
}
#endif
