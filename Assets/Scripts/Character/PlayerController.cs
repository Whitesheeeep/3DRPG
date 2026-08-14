using System;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;

namespace RPG.Character
{
    /// <summary>统一编排角色 ASC 的 Unity 更新时序，并持有角色移动驱动。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GameplayAbilitySystemComponent), typeof(Animator), typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        #region 序列化引用与属性

        [SerializeField] private GameplayAbilitySystemComponent abilitySystemComponent;
        [SerializeField] private Animator animator;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private MotionDriver motionDriver = new();

        /// <summary>获取由当前角色长期持有的移动驱动。</summary>
        public IMotionDriver MotionDriver => motionDriver;

        #endregion

        #region Unity 生命周期

        /// <summary>校验角色序列化依赖并构造不参与 Unity 生命周期的移动驱动。</summary>
        /// <exception cref="InvalidOperationException">任一必需角色依赖未配置时抛出。</exception>
        private void Awake()
        {
            // 同节点组件是 PlayerController 的稳定契约；序列化引用为空时按该契约自动取得。
            if (abilitySystemComponent == null)
                abilitySystemComponent = GetComponent<GameplayAbilitySystemComponent>();
            if (animator == null) animator = GetComponent<Animator>();
            if (characterController == null) characterController = GetComponent<CharacterController>();
            if (abilitySystemComponent == null || animator == null || characterController == null)
                throw new InvalidOperationException(
                    $"PlayerController '{name}' 必须配置 ASC、Animator 和 CharacterController。");

            motionDriver.Initialize(animator, characterController, abilitySystemComponent);
        }

        /// <summary>按角色控制器定义的顺序推进 ASC 普通阶段。</summary>
        private void Update() => abilitySystemComponent.Tick(Time.deltaTime);

        /// <summary>先推进 ASC 固定状态，再为后续 Locomotion 推进移动驱动固定阶段。</summary>
        private void FixedUpdate()
        {
            abilitySystemComponent.FixedTick(Time.fixedDeltaTime);
        }

        /// <summary>在 Animator 求值完成后让当前 Active Ability 决定是否消费根运动。</summary>
        private void OnAnimatorMove() => abilitySystemComponent.UpdateAnimationMove();

        /// <summary>在普通更新和 Animator 求值后推进 ASC 延迟阶段。</summary>
        private void LateUpdate() => abilitySystemComponent.LateTick(Time.deltaTime);

        #endregion
    }
}
