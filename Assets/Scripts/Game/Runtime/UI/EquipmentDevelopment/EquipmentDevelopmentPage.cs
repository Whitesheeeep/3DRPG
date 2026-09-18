namespace RPG.Game.UI.WeaponDevelopment
{
    /// <summary>装备培养窗口当前显示的一级页面；成长页内部再按等级状态显示升级或突破。</summary>
    public enum EquipmentDevelopmentPage
    {
        /// <summary>武器或圣遗物升级、武器突破共用的成长页。</summary>
        Growth,
        /// <summary>武器精炼。</summary>
        Refinement
    }

    /// <summary>成长页根据当前装备等级解析出的展示模式。</summary>
    public enum EquipmentGrowthMode
    {
        /// <summary>当前等级未达到阶段上限，可继续使用经验素材升级。</summary>
        Enhancement,
        /// <summary>当前等级已达到阶段上限，存在可用的下一突破阶段。</summary>
        Ascension,
        /// <summary>武器已达到 Definition 全局最大等级。</summary>
        MaxLevel,
        /// <summary>突破阶段配置不足以安全判断下一步操作。</summary>
        ConfigurationUnavailable
    }
}
