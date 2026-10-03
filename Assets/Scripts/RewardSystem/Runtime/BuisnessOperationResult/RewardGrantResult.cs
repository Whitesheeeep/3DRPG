using RPG.CurrencySystemNS;
using RPG.ItemSystem;

namespace RPG.RewardSystemNS
{
    /// <summary>标识奖励发放被拒绝的业务领域。</summary>
    public enum RewardGrantFailureDomain
    {
        /// <summary>奖励已成功提交。</summary>
        None = 0,
        /// <summary>货币钱包拒绝了批次。</summary>
        Currency = 1,
        /// <summary>物品或装备库存拒绝了批次。</summary>
        Item = 2,
        /// <summary>提交前领域状态已经变化，需要重新准备。</summary>
        StateChanged = 3
    }

    /// <summary>描述一次通用奖励准备或发放的业务结果。</summary>
    public sealed class RewardGrantResult
    {
        #region 构造与查询

        /// <summary>创建通用奖励结果。</summary>
        /// <param name="failureDomain">失败领域。</param>
        /// <param name="currencyStatus">钱包操作状态。</param>
        /// <param name="inventoryStatus">库存操作状态。</param>
        /// <param name="currencyId">相关货币标识。</param>
        /// <param name="itemId">相关物品标识。</param>
        public RewardGrantResult(
            RewardGrantFailureDomain failureDomain,
            CurrencyOperationStatus? currencyStatus = null,
            InventoryOperationStatus? inventoryStatus = null,
            CurrencyId currencyId = CurrencyId.None,
            ItemId itemId = default)
        {
            FailureDomain = failureDomain;
            CurrencyStatus = currencyStatus;
            InventoryStatus = inventoryStatus;
            CurrencyId = currencyId;
            ItemId = itemId;
        }

        /// <summary>创建成功结果。</summary>
        /// <returns>成功结果。</returns>
        public static RewardGrantResult Success() => new RewardGrantResult(RewardGrantFailureDomain.None);

        /// <summary>获取本次发放是否成功。</summary>
        public bool Succeeded => FailureDomain == RewardGrantFailureDomain.None;

        /// <summary>获取失败领域。</summary>
        public RewardGrantFailureDomain FailureDomain { get; }

        /// <summary>获取货币钱包的详细状态。</summary>
        public CurrencyOperationStatus? CurrencyStatus { get; }

        /// <summary>获取物品库存的详细状态。</summary>
        public InventoryOperationStatus? InventoryStatus { get; }

        /// <summary>获取失败关联的货币标识。</summary>
        public CurrencyId CurrencyId { get; }

        /// <summary>获取失败关联的物品标识。</summary>
        public ItemId ItemId { get; }

        #endregion
    }
}
