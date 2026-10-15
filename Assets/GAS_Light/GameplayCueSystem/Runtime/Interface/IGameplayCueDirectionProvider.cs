using UnityEngine;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>由具体 Cue 表现行为提供初始世界方向，在通用摆放完成后决定视觉对象的朝向。</summary>
    public interface IGameplayCueDirectionProvider
    {
        /// <summary>解析本次 Cue 的世界方向；每次对象池取出时调用一次，不负责持续追踪。</summary>
        /// <param name="request">包含本次来源、目标和显式空间信息的 Cue 请求。</param>
        /// <param name="direction">成功时返回有效的世界空间单位向量，不包含资源旋转偏移。</param>
        /// <returns>提供方向时返回 true；无法解析时返回 false，保留通用摆放的旋转。</returns>
        bool TryGetWorldDirection(GameplayCueRequest request, out Vector3 direction);
    }
}
