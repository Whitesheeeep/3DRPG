using UnityEngine;

namespace RPG.InteractionSystem
{
    /// <summary>
    /// 描述一次交互选项查询所需的玩家、位置、摄像机和检测范围。
    /// </summary>
    public readonly struct InteractionQueryContext
    {
        #region 属性

        /// <summary>获取发起查询的玩家对象。</summary>
        public GameObject Interactor { get; }

        /// <summary>获取发起查询的玩家 Transform。</summary>
        public Transform InteractorTransform { get; }

        /// <summary>获取用于 Viewport 和遮挡判断的摄像机。</summary>
        public Camera ViewCamera { get; }

        /// <summary>获取玩家侧检测器使用的粗筛范围。</summary>
        public float DetectionRange { get; }

        #endregion

        #region 构造

        /// <summary>创建一次 Provider Option 查询上下文。</summary>
        /// <param name="interactor">发起查询的玩家对象。</param>
        /// <param name="interactorTransform">发起查询的玩家 Transform。</param>
        /// <param name="viewCamera">用于视野判断的摄像机。</param>
        /// <param name="detectionRange">玩家侧检测器的粗筛范围。</param>
        public InteractionQueryContext(GameObject interactor, Transform interactorTransform,
            Camera viewCamera, float detectionRange)
        {
            Interactor = interactor;
            InteractorTransform = interactorTransform;
            ViewCamera = viewCamera;
            DetectionRange = detectionRange;
        }

        #endregion
    }
}
