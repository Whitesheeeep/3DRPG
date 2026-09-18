using RPG.ItemSystem;

namespace RPG.Game.UI.WeaponDevelopment
{
    /// <summary>统一装备培养窗口支持的目标类型。</summary>
    public enum EquipmentDevelopmentTargetKind
    {
        /// <summary>武器目标。</summary>
        Weapon,
        /// <summary>圣遗物目标。</summary>
        Artifact
    }

    /// <summary>传入统一装备培养窗口的本次目标实例。</summary>
    public readonly struct EquipmentDevelopmentOpenContext
    {
        /// <summary>创建装备培养打开上下文。</summary>
        /// <param name="targetKind">目标装备类型。</param>
        /// <param name="instanceId">目标装备实例。</param>
        private EquipmentDevelopmentOpenContext(EquipmentDevelopmentTargetKind targetKind, EquipmentInstanceId instanceId)
        {
            if (!instanceId.IsValid) throw new System.ArgumentException("装备实例标识无效。", nameof(instanceId));
            TargetKind = targetKind;
            InstanceId = instanceId;
        }

        /// <summary>创建武器培养上下文。</summary>
        /// <param name="instanceId">目标武器实例。</param>
        /// <returns>武器培养上下文。</returns>
        public static EquipmentDevelopmentOpenContext ForWeapon(EquipmentInstanceId instanceId) =>
            new EquipmentDevelopmentOpenContext(EquipmentDevelopmentTargetKind.Weapon, instanceId);

        /// <summary>创建圣遗物培养上下文。</summary>
        /// <param name="instanceId">目标圣遗物实例。</param>
        /// <returns>圣遗物培养上下文。</returns>
        public static EquipmentDevelopmentOpenContext ForArtifact(EquipmentInstanceId instanceId) =>
            new EquipmentDevelopmentOpenContext(EquipmentDevelopmentTargetKind.Artifact, instanceId);

        /// <summary>获取目标装备类型。</summary>
        public EquipmentDevelopmentTargetKind TargetKind { get; }
        /// <summary>获取目标装备实例。</summary>
        public EquipmentInstanceId InstanceId { get; }
    }
}
