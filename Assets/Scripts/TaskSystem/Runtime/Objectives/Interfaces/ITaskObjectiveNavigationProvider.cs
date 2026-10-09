using UnityEngine;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 为当前未完成目标提供可选的世界导航 Transform。
    /// </summary>
    public interface ITaskObjectiveNavigationProvider
    {
        /// <summary>获取目标是否配置了导航意图，与目标对象当前是否已加载无关。</summary>
        bool HasNavigationTarget { get; }

        /// <summary>尝试解析该目标当前可用的导航 Transform。</summary>
        /// <param name="target">解析成功后的世界位置 Transform。</param>
        /// <param name="offset">导航锚点的偏移量。</param>
        /// <returns>目标对象当前已注册并可导航时返回 true。</returns>
        bool TryGetNavigationTarget(out Transform target, out Vector3 offset);
    }
}
