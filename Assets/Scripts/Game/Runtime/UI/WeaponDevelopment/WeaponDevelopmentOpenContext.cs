using RPG.ItemSystem;

namespace RPG.Game.UI.WeaponDevelopment
{
    /// <summary>传入武器培养窗口的本次目标实例。</summary>
    public readonly struct WeaponDevelopmentOpenContext
    {
        /// <summary>创建武器培养打开上下文。</summary>
        /// <param name="weaponInstanceId">目标武器实例。</param>
        public WeaponDevelopmentOpenContext(EquipmentInstanceId weaponInstanceId)
        {
            WeaponInstanceId = weaponInstanceId;
        }

        /// <summary>获取目标武器实例。</summary>
        public EquipmentInstanceId WeaponInstanceId { get; }
    }
}
