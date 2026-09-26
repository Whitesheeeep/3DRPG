using System;
using RPG.Character.Animation;
using RPG.Markers;
using RPG.SkillSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;

namespace RPG.Character
{
    /// <summary>为玩家角色和 NPC 角色提供共用的 GAS Owner 能力组件入口。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖同节点 Humanoid Animator，以及同节点或子节点中的 ASC、MarkerProvider、SkillRuntimeHost 与 AnimationController。派生角色还需提供空间根节点、MotionDriver 和 FullBody Action Arbiter。")]
    public abstract class CharacterAbilityActor : MonoBehaviour, IGameplayAbilitySystemOwner
    {
        #region 依赖字段

        // Ability Owner 的共用表现与战斗依赖由玩家或 NPC 派生 Actor 绑定。
        [SerializeField] protected GameplayAbilitySystemComponent abilitySystemComponent;
        [SerializeField] protected Animator animator;
        [SerializeField] protected MarkerProvider markerProvider;
        [SerializeField] protected SkillRuntimeHost skillRuntimeHost;
        [SerializeField] protected AnimationController animationController;

        #endregion

        #region GAS Owner 属性

        /// <summary>获取角色独立 ASC。</summary>
        public GameplayAbilitySystemComponent AbilitySystemComponent
        {
            get
            {
                EnsureAbilityDependencies();
                return abilitySystemComponent;
            }
        }

        /// <summary>获取角色 Animator。</summary>
        public Animator Animator
        {
            get
            {
                EnsureAbilityDependencies();
                return animator;
            }
        }

        /// <inheritdoc />
        public IMarkerProvider MarkerProvider
        {
            get
            {
                EnsureAbilityDependencies();
                return markerProvider;
            }
        }

        /// <inheritdoc />
        public ISkillRuntimeHost SkillRuntimeHost
        {
            get
            {
                EnsureAbilityDependencies();
                return skillRuntimeHost;
            }
        }

        /// <inheritdoc />
        public IAnimationPlayer AnimationPlayer
        {
            get
            {
                EnsureAbilityDependencies();
                return animationController;
            }
        }

        /// <inheritdoc />
        public abstract Transform RootTransform { get; }

        /// <inheritdoc />
        public abstract IMotionDriver MotionDriver { get; }

        /// <inheritdoc />
        public abstract IFullBodyActionArbiter FullBodyActionArbiter { get; }

        #endregion

        #region 校验与内部辅助

        /// <summary>解析并校验 GAS Owner 共用的 Animator、ASC、动画与挂点依赖。</summary>
        protected void EnsureAbilityDependencies()
        {
            if (abilitySystemComponent == null)
                abilitySystemComponent = GetComponentInChildren<GameplayAbilitySystemComponent>(true);
            if (animator == null)
                animator = GetComponent<Animator>();
            if (markerProvider == null)
                markerProvider = GetComponentInChildren<MarkerProvider>(true);
            if (skillRuntimeHost == null)
                skillRuntimeHost = GetComponentInChildren<SkillRuntimeHost>(true);
            if (animationController == null)
                animationController = GetComponentInChildren<AnimationController>(true);

            if (animator == null || abilitySystemComponent == null || markerProvider == null ||
                skillRuntimeHost == null || animationController == null)
                throw new InvalidOperationException(
                    $"CharacterAbilityActor '{name}' 缺少同节点 Animator 或子树中的 ASC、MarkerProvider、SkillRuntimeHost、AnimationController。 ");

            // GAS 根运动必须由 MotionDriver 按当前技能控制权统一接收。
            animator.applyRootMotion = true;
        }

        #endregion
    }
}
