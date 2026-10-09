using System;
using RPG.ItemSystem;

namespace RPG.Game.Runtime.WeaponDevelopment
{
    /// <summary>表示一把武器已成功完成一次等级提升。</summary>
    public readonly struct WeaponUpgradedEventArgs
    {
        /// <summary>创建已提交的武器等级提升事实。</summary>
        /// <param name="instanceId">发生升级的装备实例。</param>
        /// <param name="definitionItemId">武器定义的稳定物品标识。</param>
        /// <param name="previousLevel">升级前等级。</param>
        /// <param name="newLevel">升级后等级。</param>
        /// <exception cref="ArgumentException">实例、定义标识或等级变化无效时抛出。</exception>
        public WeaponUpgradedEventArgs(
            EquipmentInstanceId instanceId,
            ItemId definitionItemId,
            int previousLevel,
            int newLevel)
        {
            if (!instanceId.IsValid)
                throw new ArgumentException("武器升级事件必须包含有效实例 ID。", nameof(instanceId));
            if (!definitionItemId.IsValid)
                throw new ArgumentException("武器升级事件必须包含有效武器定义 ID。", nameof(definitionItemId));
            if (previousLevel < 1 || newLevel <= previousLevel)
                throw new ArgumentException("武器升级事件必须表示等级实际提高。", nameof(newLevel));

            InstanceId = instanceId;
            DefinitionItemId = definitionItemId;
            PreviousLevel = previousLevel;
            NewLevel = newLevel;
        }

        /// <summary>获取发生升级的装备实例标识。</summary>
        public EquipmentInstanceId InstanceId { get; }

        /// <summary>获取武器定义的稳定 ItemId。</summary>
        public ItemId DefinitionItemId { get; }

        /// <summary>获取升级前等级。</summary>
        public int PreviousLevel { get; }

        /// <summary>获取升级后等级。</summary>
        public int NewLevel { get; }
    }
}
