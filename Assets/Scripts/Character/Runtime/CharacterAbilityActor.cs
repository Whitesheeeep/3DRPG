using System;
using RPG.Character.Animation;
using RPG.Markers;
using RPG.SkillSystem;
using RPG.Character.Combat;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;

namespace RPG.Character
{
    /// <summary>为玩家角色和 NPC 角色提供共用的 GAS Owner 能力组件入口。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖同节点 Humanoid Animator，以及同节点或子节点中的 ASC、MarkerProvider、SkillRuntimeHost 与 AnimationController。派生角色还需提供空间根节点、MotionDriver 和 FullBody Action Arbiter。")]
    public abstract class CharacterAbilityActor : MonoBehaviour, IGameplayAbilitySystemOwner, IHitStopReceiver
    {
        #region 依赖字段

        // Ability Owner 的共用表现与战斗依赖由玩家或 NPC 派生 Actor 绑定。
        [SerializeField] protected GameplayAbilitySystemComponent abilitySystemComponent;
        [SerializeField] protected Animator animator;
        [SerializeField] protected MarkerProvider markerProvider;
        [SerializeField] protected SkillRuntimeHost skillRuntimeHost;
        [SerializeField] protected AnimationController animationController;
        private CharacterHitStopState hitStopState;

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
        public bool IsActionPaused => hitStopState?.IsActive ?? false;

        /// <inheritdoc />
        public bool IsActionPauseAppliedThisFrame => hitStopState?.WasAppliedThisFrame ?? false;

        /// <inheritdoc />
        public abstract Transform RootTransform { get; }

        /// <inheritdoc />
        public abstract IMotionDriver MotionDriver { get; }

        /// <inheritdoc />
        public abstract IFullBodyActionArbiter FullBodyActionArbiter { get; }

        #endregion

        #region 局部暂停

        /// <summary>延长当前角色局部卡帧的未缩放恢复截止时间。</summary>
        /// <param name="durationSeconds">卡帧持续时间，单位为秒。</param>
        /// <exception cref="ArgumentOutOfRangeException">时长不是有限正数时抛出。</exception>
        public void ApplyHitStop(float durationSeconds)
        {
            EnsureAbilityDependencies();
            hitStopState ??= new CharacterHitStopState(this, SetHitStopPaused);
            hitStopState.Apply(durationSeconds);
        }

        /// <summary>将角色动画图切换到 HitStop 对应的暂停状态。</summary>
        /// <param name="paused">是否暂停动画图的动作采样。</param>
        internal void SetHitStopPaused(bool paused)
        {
            if (animationController == null)
            {
                // OnDestroy 可能先于子级 AnimationController 销毁，解除暂停时允许跳过已销毁依赖。
                if (!paused) return;
                EnsureAbilityDependencies();
            }
            animationController.SetHitStopPaused(paused);
        }

        /// <summary>在派生角色销毁时取消卡帧恢复计时并恢复动画图速度。</summary>
        protected virtual void OnDestroy()
        {
            hitStopState?.Clear();
        }

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
