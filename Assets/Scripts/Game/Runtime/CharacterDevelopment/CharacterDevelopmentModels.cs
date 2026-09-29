using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using WS_Modules.GAS.AttributeSystem;

namespace RPG.Game.Runtime.CharacterDevelopment
{
    /// <summary>角色窗口内的培养子页面类型。</summary>
    public enum CharacterDevelopmentMode
    {
        /// <summary>当前没有打开角色培养页面。</summary>
        None,
        /// <summary>使用角色经验素材升级。</summary>
        LevelUp,
        /// <summary>提交当前角色突破阶段。</summary>
        Ascension
    }

    /// <summary>角色培养事务的结果状态。</summary>
    public enum CharacterDevelopmentStatus
    {
        /// <summary>角色培养事务已完成。</summary>
        Succeeded,
        /// <summary>角色不存在或当前培养目标失效。</summary>
        InvalidTarget,
        /// <summary>当前角色已经达到培养上限。</summary>
        ProgressAtCap,
        /// <summary>候选材料选择或成长配置不合法。</summary>
        InvalidSelection,
        /// <summary>材料数量不足。</summary>
        InsufficientMaterials,
        /// <summary>摩拉余额不足。</summary>
        InsufficientCurrency,
        /// <summary>角色成长配置不可用。</summary>
        InvalidConfiguration,
        /// <summary>库存、钱包或角色进度管理器拒绝了提交。</summary>
        ManagerRejected
    }

    /// <summary>角色培养操作返回的状态及玩家可读消息。</summary>
    public sealed class CharacterDevelopmentOperationResult
    {
        /// <summary>创建角色培养结果。</summary>
        /// <param name="status">事务状态。</param>
        /// <param name="message">用户可读结果说明。</param>
        public CharacterDevelopmentOperationResult(CharacterDevelopmentStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
        }

        /// <summary>获取事务状态。</summary>
        public CharacterDevelopmentStatus Status { get; }
        /// <summary>获取用户可读结果说明。</summary>
        public string Message { get; }
        /// <summary>判断事务是否成功。</summary>
        public bool Succeeded => Status == CharacterDevelopmentStatus.Succeeded;
    }

    /// <summary>一个角色 Stat 在升级前后的静态投影值。</summary>
    public readonly struct CharacterDevelopmentAttributeValue
    {
        /// <summary>创建角色升级属性投影值。</summary>
        /// <param name="attribute">稳定属性定义。</param>
        /// <param name="currentValue">当前等级装备结算后的属性。</param>
        /// <param name="projectedValue">预计等级装备结算后的属性。</param>
        public CharacterDevelopmentAttributeValue(GameplayAttribute attribute, float currentValue, float projectedValue)
        {
            Attribute = attribute;
            CurrentValue = currentValue;
            ProjectedValue = projectedValue;
        }

        /// <summary>获取稳定属性定义。</summary>
        public GameplayAttribute Attribute { get; }
        /// <summary>获取当前属性值。</summary>
        public float CurrentValue { get; }
        /// <summary>获取预计属性值。</summary>
        public float ProjectedValue { get; }
    }

    /// <summary>角色经验升级预览及实际可扣除的材料计划。</summary>
    public sealed class CharacterLevelUpPreview
    {
        /// <summary>创建角色等级升级预览。</summary>
        /// <param name="status">预览状态。</param>
        /// <param name="message">当前预览说明。</param>
        /// <param name="currentLevel">当前等级。</param>
        /// <param name="projectedLevel">预计等级。</param>
        /// <param name="currentExperience">当前等级内经验。</param>
        /// <param name="projectedExperience">预计等级内经验。</param>
        /// <param name="nextExperience">预计等级下一级所需经验。</param>
        /// <param name="selectedExperience">已选素材提供的经验。</param>
        /// <param name="currencyCost">跨越等级产生的摩拉成本。</param>
        /// <param name="consumedQuantityByItemIdMap">规划器判定实际扣除的材料数量。</param>
        /// <param name="attributes">当前与预计等级的属性投影。</param>
        public CharacterLevelUpPreview(CharacterDevelopmentStatus status, string message, int currentLevel,
            int projectedLevel, int currentExperience, int projectedExperience, int nextExperience,
            long selectedExperience, long currencyCost,
            IReadOnlyDictionary<ItemId, int> consumedQuantityByItemIdMap,
            IReadOnlyList<CharacterDevelopmentAttributeValue> attributes)
        {
            Status = status;
            Message = message ?? string.Empty;
            CurrentLevel = currentLevel;
            ProjectedLevel = projectedLevel;
            CurrentExperience = currentExperience;
            ProjectedExperience = projectedExperience;
            NextExperience = nextExperience;
            SelectedExperience = selectedExperience;
            CurrencyCost = currencyCost;
            ConsumedQuantityByItemIdMap = consumedQuantityByItemIdMap ??
                                          new Dictionary<ItemId, int>();
            Attributes = attributes ?? Array.Empty<CharacterDevelopmentAttributeValue>();
        }

        /// <summary>获取预览状态。</summary>
        public CharacterDevelopmentStatus Status { get; }
        /// <summary>获取预览说明。</summary>
        public string Message { get; }
        /// <summary>获取当前等级。</summary>
        public int CurrentLevel { get; }
        /// <summary>获取预计等级。</summary>
        public int ProjectedLevel { get; }
        /// <summary>获取当前等级内经验。</summary>
        public int CurrentExperience { get; }
        /// <summary>获取预计等级内经验。</summary>
        public int ProjectedExperience { get; }
        /// <summary>获取预计等级升到下一级所需经验。</summary>
        public int NextExperience { get; }
        /// <summary>获取所选素材提供的经验总量。</summary>
        public long SelectedExperience { get; }
        /// <summary>获取预计摩拉成本。</summary>
        public long CurrencyCost { get; }
        /// <summary>获取实际扣除材料数量；值为本体数量且按 ItemId 汇总。</summary>
        public IReadOnlyDictionary<ItemId, int> ConsumedQuantityByItemIdMap { get; }
        /// <summary>获取固定角色属性的当前与预计投影。</summary>
        public IReadOnlyList<CharacterDevelopmentAttributeValue> Attributes { get; }
        /// <summary>判断当前选择能否提交。</summary>
        public bool CanSubmit => Status == CharacterDevelopmentStatus.Succeeded &&
                                 ConsumedQuantityByItemIdMap.Count > 0 &&
                                 (CurrentLevel != ProjectedLevel || CurrentExperience != ProjectedExperience);
    }

    /// <summary>单项角色突破材料的配置需求和当前库存数量。</summary>
    public readonly struct CharacterAscensionMaterialRequirement
    {
        /// <summary>创建突破材料需求展示。</summary>
        /// <param name="itemId">材料 ItemId。</param>
        /// <param name="requiredQuantity">配置需求数量。</param>
        /// <param name="ownedQuantity">当前背包数量。</param>
        public CharacterAscensionMaterialRequirement(ItemId itemId, int requiredQuantity, int ownedQuantity)
        {
            ItemId = itemId;
            RequiredQuantity = requiredQuantity;
            OwnedQuantity = ownedQuantity;
        }

        /// <summary>获取材料 ItemId。</summary>
        public ItemId ItemId { get; }
        /// <summary>获取需求数量。</summary>
        public int RequiredQuantity { get; }
        /// <summary>获取当前拥有数量。</summary>
        public int OwnedQuantity { get; }
    }

    /// <summary>角色下一突破阶段的配置、库存和费用快照。</summary>
    public sealed class CharacterAscensionPreview
    {
        /// <summary>创建角色突破预览。</summary>
        /// <param name="status">当前突破是否具备有效配置。</param>
        /// <param name="message">资格或配置说明。</param>
        /// <param name="currentRank">当前突破阶数。</param>
        /// <param name="nextRank">突破后阶数。</param>
        /// <param name="currentLevelCap">当前等级上限。</param>
        /// <param name="nextLevelCap">突破后等级上限。</param>
        /// <param name="currencyOwned">当前摩拉余额。</param>
        /// <param name="currencyCost">突破摩拉成本。</param>
        /// <param name="materials">突破材料需求列表。</param>
        /// <param name="isAtLevelRequirement">当前角色是否达到等级要求。</param>
        public CharacterAscensionPreview(CharacterDevelopmentStatus status, string message,
            int currentRank, int nextRank, int currentLevelCap, int nextLevelCap,
            long currencyOwned, long currencyCost,
            IReadOnlyList<CharacterAscensionMaterialRequirement> materials, bool isAtLevelRequirement)
        {
            Status = status;
            Message = message ?? string.Empty;
            CurrentRank = currentRank;
            NextRank = nextRank;
            CurrentLevelCap = currentLevelCap;
            NextLevelCap = nextLevelCap;
            CurrencyOwned = currencyOwned;
            CurrencyCost = currencyCost;
            Materials = materials ?? Array.Empty<CharacterAscensionMaterialRequirement>();
            IsAtLevelRequirement = isAtLevelRequirement;
        }

        /// <summary>获取突破预览状态。</summary>
        public CharacterDevelopmentStatus Status { get; }
        /// <summary>获取突破预览说明。</summary>
        public string Message { get; }
        /// <summary>获取当前突破阶数。</summary>
        public int CurrentRank { get; }
        /// <summary>获取预计突破阶数。</summary>
        public int NextRank { get; }
        /// <summary>获取当前等级上限。</summary>
        public int CurrentLevelCap { get; }
        /// <summary>获取突破后等级上限。</summary>
        public int NextLevelCap { get; }
        /// <summary>获取当前摩拉余额。</summary>
        public long CurrencyOwned { get; }
        /// <summary>获取突破摩拉成本。</summary>
        public long CurrencyCost { get; }
        /// <summary>获取各类突破材料需求。</summary>
        public IReadOnlyList<CharacterAscensionMaterialRequirement> Materials { get; }
        /// <summary>判断等级要求、材料与摩拉均满足时是否可提交。</summary>
        public bool CanSubmit => Status == CharacterDevelopmentStatus.Succeeded &&
                                 NextRank > CurrentRank &&
                                 Materials.Count > 0 &&
                                 CurrencyOwned >= CurrencyCost &&
                                 IsAtLevelRequirement && HasAllMaterials;

        /// <summary>获取当前角色是否达到该阶段的等级要求。</summary>
        public bool IsAtLevelRequirement { get; }

        /// <summary>判断所有配置材料是否充足。</summary>
        private bool HasAllMaterials
        {
            get
            {
                for (int index = 0; index < Materials.Count; index++)
                    if (Materials[index].OwnedQuantity < Materials[index].RequiredQuantity) return false;
                return true;
            }
        }
    }
}
