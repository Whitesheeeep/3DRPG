using System;

namespace RPG.CurrencySystem
{
    /// <summary>货币与金额组成的只读值对象。</summary>
    public readonly struct CurrencyAmount
    {
        /// <summary>创建货币金额。</summary>
        /// <param name="currencyId">货币标识。</param>
        /// <param name="amount">金额。</param>
        public CurrencyAmount(CurrencyId currencyId, int amount)
        {
            CurrencyId = currencyId;
            Amount = amount;
        }

        /// <summary>获取货币标识。</summary>
        public CurrencyId CurrencyId { get; }

        /// <summary>获取金额。</summary>
        public int Amount { get; }
    }

    /// <summary>货币余额的有符号变化。</summary>
    public readonly struct CurrencyDelta
    {
        /// <summary>创建货币变化。</summary>
        /// <param name="currencyId">货币标识。</param>
        /// <param name="delta">有符号变化量。</param>
        public CurrencyDelta(CurrencyId currencyId, int delta)
        {
            CurrencyId = currencyId;
            Delta = delta;
        }

        /// <summary>获取货币标识。</summary>
        public CurrencyId CurrencyId { get; }

        /// <summary>获取变化量。</summary>
        public int Delta { get; }
    }

    /// <summary>货币批次操作结果。</summary>
    public readonly struct CurrencyOperationResult
    {
        /// <summary>创建货币操作结果。</summary>
        /// <param name="status">操作状态。</param>
        /// <param name="currencyId">关联货币。</param>
        public CurrencyOperationResult(CurrencyOperationStatus status, CurrencyId currencyId)
        {
            Status = status;
            CurrencyId = currencyId;
        }

        /// <summary>获取状态。</summary>
        public CurrencyOperationStatus Status { get; }

        /// <summary>获取关联货币。</summary>
        public CurrencyId CurrencyId { get; }

        /// <summary>判断是否成功。</summary>
        public bool Succeeded => Status == CurrencyOperationStatus.Succeeded;
    }

    /// <summary>货币操作的失败状态。</summary>
    public enum CurrencyOperationStatus
    {
        /// <summary>操作成功。</summary>
        Succeeded = 0,
        /// <summary>货币标识无效。</summary>
        InvalidCurrency,
        /// <summary>金额无效。</summary>
        InvalidAmount,
        /// <summary>余额不足。</summary>
        InsufficientBalance,
        /// <summary>超过余额上限。</summary>
        BalanceLimitExceeded,
        /// <summary>整数溢出。</summary>
        ArithmeticOverflow
    }
}
