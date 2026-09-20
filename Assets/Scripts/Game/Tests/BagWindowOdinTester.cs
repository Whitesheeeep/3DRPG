#if UNITY_EDITOR
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.CurrencySystem;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Escape;
using RPG.Game.UI.WeaponDevelopment;
using RPG.ItemSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.UIModule;

namespace RPG.Game.Tests
{
    /// <summary>
    /// 通过正式装备、堆叠库存和货币 API 生成可追踪测试数据，并验证 BagWindow 与装备培养窗口的事件联动。
    /// </summary>
    public sealed class BagWindowOdinTester : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required, LabelText("测试武器定义")] private WeaponDefinition testWeapon;
        [SerializeField, LabelText("测试圣遗物定义")] private ArtifactDefinition testArtifact;
        [SerializeField, LabelText("测试食物定义")] private FoodItemDefinition testFood;
        [SerializeField, LabelText("测试武器经验素材")] private DevelopmentExperienceItemDefinition[] testEnhancementMaterials = new DevelopmentExperienceItemDefinition[0];
        [SerializeField, LabelText("测试圣遗物经验素材")] private DevelopmentExperienceItemDefinition testArtifactExperienceMaterial;
        [SerializeField, LabelText("测试武器突破素材")] private DevelopmentItemDefinition testAscensionMaterial;

        #endregion

        #region 测试参数

        [SerializeField, MinValue(1), LabelText("批量添加数量")] private int batchQuantity = 8;
        [SerializeField, MinValue(1), LabelText("圣遗物添加数量")] private int artifactQuantity = 3;
        [SerializeField, MinValue(1), LabelText("食物添加数量")] private int foodQuantity = 5;
        [SerializeField, MinValue(1), LabelText("每种经验素材添加数量")] private int enhancementMaterialQuantity = 20;
        [SerializeField, MinValue(1), LabelText("圣遗物经验素材添加数量")] private int artifactExperienceMaterialQuantity = 20;
        [SerializeField, MinValue(1), LabelText("突破素材添加数量")] private int ascensionMaterialQuantity = 3;
        [SerializeField, MinValue(1), LabelText("测试摩拉添加数量")] private int testMolaAmount = 100000;
        [SerializeField, MinValue(1), LabelText("目标等级")] private int targetLevel = 20;
        [SerializeField, MinValue(0), LabelText("当前经验")] private int targetExperience;
        [SerializeField, MinValue(0), LabelText("目标突破阶数")] private int targetAscensionRank;
        [SerializeField, MinValue(1), LabelText("目标精炼阶数")] private int targetRefinementRank = 2;

        #endregion

        #region 运行时测试状态

        // 只记录本组件创建的实例，清理时不会触碰玩家原有库存。
        private readonly List<EquipmentInstanceId> createdInstanceIds = new();
        private readonly List<EquipmentInstanceId> createdArtifactInstanceIds = new();
        private EquipmentInstanceId? latestInstanceId;
        private EquipmentInstanceId? latestArtifactInstanceId;

        #endregion

        #region 架构依赖访问

        /// <summary>获取当前架构持有的武器库存 Manager；不跨 Play Mode 缓存实例。</summary>
        private WeaponInventoryManager WeaponManager =>
            RPG.Game.GameArchitecture.Interface.GetManager<WeaponInventoryManager>();

        /// <summary>获取当前架构持有的圣遗物库存 Manager；不跨 Play Mode 缓存实例。</summary>
        private ArtifactInventoryManager ArtifactManager =>
            RPG.Game.GameArchitecture.Interface.GetManager<ArtifactInventoryManager>();

        /// <summary>获取当前架构持有的可堆叠库存 Manager；不跨 Play Mode 缓存实例。</summary>
        private StackableInventoryManager StackableManager =>
            RPG.Game.GameArchitecture.Interface.GetManager<StackableInventoryManager>();

        /// <summary>获取当前架构持有的物品发现 Manager；不跨 Play Mode 缓存实例。</summary>
        private ItemDiscoveryManager DiscoveryManager =>
            RPG.Game.GameArchitecture.Interface.GetManager<ItemDiscoveryManager>();

        #endregion

        #region 窗口与添加操作

        /// <summary>通过正式圣遗物库存 API 添加测试实例并记录实例标识。</summary>
        [Button("添加测试圣遗物")]
        public void AddTestArtifact()
        {
            if (!TryGetTestArtifact(out ArtifactDefinition definition) || !EnsureInventoryReady("添加圣遗物"))
                return;
            if (!ArtifactInventoryManager.IsConfigured)
            {
                Debug.LogError("[BagWindowTest] 圣遗物库存尚未配置，不能添加测试圣遗物。", this);
                return;
            }

            ItemId[] definitionIds = new ItemId[Mathf.Max(1, artifactQuantity)];
            for (int index = 0; index < definitionIds.Length; index++) definitionIds[index] = definition.ItemId;
            EquipmentBatchAddResult<ArtifactInstance> result = ArtifactManager.AddArtifacts(definitionIds);
            Debug.Log($"[BagWindowTest] add artifact status={result.Status}, count={result.Instances.Count}。", this);
            if (!result.Succeeded) return;
            for (int index = 0; index < result.Instances.Count; index++)
                createdArtifactInstanceIds.Add(result.Instances[index].InstanceId);
            if (result.Instances.Count > 0)
                latestArtifactInstanceId = result.Instances[result.Instances.Count - 1].InstanceId;
        }

        /// <summary>通过正式可堆叠库存 API 添加测试食物。</summary>
        [Button("添加测试食物")]
        public void AddTestFood()
        {
            if (!TryGetTestFood(out FoodItemDefinition definition) || !EnsureInventoryReady("添加食物")) return;

            StackableItemOperationResult result = StackableManager.AddItem(
                definition.ItemId, foodQuantity);
            Debug.Log($"[BagWindowTest] add food status={result.Status}, item={definition.ItemId}, quantity={foodQuantity}。", this);
        }

        /// <summary>一次添加全部非武器分类的测试数据，便于验证分类切换和通用详情。</summary>
        [Button("添加所有非武器分类测试数据")]
        public void AddAllNonWeaponTestData()
        {
            AddTestArtifact();
            AddTestFood();
            AddTestDevelopmentMaterials();
        }

        /// <summary>通过正式背包切换事件打开或关闭 BagWindow。</summary>
        [Button("打开/关闭背包")]
        public void ToggleBagWindow()
        {
            if (!UIManager.Instance.IsInitialized)
            {
                Debug.LogError("[BagWindowTest] UIManager 尚未初始化，不能切换 BagWindow。", this);
                return;
            }

            if (UIManager.Instance.TryGetWindow<BagWindow>(out BagWindow bagWindow) && bagWindow.Visible)
            {
                RPG.Game.GameArchitecture.Interface.SendCommand(new CloseBagWindowCommand());
                return;
            }

            EventSystem.EventTrigger_Type(
                typeof(BagWindowOpenRequestedEventArgs),
                new BagWindowOpenRequestedEventArgs(BagWindowRequestSource.Tester));
        }

        /// <summary>通过 UIManager 的 OpenContext 打开最近创建武器的培养窗口。</summary>
        [Button("打开最近创建武器培养窗口")]
        public void OpenLatestEquipmentDevelopmentWindow()
        {
            if (!UIManager.Instance.IsInitialized)
            {
                Debug.LogError("[BagWindowTest] UIManager 尚未初始化，不能打开装备培养窗口。", this);
                return;
            }

            if (latestArtifactInstanceId.HasValue && ArtifactManager.TryGetInstance(latestArtifactInstanceId.Value,
                    out ArtifactInstance artifact))
            {
                UIManager.Instance.PopUpWindowAsync<EquipmentDevelopmentWindow, EquipmentDevelopmentOpenContext>(
                    EquipmentDevelopmentOpenContext.ForArtifact(artifact.InstanceId)).Forget();
                return;
            }

            if (!TryGetLatestInstance(out WeaponInstance instance)) return;
            UIManager.Instance.PopUpWindowAsync<EquipmentDevelopmentWindow, EquipmentDevelopmentOpenContext>(
                EquipmentDevelopmentOpenContext.ForWeapon(instance.InstanceId)).Forget();
        }

        /// <summary>显式打开最近创建的圣遗物统一培养窗口。</summary>
        [Button("打开最近圣遗物培养窗口")]
        public void OpenLatestArtifactDevelopmentWindow()
        {
            if (!latestArtifactInstanceId.HasValue || !ArtifactManager.TryGetInstance(latestArtifactInstanceId.Value,
                    out ArtifactInstance instance))
            {
                Debug.LogWarning("[BagWindowTest] 当前没有本 Tester 创建的最近圣遗物。", this);
                return;
            }

            if (!UIManager.Instance.IsInitialized)
            {
                Debug.LogError("[BagWindowTest] UIManager 尚未初始化，不能打开圣遗物培养窗口。", this);
                return;
            }

            UIManager.Instance.PopUpWindowAsync<EquipmentDevelopmentWindow, EquipmentDevelopmentOpenContext>(
                EquipmentDevelopmentOpenContext.ForArtifact(instance.InstanceId)).Forget();
        }

        /// <summary>调用真实武器库存 API 添加一把测试武器并记录其实例标识。</summary>
        [Button("添加一把测试武器")]
        public void AddTestWeapon()
        {
            if (!TryGetTestWeapon(out WeaponDefinition definition) || !EnsureInventoryReady("添加武器")) return;

            EquipmentAddResult<WeaponInstance> result = WeaponManager.AddWeapon(definition.ItemId);
            Debug.Log($"[BagWindowTest] add status={result.Status}, definition={definition.ItemId}。", this);
            if (!result.Succeeded) return;

            RememberCreatedInstance(result.Instance);
        }

        /// <summary>调用真实批量添加 API，生成多条可供虚拟网格和排序测试的武器实例。</summary>
        [Button("批量添加测试武器")]
        public void AddBatchTestWeapons()
        {
            if (!TryGetTestWeapon(out WeaponDefinition definition) || !EnsureInventoryReady("批量添加武器")) return;

            ItemId[] definitionIds = new ItemId[Mathf.Max(1, batchQuantity)];
            for (int index = 0; index < definitionIds.Length; index++) definitionIds[index] = definition.ItemId;

            EquipmentBatchAddResult<WeaponInstance> result = WeaponManager.AddWeapons(definitionIds);
            Debug.Log($"[BagWindowTest] batchAdd status={result.Status}, count={result.Instances.Count}。", this);
            if (!result.Succeeded) return;

            for (int index = 0; index < result.Instances.Count; index++) RememberCreatedInstance(result.Instances[index]);
        }

        /// <summary>向正式堆叠背包添加测试用武器经验和突破素材。</summary>
        [Button("添加测试培养素材")]
        public void AddTestDevelopmentMaterials()
        {
            if (!EnsureInventoryReady("添加培养素材")) return;

            int addedKinds = 0;
            if (testEnhancementMaterials != null)
            {
                for (int index = 0; index < testEnhancementMaterials.Length; index++)
                {
                    DevelopmentExperienceItemDefinition material = testEnhancementMaterials[index];
                    if (!IsExperienceMaterial(material, DevelopmentExperienceItemType.Weapon)) continue;
                    StackableItemOperationResult result = StackableManager.AddItem(
                        material.ItemId, enhancementMaterialQuantity);
                    Debug.Log($"[BagWindowTest] add enhancement material status={result.Status}, item={material.ItemId}, " +
                              $"quantity={enhancementMaterialQuantity}。", this);
                    if (result.Succeeded) addedKinds++;
                }
            }

            if (IsDevelopmentMaterial(testAscensionMaterial, DevelopmentItemType.WeaponAscension))
            {
                StackableItemOperationResult result = StackableManager.AddItem(
                    testAscensionMaterial.ItemId, ascensionMaterialQuantity);
                Debug.Log($"[BagWindowTest] add ascension material status={result.Status}, item={testAscensionMaterial.ItemId}, " +
                          $"quantity={ascensionMaterialQuantity}。", this);
                if (result.Succeeded) addedKinds++;
            }

            Debug.Log($"[BagWindowTest] 培养素材添加完成：成功种类={addedKinds}。", this);
        }

        /// <summary>向正式堆叠背包添加支持圣遗物升级的经验素材。</summary>
        [Button("添加圣遗物经验素材")]
        public void AddTestArtifactExperienceMaterial()
        {
            if (!EnsureInventoryReady("添加圣遗物经验素材") ||
                !IsExperienceMaterial(testArtifactExperienceMaterial, DevelopmentExperienceItemType.Artifact)) return;

            StackableItemOperationResult result = StackableManager.AddItem(
                testArtifactExperienceMaterial.ItemId, artifactExperienceMaterialQuantity);
            Debug.Log($"[BagWindowTest] add artifact experience status={result.Status}, " +
                      $"item={testArtifactExperienceMaterial.ItemId}, quantity={artifactExperienceMaterialQuantity}。", this);
        }

        /// <summary>一次准备圣遗物升级所需经验素材和摩拉。</summary>
        [Button("添加圣遗物培养测试资源")]
        public void AddAllArtifactDevelopmentTestResources()
        {
            // 先创建可立即打开的测试实例，再补充该会话所需的经验素材和摩拉。
            AddTestArtifact();
            AddTestArtifactExperienceMaterial();
            AddTestMola();
        }

        /// <summary>通过正式货币钱包 API 增加测试摩拉。</summary>
        [Button("添加测试摩拉")]
        public void AddTestMola()
        {
            CurrencyManager currencyManager = CurrencyManager.Instance;
            int previousBalance = currencyManager.GetBalance(CurrencyId.Mola);
            CurrencyOperationResult result = currencyManager.AddCurrencies(
                new[] { new CurrencyAmount(CurrencyId.Mola, testMolaAmount) });
            int currentBalance = currencyManager.GetBalance(CurrencyId.Mola);
            Debug.Log(
                $"[BagWindowTest] add currency status={result.Status}, currency={CurrencyId.Mola}, " +
                $"requested={testMolaAmount}, previous={previousBalance}, current={currentBalance}, " +
                $"errorCurrency={result.CurrencyId}。", this);
        }

        /// <summary>一次准备武器升级、突破所需的经验素材、突破素材和摩拉。</summary>
        [Button("添加武器培养测试资源")]
        public void AddAllWeaponDevelopmentTestResources()
        {
            AddTestDevelopmentMaterials();
            AddTestMola();
        }

        #endregion

        #region 状态修改与移除

        /// <summary>只清理本组件记录且仍可移除的测试圣遗物实例。</summary>
        [Button("清理本 Tester 创建的圣遗物")]
        public void ClearCreatedArtifacts()
        {
            if (!EnsureInventoryReady("清理测试圣遗物") || !ArtifactInventoryManager.IsConfigured) return;

            IReadOnlyList<EquipmentInstanceId> snapshot = new List<EquipmentInstanceId>(createdArtifactInstanceIds);
            for (int index = 0; index < snapshot.Count; index++)
            {
                EquipmentInstanceId instanceId = snapshot[index];
                if (!ArtifactManager.TryGetInstance(instanceId, out ArtifactInstance instance))
                {
                    createdArtifactInstanceIds.Remove(instanceId);
                    continue;
                }

                EquipmentOperationResult result = ArtifactManager.RemoveArtifact(instanceId);
                Debug.Log($"[BagWindowTest] cleanup artifact status={result.Status}, instance={instanceId}。", this);
                if (result.Succeeded)
                {
                    createdArtifactInstanceIds.Remove(instanceId);
                    if (latestArtifactInstanceId == instanceId)
                        latestArtifactInstanceId = createdArtifactInstanceIds.Count == 0
                            ? (EquipmentInstanceId?)null
                            : createdArtifactInstanceIds[createdArtifactInstanceIds.Count - 1];
                }
            }
        }

        /// <summary>更新最近创建实例的成长数据，验证列表与当前详情同时刷新。</summary>
        [Button("更新最近创建的武器")]
        public void UpdateLatestWeapon()
        {
            if (!TryGetLatestInstance(out WeaponInstance instance) || !EnsureInventoryReady("更新武器")) return;

            WeaponProgressUpdate update = new WeaponProgressUpdate(
                targetLevel,
                targetExperience,
                targetAscensionRank,
                targetRefinementRank);
            EquipmentOperationResult result = WeaponManager.UpdateWeaponProgress(instance.InstanceId, update);
            Debug.Log($"[BagWindowTest] update status={result.Status}, instance={instance.InstanceId}。", this);
        }

        /// <summary>切换最近创建实例的锁定状态，验证锁定图标和移除约束。</summary>
        [Button("切换最近创建武器锁定")]
        public void ToggleLatestWeaponLock()
        {
            if (!TryGetLatestInstance(out WeaponInstance instance) || !EnsureInventoryReady("切换武器锁定")) return;

            EquipmentOperationResult result = WeaponManager.SetLocked(instance.InstanceId, !instance.IsLocked);
            Debug.Log($"[BagWindowTest] setLocked status={result.Status}, locked={!instance.IsLocked}, instance={instance.InstanceId}。", this);
        }

        /// <summary>确认最近创建实例所属 Definition 的新获得状态，验证共享 New 标记刷新。</summary>
        [Button("确认最近创建武器")]
        public void AcknowledgeLatestWeapon()
        {
            if (!TryGetLatestInstance(out WeaponInstance instance) || !EnsureInventoryReady("确认武器新获得状态")) return;

            EquipmentOperationResult result = WeaponManager.AcknowledgeNew(instance.InstanceId);
            Debug.Log(
                $"[BagWindowTest] acknowledge status={result.Status}, definition={instance.DefinitionId}, instance={instance.InstanceId}。", this);
        }

        /// <summary>移除最近创建的实例，不自动解锁或绕过 Manager 的业务约束。</summary>
        [Button("移除最近创建武器")]
        public void RemoveLatestWeapon()
        {
            if (!TryGetLatestInstance(out WeaponInstance instance) || !EnsureInventoryReady("移除武器")) return;

            EquipmentOperationResult result = WeaponManager.RemoveWeapon(instance.InstanceId);
            Debug.Log($"[BagWindowTest] remove status={result.Status}, instance={instance.InstanceId}。", this);
            if (!result.Succeeded) return;

            ForgetCreatedInstance(instance.InstanceId);
        }

        /// <summary>只清理本组件记录且仍可移除的测试实例。</summary>
        [Button("清理本 Tester 创建的武器")]
        public void ClearCreatedWeapons()
        {
            if (!EnsureInventoryReady("清理测试武器")) return;

            IReadOnlyList<EquipmentInstanceId> snapshot = new List<EquipmentInstanceId>(createdInstanceIds);
            for (int index = 0; index < snapshot.Count; index++)
            {
                EquipmentInstanceId instanceId = snapshot[index];
                if (!WeaponManager.TryGetInstance(instanceId, out WeaponInstance instance))
                {
                    ForgetCreatedInstance(instanceId);
                    continue;
                }

                EquipmentOperationResult result = WeaponManager.RemoveWeapon(instanceId);
                Debug.Log($"[BagWindowTest] cleanup status={result.Status}, instance={instanceId}。", this);
                if (result.Succeeded) ForgetCreatedInstance(instanceId);
            }
        }

        #endregion

        #region 诊断

        /// <summary>输出当前武器库存快照，辅助核对 Manager 状态和背包显示。</summary>
        [Button("输出武器库存快照")]
        public void LogWeaponSnapshot()
        {
            if (!EnsureInventoryReady("输出武器库存")) return;

            WeaponInventoryManager manager = WeaponManager;
            ItemDiscoveryManager discoveryManager = DiscoveryManager;
            IReadOnlyList<WeaponInstance> instances = manager.GetInstances();
            IReadOnlyList<ItemId> newDefinitionIds = manager.GetNewDefinitionIds();
            IReadOnlyList<ItemId> discoveredDefinitionIds = discoveryManager.GetDiscoveredDefinitionIds();
            Debug.Log(
                $"[BagWindowTest] inventory total={manager.TotalCount}, stored={manager.StoredCount}/{manager.Capacity}, " +
                $"equipped={manager.EquippedCount}, tracked={createdInstanceIds.Count}, " +
                $"newDefinitions={newDefinitionIds.Count} [{string.Join(", ", newDefinitionIds)}], " +
                $"discoveredDefinitions={discoveredDefinitionIds.Count} [{string.Join(", ", discoveredDefinitionIds)}]。", this);
            for (int index = 0; index < instances.Count; index++)
            {
                WeaponInstance instance = instances[index];
                Debug.Log(
                    $"[BagWindowTest] instance={instance.InstanceId}, definition={instance.DefinitionId}, level={instance.Level}, " +
                    $"refinement={instance.RefinementRank}, locked={instance.IsLocked}, " +
                    $"definitionNew={manager.IsDefinitionNew(instance.DefinitionId)}, " +
                    $"isDiscovered={discoveryManager.IsDiscovered(instance.DefinitionId)}, " +
                    $"equipped={GameArchitecture.Interface.GetManager<CharacterRosterManager>().IsEquipmentEquipped(instance.InstanceId)}。",
                    this);
            }
        }

        #endregion

        #region 前置条件与记录辅助

        /// <summary>检查测试圣遗物引用和圣遗物分类契约。</summary>
        /// <param name="definition">通过检查的圣遗物定义。</param>
        /// <returns>配置有效时返回 true。</returns>
        private bool TryGetTestArtifact(out ArtifactDefinition definition)
        {
            definition = testArtifact;
            if (definition == null)
            {
                Debug.LogError("[BagWindowTest] 请先在 Inspector 绑定测试圣遗物定义。", this);
                return false;
            }

            if (!definition.ItemId.IsValid || definition.Category != ItemCategory.Artifact)
            {
                Debug.LogError("[BagWindowTest] 测试圣遗物 Definition 的 ItemId 或分类无效。", this);
                return false;
            }

            return true;
        }

        /// <summary>检查测试食物引用和食物分类契约。</summary>
        /// <param name="definition">通过检查的食物定义。</param>
        /// <returns>配置有效时返回 true。</returns>
        private bool TryGetTestFood(out FoodItemDefinition definition)
        {
            definition = testFood;
            if (definition == null)
            {
                Debug.LogError("[BagWindowTest] 请先在 Inspector 绑定测试食物定义。", this);
                return false;
            }

            if (!definition.ItemId.IsValid || definition.Category != ItemCategory.Food)
            {
                Debug.LogError("[BagWindowTest] 测试食物 Definition 的 ItemId 或分类无效。", this);
                return false;
            }

            return true;
        }

        /// <summary>检查测试武器引用和武器分类契约。</summary>
        /// <param name="definition">通过检查的武器定义。</param>
        /// <returns>配置有效时返回 true。</returns>
        private bool TryGetTestWeapon(out WeaponDefinition definition)
        {
            definition = testWeapon;
            if (definition == null)
            {
                Debug.LogError("[BagWindowTest] 请先在 Inspector 绑定测试武器定义。", this);
                return false;
            }

            if (!definition.ItemId.IsValid || definition.Category != ItemCategory.Weapon)
            {
                Debug.LogError("[BagWindowTest] 测试武器 Definition 的 ItemId 或分类无效。", this);
                return false;
            }

            return true;
        }

        /// <summary>检查一个培养素材是否属于指定武器培养用途。</summary>
        /// <param name="material">待检查的素材定义。</param>
        /// <param name="type">期望的培养用途。</param>
        /// <returns>定义存在且用途匹配时返回 true。</returns>
        private static bool IsDevelopmentMaterial(DevelopmentItemDefinition material, DevelopmentItemType type)
        {
            return material != null && material.ItemId.IsValid && material.Category == ItemCategory.DevelopmentItem &&
                   material.SupportsDevelopmentType(type);
        }

        /// <summary>检查一个养成经验道具是否支持指定成长对象。</summary>
        /// <param name="material">待检查经验道具。</param>
        /// <param name="type">期望成长对象。</param>
        /// <returns>定义存在且用途匹配时返回 true。</returns>
        private static bool IsExperienceMaterial(DevelopmentExperienceItemDefinition material,
            DevelopmentExperienceItemType type)
        {
            return material != null && material.ItemId.IsValid &&
                   material.Category == ItemCategory.DevelopmentExperienceItem &&
                   material.SupportsExperienceType(type);
        }

        /// <summary>检查正式配置是否已经安装，避免 Tester 创建备用库存或数据库。</summary>
        /// <param name="operationName">当前操作名称。</param>
        /// <returns>正式配置已就绪时返回 true。</returns>
        private bool EnsureInventoryReady(string operationName)
        {
            if (!WeaponInventoryManager.IsConfigured)
            {
                Debug.LogError($"[BagWindowTest] {operationName}前置条件不满足：WeaponInventoryManager 尚未配置。", this);
                return false;
            }

            if (!ItemManager.Instance.IsConfigured)
            {
                Debug.LogError($"[BagWindowTest] {operationName}前置条件不满足：ItemManager 尚未配置。", this);
                return false;
            }

            return true;
        }

        /// <summary>获取最近创建且仍存在于库存中的实例。</summary>
        /// <param name="instance">找到的武器实例。</param>
        /// <returns>存在可操作实例时返回 true。</returns>
        private bool TryGetLatestInstance(out WeaponInstance instance)
        {
            instance = null;
            if (!latestInstanceId.HasValue)
            {
                Debug.LogWarning("[BagWindowTest] 当前没有本 Tester 创建的最近武器。", this);
                return false;
            }

            if (!WeaponInventoryManager.IsConfigured || !WeaponManager.TryGetInstance(latestInstanceId.Value, out instance))
            {
                Debug.LogWarning("[BagWindowTest] 最近创建的武器已经不在库存中。", this);
                return false;
            }

            return true;
        }

        /// <summary>记录新增实例并把它设为后续操作目标。</summary>
        /// <param name="instance">新增武器实例。</param>
        private void RememberCreatedInstance(WeaponInstance instance)
        {
            createdInstanceIds.Add(instance.InstanceId);
            latestInstanceId = instance.InstanceId;
        }

        /// <summary>从 Tester 追踪列表移除已删除实例并更新最近目标。</summary>
        /// <param name="instanceId">已删除实例标识。</param>
        private void ForgetCreatedInstance(EquipmentInstanceId instanceId)
        {
            createdInstanceIds.Remove(instanceId);
            latestInstanceId = createdInstanceIds.Count == 0
                ? (EquipmentInstanceId?)null
                : createdInstanceIds[createdInstanceIds.Count - 1];
        }

        #endregion
    }
}
#endif
