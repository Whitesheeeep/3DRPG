using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using WS_Modules.BusinessArchitecture;

namespace RPG.SaveSystem
{
    /// <summary>
    /// 组合存储、序列化、模块和迁移能力，并向业务提供统一存档接口。
    /// </summary>
    public sealed class SaveManager : AbstractManager, ISaveManager
    {
        #region 依赖与生命周期

        private readonly SaveManagerOptions options;
        private readonly ISaveStorage storage;
        private readonly SaveSerializerRegistry serializerRegistry;
        private readonly SaveSnapshotTypeRegistry snapshotTypeRegistry;
        private readonly object lifecycleGate = new object();
        private readonly List<ISaveModule> registeredModules = new List<ISaveModule>();
        private readonly List<ISaveMigration> registeredMigrations = new List<ISaveMigration>();

        private SaveModuleRegistry moduleRegistry;
        private SaveMigrationRegistry migrationRegistry;
        private CancellationTokenSource lifetimeCancellation;
        private int activeOperation;
        private int disposed;
        private int cancellationDisposed;

        /// <summary>
        /// 创建存档管理器并注入存储、序列化和快照类型基础依赖。
        /// </summary>
        /// <param name="options">管理器运行配置。</param>
        /// <param name="storage">容器存储实现。</param>
        /// <param name="serializerRegistry">序列化器注册表。</param>
        /// <param name="snapshotTypeRegistry">快照类型注册表。</param>
        /// <exception cref="ArgumentNullException">任何依赖为空时抛出。</exception>
        public SaveManager(
            SaveManagerOptions options,
            ISaveStorage storage,
            SaveSerializerRegistry serializerRegistry,
            SaveSnapshotTypeRegistry snapshotTypeRegistry)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            this.serializerRegistry = serializerRegistry ??
                                      throw new ArgumentNullException(nameof(serializerRegistry));
            this.snapshotTypeRegistry = snapshotTypeRegistry ??
                                        throw new ArgumentNullException(nameof(snapshotTypeRegistry));
            moduleRegistry = new SaveModuleRegistry(Array.Empty<ISaveModule>());
            migrationRegistry = new SaveMigrationRegistry(Array.Empty<ISaveMigration>());
        }

        /// <summary>
        /// 获取当前是否已有操作占用管理器。
        /// </summary>
        public bool IsBusy => Volatile.Read(ref activeOperation) != 0;

        /// <summary>
        /// 在存档操作完成后发布轻量通知。
        /// </summary>
        public event Action<SaveOperationCompleted> OperationCompleted;

        /// <summary>
        /// 初始化 SaveManager 的运行时取消源和注册快照。
        /// </summary>
        protected override void OnInit()
        {
            lock (lifecycleGate)
            {
                disposed = 0;
                cancellationDisposed = 0;
                activeOperation = 0;
                lifetimeCancellation = new CancellationTokenSource();
                moduleRegistry = new SaveModuleRegistry(registeredModules);
                migrationRegistry = new SaveMigrationRegistry(registeredMigrations);
            }
        }

        /// <summary>
        /// 取消未完成存档操作并清理 Manager 生命周期资源。
        /// </summary>
        protected override void OnDeinit()
        {
            lock (lifecycleGate)
            {
                if (disposed != 0)
                {
                    return;
                }

                disposed = 1;
                lifetimeCancellation?.Cancel();
                if (activeOperation == 0)
                {
                    DisposeLifetimeCancellation();
                }

                OperationCompleted = null;
                registeredModules.Clear();
                registeredMigrations.Clear();
            }
        }

        #endregion

        #region 模块与迁移注册

        /// <summary>
        /// 注册一个业务存档模块；注册结果从下一次存档操作开始生效。
        /// </summary>
        /// <param name="module">待注册的存档模块。</param>
        /// <exception cref="ArgumentNullException">模块为空时抛出。</exception>
        /// <exception cref="ArgumentException">模块重复、依赖非法或快照类型冲突时抛出。</exception>
        public void RegisterModule(ISaveModule module)
        {
            if (module == null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            lock (lifecycleGate)
            {
                EnsureRegistrationAvailable();
                var candidateModules = new List<ISaveModule>(registeredModules)
                {
                    module
                };
                var candidateRegistry = new SaveModuleRegistry(candidateModules);
                RegisterSnapshotType(module.ModuleId, module.CurrentVersion, module.CurrentSnapshotType);
                registeredModules.Add(module);
                moduleRegistry = candidateRegistry;
            }
        }

        /// <summary>
        /// 注册一个相邻版本迁移；注册结果从下一次加载操作开始生效。
        /// </summary>
        /// <param name="migration">待注册的迁移。</param>
        /// <exception cref="ArgumentNullException">迁移为空时抛出。</exception>
        /// <exception cref="ArgumentException">迁移重复或快照类型冲突时抛出。</exception>
        public void RegisterMigration(ISaveMigration migration)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration));
            }

            lock (lifecycleGate)
            {
                EnsureRegistrationAvailable();
                var candidateMigrations = new List<ISaveMigration>(registeredMigrations)
                {
                    migration
                };
                var candidateRegistry = new SaveMigrationRegistry(candidateMigrations);
                RegisterSnapshotType(
                    migration.ModuleId,
                    migration.FromVersion,
                    migration.SourceSnapshotType);
                RegisterSnapshotType(
                    migration.ModuleId,
                    migration.ToVersion,
                    migration.TargetSnapshotType);
                registeredMigrations.Add(migration);
                migrationRegistry = candidateRegistry;
            }
        }

        #endregion

        #region 对外操作

        /// <summary>
        /// 列出全部可识别槽位，不反序列化业务 Payload。
        /// </summary>
        /// <returns>槽位元数据或存储错误。</returns>
        public async UniTask<SaveResult<IReadOnlyList<SaveStorageEntry>>> ListSlotsAsync()
        {
            EnsureNotDisposed();
            if (!TryBeginOperation(
                    out CancellationToken cancellationToken,
                    out _,
                    out _,
                    out _))
            {
                return SaveResult<IReadOnlyList<SaveStorageEntry>>.Failure(
                    SaveErrorCode.OperationBusy,
                    "存档管理器已有操作正在执行。");
            }

            try
            {
                SaveResult<IReadOnlyList<SaveStorageEntry>> result =
                    await storage.ListEntriesAsync(cancellationToken);
                Publish(SaveOperationKind.List, default, ToBaseResult(result));
                return result;
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// 采集全部模块快照并保存到指定槽位。
        /// </summary>
        /// <param name="request">保存请求。</param>
        /// <returns>保存结果。</returns>
        public async UniTask<SaveResult> SaveAsync(SaveRequest request)
        {
            EnsureNotDisposed();
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!TryBeginOperation(
                    out CancellationToken cancellationToken,
                    out SaveModuleRegistry operationModuleRegistry,
                    out _,
                    out _))
            {
                return SaveResult.Failure(SaveErrorCode.OperationBusy, "存档管理器已有操作正在执行。");
            }

            try
            {
                SaveResult result = await SaveCoreAsync(
                    request,
                    cancellationToken,
                    operationModuleRegistry);
                Publish(SaveOperationKind.Save, request.SlotId, result);
                return result;
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// 读取、迁移、校验并恢复指定槽位。
        /// </summary>
        /// <param name="slotId">待加载槽位。</param>
        /// <returns>加载摘要和迁移报告，或结构化失败。</returns>
        public async UniTask<SaveResult<SaveLoadResult>> LoadAsync(SaveSlotId slotId)
        {
            EnsureNotDisposed();
            if (!TryBeginOperation(
                    out CancellationToken cancellationToken,
                    out SaveModuleRegistry operationModuleRegistry,
                    out SaveMigrationRegistry operationMigrationRegistry,
                    out SaveSnapshotTypeRegistry operationSnapshotTypeRegistry))
            {
                return SaveResult<SaveLoadResult>.Failure(
                    SaveErrorCode.OperationBusy,
                    "存档管理器已有操作正在执行。");
            }

            try
            {
                SaveResult<SaveLoadResult> result = await LoadCoreAsync(
                    slotId,
                    cancellationToken,
                    operationModuleRegistry,
                    operationMigrationRegistry,
                    operationSnapshotTypeRegistry);
                Publish(SaveOperationKind.Load, slotId, ToBaseResult(result));
                return result;
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// 删除指定槽位的正式存档文件。
        /// </summary>
        /// <param name="slotId">待删除槽位。</param>
        /// <returns>删除结果。</returns>
        public async UniTask<SaveResult> DeleteAsync(SaveSlotId slotId)
        {
            EnsureNotDisposed();
            if (!TryBeginOperation(
                    out CancellationToken cancellationToken,
                    out _,
                    out _,
                    out _))
            {
                return SaveResult.Failure(SaveErrorCode.OperationBusy, "存档管理器已有操作正在执行。");
            }

            try
            {
                SaveResult result = await storage.DeleteAsync(slotId, cancellationToken);
                Publish(SaveOperationKind.Delete, slotId, result);
                return result;
            }
            finally
            {
                EndOperation();
            }
        }

        #endregion

        #region 保存编排

        /// <summary>
        /// 在主线程采集快照、序列化完整 Envelope，并提交 Payload 到存储层。
        /// </summary>
        /// <param name="request">保存请求。</param>
        /// <param name="cancellationToken">管理器生命周期令牌。</param>
        /// <returns>保存结果。</returns>
        private async UniTask<SaveResult> SaveCoreAsync(
            SaveRequest request,
            CancellationToken cancellationToken,
            SaveModuleRegistry operationModuleRegistry)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 获取当前的序列化器
            SaveResult<ISaveSerializer> serializerResult =
                serializerRegistry.Resolve(options.DefaultFormatId);
            if (!serializerResult.IsSuccess)
            {
                return SaveResult.Failure(
                    serializerResult.ErrorCode,
                    serializerResult.Message,
                    serializerResult.Exception);
            }

            // 获取当前的模块快照并组装成 Envelope
            SaveResult<SaveEnvelope> envelopeResult = CaptureEnvelope(
                request,
                cancellationToken,
                operationModuleRegistry);
            if (!envelopeResult.IsSuccess)
            {
                return SaveResult.Failure(
                    envelopeResult.ErrorCode,
                    envelopeResult.Message,
                    envelopeResult.Exception);
            }

            // 开始序列化并提交 Payload
            using (var payload = new MemoryStream())
            {
                SaveResult serializeResult = await serializerResult.Value.SerializeAsync(
                    envelopeResult.Value,
                    payload,
                    cancellationToken);
                if (!serializeResult.IsSuccess)
                {
                    return serializeResult;
                }

                // 序列化器从当前位置写入，存储器从当前位置读取，因此提交前必须回到 Payload 起点。
                payload.Position = 0;
                return await storage.WriteAsync(
                    request.SlotId,
                    serializerResult.Value.FormatId,
                    payload,
                    cancellationToken);
            }
        }

        /// <summary>
        /// 在调用方线程采集并排序所有模块快照。
        /// </summary>
        /// <param name="request">保存请求。</param>
        /// <param name="cancellationToken">管理器生命周期令牌。</param>
        /// <returns>组装后的 Envelope 或快照采集失败。</returns>
        private SaveResult<SaveEnvelope> CaptureEnvelope(
            SaveRequest request,
            CancellationToken cancellationToken,
            SaveModuleRegistry operationModuleRegistry)
        {
            var moduleData = new List<SaveModuleData>(operationModuleRegistry.OrderedModules.Count);

            // 保存时，快照是“读取当前状态并打包成 Envelope”，不需要关心依赖恢复顺序，反而需要稳定顺序，便于序列化、审计、兼容和 diff
            foreach (ISaveModule module in operationModuleRegistry.OrderedModules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    ISaveModuleSnapshot snapshot = module.CaptureSnapshot();
                    if (snapshot == null || !module.CurrentSnapshotType.IsInstanceOfType(snapshot))
                    {
                        return SaveResult<SaveEnvelope>.Failure(
                            SaveErrorCode.InvalidSnapshot,
                            $"模块 {module.ModuleId} 返回了不匹配当前版本的快照。");
                    }

                    moduleData.Add(new SaveModuleData(module.ModuleId, module.CurrentVersion, snapshot));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return SaveResult<SaveEnvelope>.Failure(
                        SaveErrorCode.SnapshotCaptureFailed,
                        $"模块 {module.ModuleId} 采集快照失败。",
                        exception);
                }
            }

            var summary = new SaveSlotSummary(
                request.SlotId,
                request.CharacterName,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                options.CurrentSaveFormatVersion);
            return SaveResult<SaveEnvelope>.Success(new SaveEnvelope(summary, moduleData));
        }

        #endregion

        #region 加载编排

        /// <summary>
        /// 打开容器、反序列化、迁移并恢复模块。
        /// </summary>
        /// <param name="slotId">待加载槽位。</param>
        /// <param name="cancellationToken">管理器生命周期令牌。</param>
        /// <returns>加载结果。</returns>
        private async UniTask<SaveResult<SaveLoadResult>> LoadCoreAsync(
            SaveSlotId slotId,
            CancellationToken cancellationToken,
            SaveModuleRegistry operationModuleRegistry,
            SaveMigrationRegistry operationMigrationRegistry,
            SaveSnapshotTypeRegistry operationSnapshotTypeRegistry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveResult<ISaveReadHandle> openResult = await storage.OpenReadAsync(slotId, cancellationToken);
            if (!openResult.IsSuccess)
            {
                return SaveResult<SaveLoadResult>.Failure(
                    openResult.ErrorCode,
                    openResult.Message,
                    openResult.Exception);
            }

            using (ISaveReadHandle handle = openResult.Value)
            {
                SaveResult<ISaveSerializer> serializerResult = serializerRegistry.Resolve(handle.FormatId);
                if (!serializerResult.IsSuccess)
                {
                    return SaveResult<SaveLoadResult>.Failure(
                        serializerResult.ErrorCode,
                        serializerResult.Message,
                        serializerResult.Exception);
                }

                SaveResult<SaveEnvelope> envelopeResult = await serializerResult.Value.DeserializeAsync(
                    handle.Content,
                    operationSnapshotTypeRegistry,
                    cancellationToken);
                if (!envelopeResult.IsSuccess)
                {
                    return SaveResult<SaveLoadResult>.Failure(
                        envelopeResult.ErrorCode,
                        envelopeResult.Message,
                        envelopeResult.Exception);
                }

                SaveResult<PreparedLoad> preparedResult = PrepareLoad(
                    slotId,
                    envelopeResult.Value,
                    cancellationToken,
                    operationModuleRegistry,
                    operationMigrationRegistry);
                if (!preparedResult.IsSuccess)
                {
                    return SaveResult<SaveLoadResult>.Failure(
                        preparedResult.ErrorCode,
                        preparedResult.Message,
                        preparedResult.Exception);
                }

                SaveResult restoreResult = RestoreModules(
                    preparedResult.Value,
                    cancellationToken,
                    operationModuleRegistry);
                if (!restoreResult.IsSuccess)
                {
                    return SaveResult<SaveLoadResult>.Failure(
                        restoreResult.ErrorCode,
                        restoreResult.Message,
                        restoreResult.Exception);
                }

                return SaveResult<SaveLoadResult>.Success(new SaveLoadResult(
                    preparedResult.Value.Summary,
                    preparedResult.Value.MigratedModules));
            }
        }

        /// <summary>
        /// 在任何模块恢复前完成摘要、模块集合、迁移和快照校验。
        /// </summary>
        /// <param name="slotId">目标槽位。</param>
        /// <param name="envelope">反序列化后的存档容器。</param>
        /// <param name="cancellationToken">管理器生命周期令牌。</param>
        /// <returns>待恢复模块集合或结构化失败。</returns>
        private SaveResult<PreparedLoad> PrepareLoad(
            SaveSlotId slotId,
            SaveEnvelope envelope,
            CancellationToken cancellationToken,
            SaveModuleRegistry operationModuleRegistry,
            SaveMigrationRegistry operationMigrationRegistry)
        {
            if (envelope == null || envelope.Summary == null || envelope.Modules == null)
            {
                return SaveResult<PreparedLoad>.Failure(
                    SaveErrorCode.InvalidSnapshot,
                    "存档缺少有效 Summary 或 Modules。");
            }

            if (envelope.Summary.SlotId != slotId)
            {
                return SaveResult<PreparedLoad>.Failure(
                    SaveErrorCode.SlotIdMismatch,
                    $"存档摘要槽位与目标槽位不一致：{envelope.Summary.SlotId} != {slotId}");
            }

            if (envelope.Summary.FormatVersion != options.CurrentSaveFormatVersion)
            {
                return SaveResult<PreparedLoad>.Failure(
                    SaveErrorCode.UnsupportedFormatVersion,
                    $"不支持的完整存档格式版本：{envelope.Summary.FormatVersion}");
            }

            var rawModules = new Dictionary<SaveModuleId, SaveModuleData>();
            foreach (SaveModuleData moduleData in envelope.Modules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (moduleData == null || !moduleData.ModuleId.IsValid || moduleData.Snapshot == null)
                {
                    return SaveResult<PreparedLoad>.Failure(
                        SaveErrorCode.InvalidSnapshot,
                        "存档包含无效模块数据。");
                }

                if (!operationModuleRegistry.TryGet(moduleData.ModuleId, out _))
                {
                    return SaveResult<PreparedLoad>.Failure(
                        SaveErrorCode.UnknownModule,
                        $"存档包含未注册模块：{moduleData.ModuleId}");
                }

                if (!rawModules.TryAdd(moduleData.ModuleId, moduleData))
                {
                    return SaveResult<PreparedLoad>.Failure(
                        SaveErrorCode.DuplicateModule,
                        $"存档包含重复模块：{moduleData.ModuleId}");
                }
            }

            var preparedModules = new Dictionary<SaveModuleId, SaveModuleData>();
            var migratedModules = new List<SaveModuleId>();
            foreach (ISaveModule module in operationModuleRegistry.OrderedModules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SaveResult<SaveModuleData> moduleResult = rawModules.TryGetValue(
                    module.ModuleId,
                    out SaveModuleData moduleData)
                    ? MigrateModule(module, moduleData, migratedModules, operationMigrationRegistry)
                    : CreateMissingModule(module);
                if (!moduleResult.IsSuccess)
                {
                    return SaveResult<PreparedLoad>.Failure(
                        moduleResult.ErrorCode,
                        moduleResult.Message,
                        moduleResult.Exception);
                }

                try
                {
                    module.ValidateSnapshot(moduleResult.Value.Snapshot);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return SaveResult<PreparedLoad>.Failure(
                        SaveErrorCode.InvalidSnapshot,
                        $"模块 {module.ModuleId} 快照校验失败。",
                        exception);
                }

                preparedModules.Add(module.ModuleId, moduleResult.Value);
            }

            return SaveResult<PreparedLoad>.Success(new PreparedLoad(
                envelope.Summary,
                preparedModules,
                migratedModules));
        }

        /// <summary>
        /// 将一个已反序列化模块沿相邻迁移链升级到当前版本。
        /// </summary>
        /// <param name="module">当前注册模块。</param>
        /// <param name="moduleData">存档中的模块数据。</param>
        /// <param name="migratedModules">迁移报告列表。</param>
        /// <returns>当前版本模块数据或迁移失败。</returns>
        private SaveResult<SaveModuleData> MigrateModule(
            ISaveModule module,
            SaveModuleData moduleData,
            ICollection<SaveModuleId> migratedModules,
            SaveMigrationRegistry operationMigrationRegistry)
        {
            if (moduleData.Version <= 0)
            {
                return SaveResult<SaveModuleData>.Failure(
                    SaveErrorCode.InvalidSnapshot,
                    $"模块 {module.ModuleId} 的存档版本无效：{moduleData.Version}");
            }

            if (moduleData.Version > module.CurrentVersion)
            {
                return SaveResult<SaveModuleData>.Failure(
                    SaveErrorCode.UnsupportedModuleVersion,
                    $"模块 {module.ModuleId} 的存档版本高于当前版本：{moduleData.Version}");
            }

            ISaveModuleSnapshot snapshot = moduleData.Snapshot;
            int version = moduleData.Version;
            bool migrated = false;
            while (version < module.CurrentVersion)
            {
                if (!operationMigrationRegistry.TryGet(
                        module.ModuleId,
                        version,
                        out ISaveMigration migration))
                {
                    return SaveResult<SaveModuleData>.Failure(
                        SaveErrorCode.MissingMigration,
                        $"模块 {module.ModuleId} 缺少 v{version}->v{version + 1} 迁移。");
                }

                try
                {
                    snapshot = migration.Migrate(snapshot);
                    if (snapshot == null || !migration.TargetSnapshotType.IsInstanceOfType(snapshot))
                    {
                        return SaveResult<SaveModuleData>.Failure(
                            SaveErrorCode.MigrationFailed,
                            $"模块 {module.ModuleId} 的 v{version}->v{migration.ToVersion} 迁移返回类型错误。");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return SaveResult<SaveModuleData>.Failure(
                        SaveErrorCode.MigrationFailed,
                        $"模块 {module.ModuleId} 的 v{version}->v{migration.ToVersion} 迁移失败。",
                        exception);
                }

                version = migration.ToVersion;
                migrated = true;
            }

            if (!module.CurrentSnapshotType.IsInstanceOfType(snapshot))
            {
                return SaveResult<SaveModuleData>.Failure(
                    SaveErrorCode.InvalidSnapshot,
                    $"模块 {module.ModuleId} 的最终快照类型不匹配当前版本。");
            }

            if (migrated)
            {
                migratedModules.Add(module.ModuleId);
            }

            return SaveResult<SaveModuleData>.Success(
                new SaveModuleData(module.ModuleId, module.CurrentVersion, snapshot));
        }

        /// <summary>
        /// 为允许缺失的模块创建默认快照。
        /// </summary>
        /// <param name="module">缺失的注册模块。</param>
        /// <returns>默认版本模块数据或结构化失败。</returns>
        private static SaveResult<SaveModuleData> CreateMissingModule(ISaveModule module)
        {
            if (module.MissingModulePolicy == SaveMissingModulePolicy.Required)
            {
                return SaveResult<SaveModuleData>.Failure(
                    SaveErrorCode.MissingModule,
                    $"存档缺少必需模块：{module.ModuleId}");
            }

            try
            {
                ISaveModuleSnapshot snapshot = module.CreateDefaultSnapshot();
                if (snapshot == null || !module.CurrentSnapshotType.IsInstanceOfType(snapshot))
                {
                    return SaveResult<SaveModuleData>.Failure(
                        SaveErrorCode.InvalidSnapshot,
                        $"模块 {module.ModuleId} 的默认快照类型不匹配当前版本。");
                }

                return SaveResult<SaveModuleData>.Success(
                    new SaveModuleData(module.ModuleId, module.CurrentVersion, snapshot));
            }
            catch (Exception exception)
            {
                return SaveResult<SaveModuleData>.Failure(
                    SaveErrorCode.InvalidSnapshot,
                    $"模块 {module.ModuleId} 创建默认快照失败。",
                    exception);
            }
        }

        /// <summary>
        /// 按预先计算的依赖顺序恢复全部模块。
        /// </summary>
        /// <param name="preparedLoad">已经完成迁移和校验的加载数据。</param>
        /// <param name="cancellationToken">管理器生命周期令牌。</param>
        /// <returns>恢复结果。</returns>
        private SaveResult RestoreModules(
            PreparedLoad preparedLoad,
            CancellationToken cancellationToken,
            SaveModuleRegistry operationModuleRegistry)
        {
            foreach (ISaveModule module in operationModuleRegistry.RestoreOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    module.RestoreSnapshot(preparedLoad.Modules[module.ModuleId].Snapshot);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return SaveResult.Failure(
                        SaveErrorCode.RestoreFailed,
                        $"模块 {module.ModuleId} 恢复失败。",
                        exception);
                }
            }

            return SaveResult.Success();
        }

        #endregion

        #region 操作生命周期与辅助

        /// <summary>
        /// 检查管理器尚未释放。
        /// </summary>
        /// <exception cref="ObjectDisposedException">管理器已经注销时抛出。</exception>
        /// <exception cref="InvalidOperationException">Manager 尚未完成 Architecture 初始化时抛出。</exception>
        private void EnsureNotDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(SaveManager));
            }

            if (!Initialized || lifetimeCancellation == null)
            {
                throw new InvalidOperationException("SaveManager 尚未完成 BusinessArchitecture 初始化。");
            }
        }

        /// <summary>
        /// 尝试独占当前管理器的单一操作通道。
        /// </summary>
        /// <param name="cancellationToken">成功占用时返回生命周期令牌。</param>
        /// <param name="operationModuleRegistry">本次操作使用的模块注册快照。</param>
        /// <param name="operationMigrationRegistry">本次操作使用的迁移注册快照。</param>
        /// <param name="operationSnapshotTypeRegistry">本次操作使用的快照类型注册快照。</param>
        /// <returns>成功占用时返回 true。</returns>
        private bool TryBeginOperation(
            out CancellationToken cancellationToken,
            out SaveModuleRegistry operationModuleRegistry,
            out SaveMigrationRegistry operationMigrationRegistry,
            out SaveSnapshotTypeRegistry operationSnapshotTypeRegistry)
        {
            lock (lifecycleGate)
            {
                EnsureNotDisposed();
                if (activeOperation != 0)
                {
                    cancellationToken = default;
                    operationModuleRegistry = null;
                    operationMigrationRegistry = null;
                    operationSnapshotTypeRegistry = null;
                    return false;
                }

                activeOperation = 1;
                cancellationToken = lifetimeCancellation.Token;
                operationModuleRegistry = moduleRegistry;
                operationMigrationRegistry = migrationRegistry;
                operationSnapshotTypeRegistry = snapshotTypeRegistry.CreateSnapshot();
                return true;
            }
        }

        /// <summary>
        /// 释放当前操作通道。
        /// </summary>
        private void EndOperation()
        {
            lock (lifecycleGate)
            {
                activeOperation = 0;
                if (disposed != 0)
                {
                    DisposeLifetimeCancellation();
                }
            }
        }

        /// <summary>
        /// 在没有活动操作时释放生命周期取消源。
        /// </summary>
        private void DisposeLifetimeCancellation()
        {
            if (Interlocked.Exchange(ref cancellationDisposed, 1) == 0)
            {
                lifetimeCancellation?.Dispose();
            }
        }

        /// <summary>
        /// 检查模块或迁移仍可在当前架构生命周期内注册。
        /// </summary>
        /// <exception cref="ObjectDisposedException">Manager 已经注销时抛出。</exception>
        private void EnsureRegistrationAvailable()
        {
            if (disposed != 0)
            {
                throw new ObjectDisposedException(nameof(SaveManager));
            }
        }

        /// <summary>
        /// 注册快照类型并允许相同键重复指向同一 CLR 类型。
        /// </summary>
        /// <param name="moduleId">模块标识。</param>
        /// <param name="version">快照版本。</param>
        /// <param name="snapshotType">快照具体类型。</param>
        /// <exception cref="ArgumentException">同一模块版本已经指向其他类型时抛出。</exception>
        private void RegisterSnapshotType(
            SaveModuleId moduleId,
            int version,
            Type snapshotType)
        {
            SaveResult<Type> result = snapshotTypeRegistry.Resolve(moduleId, version);
            if (result.IsSuccess)
            {
                if (result.Value != snapshotType)
                {
                    throw new ArgumentException(
                        $"模块 {moduleId} v{version} 已注册其他快照类型：{result.Value.FullName}");
                }

                return;
            }

            snapshotTypeRegistry.Register(moduleId, version, snapshotType);
        }

        /// <summary>
        /// 发布操作完成事件，不改变调用方收到的主结果。
        /// </summary>
        /// <param name="kind">操作类型。</param>
        /// <param name="slotId">相关槽位。</param>
        /// <param name="result">操作结果。</param>
        private void Publish(SaveOperationKind kind, SaveSlotId slotId, SaveResult result)
        {
            OperationCompleted?.Invoke(new SaveOperationCompleted(kind, slotId, result));
        }

        /// <summary>
        /// 将泛型结果转换为完成事件使用的非泛型结果。
        /// </summary>
        /// <typeparam name="T">泛型结果值类型。</typeparam>
        /// <param name="result">待转换结果。</param>
        /// <returns>只保留成功状态和错误信息的结果。</returns>
        private static SaveResult ToBaseResult<T>(SaveResult<T> result)
        {
            return result.IsSuccess
                ? SaveResult.Success()
                : SaveResult.Failure(result.ErrorCode, result.Message, result.Exception);
        }

        /// <summary>
        /// 表示已经完成全部加载校验、等待模块恢复的数据。
        /// </summary>
        private sealed class PreparedLoad
        {
            /// <summary>
            /// 创建待恢复加载数据。
            /// </summary>
            /// <param name="summary">槽位摘要。</param>
            /// <param name="modules">当前版本模块数据。</param>
            /// <param name="migratedModules">迁移过的模块。</param>
            public PreparedLoad(
                SaveSlotSummary summary,
                IReadOnlyDictionary<SaveModuleId, SaveModuleData> modules,
                IReadOnlyList<SaveModuleId> migratedModules)
            {
                Summary = summary;
                Modules = modules;
                MigratedModules = migratedModules;
            }

            /// <summary>
            /// 获取槽位摘要。
            /// </summary>
            public SaveSlotSummary Summary { get; }

            /// <summary>
            /// 获取已经完成迁移和校验的模块数据。
            /// </summary>
            public IReadOnlyDictionary<SaveModuleId, SaveModuleData> Modules { get; }

            /// <summary>
            /// 获取迁移报告列表。
            /// </summary>
            public IReadOnlyList<SaveModuleId> MigratedModules { get; }
        }

        #endregion
    }
}
