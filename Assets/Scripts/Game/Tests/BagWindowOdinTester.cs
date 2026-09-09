#if UNITY_EDITOR
using System.Collections.Generic;
using RPG.Game.UI.Bag;
using RPG.ItemSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.UIModule;

namespace RPG.Game.Tests
{
    /// <summary>
    /// 通过正式武器库存 API 生成可追踪测试数据，并验证 BagWindow 的事件到 View 联动。
    /// </summary>
    public sealed class BagWindowOdinTester : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required, LabelText("测试武器定义")] private WeaponDefinition testWeapon;

        #endregion

        #region 测试参数

        [SerializeField, MinValue(1), LabelText("批量添加数量")] private int batchQuantity = 8;
        [SerializeField, MinValue(1), LabelText("目标等级")] private int targetLevel = 20;
        [SerializeField, MinValue(0), LabelText("当前经验")] private int targetExperience;
        [SerializeField, MinValue(0), LabelText("目标突破阶数")] private int targetAscensionRank;
        [SerializeField, MinValue(1), LabelText("目标精炼阶数")] private int targetRefinementRank = 2;

        #endregion

        #region 运行时测试状态

        // 只记录本组件创建的实例，清理时不会触碰玩家原有库存。
        private readonly List<EquipmentInstanceId> createdInstanceIds = new();
        private EquipmentInstanceId? latestInstanceId;

        #endregion

        #region 窗口与添加操作

        /// <summary>通过正式背包切换事件打开或关闭 BagWindow。</summary>
        [Button("打开/关闭背包")]
        public void ToggleBagWindow()
        {
            if (!UIManager.Instance.IsInitialized)
            {
                Debug.LogError("[BagWindowTest] UIManager 尚未初始化，不能切换 BagWindow。", this);
                return;
            }

            EventSystem.EventTrigger_Type(
                typeof(BagWindowToggleRequestedEventArgs),
                new BagWindowToggleRequestedEventArgs(BagWindowRequestSource.Shortcut));
        }

        /// <summary>调用真实武器库存 API 添加一把测试武器并记录其实例标识。</summary>
        [Button("添加一把测试武器")]
        public void AddTestWeapon()
        {
            if (!TryGetTestWeapon(out WeaponDefinition definition) || !EnsureInventoryReady("添加武器")) return;

            EquipmentAddResult<WeaponInstance> result = WeaponInventoryManager.Instance.AddWeapon(definition.ItemId);
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

            EquipmentBatchAddResult<WeaponInstance> result = WeaponInventoryManager.Instance.AddWeapons(definitionIds);
            Debug.Log($"[BagWindowTest] batchAdd status={result.Status}, count={result.Instances.Count}。", this);
            if (!result.Succeeded) return;

            for (int index = 0; index < result.Instances.Count; index++) RememberCreatedInstance(result.Instances[index]);
        }

        #endregion

        #region 状态修改与移除

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
            EquipmentOperationResult result = WeaponInventoryManager.Instance.UpdateWeaponProgress(instance.InstanceId, update);
            Debug.Log($"[BagWindowTest] update status={result.Status}, instance={instance.InstanceId}。", this);
        }

        /// <summary>切换最近创建实例的锁定状态，验证锁定图标和移除约束。</summary>
        [Button("切换最近创建武器锁定")]
        public void ToggleLatestWeaponLock()
        {
            if (!TryGetLatestInstance(out WeaponInstance instance) || !EnsureInventoryReady("切换武器锁定")) return;

            EquipmentOperationResult result = WeaponInventoryManager.Instance.SetLocked(instance.InstanceId, !instance.IsLocked);
            Debug.Log($"[BagWindowTest] setLocked status={result.Status}, locked={!instance.IsLocked}, instance={instance.InstanceId}。", this);
        }

        /// <summary>确认最近创建实例的新获得状态，验证 New 标记刷新。</summary>
        [Button("确认最近创建武器")]
        public void AcknowledgeLatestWeapon()
        {
            if (!TryGetLatestInstance(out WeaponInstance instance) || !EnsureInventoryReady("确认武器新获得状态")) return;

            EquipmentOperationResult result = WeaponInventoryManager.Instance.AcknowledgeNew(instance.InstanceId);
            Debug.Log($"[BagWindowTest] acknowledge status={result.Status}, instance={instance.InstanceId}。", this);
        }

        /// <summary>移除最近创建的实例，不自动解锁或绕过 Manager 的业务约束。</summary>
        [Button("移除最近创建武器")]
        public void RemoveLatestWeapon()
        {
            if (!TryGetLatestInstance(out WeaponInstance instance) || !EnsureInventoryReady("移除武器")) return;

            EquipmentOperationResult result = WeaponInventoryManager.Instance.RemoveWeapon(instance.InstanceId);
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
                if (!WeaponInventoryManager.Instance.TryGetInstance(instanceId, out WeaponInstance instance))
                {
                    ForgetCreatedInstance(instanceId);
                    continue;
                }

                EquipmentOperationResult result = WeaponInventoryManager.Instance.RemoveWeapon(instanceId);
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

            WeaponInventoryManager manager = WeaponInventoryManager.Instance;
            IReadOnlyList<WeaponInstance> instances = manager.GetInstances();
            Debug.Log($"[BagWindowTest] inventory count={manager.Count}/{manager.Capacity}, tracked={createdInstanceIds.Count}。", this);
            for (int index = 0; index < instances.Count; index++)
            {
                WeaponInstance instance = instances[index];
                Debug.Log(
                    $"[BagWindowTest] instance={instance.InstanceId}, definition={instance.DefinitionId}, level={instance.Level}, refinement={instance.RefinementRank}, locked={instance.IsLocked}, new={instance.IsNew}, equipped={instance.IsEquipped}。",
                    this);
            }
        }

        #endregion

        #region 前置条件与记录辅助

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

            if (!WeaponInventoryManager.IsConfigured || !WeaponInventoryManager.Instance.TryGetInstance(latestInstanceId.Value, out instance))
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
