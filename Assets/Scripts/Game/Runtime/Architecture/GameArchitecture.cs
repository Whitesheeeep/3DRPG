using System.IO;
using RPG.DialogueSystemModule;
using RPG.SaveSystem;
using RPG.TaskSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;

namespace RPG.Game
{
    /// <summary>
    /// RPG 项目的业务架构入口，统一持有 SaveManager 和后续业务 Manager/System。
    /// </summary>
    public sealed class GameArchitecture : Architecture<GameArchitecture>
    {
        /// <summary>
        /// 注册项目级存档 Manager 及未来扩展的业务模块。
        /// </summary>
        protected override void Init()
        {
            #region 存档系统
            var serializer = new NewtonsoftJsonSaveSerializer();
            var storage = new LocalFileSaveStorage(
                Path.Combine(
                    Application.persistentDataPath,
                    SaveStorageDefaults.LocalDirectoryName));

            // 注册存档相关组件
            var serializerRegistry = new SaveSerializerRegistry(
                new ISaveSerializer[] { serializer });
            var snapshotTypeRegistry = new SaveSnapshotTypeRegistry();

            // SaveManager 作为同一业务架构中的 Manager 注册，后续 System 可通过 GetManager 获取。
            RegisterManager(new SaveManager(
                new SaveManagerOptions(serializer.FormatId, 1),
                storage,
                serializerRegistry,
                snapshotTypeRegistry));

            // TaskManager 由 WSFrame ConfigInstaller 注入 TaskDatabase；
            // TaskProgressSystem 只协调任务实例运行时，并在初始化时注册 TaskSaveModule。
            RegisterSystem(new TaskProgressSystem(new TaskObjectiveHandlerRegistry()));
            RegisterSystem(new DialogueSystem());

            // 角色、背包等跨业务 System 在这里继续注册；
            // 它们对应的 SaveModule 由各自 System 在 OnInit 中注册到 SaveManager。
            TaskSaveModule taskSaveModule = new TaskSaveModule(TaskManager.Instance);
            snapshotTypeRegistry.Register<TaskSaveSnapshot>(taskSaveModule.ModuleId, taskSaveModule.CurrentVersion);
            #endregion
        }
    }
}
