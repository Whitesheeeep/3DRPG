using System.IO;
using RPG.CurrencySystem;
using RPG.Character;
using RPG.DialogueSystemModule;
using RPG.ItemSystem;
using RPG.RedDotSystemNS;
using RPG.SaveSystem;
using RPG.TaskSystem;
using RPG.Game.UI.Escape;
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
            SaveManager saveManager = new SaveManager(
                new SaveManagerOptions(serializer.FormatId, 1),
                storage,
                serializerRegistry,
                snapshotTypeRegistry);
            RegisterManager(saveManager);

            // 角色拥有模块先于装备 Manager 注册，使武器存档的恢复依赖始终可解析。
            CharacterRosterManager characterRosterManager = new CharacterRosterManager(saveManager);
            RegisterManager(characterRosterManager);

            // 业务配置由统一 Provider 建立类型索引；RedDotSystem 负责正式树的运行时组装和校验。
            BagRedDotConfig bagRedDotConfig =
                RedDotBusinessConfigProvider.GetConfig<BagRedDotConfig>();

            // 红点系统对象在 Manager 构造前创建并复用；实际 OnInit 会在所有 Manager 初始化后执行。
            var redDotSystem = new RedDotSystem();

            var itemDiscoveryManager = new ItemDiscoveryManager(saveManager);
            RegisterManager(itemDiscoveryManager);
            RegisterManager(new StackableInventoryManager(
                saveManager,
                itemDiscoveryManager,
                redDotSystem,
                bagRedDotConfig.DevelopmentExperienceItemNewKey,
                bagRedDotConfig.FoodNewKey,
                bagRedDotConfig.DevelopmentItemNewKey));
            RegisterSystem(redDotSystem);

            RegisterManager(new WeaponInventoryManager(
                saveManager,
                characterRosterManager,
                itemDiscoveryManager,
                redDotSystem,
                bagRedDotConfig.WeaponNewKey));
            RegisterManager(new ArtifactInventoryManager(
                saveManager,
                itemDiscoveryManager,
                redDotSystem,
                bagRedDotConfig.ArtifactNewKey));

            // TaskManager 由 WSFrame ConfigInstaller 注入 TaskDatabase；
            // TaskProgressSystem 只协调任务实例运行时，并在初始化时注册 TaskSaveModule。
            RegisterSystem(new TaskProgressSystem(new TaskObjectiveHandlerRegistry()));
            RegisterSystem(new DialogueSystem());
            RegisterSystem(new CharacterEquipmentSystem());
            RegisterSystem(new RPG.CurrencySystem.CurrencySystem());
            RegisterManager(new EscCommandManager());

            // 角色、背包等跨业务模块在这里继续注册；各 Manager 在自身 OnInit 中注册 SaveModule。
            TaskSaveModule taskSaveModule = new TaskSaveModule(TaskManager.Instance);
            snapshotTypeRegistry.Register<TaskSaveSnapshot>(taskSaveModule.ModuleId, taskSaveModule.CurrentVersion);
            snapshotTypeRegistry.Register<CharacterRosterSaveSnapshot>(CharacterRosterSaveModule.StableModuleId, 1);
            snapshotTypeRegistry.Register<ItemDiscoverySaveSnapshot>(ItemDiscoverySaveModule.StableModuleId, 1);
            snapshotTypeRegistry.Register<StackableInventorySaveSnapshot>(new SaveModuleId("stackable-inventory"), 1);
            snapshotTypeRegistry.Register<WeaponInventorySaveSnapshot>(new SaveModuleId("weapon-inventory"), 1);
            snapshotTypeRegistry.Register<ArtifactInventorySaveSnapshot>(new SaveModuleId("artifact-inventory"), 1);
            snapshotTypeRegistry.Register<CurrencySaveSnapshot>(new SaveModuleId("currency"), 1);
            #endregion
        }
    }
}
