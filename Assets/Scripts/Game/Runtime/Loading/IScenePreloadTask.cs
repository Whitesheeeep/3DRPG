using Cysharp.Threading.Tasks;

namespace RPG.Game.Loading
{
    /// <summary>
    /// 场景加载流程可统一等待的异步预热任务契约。
    /// </summary>
    public interface IScenePreloadTask
    {
        /// <summary>
        /// 执行当前系统的预热任务。
        /// </summary>
        /// <returns>任务完成时表示当前预热已经结束。</returns>
        UniTask PreloadAsync();
    }
}
