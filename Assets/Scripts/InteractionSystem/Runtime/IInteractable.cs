using System.Collections.Generic;
using UnityEngine;

namespace RPG.InteractionSystem
{
    #region 交互契约

    /// <summary>
    /// 定义可被玩家交互检测器发现并贡献交互选项的 Provider 入口。
    /// </summary>
    public interface IInteractable
    {
        /// <summary>获取用于距离、视野和遮挡判断的目标对象。</summary>
        GameObject InteractionObject { get; }

        /// <summary>获取目标在场景中的交互中心。</summary>
        Transform InteractionOrigin { get; }

        /// <summary>把当前 Provider 能贡献的 Option 写入调用方提供的候选集合。</summary>
        /// <param name="context">本次查询的玩家、摄像机和检测范围上下文。</param>
        /// <param name="results">由调用方复用且只允许追加结果的候选集合。</param>
        void CollectInteractionOptions(in InteractionQueryContext context, List<InteractionOption> results);
    }
    #endregion
}
