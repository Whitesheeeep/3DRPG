using System;
using RPG.RedDotSystemNS;
using UnityEngine;

namespace RPG.TaskSystemNS
{
    /// <summary>保存任务未读红点业务写入所需的主线与支线节点引用。</summary>
    [CreateAssetMenu(fileName = "TaskRedDotConfig", menuName = "RPG/TaskSystem/Task Red Dot Config", order = 40)]
    public sealed class TaskRedDotConfig : RedDotBusinessConfig
    {
        #region 配置字段
        [SerializeField] private RedDotKey mainUnreadKey;
        [SerializeField] private RedDotKey sideUnreadKey;
        #endregion

        #region 查询
        /// <summary>获取主线未读红点叶节点。</summary>
        public RedDotKey MainUnreadKey => mainUnreadKey;
        /// <summary>获取支线未读红点叶节点。</summary>
        public RedDotKey SideUnreadKey => sideUnreadKey;
        #endregion

        #region 配置校验
        /// <summary>检查主线与支线未读必须指向两个已配置的独立红点节点。</summary>
        /// <exception cref="InvalidOperationException">节点缺失或两类任务复用同一节点时抛出。</exception>
        public void Validate()
        {
            if (mainUnreadKey == null || sideUnreadKey == null || mainUnreadKey == sideUnreadKey)
            {
                const string message = "[TaskRedDotConfig] 必须分别配置不同的主线和支线未读红点节点。";
                Debug.LogError(message, this);
                throw new InvalidOperationException(
                    message);
            }
        }
        #endregion
    }
}
