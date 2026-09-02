namespace RPG.CurrencySystem
{
    /// <summary>一种货币余额变化事件。</summary>
    public readonly struct CurrencyBalanceChangedEvent
    {
        /// <summary>创建余额变化事件。</summary>
        /// <param name="currencyId">货币标识。</param>
        /// <param name="previousBalance">旧余额。</param>
        /// <param name="currentBalance">新余额。</param>
        public CurrencyBalanceChangedEvent(CurrencyId currencyId, int previousBalance, int currentBalance)
        {
            CurrencyId = currencyId;
            PreviousBalance = previousBalance;
            CurrentBalance = currentBalance;
        }

        /// <summary>获取货币标识。</summary>
        public CurrencyId CurrencyId { get; }

        /// <summary>获取旧余额。</summary>
        public int PreviousBalance { get; }

        /// <summary>获取新余额。</summary>
        public int CurrentBalance { get; }
    }

    /// <summary>货币钱包恢复完成事件。</summary>
    public readonly struct CurrencyWalletRestoredEvent
    {
    }
}
