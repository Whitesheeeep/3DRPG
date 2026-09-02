using System.Collections.Generic;

namespace RPG.CurrencySystem
{
    /// <summary>向商店、强化和奖励系统提供的货币钱包边界。</summary>
    public interface ICurrencyWallet
    {
        /// <summary>获取指定货币余额。</summary>
        /// <param name="currencyId">货币标识。</param>
        /// <returns>余额。</returns>
        int GetBalance(CurrencyId currencyId);

        /// <summary>判断是否能够支付全部成本。</summary>
        /// <param name="costs">成本列表。</param>
        /// <returns>余额足够时返回 true。</returns>
        bool CanAfford(IReadOnlyList<CurrencyAmount> costs);

        /// <summary>原子增加多种货币。</summary>
        /// <param name="amounts">增加金额。</param>
        /// <returns>操作结果。</returns>
        CurrencyOperationResult AddCurrencies(IReadOnlyList<CurrencyAmount> amounts);

        /// <summary>原子消耗多种货币。</summary>
        /// <param name="costs">消耗金额。</param>
        /// <returns>操作结果。</returns>
        CurrencyOperationResult ConsumeCurrencies(IReadOnlyList<CurrencyAmount> costs);

        /// <summary>原子应用有符号货币变化。</summary>
        /// <param name="changes">变化列表。</param>
        /// <returns>操作结果。</returns>
        CurrencyOperationResult ApplyChanges(IReadOnlyList<CurrencyDelta> changes);
    }
}
