using RPG.Game.Loading;

namespace RPG.Game.UI
{
    /// <summary>
    /// 项目窗口预加载服务契约，并作为通用场景预热任务参与场景加载编排。
    /// </summary>
    public interface IWindowPreloadService : IScenePreloadTask
    {
        /// <summary>获取当前窗口集合是否已经完成预加载。</summary>
        bool IsPreloaded { get; }
    }
}
