using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>配置蓄力技能准备阶段使用的单段 Animancer Transition。</summary>
    [System.Serializable]
    public sealed class ChargePreparationGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
    {
        #region 配置字段

        [SerializeField, Required, Tooltip("准备动画 Transition；其 End Event 标记准备阶段结束。")]
        private TransitionAsset preparationTransition;

        #endregion

        #region 配置校验与工厂

        /// <summary>检查 Transition 类型、播放速度和显式 End Event 是否满足准备阶段契约。</summary>
        internal override bool IsConfigurationValid => IsPreparationTransitionValid();

        /// <summary>创建只保存 Transition 快照、尚未播放动画的准备 Task。</summary>
        /// <param name="runtime">拥有本 Task 的异步 Ability Runtime。</param>
        /// <returns>本次激活独立的准备 Task。</returns>
        protected override GameplayAbilityTask CreateTask(AsynchronousGameplayAbilityRuntime runtime) =>
            new ChargePreparationGameplayAbilityTask(runtime, preparationTransition);

        /// <summary>验证准备 Transition 是有效 ClipTransition，并配置了片段内部的 End Event。</summary>
        /// <returns>全部配置有效时返回 true。</returns>
        private bool IsPreparationTransitionValid()
        {
            if (preparationTransition == null)
                return false;

            ITransition transition = preparationTransition.GetTransition();
            if (transition is not ClipTransition clipTransition || !preparationTransition.IsValid)
                return false;

            float speed = clipTransition.Speed;
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f)
                return false;

            AnimancerEvent.Sequence.Serializable serializedEvents = clipTransition.SerializedEvents;
            if (serializedEvents == null)
                return false;

            float[] normalizedTimes = serializedEvents.NormalizedTimes;
            if (normalizedTimes == null || normalizedTimes.Length == 0)
                return false;

            // End Event 必须落在素材循环回绕之前，避免把完整素材结尾误当作准备切分点。
            float normalizedEndTime = normalizedTimes[^1];
            return !float.IsNaN(normalizedEndTime) && !float.IsInfinity(normalizedEndTime) &&
                   normalizedEndTime > 0f && normalizedEndTime < 1f;
        }

        #endregion
    }
}
