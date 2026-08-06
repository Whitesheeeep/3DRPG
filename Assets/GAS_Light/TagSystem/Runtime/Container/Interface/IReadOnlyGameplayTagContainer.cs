using System.Collections.Generic;

namespace WS_Modules.GAS.TAG
{
    /// <summary>定义 Gameplay Tag 容器的只读集合与匹配能力。</summary>
    public interface IReadOnlyGameplayTagContainer
    {
        /// <summary>获取显式拥有的标签只读集合。</summary>
        IReadOnlyCollection<GameplayTag> Tags { get; }
        /// <summary>获取由显式标签派生的隐式祖先只读集合。</summary>
        IReadOnlyCollection<GameplayTag> ParentTags { get; }
        /// <summary>获取显式标签数量。</summary>
        int Count { get; }
        /// <summary>获取容器是否不含任何显式标签。</summary>
        bool IsEmpty { get; }
        /// <summary>判断容器是否拥有指定标签或能满足该标签的子标签。</summary>
        bool HasTag(GameplayTag tag);
        /// <summary>判断容器是否显式拥有指定标签。</summary>
        bool HasTagExact(GameplayTag tag);
    }
}