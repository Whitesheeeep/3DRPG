using UnityEngine;

namespace RPG.InteractionSystem
{
    /// <summary>
    /// 描述一次交互选项查询所需的玩家、位置和摄像机。
    /// </summary>
    public readonly struct InteractionQueryContext
    {
        #region 属性

        /// <summary>获取 PlayerController 所在的稳定玩家对象，供业务查找参与者或物品接收器。</summary>
        public GameObject Interactor { get; }

        /// <summary>获取交互组件所在的移动角色 Transform；与稳定玩家对象的 Transform 可以不同。</summary>
        public Transform InteractorTransform { get; }

        /// <summary>获取供 Provider 查询的摄像机；当前玩家筛选不启用 Viewport 或遮挡判断。</summary>
        public Camera ViewCamera { get; }

        #endregion

        #region 构造

        /// <summary>创建一次 Provider Option 查询上下文。</summary>
        /// <param name="interactor">发起查询的稳定玩家对象。</param>
        /// <param name="interactorTransform">提供实时世界位置的移动角色 Transform，不要求属于 interactor 自身。</param>
        /// <param name="viewCamera">供 Provider 查询的摄像机。</param>
        public InteractionQueryContext(GameObject interactor, Transform interactorTransform,
            Camera viewCamera)
        {
            Interactor = interactor;
            InteractorTransform = interactorTransform;
            ViewCamera = viewCamera;
        }

        #endregion
    }
}
