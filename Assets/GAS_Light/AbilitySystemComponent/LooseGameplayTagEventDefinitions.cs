using UnityEngine;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>
    /// 表示一次 Loose GameplayTag 来源引用的增减操作。
    /// </summary>
    public enum LooseGameplayTagChangeOperation
    {
        /// <summary>为来源增加一份 Tag 引用。</summary>
        Add,

        /// <summary>移除来源已经增加的一份 Tag 引用。</summary>
        Remove
    }

    /// <summary>
    /// 请求目标 ASC 为指定来源增减一个 Loose GameplayTag。
    /// </summary>
    public readonly struct LooseGameplayTagChangeRequestedEventArgs
    {
        /// <summary>
        /// 创建 Loose GameplayTag 变更请求。
        /// </summary>
        /// <param name="target">应接收 Tag 的角色对象。</param>
        /// <param name="sourceId">负责本次引用的稳定来源标识。</param>
        /// <param name="tag">待增加或移除的 Tag。</param>
        /// <param name="operation">本次增减操作。</param>
        public LooseGameplayTagChangeRequestedEventArgs(
            GameObject target,
            string sourceId,
            GameplayTag tag,
            LooseGameplayTagChangeOperation operation)
        {
            Target = target;
            SourceId = sourceId ?? string.Empty;
            Tag = tag;
            Operation = operation;
        }

        /// <summary>获取请求目标对象。</summary>
        public GameObject Target { get; }

        /// <summary>获取负责本次引用的来源标识。</summary>
        public string SourceId { get; }

        /// <summary>获取待处理的 GameplayTag。</summary>
        public GameplayTag Tag { get; }

        /// <summary>获取本次增加或移除操作。</summary>
        public LooseGameplayTagChangeOperation Operation { get; }
    }
}
