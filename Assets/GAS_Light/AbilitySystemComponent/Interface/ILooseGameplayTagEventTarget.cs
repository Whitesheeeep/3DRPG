using UnityEngine;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>
    /// 为角色提供 Loose GameplayTag 事件桥接所需的 ASC 和目标对象。
    /// 提供接收 Tag 事件的 ASC 和事件目标，桥接器不直接依赖具体角色类型。
    /// </summary>
    public interface ILooseGameplayTagEventTarget
    {
        /// <summary>获取接收 Tag 变更的 ASC。</summary>
        GameplayAbilitySystemComponent AbilitySystemComponent { get; }

        /// <summary>获取用于匹配外部事件 Target 的角色对象。</summary>
        GameObject TagEventTarget { get; }
    }
}
