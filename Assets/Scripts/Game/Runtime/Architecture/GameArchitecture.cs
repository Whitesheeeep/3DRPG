using System.IO;
using RPG.CurrencySystemNS;
using RPG.Character;
using RPG.DialogueSystemModule;
using RPG.ItemSystem;
using RPG.RedDotSystemNS;
using RPG.RewardSystemNS;
using RPG.SaveSystem;
using RPG.TaskSystemNS;
using RPG.Game.Loading;
using RPG.Game.UI.Escape;
using RPG.Game.UI.Loading;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.SceneModule;

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

            // 角色实例先于装备 Manager 注册，后续角色装备关系存档会在三类实例存档之后恢复。
            CharacterRosterManager characterRosterManager = new CharacterRosterManager(saveManager);
            RegisterManager(characterRosterManager);
            CharacterPartyManager characterPartyManager = new CharacterPartyManager(saveManager, characterRosterManager);
            RegisterManager(characterPartyManager);

            // 业务配置由统一 Provider 建立类型索引；RedDotSystem 负责正式树的运行时组装和校验。
            BagRedDotConfig bagRedDotConfig =
                RedDotBusinessConfigProvider.GetConfig<BagRedDotConfig>();
            TaskRedDotConfig taskRedDotConfig =
                RedDotBusinessConfigProvider.GetConfig<TaskRedDotConfig>();

            // 红点系统对象在 Manager 构造前创建并复用；实际 OnInit 会在所有 Manager 初始化后执行。
            var redDotSystem = new RedDotSystem();

            var itemDiscoveryManager = new ItemDiscoveryManager(saveManager);
            RegisterManager(itemDiscoveryManager);
            var stackableInventoryManager = new StackableInventoryManager(
                saveManager,
                itemDiscoveryManager,
                redDotSystem,
                bagRedDotConfig.DevelopmentExperienceItemNewKey,
                bagRedDotConfig.FoodNewKey,
                bagRedDotConfig.DevelopmentItemNewKey);
            RegisterManager(stackableInventoryManager);
            RegisterSystem(redDotSystem);

            var weaponInventoryManager = new WeaponInventoryManager(
                saveManager,
                characterRosterManager,
                itemDiscoveryManager,
                redDotSystem,
                bagRedDotConfig.WeaponNewKey);
            RegisterManager(weaponInventoryManager);
            var artifactInventoryManager = new ArtifactInventoryManager(
                saveManager,
                characterRosterManager,
                itemDiscoveryManager,
                redDotSystem,
                bagRedDotConfig.ArtifactNewKey);
            RegisterManager(artifactInventoryManager);

            // 钱包和库存 Manager 先完成注册，再装配统一奖励 Handler。
            RegisterSystem(new CurrencySystem());
            var rewardHandlerRegistry = new RewardHandlerRegistry();
            rewardHandlerRegistry.RegisterDefault(
                new CurrencyRewardHandler(CurrencyManager.Instance),
                new ItemRewardHandler(
                    stackableInventoryManager,
                    weaponInventoryManager,
                    artifactInventoryManager));
            var rewardSystem = new RewardSystem(rewardHandlerRegistry);
            RegisterSystem(rewardSystem);

            // 任务定义由 ConfigInstaller 注入；默认 Handler 由各领域注册表集中登记。
            var taskObjectiveHandlerRegistry = new TaskObjectiveHandlerRegistry();
            taskObjectiveHandlerRegistry.RegisterDefault();
            var taskConditionHandlerRegistry = new TaskConditionHandlerRegistry();
            taskConditionHandlerRegistry.RegisterDefault();
            var taskSystem = new TaskSystem(
                taskObjectiveHandlerRegistry,
                taskConditionHandlerRegistry,
                rewardSystem,
                redDotSystem,
                taskRedDotConfig);
            RegisterSystem(taskSystem);
            RegisterSystem(new DialogueSystem());
            RegisterSystem(new CharacterEquipmentSystem());
            RegisterSystem(new SceneLoadingSystem(new GameSceneLoadingPresentation()));
            RegisterSystem(new GameSceneFlowSystem());
            RegisterManager(new EscCommandManager());

            // 角色、背包等跨业务模块在这里继续注册；各 Manager 在自身 OnInit 中注册 SaveModule。
            snapshotTypeRegistry.Register<TaskSaveSnapshot>(TaskSaveModule.StableModuleId, 2);
            snapshotTypeRegistry.Register<CharacterRosterSaveSnapshot>(CharacterRosterSaveModule.StableModuleId, 2);
            snapshotTypeRegistry.Register<CharacterPartySaveSnapshot>(CharacterPartySaveModule.StableModuleId, 1);
            snapshotTypeRegistry.Register<ItemDiscoverySaveSnapshot>(ItemDiscoverySaveModule.StableModuleId, 1);
            snapshotTypeRegistry.Register<StackableInventorySaveSnapshot>(StackableInventorySaveModule.StableModuleId, 1);
            snapshotTypeRegistry.Register<WeaponInventorySaveSnapshot>(WeaponInventorySaveModule.StableModuleId, 1);
            snapshotTypeRegistry.Register<ArtifactInventorySaveSnapshot>(ArtifactInventorySaveModule.StableModuleId, 1);
            snapshotTypeRegistry.Register<CharacterEquipmentSaveSnapshot>(CharacterEquipmentSaveModule.StableModuleId, 1);
            snapshotTypeRegistry.Register<CurrencySaveSnapshot>(CurrencySaveModule.StableModuleId, 1);
            #endregion
        }
    }
}
