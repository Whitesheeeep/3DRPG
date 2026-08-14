using System;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>读取 Animator 当前帧根运动，并通过 CharacterController 应用完整位移与旋转。</summary>
    public sealed class MotionDriver
    {
        #region 字段 (依赖)

        private readonly Animator animator;
        private readonly CharacterController characterController;

        #endregion

        #region 构造

        /// <summary>创建绑定到指定动画器和角色碰撞控制器的移动驱动。</summary>
        /// <param name="animator">提供当前动画帧根运动增量的 Animator。</param>
        /// <param name="characterController">负责碰撞约束和角色根节点位移的控制器。</param>
        /// <exception cref="ArgumentNullException">任一运行依赖缺失时抛出。</exception>
        public MotionDriver(Animator animator, CharacterController characterController)
        {
            this.animator = animator ?? throw new ArgumentNullException(nameof(animator));
            this.characterController = characterController ??
                throw new ArgumentNullException(nameof(characterController));
        }

        #endregion

        #region 公开操作

        /// <summary>消费 Animator 当前帧的完整根位移与根旋转。</summary>
        public void UpdateAnimationMove()
        {
            // 位移必须经过 CharacterController，确保技能根运动仍遵守场景碰撞约束。
            characterController.Move(animator.deltaPosition);
            characterController.transform.rotation *= animator.deltaRotation;
        }

        #endregion
    }
}
