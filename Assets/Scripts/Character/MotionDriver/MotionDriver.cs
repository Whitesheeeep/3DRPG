using System;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;

namespace RPG.Character
{
    /// <summary>根据 ASC Tag 限制统一处理角色主动位移与 Animator 根运动。</summary>
    [Serializable]
    public sealed class MotionDriver : IMotionDriver
    {
        #region 序列化配置与运行时依赖

        [SerializeField, Tooltip("拥有任意配置 Tag 或其子 Tag 时清零 X/Z 位移，但保留 Y 位移与根旋转。")]
        private GameplayTag[] horizontalMovementBlockedTags = Array.Empty<GameplayTag>();

        [SerializeField, Tooltip("拥有任意配置 Tag 或其子 Tag 时阻止全部位移与 Animator 根旋转。")]
        private GameplayTag[] allMovementBlockedTags = Array.Empty<GameplayTag>();

        [NonSerialized] private Animator animator;
        [NonSerialized] private CharacterController characterController;
        [NonSerialized] private GameplayAbilitySystemComponent abilitySystemComponent;

        #endregion

        #region 状态查询

        /// <inheritdoc />
        public bool CanMoveHorizontally =>
            !HasAnyTag(allMovementBlockedTags) &&
            !IsHorizontalMovementBlocked;

        /// <summary>获取固定的移动阻断状态与作者配置状态的合并结果。</summary>
        private bool IsHorizontalMovementBlocked =>
            HasTag(GameplayTags.Tag_State_Block_Movement) ||
            HasAnyTag(horizontalMovementBlockedTags);

        /// <summary>获取当前是否由最高优先级 Tag 阻止全部位移和根旋转。</summary>
        private bool IsAllMovementBlocked => HasAnyTag(allMovementBlockedTags);

        #endregion

        #region 初始化与推进

        /// <summary>注入当前角色长期持有的动画、碰撞和 ASC 运行时依赖。</summary>
        /// <param name="sourceAnimator">提供根运动增量的 Animator。</param>
        /// <param name="sourceCharacterController">负责应用角色位移的 CharacterController。</param>
        /// <param name="sourceAbilitySystemComponent">提供当前 Owner GameplayTag 的 ASC。</param>
        /// <exception cref="ArgumentNullException">任一必需依赖缺失时抛出。</exception>
        public void Initialize(
            Animator sourceAnimator,
            CharacterController sourceCharacterController,
            GameplayAbilitySystemComponent sourceAbilitySystemComponent)
        {
            animator = sourceAnimator ?? throw new ArgumentNullException(nameof(sourceAnimator));
            characterController = sourceCharacterController ??
                throw new ArgumentNullException(nameof(sourceCharacterController));
            abilitySystemComponent = sourceAbilitySystemComponent ??
                throw new ArgumentNullException(nameof(sourceAbilitySystemComponent));
        }

        #endregion

        #region 公开操作

        /// <inheritdoc />
        public void FixedUpdateMove(Vector3 movement)
        {
            if (IsAllMovementBlocked || !characterController.enabled) return;

            // 水平限制只删除 X/Z，使重力和其他垂直位移仍能正常结算。
            if (IsHorizontalMovementBlocked)
                movement = new Vector3(0f, movement.y, 0f);
            characterController.Move(movement);
        }

        /// <inheritdoc />
        public void UpdateAnimationMove()
        {
            if (IsAllMovementBlocked || !characterController.enabled) return;

            // 位移统一经过 Move，使技能根运动和普通移动共享同一套 Tag 限制。
            FixedUpdateMove(animator.deltaPosition);
            characterController.transform.rotation *= animator.deltaRotation;
        }

        #endregion

        #region Tag 判断

        /// <summary>判断 ASC 是否拥有数组中任一 Tag 或其层级匹配 Tag。</summary>
        /// <param name="tags">作者配置的移动限制标签。</param>
        /// <returns>任一标签匹配时返回 true。</returns>
        private bool HasAnyTag(GameplayTag[] tags)
        {
            for (int index = 0; index < tags.Length; index++)
            {
                if (abilitySystemComponent.HasTag(tags[index])) return true;
            }
            return false;
        }

        /// <summary>判断 ASC 是否拥有指定 Tag 或其子标签。</summary>
        /// <param name="tag">待判断的固定移动规则 Tag。</param>
        /// <returns>存在匹配 Tag 时返回 true。</returns>
        private bool HasTag(GameplayTag tag) => abilitySystemComponent.HasTag(tag);

        #endregion
    }
}
