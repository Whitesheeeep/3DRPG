using System;
using System.Collections.Generic;
using RPG.SaveSystem;
using UnityEngine;

namespace RPG.CurrencySystemNS
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
        #region 模块标识

        /// <summary>货币存档模块的稳定 ID。</summary>
        public static readonly SaveModuleId StableModuleId = new SaveModuleId("currency");

        #endregion

        #region 依赖字段

        private readonly CurrencyManager manager;

        #endregion

        /// <summary>创建货币存档模块。</summary>
        /// <param name="manager">货币 Manager。</param>
        /// <exception cref="ArgumentNullException">货币 Manager 为空时抛出。</exception>
        public CurrencySaveModule(CurrencyManager manager)
            : base(StableModuleId, 1, SaveMissingModulePolicy.Required)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>按配置顺序采集当前货币余额。</summary>
        /// <returns>包含每种已配置货币余额的快照。</returns>
        protected override CurrencySaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new CurrencySaveSnapshot();
            IReadOnlyList<CurrencySettings.CurrencyRule> rules = manager.Rules;
            // 按配置顺序读取，避免快照条目顺序受 Dictionary 迭代影响。
            for (int index = 0; index < rules.Count; index++)
            {
                snapshot.Balances.Add(new CurrencyBalanceSaveEntry
                {
                    CurrencyId = rules[index].CurrencyId,
                    Balance = manager.GetBalance(rules[index].CurrencyId)
                });
            }

            return snapshot;
        }

        /// <summary>将已校验的货币余额快照整体恢复到钱包。</summary>
        /// <param name="snapshot">已校验的当前版本快照。</param>
        protected override void RestoreTypedSnapshot(CurrencySaveSnapshot snapshot)
        {
            // 先构建完整替换表，再调用钱包统一入口发布恢复事件。
            // key：CurrencyId；value：该货币恢复后的余额。
            var balancesByCurrencyIdMap = new Dictionary<CurrencyId, int>();
            for (int index = 0; index < snapshot.Balances.Count; index++)
            {
                CurrencyBalanceSaveEntry entry = snapshot.Balances[index];
                balancesByCurrencyIdMap.Add(entry.CurrencyId, entry.Balance);
            }

            manager.RestoreState(balancesByCurrencyIdMap);
            Debug.Log($"[CurrencySaveModule] 已恢复货币余额，currencyCount={balancesByCurrencyIdMap.Count}。");
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

    }
}
