using System;
using RPG.Character;
using UnityEngine;
using WS_Modules.Utilities;

namespace RPG.Character.Combat
{
    /// <summary>
    /// 定义可接收局部卡帧请求的角色或测试宿主。
    /// </summary>
    public interface IHitStopReceiver
    {
        /// <summary>为当前宿主延长未缩放时间的局部暂停。</summary>
        /// <param name="durationSeconds">本次暂停请求的时长，单位为秒。</param>
        void ApplyHitStop(float durationSeconds);
    }

    /// <summary>
    /// 为单个角色维护未缩放时间截止点和可取消的恢复计时器。
    /// </summary>
    public sealed class CharacterHitStopState
    {
        #region 依赖字段

        // 宿主提供日志上下文和状态回调；TimerHandle 只控制该宿主的恢复时序。
        private readonly MonoBehaviour ownerContext;
        private readonly string ownerName;
        private readonly Action<bool> setPaused;

        #endregion

        #region 状态字段

        private TimerHandle recoveryTimer;
        private double deadline;
        private bool isActive;
        private int appliedFrame = -1;

        #endregion

        #region 构造函数与属性

        /// <summary>创建宿主专属卡帧状态。</summary>
        /// <param name="owner">持有当前暂停状态的角色或测试宿主。</param>
        /// <param name="setPaused">实际应用或恢复该宿主动作暂停的回调。</param>
        /// <exception cref="ArgumentNullException">任一依赖为空时抛出。</exception>
        public CharacterHitStopState(MonoBehaviour owner, Action<bool> setPaused)
        {
            ownerContext = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            ownerName = owner.name;
            this.setPaused = setPaused ?? throw new ArgumentNullException(nameof(setPaused));
        }

        /// <summary>获取当前角色是否仍处于局部暂停状态。</summary>
        public bool IsActive => isActive;

        /// <summary>判断暂停是否在当前 Unity 帧首次生效或被延长。</summary>
        public bool WasAppliedThisFrame => isActive && appliedFrame == Time.frameCount;

        #endregion

        #region 卡帧生命周期

        /// <summary>延长当前截止时间并安排一次未缩放恢复回调。</summary>
        /// <param name="durationSeconds">本次请求的暂停秒数。</param>
        /// <exception cref="ArgumentOutOfRangeException">时长不是有限正数时抛出。</exception>
        public void Apply(float durationSeconds)
        {
            if (durationSeconds <= 0f || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds,
                    "卡帧时长必须是有限正数。");

            double now = Time.unscaledTimeAsDouble;
            double proposedDeadline = now + durationSeconds;
            if (isActive && proposedDeadline <= deadline) return;

            bool wasActive = isActive;
            if (recoveryTimer.IsValid) recoveryTimer.Cancel();
            deadline = proposedDeadline;
            isActive = true;
            appliedFrame = Time.frameCount;
            recoveryTimer = TimerManager.Register(durationSeconds, OnRecoveryTimerElapsed)
                .SetUnscaledTime(true);

            if (!wasActive)
            {
                setPaused(true);
                Debug.Log($"[CharacterHitStopState] 宿主 '{ownerName}' 开始卡帧，duration={durationSeconds:F3}s。", ownerContext);
            }
            else
            {
                Debug.Log($"[CharacterHitStopState] 宿主 '{ownerName}' 延长卡帧，remaining={deadline - now:F3}s。", ownerContext);
            }
        }

        /// <summary>取消恢复回调并立即清除角色暂停状态。</summary>
        public void Clear()
        {
            bool hadState = isActive || recoveryTimer.IsValid;
            if (recoveryTimer.IsValid) recoveryTimer.Cancel();
            recoveryTimer = default;
            deadline = 0d;
            appliedFrame = -1;
            if (!isActive) return;

            isActive = false;
            setPaused(false);
            if (hadState)
                Debug.Log($"[CharacterHitStopState] 宿主 '{ownerName}' 结束卡帧。", ownerContext);
        }

        /// <summary>处理未缩放计时器回调，并在截止点尚未到达时补排剩余时间。</summary>
        private void OnRecoveryTimerElapsed()
        {
            recoveryTimer = default;
            if (!isActive) return;

            double remainingSeconds = deadline - Time.unscaledTimeAsDouble;
            if (remainingSeconds > 0d)
            {
                recoveryTimer = TimerManager.Register((float)remainingSeconds, OnRecoveryTimerElapsed)
                    .SetUnscaledTime(true);
                Debug.Log(
                    $"[CharacterHitStopState] 宿主 '{ownerName}' 恢复计时提前触发，重新安排 remaining={remainingSeconds:F3}s。",
                    ownerContext);
                return;
            }

            Clear();
        }

        #endregion
    }
}
