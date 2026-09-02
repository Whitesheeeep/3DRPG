using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.CurrencySystem
{
    /// <summary>账号货币的初始值与 int 上限配置。</summary>
    [CreateAssetMenu(fileName = "CurrencySettings", menuName = "RPG/Currency/Currency Settings", order = 0)]
    public sealed class CurrencySettings : ScriptableObject
    {
        [SerializeField, LabelText("货币规则")] private List<CurrencyRule> rules = new List<CurrencyRule>
        {
            new CurrencyRule(CurrencyId.Mola),
            new CurrencyRule(CurrencyId.YuanShi)
        };

        /// <summary>获取货币规则。</summary>
        public IReadOnlyList<CurrencyRule> Rules => rules;

        /// <summary>验证每种货币只配置一次且余额合法。</summary>
        /// <exception cref="InvalidOperationException">规则无效时抛出。</exception>
        public void Validate()
        {
            if (rules == null) throw new InvalidOperationException("CurrencySettings 的规则列表不能为 null。");
            var ids = new HashSet<CurrencyId>();
            for (int index = 0; index < rules.Count; index++)
            {
                CurrencyRule rule = rules[index];
                if (rule == null || rule.CurrencyId == CurrencyId.None || !ids.Add(rule.CurrencyId))
                    throw new InvalidOperationException("CurrencySettings 包含重复或无效货币规则。");
                rule.Validate();
            }
        }

        /// <summary>单种货币的上下限规则。</summary>
        [Serializable]
        public sealed class CurrencyRule
        {
            [SerializeField, LabelText("货币标识")] private CurrencyId currencyId;
            [SerializeField, MinValue(0), LabelText("初始余额")] private int initialBalance;
            [SerializeField, MinValue(1), LabelText("最大余额")] private int maxBalance = int.MaxValue;

            /// <summary>创建货币规则。</summary>
            public CurrencyRule(CurrencyId currencyId) => this.currencyId = currencyId;

            /// <summary>获取货币标识。</summary>
            public CurrencyId CurrencyId => currencyId;

            /// <summary>获取初始余额。</summary>
            public int InitialBalance => initialBalance;

            /// <summary>获取最大余额。</summary>
            public int MaxBalance => maxBalance;

            /// <summary>验证余额范围。</summary>
            /// <exception cref="InvalidOperationException">余额范围无效时抛出。</exception>
            public void Validate()
            {
                if (initialBalance < 0 || maxBalance <= 0 || initialBalance > maxBalance)
                    throw new InvalidOperationException($"货币 {currencyId} 的余额范围无效。");
            }
        }
    }
}
