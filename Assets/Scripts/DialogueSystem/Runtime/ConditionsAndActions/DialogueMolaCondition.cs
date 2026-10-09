using System;
using RPG.CurrencySystemNS;
using UnityEngine;
using Sirenix.OdinInspector;

namespace RPG.DialogueSystemModule
{
    /// <summary>检查玩家摩拉余额是否达到对话选项要求。</summary>
    [Serializable]
    public sealed class DialogueMolaCondition : DialogueCondition
    {
        #region 配置字段

        [SerializeField, MinValue(1), LabelText("所需摩拉")]
        private int requiredMola = 1000;

        #endregion

        #region 构造

        /// <summary>创建可由 SerializeReference 命令抽屉实例化的摩拉条件。</summary>
        public DialogueMolaCondition()
        {
        }

        #endregion

        #region 配置校验与判断

        /// <summary>校验摩拉需求为正数。</summary>
        /// <exception cref="ArgumentOutOfRangeException">所需摩拉不是正数时抛出。</exception>
        public override void Validate()
        {
        }

        /// <summary>读取当前摩拉余额，并为未满足选项生成可展示原因。</summary>
        /// <param name="context">当前对话命令上下文。</param>
        /// <returns>余额满足要求时返回满足结果，否则返回需求提示。</returns>
        public override DialogueConditionResult Evaluate(DialogueCommandContext context)
        {
            int balance = CurrencyManager.Instance.GetBalance(CurrencyId.Mola);
            return balance >= requiredMola
                ? DialogueConditionResult.Met()
                : DialogueConditionResult.NotMet($"需要{requiredMola}摩拉");
        }

        #endregion
    }
}
