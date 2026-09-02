using System;
using System.Collections.Generic;
using WS_Modules.Singleton;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.CurrencySystem
{
    /// <summary>独立管理账号货币余额，不将货币混入 GAS 属性或物品背包。</summary>
    public sealed class CurrencyManager : SingletonBase<CurrencyManager>, ICurrencyWallet
    {
        #region 配置与生命周期

        private static CurrencySettings settings;
        private static bool configured;
        private readonly Dictionary<CurrencyId, int> balances = new Dictionary<CurrencyId, int>();

        /// <summary>创建货币钱包并载入配置初始余额。</summary>
        private CurrencyManager()
        {
            EnsureConfigured();
            for (int index = 0; index < settings.Rules.Count; index++) balances.Add(settings.Rules[index].CurrencyId, settings.Rules[index].InitialBalance);
        }

        /// <summary>获取当前货币规则的只读列表。</summary>
        internal IReadOnlyList<CurrencySettings.CurrencyRule> Rules => settings.Rules;

        /// <summary>静态注入货币规则。</summary>
        /// <param name="currencySettings">货币配置。</param>
        /// <exception cref="ArgumentNullException">配置为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">注入不同配置时抛出。</exception>
        public static void Initialize(CurrencySettings currencySettings)
        {
            if (currencySettings == null) throw new ArgumentNullException(nameof(currencySettings));
            currencySettings.Validate();
            if (configured && !ReferenceEquals(settings, currencySettings)) throw new InvalidOperationException("[CurrencyManager] 已注入其他货币配置，不能静默覆盖。");
            settings = currencySettings;
            configured = true;
        }

        #endregion

        #region 钱包操作

        /// <summary>获取货币余额。</summary>
        /// <param name="currencyId">货币标识。</param>
        /// <returns>余额。</returns>
        public int GetBalance(CurrencyId currencyId)
        {
            EnsureCurrency(currencyId);
            return balances[currencyId];
        }

        /// <summary>判断是否能够支付全部货币成本。</summary>
        /// <param name="costs">成本列表。</param>
        /// <returns>余额足够时返回 true。</returns>
        public bool CanAfford(IReadOnlyList<CurrencyAmount> costs)
        {
            Dictionary<CurrencyId, int> merged = MergeAmounts(costs, false, out CurrencyOperationStatus status, out _);
            if (status != CurrencyOperationStatus.Succeeded) return false;
            foreach (KeyValuePair<CurrencyId, int> pair in merged) if (balances[pair.Key] < pair.Value) return false;
            return true;
        }

        /// <summary>原子增加多种货币。</summary>
        /// <param name="amounts">增加金额。</param>
        /// <returns>操作结果。</returns>
        public CurrencyOperationResult AddCurrencies(IReadOnlyList<CurrencyAmount> amounts) => ApplyAmounts(amounts, false);

        /// <summary>原子消耗多种货币。</summary>
        /// <param name="costs">消耗金额。</param>
        /// <returns>操作结果。</returns>
        public CurrencyOperationResult ConsumeCurrencies(IReadOnlyList<CurrencyAmount> costs) => ApplyAmounts(costs, true);

        /// <summary>原子应用多种有符号货币变化。</summary>
        /// <param name="changes">变化列表。</param>
        /// <returns>操作结果。</returns>
        public CurrencyOperationResult ApplyChanges(IReadOnlyList<CurrencyDelta> changes)
        {
            EnsureConfigured();
            if (changes == null || changes.Count == 0) return new CurrencyOperationResult(CurrencyOperationStatus.InvalidAmount, CurrencyId.None);
            var merged = new Dictionary<CurrencyId, int>();
            for (int index = 0; index < changes.Count; index++)
            {
                CurrencyDelta change = changes[index];
                CurrencyOperationResult validation = ValidateChange(change.CurrencyId, change.Delta);
                if (!validation.Succeeded) return validation;
                try { merged[change.CurrencyId] = checked(merged.TryGetValue(change.CurrencyId, out int current) ? current + change.Delta : change.Delta); }
                catch (OverflowException) { return new CurrencyOperationResult(CurrencyOperationStatus.ArithmeticOverflow, change.CurrencyId); }
            }

            foreach (KeyValuePair<CurrencyId, int> pair in merged)
            {
                int current = balances[pair.Key];
                int next;
                try { next = checked(current + pair.Value); }
                catch (OverflowException) { return new CurrencyOperationResult(CurrencyOperationStatus.ArithmeticOverflow, pair.Key); }
                if (next < 0) return new CurrencyOperationResult(CurrencyOperationStatus.InsufficientBalance, pair.Key);
                if (next > GetRule(pair.Key).MaxBalance) return new CurrencyOperationResult(CurrencyOperationStatus.BalanceLimitExceeded, pair.Key);
            }

            var committedChanges = new List<CurrencyBalanceChangedEvent>(merged.Count);
            foreach (KeyValuePair<CurrencyId, int> pair in merged)
            {
                int previous = balances[pair.Key];
                int current = previous + pair.Value;
                balances[pair.Key] = current;
                committedChanges.Add(new CurrencyBalanceChangedEvent(pair.Key, previous, current));
            }

            // 所有货币余额写入完成后统一广播，保证多货币交易不会暴露半提交状态。
            for (int index = 0; index < committedChanges.Count; index++)
                WSEventSystem.EventTrigger_Type(typeof(CurrencyBalanceChangedEvent), committedChanges[index]);

            return new CurrencyOperationResult(CurrencyOperationStatus.Succeeded, CurrencyId.None);
        }

        #endregion

        #region 存档支持

        /// <summary>清空钱包并恢复配置初始值。</summary>
        internal void ClearRuntimeState()
        {
            balances.Clear();
            for (int index = 0; index < settings.Rules.Count; index++) balances.Add(settings.Rules[index].CurrencyId, settings.Rules[index].InitialBalance);
        }

        /// <summary>替换已完成验证的钱包余额。</summary>
        /// <param name="restoredBalances">恢复余额。</param>
        internal void RestoreState(IReadOnlyDictionary<CurrencyId, int> restoredBalances)
        {
            EnsureConfigured();
            balances.Clear();
            foreach (KeyValuePair<CurrencyId, int> pair in restoredBalances) balances.Add(pair.Key, pair.Value);
            WSEventSystem.EventTrigger_Type(typeof(CurrencyWalletRestoredEvent), new CurrencyWalletRestoredEvent());
        }

        #endregion

        #region 内部校验

        /// <summary>确认钱包已经完成配置。</summary>
        private static void EnsureConfigured()
        {
            if (!configured || settings == null) throw new InvalidOperationException("[CurrencyManager] 尚未注入 CurrencySettings。");
        }

        /// <summary>确认货币标识有效。</summary>
        /// <param name="currencyId">货币标识。</param>
        private void EnsureCurrency(CurrencyId currencyId)
        {
            EnsureConfigured();
            if (!balances.ContainsKey(currencyId)) throw new InvalidOperationException($"[CurrencyManager] 未配置货币：{currencyId}。");
        }

        /// <summary>查询货币规则。</summary>
        /// <param name="currencyId">货币标识。</param>
        /// <returns>规则。</returns>
        private CurrencySettings.CurrencyRule GetRule(CurrencyId currencyId)
        {
            for (int index = 0; index < settings.Rules.Count; index++) if (settings.Rules[index].CurrencyId == currencyId) return settings.Rules[index];
            throw new InvalidOperationException($"[CurrencyManager] 未找到货币规则：{currencyId}。");
        }

        /// <summary>为存档校验获取货币规则。</summary>
        /// <param name="currencyId">货币标识。</param>
        /// <returns>匹配规则。</returns>
        internal CurrencySettings.CurrencyRule GetRuleForSave(CurrencyId currencyId) => GetRule(currencyId);

        /// <summary>验证单个货币变化。</summary>
        /// <param name="currencyId">货币标识。</param>
        /// <param name="delta">变化量。</param>
        /// <returns>验证结果。</returns>
        private CurrencyOperationResult ValidateChange(CurrencyId currencyId, int delta)
        {
            if (currencyId == CurrencyId.None || !balances.ContainsKey(currencyId)) return new CurrencyOperationResult(CurrencyOperationStatus.InvalidCurrency, currencyId);
            if (delta == 0) return new CurrencyOperationResult(CurrencyOperationStatus.InvalidAmount, currencyId);
            return new CurrencyOperationResult(CurrencyOperationStatus.Succeeded, currencyId);
        }

        /// <summary>校验正数金额并转换为有符号变化列表。</summary>
        /// <param name="amounts">金额列表。</param>
        /// <param name="negative">是否转换为消耗。</param>
        /// <returns>批量操作结果。</returns>
        private CurrencyOperationResult ApplyAmounts(IReadOnlyList<CurrencyAmount> amounts, bool negative)
        {
            if (amounts == null || amounts.Count == 0)
                return new CurrencyOperationResult(CurrencyOperationStatus.InvalidAmount, CurrencyId.None);

            var result = new CurrencyDelta[amounts.Count];
            for (int index = 0; index < amounts.Count; index++)
            {
                CurrencyAmount amount = amounts[index];
                if (amount.Amount <= 0)
                    return new CurrencyOperationResult(CurrencyOperationStatus.InvalidAmount, amount.CurrencyId);

                // 先限制为正数，再执行负号转换，避免 int.MinValue 取反溢出。
                int delta = negative ? -amount.Amount : amount.Amount;
                result[index] = new CurrencyDelta(amount.CurrencyId, delta);
            }

            return ApplyChanges(result);
        }

        /// <summary>合并金额并返回校验状态。</summary>
        /// <param name="amounts">金额列表。</param>
        /// <param name="negative">是否按消耗语义校验。</param>
        /// <param name="status">校验状态。</param>
        /// <param name="currencyId">首个错误货币。</param>
        /// <returns>合并结果。</returns>
        private Dictionary<CurrencyId, int> MergeAmounts(IReadOnlyList<CurrencyAmount> amounts, bool negative, out CurrencyOperationStatus status, out CurrencyId currencyId)
        {
            var result = new Dictionary<CurrencyId, int>();
            status = CurrencyOperationStatus.Succeeded;
            currencyId = CurrencyId.None;
            if (amounts == null || amounts.Count == 0) { status = CurrencyOperationStatus.InvalidAmount; return result; }
            for (int index = 0; index < amounts.Count; index++)
            {
                CurrencyAmount amount = amounts[index];
                if (amount.CurrencyId == CurrencyId.None || !balances.ContainsKey(amount.CurrencyId)) { status = CurrencyOperationStatus.InvalidCurrency; currencyId = amount.CurrencyId; return result; }
                if (amount.Amount <= 0) { status = CurrencyOperationStatus.InvalidAmount; currencyId = amount.CurrencyId; return result; }
                try { result[amount.CurrencyId] = checked(result.TryGetValue(amount.CurrencyId, out int current) ? current + amount.Amount : amount.Amount); }
                catch (OverflowException) { status = CurrencyOperationStatus.ArithmeticOverflow; currencyId = amount.CurrencyId; return result; }
            }
            return result;
        }

        #endregion
    }
}
