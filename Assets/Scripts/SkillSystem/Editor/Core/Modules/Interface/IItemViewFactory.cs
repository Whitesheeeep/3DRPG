#if UNITY_EDITOR


namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 为一种具体 Item ViewData 创建对应的 UI Toolkit 小型视图。
    /// </summary>
    internal interface IItemViewFactory
    {
        /// <summary>
        /// 使用模块模板创建具体 Item View。
        /// </summary>
        ItemView Create(TrackViewData track, ItemViewData item, ElementFactory elements, CoordinateMapper mapper);
    }
}
#endif
