using System;
using System.Collections.Generic;
using RPG.SaveSystem;

namespace RPG.CurrencySystem
{
    /// <summary>货币钱包版本化存档快照。</summary>
    [Serializable]
    public sealed class CurrencySaveSnapshot : ISaveModuleSnapshot
    {
        /// <summary>创建空货币快照。</summary>
        public CurrencySaveSnapshot() => Balances = new List<CurrencyBalanceSaveEntry>();

        /// <summary>货币余额。</summary>
        public List<CurrencyBalanceSaveEntry> Balances { get; set; }

        /// <summary>验证快照结构。</summary>
        public void ValidateShape()
        {
            if (Balances == null) throw new InvalidOperationException("货币快照结构无效。");
            var ids = new HashSet<CurrencyId>();
            for (int index = 0; index < Balances.Count; index++)
            {
                CurrencyBalanceSaveEntry entry = Balances[index];
                if (entry == null || entry.CurrencyId == CurrencyId.None || entry.Balance < 0 || !ids.Add(entry.CurrencyId))
                    throw new InvalidOperationException("货币快照包含非法或重复货币。");
            }
        }
    }

    /// <summary>单种货币快照数据。</summary>
    [Serializable]
    public sealed class CurrencyBalanceSaveEntry
    {
        /// <summary>货币标识。</summary>
        public CurrencyId CurrencyId { get; set; }
        /// <summary>余额。</summary>
        public int Balance { get; set; }
    }

    /// <summary>将 CurrencyManager 状态接入 SaveSystem。</summary>
    public sealed class CurrencySaveModule : SaveModule<CurrencySaveSnapshot>
    {
        private readonly CurrencyManager manager;

        /// <summary>创建货币存档模块。</summary>
        /// <param name="manager">货币 Manager。</param>
        public CurrencySaveModule(CurrencyManager manager)
            : base(new SaveModuleId("currency"), 1, SaveMissingModulePolicy.Required)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>采集货币状态。</summary>
        /// <returns>快照。</returns>
        protected override CurrencySaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new CurrencySaveSnapshot();
            IReadOnlyList<CurrencySettings.CurrencyRule> rules = manager.Rules;
            for (int index = 0; index < rules.Count; index++) snapshot.Balances.Add(new CurrencyBalanceSaveEntry { CurrencyId = rules[index].CurrencyId, Balance = manager.GetBalance(rules[index].CurrencyId) });
            return snapshot;
        }

        /// <summary>验证货币快照。</summary>
        /// <param name="snapshot">快照。</param>
        protected override void ValidateTypedSnapshot(CurrencySaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.ValidateShape();
            if (snapshot.Balances.Count != manager.Rules.Count) throw new InvalidOperationException("货币快照与当前货币配置不一致。");
            for (int index = 0; index < snapshot.Balances.Count; index++)
            {
                CurrencyBalanceSaveEntry entry = snapshot.Balances[index];
                CurrencySettings.CurrencyRule rule = manager.GetRuleForSave(entry.CurrencyId);
                if (entry.Balance > rule.MaxBalance) throw new InvalidOperationException($"货币 {entry.CurrencyId} 超过最大余额。");
            }
        }

        /// <summary>恢复已验证的货币状态。</summary>
        /// <param name="snapshot">快照。</param>
        protected override void RestoreTypedSnapshot(CurrencySaveSnapshot snapshot)
        {
            var balances = new Dictionary<CurrencyId, int>();
            for (int index = 0; index < snapshot.Balances.Count; index++) balances.Add(snapshot.Balances[index].CurrencyId, snapshot.Balances[index].Balance);
            manager.RestoreState(balances);
        }
    }
}
