namespace RPG.ItemSystem
{
    /// <summary>保存单件圣遗物基础成长状态的装备实例。</summary>
    public sealed class ArtifactInstance : EquipmentInstance
    {
        /// <summary>创建圣遗物实例。</summary>
        /// <param name="instanceId">实例标识。</param>
        /// <param name="definitionId">圣遗物 Definition 标识。</param>
        /// <param name="level">当前等级。</param>
        /// <param name="currentExperience">当前等级内经验。</param>
        /// <param name="isLocked">是否锁定。</param>
        /// <param name="isNew">是否新获得。</param>
        /// <param name="acquisitionSequence">获得顺序。</param>
        internal ArtifactInstance(EquipmentInstanceId instanceId, ItemId definitionId, int level, int currentExperience,
            bool isLocked, bool isNew, long acquisitionSequence)
            : base(instanceId, definitionId, level, currentExperience, isLocked, isNew, acquisitionSequence)
        {
        }
    }
}
