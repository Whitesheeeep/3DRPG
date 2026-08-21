using System.Collections.Generic;
using UnityEngine;

namespace RPG.InteractionSystem
{
    /// <summary>
    /// 为 Provider 提供场景对象和交互中心默认值的可选便利基类。
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class InteractableObject : MonoBehaviour, IInteractable
    {
        #region Provider 默认属性

        /// <inheritdoc />
        public GameObject InteractionObject => gameObject;

        /// <inheritdoc />
        public Transform InteractionOrigin => transform;

        #endregion

        #region Option 收集

        /// <summary>由具体 Provider 收集当前可提供的交互选项。</summary>
        /// <param name="context">本次查询的玩家、摄像机和检测范围上下文。</param>
        /// <param name="results">由调用方复用的候选集合。</param>
        public abstract void CollectInteractionOptions(in InteractionQueryContext context,
            List<InteractionOption> results);

        #endregion
    }
}
