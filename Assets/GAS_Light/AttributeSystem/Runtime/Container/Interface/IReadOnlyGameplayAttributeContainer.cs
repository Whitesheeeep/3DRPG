using System.Collections.Generic;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>提供 Attribute 运行时数据的只读查询能力。</summary>
    public interface IReadOnlyGameplayAttributeContainer
    {
        /// <summary>获取已初始化 Attribute 数量。</summary>
        int Count { get; }

        /// <summary>获取运行时 Definition 的只读列表视图。</summary>
        IReadOnlyList<GameplayAttributeDefinition> Attributes { get; }

        /// <summary>判断容器是否包含指定 Attribute。</summary>
        /// <param name="attribute">待查询 Attribute。</param>
        /// <returns>存在时返回 true。</returns>
        bool Contains(GameplayAttribute attribute);

        /// <summary>尝试取得指定 Attribute 的只读运行时 Definition。</summary>
        /// <param name="attribute">待查询 Attribute。</param>
        /// <param name="definition">成功时返回运行时 Definition 引用。</param>
        /// <returns>存在时返回 true。</returns>
        bool TryGetDefinition(
            GameplayAttribute attribute,
            out GameplayAttributeDefinition definition);

        /// <summary>尝试取得指定 Attribute 的 CurrentValue。</summary>
        /// <param name="attribute">待查询 Attribute。</param>
        /// <param name="value">成功时返回 CurrentValue。</param>
        /// <returns>存在时返回 true。</returns>
        bool TryGetCurrentValue(GameplayAttribute attribute, out float value);
    }
}
