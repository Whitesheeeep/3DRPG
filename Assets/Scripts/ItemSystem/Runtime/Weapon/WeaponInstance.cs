namespace RPG.ItemSystem
{
    /// <summary>保存单把武器独立成长状态的装备实例。</summary>
    public sealed class WeaponInstance : EquipmentInstance
    {
        /// <summary>创建武器实例。</summary>
        /// <param name="instanceId">实例标识。</param>
        /// <param name="definitionId">武器 Definition 标识。</param>
        /// <param name="level">当前等级。</param>
        /// <param name="currentExperience">当前等级内经验。</param>
        /// <param name="ascensionRank">突破阶数。</param>
        /// <param name="refinementRank">精炼阶数。</param>
        /// <param name="isLocked">是否锁定。</param>
        /// <param name="acquisitionSequence">获得顺序。</param>
        internal WeaponInstance(EquipmentInstanceId instanceId, ItemId definitionId, int level, int currentExperience,
            int ascensionRank, int refinementRank, bool isLocked, long acquisitionSequence)
            : base(instanceId, definitionId, level, currentExperience, isLocked, acquisitionSequence)
        {
            AscensionRank = ascensionRank;
            RefinementRank = refinementRank;
        }

        /// <summary>获取突破阶数。</summary>
        public int AscensionRank { get; }

        /// <summary>获取精炼阶数。</summary>
        public int RefinementRank { get; }

    }
}
