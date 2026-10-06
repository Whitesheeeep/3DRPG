namespace RPG.Character.Animation
{
    /// <summary>切换武器在角色骨骼上的手持与背负姿态。</summary>
    public interface IWeaponSwitch
    {
        /// <summary>将武器切换至手持姿态。</summary>
        void HoldWeapon();

        /// <summary>将武器切换至背负姿态。</summary>
        void CarryWeaponOnBack();
    }
}
