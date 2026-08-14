using UnityEngine;

namespace RPG.Character
{
    /// <summary>
    /// 定义角色主动移动、动画根运动与固定阶段推进的统一边界。
    /// </summary>
    public interface IMotionDriver
    {
        /// <summary>获取当前 ASC Tag 是否允许角色进行水平移动。</summary>
        bool CanMoveHorizontally { get; }

        /// <summary>
        /// 按当前 ASC Tag 限制处理一次角色位移，通常在固定更新阶段调用。
        /// </summary>
        /// <param name="movement">本次希望应用的世界空间位移。</param>
        void FixedUpdateMove(Vector3 movement);

        /// <summary>读取 Animator 当前帧根运动，并按当前限制应用位移与旋转。</summary>
        void UpdateAnimationMove();
    }
}
