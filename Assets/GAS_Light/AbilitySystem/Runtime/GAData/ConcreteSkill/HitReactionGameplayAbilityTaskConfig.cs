using System;
using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>描述四方向受击动画选择顺序。</summary>
    public enum E_HitReactionDirection
    {
        /// <summary>从角色前方来袭。</summary>
        Forward = 0,
        /// <summary>从角色后方来袭。</summary>
        Backward = 1,
        /// <summary>从角色左侧来袭。</summary>
        Left = 2,
        /// <summary>从角色右侧来袭。</summary>
        Right = 3
    }

    /// <summary>保存四向受击动画与 GA 阻断解除时间配置。</summary>
    [Serializable]
    public sealed class HitReactionGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
    {
        #region 配置字段

        [SerializeField, Required, LabelText("前方受击 Transition")]
        private TransitionAsset forwardTransition;
        [SerializeField, Required, LabelText("后方受击 Transition")]
        private TransitionAsset backwardTransition;
        [SerializeField, Required, LabelText("左侧受击 Transition")]
        private TransitionAsset leftTransition;
        [SerializeField, Required, LabelText("右侧受击 Transition")]
        private TransitionAsset rightTransition;
        [SerializeField, FormerlySerializedAs("sprintCancelNormalizedTime"), Range(0f, 1f),
         LabelText("解除 GA 阻断的归一化时间")]
        private float abilityCancelNormalizedTime = 0.6f;
        [SerializeField, MinValue(0f), LabelText("动作层淡出时长（秒）")]
        private float fadeOutDuration = 0.2f;

        #endregion

        #region 配置校验与 Task 工厂

        /// <summary>在 GAS 提交 Cost/Cooldown 前确认四个方向资源和时间参数有效。</summary>
        internal override bool IsConfigurationValid =>
            IsTransitionValid(forwardTransition) && IsTransitionValid(backwardTransition) &&
            IsTransitionValid(leftTransition) && IsTransitionValid(rightTransition) &&
            IsFinite(abilityCancelNormalizedTime) && abilityCancelNormalizedTime >= 0f &&
            abilityCancelNormalizedTime <= 1f && IsFinite(fadeOutDuration) && fadeOutDuration >= 0f;

        /// <summary>为本次 GA 激活复制动画与阻断解除阈值快照。</summary>
        /// <param name="runtime">拥有该 Task 的异步 Runtime。</param>
        /// <returns>使用当前四方向配置快照的新 Task。</returns>
        protected override GameplayAbilityTask CreateTask(AsynchronousGameplayAbilityRuntime runtime) =>
            new HitReactionGameplayAbilityTask(
                runtime,
                new[] { forwardTransition, backwardTransition, leftTransition, rightTransition },
                abilityCancelNormalizedTime,
                fadeOutDuration);

        /// <summary>确认 Transition 是有效、正向播放且非循环的单片段动画。</summary>
        /// <param name="transition">待验证的动画资源。</param>
        /// <returns>动画可以作为一次性受击表现时返回 true。</returns>
        private static bool IsTransitionValid(TransitionAsset transition)
        {
            if (transition == null || !transition.IsValid) return false;
            ITransition transitionData = transition.GetTransition();
            return transitionData is ClipTransition && !transitionData.IsLooping &&
                   IsFinite(transitionData.Speed) && transitionData.Speed > 0f;
        }

        /// <summary>判断时间参数是否为有限数。</summary>
        /// <param name="value">待验证数值。</param>
        /// <returns>数值既非 NaN 也非 Infinity 时返回 true。</returns>
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
