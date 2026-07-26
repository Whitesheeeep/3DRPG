#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 把一批 Project 素材校验并转换为稳定的内容创建请求，不直接修改资产。
    /// </summary>
    internal interface ITrackDropHandler
    {
        /// <summary>
        /// 判断整批 Project 素材是否可以被该轨道接收。
        /// </summary>
        bool CanAccept(IReadOnlyList<UnityEngine.Object> assets);

        /// <summary>
        /// 把已校验素材复制为稳定的类型化创建请求。
        /// </summary>
        /// <param name="assets">已校验的素材列表。</param>
        /// <param name="startFrame">新内容所在的非负整数帧。</param>
        /// <returns>创建的类型化创建请求。</returns>
        IItemCreateRequest CreateRequest(IReadOnlyList<UnityEngine.Object> assets, int startFrame);
    }
}
#endif
