using System;
using System.Collections.Generic;
using Animancer;
using RPG.Character;
using RPG.Character.Animation;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>播放命中来向对应的受击动画，并在配置时间后解除本次 Runtime 的 GA 阻断。</summary>
    public sealed class HitReactionGameplayAbilityTask : GameplayAbilityTask
    {
        #region 配置快照

        // 方向索引与 E_HitReactionDirection 保持一致，Task 不持有可变作者列表。
        private readonly TransitionAsset[] transitionByDirection;
        private readonly float abilityCancelNormalizedTime;
        private readonly float fadeOutDuration;
        #endregion

        #region 依赖与运行状态

        // Task 同时拥有运动通道和 FullBody 注册；两者在所有终态中成对归还。
        private CharacterActor character;
        private IMotionDriver motionDriver;
        private MotionControlHandle motionHandle;
        private FullBodyActionHandle fullBodyHandle;
        private AnimancerState animationState;
        private AnimancerEvent.Sequence animationEvents;
        private GameplayTag[] blockAbilityTagsToRelease = Array.Empty<GameplayTag>();
        private bool blockAbilityTagsReleased;

        #endregion

        #region 构造与生命周期

        /// <summary>创建保存动画、技能取消阈值和淡出时长快照的 Task。</summary>
        /// <param name="runtime">拥有该 Task 的异步 Ability Runtime。</param>
        /// <param name="transitions">按前、后、左、右排列的 Transition 快照。</param>
        /// <param name="abilityCancelTime">动画进度达到后解除 GA 阻断的归一化时间。</param>
        /// <param name="fadeDuration">释放 Action 层时使用的淡出秒数。</param>
        /// <exception cref="ArgumentNullException">动画 Transition 数组为空时抛出。</exception>
        /// <exception cref="ArgumentException">动画 Transition 数组不是四项时抛出。</exception>
        public HitReactionGameplayAbilityTask(
            AsynchronousGameplayAbilityRuntime runtime,
            TransitionAsset[] transitions,
            float abilityCancelTime,
            float fadeDuration)
            : base(runtime)
        {
            transitionByDirection = transitions ?? throw new ArgumentNullException(nameof(transitions));
            if (transitionByDirection.Length != 4)
                throw new ArgumentException("受击动画必须按前、后、左、右提供四项。", nameof(transitions));
            abilityCancelNormalizedTime = abilityCancelTime;
            fadeOutDuration = fadeDuration;
        }

        /// <summary>解析本次命中方向，申请角色控制权并开始对应受击动画。</summary>
        protected override void OnStart()
        {
            character = GARuntime.SourceOwner as CharacterActor ??
                throw new InvalidOperationException("[HitReactionTask] 受击 GA 只能由 CharacterActor 激活。");
            if (!GARuntime.TryGetSetByCaller(HitReactionGameplayTags.Direction, out float directionValue))
                throw new InvalidOperationException("[HitReactionTask] Runtime 缺少本次命中的方向 SetByCaller。");

            int directionIndex = Mathf.RoundToInt(directionValue);
            if (directionIndex < 0 || directionIndex >= transitionByDirection.Length)
                throw new InvalidOperationException(
                    $"[HitReactionTask] 受击方向索引 {directionValue} 超出 0..3。 ");

            TransitionAsset transition = transitionByDirection[directionIndex];
            if (transition == null || !transition.IsValid)
                throw new InvalidOperationException(
                    $"[HitReactionTask] 方向 {(E_HitReactionDirection)directionIndex} 缺少有效 Transition。");

            try
            {
                // Hit Reaction 暂停水平与旋转 Locomotion 结算，但不占用垂直通道或制造击退。
                motionDriver = character.MotionDriver ??
                    throw new InvalidOperationException("[HitReactionTask] 当前角色未绑定 MotionDriver。");
                motionHandle = motionDriver.RequestControl(new MotionControlRequest(
                    character,
                    MotionPriority.Skill,
                    MotionChannels.Horizontal | MotionChannels.Rotation));
                fullBodyHandle = character.FullBodyActionArbiter.RegisterFullBodyAction(GARuntime);

                // 复制 GA 自身已登记的阻断标签，后续仅解除当前 Runtime 的这一组阻断。
                var blockTagSnapshot = new List<GameplayTag>(GARuntime.BlockAbilityTags.Tags);
                blockAbilityTagsToRelease = blockTagSnapshot.ToArray();
                blockAbilityTagsReleased = false;

                animationState = GARuntime.SourceOwner.AnimationPlayer.Play(AnimationLayerType.Action, transition);
                animationState.Events(ref animationEvents);
                animationEvents.OnEnd += OnAnimationEnd;
                if (abilityCancelNormalizedTime <= 0f)
                    ReleaseBlockAbilityTags();
                Debug.Log(
                    $"[HitReactionTask] 受击动画已启动，direction={(E_HitReactionDirection)directionIndex}，" +
                    $"abilityCancelAt={abilityCancelNormalizedTime:F2}，" +
                    $"blockTagCount={blockAbilityTagsToRelease.Length}，" +
                    $"blockReleased={blockAbilityTagsReleased}，activationId={GARuntime.ActivationId}。",
                    character);
            }
            catch
            {
                ReleaseResources("StartFailed");
                throw;
            }
        }

        /// <summary>达到配置进度后解除当前受击 Runtime 的 GA 阻断。</summary>
        /// <param name="deltaTime">当前帧经过时间；窗口使用动画归一化进度判定。</param>
        protected override void OnTick(float deltaTime)
        {
            if (blockAbilityTagsReleased || animationState == null ||
                animationState.NormalizedTime < abilityCancelNormalizedTime)
                return;

            ReleaseBlockAbilityTags();
            Debug.Log(
                $"[HitReactionTask] 受击解除 GA 阻断，normalizedTime={animationState.NormalizedTime:F2}，" +
                $"activationId={GARuntime.ActivationId}。",
                character);
        }

        /// <summary>外部 Stop 时解除动画回调并释放运动与 FullBody 占据。</summary>
        protected override void OnStop() => ReleaseResources("Stop");

        /// <summary>被 Ability Cancel 时解除动画回调并释放运动与 FullBody 占据。</summary>
        protected override void OnCancel() => ReleaseResources("Cancel");

        /// <summary>动画自然结束时解除动画回调并释放运动与 FullBody 占据。</summary>
        protected override void OnComplete() => ReleaseResources("Complete");

        #endregion

        #region GA 阻断阶段

        /// <summary>仅解除本次受击 Runtime 初始登记的 GA 阻断标签。</summary>
        private void ReleaseBlockAbilityTags()
        {
            int releasedCount = 0;
            for (int tagIndex = 0; tagIndex < blockAbilityTagsToRelease.Length; tagIndex++)
            {
                GameplayTag tag = blockAbilityTagsToRelease[tagIndex];
                if (GARuntime.TryRemoveBlockAbilityTag(tag))
                {
                    releasedCount++;
                    continue;
                }

                Debug.LogWarning(
                    $"[HitReactionTask] 当前受击 Runtime 未能解除预期 BlockAbilityTag，tagId={tag.Id}，" +
                    $"activationId={GARuntime.ActivationId}。",
                    character);
            }

            blockAbilityTagsReleased = true;
            blockAbilityTagsToRelease = Array.Empty<GameplayTag>();
            Debug.Log(
                $"[HitReactionTask] 已解除当前受击 Runtime 的 GA 阻断，released={releasedCount}，" +
                $"activationId={GARuntime.ActivationId}。",
                character);
        }

        #endregion

        #region 动画结束与资源释放

        /// <summary>Animancer 自然结束时直接完成受击 Task。</summary>
        private void OnAnimationEnd()
        {
            Debug.Log($"[HitReactionTask] 受击动画自然结束，activationId={GARuntime.ActivationId}。", character);
            Complete();
        }

        /// <summary>解绑本 Task 回调、淡出仍归属本 Task 的动作并归还控制权。</summary>
        /// <param name="reason">触发清理的 Task 终态。</param>
        private void ReleaseResources(string reason)
        {
            if (animationEvents != null)
            {
                animationEvents.OnEnd -= OnAnimationEnd;
                animationEvents = null;
            }

            if (animationState != null)
            {
                if (animationState.Layer != null && animationState.Layer.CurrentState == animationState)
                    GARuntime.SourceOwner.AnimationPlayer.FadeLayer(AnimationLayerType.Action, 0f, fadeOutDuration);
                animationState = null;
            }

            motionHandle?.Dispose();
            motionHandle = null;
            motionDriver = null;
            fullBodyHandle?.Dispose();
            fullBodyHandle = null;
            if (character != null)
                Debug.Log(
                    $"[HitReactionTask] 受击资源已清理，原因={reason}，淡出={fadeOutDuration:F2}s，activationId={GARuntime.ActivationId}。",
                    character);
            character = null;
        }

        #endregion
    }
}
