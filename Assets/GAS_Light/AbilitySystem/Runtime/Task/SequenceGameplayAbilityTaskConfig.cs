using System;
using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>按作者配置顺序创建并执行一组子 Task。</summary>
    [Serializable]
    public sealed class SequenceGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
    {
        #region 字段与属性
        [SerializeReference, Tooltip("按列表顺序执行；空列表会立即完成。")]
        private List<GameplayAbilityTaskConfig> children = new();

        /// <summary>获取只读子 Task Config 列表。</summary>
        public IReadOnlyList<GameplayAbilityTaskConfig> Children => children;

        // Sequence 递归检查空子项和内置 Config 配置。
        internal override bool IsConfigurationValid
        {
            get
            {
                for (int i = 0; i < children.Count; i++)
                    if (children[i] == null || !children[i].IsConfigurationValid)
                        return false;
                return true;
            }
        }
        #endregion

        #region 构造与工厂
        /// <summary>创建可由 Unity 反序列化的空 Sequence。</summary>
        public SequenceGameplayAbilityTaskConfig()
        {
        }

        /// <summary>使用指定子配置创建 Sequence，主要用于代码测试。</summary>
        public SequenceGameplayAbilityTaskConfig(
            IEnumerable<GameplayAbilityTaskConfig> definitions)
        {
            children = new List<GameplayAbilityTaskConfig>(definitions);
        }

        // 每次激活都从全部 Config 创建独立子 Task。
        protected override GameplayAbilityTask CreateTask(
            AsynchronousGameplayAbilityRuntime runtime) =>
            new SequenceGameplayAbilityTask(runtime, children);
        #endregion
    }
}
