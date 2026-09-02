using System;

namespace RPG.ItemSystem
{
    /// <summary>武器和圣遗物共享的只读装备实例状态。</summary>
    public abstract class EquipmentInstance
    {
        /// <summary>创建装备实例状态。</summary>
        /// <param name="instanceId">独立实例标识。</param>
        /// <param name="definitionId">静态 Definition 标识。</param>
        /// <param name="level">当前等级。</param>
        /// <param name="currentExperience">当前等级内经验。</param>
        /// <param name="isLocked">是否锁定。</param>
        /// <param name="isNew">是否为新获得。</param>
        /// <param name="acquisitionSequence">获得顺序。</param>
        protected EquipmentInstance(
            EquipmentInstanceId instanceId,
            ItemId definitionId,
            int level,
            int currentExperience,
            bool isLocked,
            bool isNew,
            long acquisitionSequence)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Level = level;
            CurrentExperience = currentExperience;
            IsLocked = isLocked;
            IsNew = isNew;
            AcquisitionSequence = acquisitionSequence;
        }

        /// <summary>获取独立实例标识。</summary>
        public EquipmentInstanceId InstanceId { get; }

        /// <summary>获取对应 Definition 标识。</summary>
        public ItemId DefinitionId { get; }

        /// <summary>获取当前等级。</summary>
        public int Level { get; }

        /// <summary>获取当前等级内经验。</summary>
        public int CurrentExperience { get; }

        /// <summary>获取锁定状态。</summary>
        public bool IsLocked { get; }

        /// <summary>获取新获得状态。</summary>
        public bool IsNew { get; }

        /// <summary>获取获得顺序。</summary>
        public long AcquisitionSequence { get; }
    }
}
