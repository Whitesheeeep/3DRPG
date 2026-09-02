using System;
using System.Collections.Generic;

namespace RPG.ItemSystem
{
    /// <summary>背包和装备实例操作的统一业务结果状态。</summary>
    public enum InventoryOperationStatus
    {
        /// <summary>操作成功。</summary>
        Succeeded = 0,
        /// <summary>找不到 Definition。</summary>
        UnknownDefinition,
        /// <summary>Definition 类型不匹配。</summary>
        DefinitionTypeMismatch,
        /// <summary>数量非法。</summary>
        InvalidQuantity,
        /// <summary>超过数量上限。</summary>
        QuantityLimitExceeded,
        /// <summary>超过实例容量。</summary>
        CapacityExceeded,
        /// <summary>找不到实例。</summary>
        InstanceNotFound,
        /// <summary>实例已锁定。</summary>
        InstanceLocked,
        /// <summary>实例 ID 重复。</summary>
        DuplicateInstanceId,
        /// <summary>等级超出范围。</summary>
        LevelOutOfRange,
        /// <summary>经验超出范围。</summary>
        ExperienceOutOfRange,
        /// <summary>突破阶数超出范围。</summary>
        AscensionRankOutOfRange,
        /// <summary>精炼阶数超出范围。</summary>
        RefinementRankOutOfRange,
        /// <summary>数量不足。</summary>
        InsufficientQuantity,
        /// <summary>货币不足。</summary>
        InsufficientCurrency,
        /// <summary>货币超过上限。</summary>
        CurrencyLimitExceeded,
        /// <summary>整数运算溢出。</summary>
        ArithmeticOverflow,
        /// <summary>实例已经装备到角色，不能直接移除。</summary>
        InstanceEquipped
    }

    /// <summary>一个物品标识和数量。</summary>
    public readonly struct ItemQuantity
    {
        /// <summary>创建物品数量请求。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="quantity">数量。</param>
        public ItemQuantity(ItemId itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        /// <summary>获取物品标识。</summary>
        public ItemId ItemId { get; }

        /// <summary>获取数量。</summary>
        public int Quantity { get; }
    }

    /// <summary>装备进度更新请求的公共字段。</summary>
    public readonly struct EquipmentProgressUpdate
    {
        /// <summary>创建进度更新请求。</summary>
        /// <param name="level">目标等级。</param>
        /// <param name="currentExperience">目标等级内经验。</param>
        public EquipmentProgressUpdate(int level, int currentExperience)
        {
            Level = level;
            CurrentExperience = currentExperience;
        }

        /// <summary>获取目标等级。</summary>
        public int Level { get; }

        /// <summary>获取目标等级内经验。</summary>
        public int CurrentExperience { get; }
    }

    /// <summary>武器专属进度更新请求。</summary>
    public readonly struct WeaponProgressUpdate
    {
        /// <summary>创建武器进度更新请求。</summary>
        /// <param name="level">目标等级。</param>
        /// <param name="currentExperience">目标等级内经验。</param>
        /// <param name="ascensionRank">突破阶数。</param>
        /// <param name="refinementRank">精炼阶数。</param>
        public WeaponProgressUpdate(int level, int currentExperience, int ascensionRank, int refinementRank)
        {
            Level = level;
            CurrentExperience = currentExperience;
            AscensionRank = ascensionRank;
            RefinementRank = refinementRank;
        }

        /// <summary>获取目标等级。</summary>
        public int Level { get; }

        /// <summary>获取目标等级内经验。</summary>
        public int CurrentExperience { get; }

        /// <summary>获取目标突破阶数。</summary>
        public int AscensionRank { get; }

        /// <summary>获取目标精炼阶数。</summary>
        public int RefinementRank { get; }
    }

    /// <summary>圣遗物专属进度更新请求。</summary>
    public readonly struct ArtifactProgressUpdate
    {
        /// <summary>创建圣遗物进度更新请求。</summary>
        /// <param name="level">目标等级。</param>
        /// <param name="currentExperience">目标等级内经验。</param>
        public ArtifactProgressUpdate(int level, int currentExperience)
        {
            Level = level;
            CurrentExperience = currentExperience;
        }

        /// <summary>获取目标等级。</summary>
        public int Level { get; }

        /// <summary>获取目标等级内经验。</summary>
        public int CurrentExperience { get; }
    }

    /// <summary>不携带新增实例时的简单操作结果。</summary>
    public readonly struct EquipmentOperationResult
    {
        /// <summary>创建装备操作结果。</summary>
        /// <param name="status">操作状态。</param>
        public EquipmentOperationResult(InventoryOperationStatus status) => Status = status;

        /// <summary>获取操作状态。</summary>
        public InventoryOperationStatus Status { get; }

        /// <summary>判断操作是否成功。</summary>
        public bool Succeeded => Status == InventoryOperationStatus.Succeeded;
    }

    /// <summary>单个装备添加结果。</summary>
    public readonly struct EquipmentAddResult<TInstance>
        where TInstance : EquipmentInstance
    {
        /// <summary>创建单个装备添加结果。</summary>
        /// <param name="status">操作状态。</param>
        /// <param name="instance">成功创建的实例。</param>
        public EquipmentAddResult(InventoryOperationStatus status, TInstance instance)
        {
            Status = status;
            Instance = instance;
        }

        /// <summary>获取操作状态。</summary>
        public InventoryOperationStatus Status { get; }

        /// <summary>获取创建的实例；失败时为空。</summary>
        public TInstance Instance { get; }

        /// <summary>判断操作是否成功。</summary>
        public bool Succeeded => Status == InventoryOperationStatus.Succeeded;
    }

    /// <summary>批量装备添加结果。</summary>
    public readonly struct EquipmentBatchAddResult<TInstance>
        where TInstance : EquipmentInstance
    {
        /// <summary>创建批量装备添加结果。</summary>
        /// <param name="status">操作状态。</param>
        /// <param name="instances">成功创建的实例。</param>
        public EquipmentBatchAddResult(InventoryOperationStatus status, IReadOnlyList<TInstance> instances)
        {
            Status = status;
            Instances = instances ?? Array.Empty<TInstance>();
        }

        /// <summary>获取操作状态。</summary>
        public InventoryOperationStatus Status { get; }

        /// <summary>获取创建的实例。</summary>
        public IReadOnlyList<TInstance> Instances { get; }

        /// <summary>判断操作是否成功。</summary>
        public bool Succeeded => Status == InventoryOperationStatus.Succeeded;
    }
}
