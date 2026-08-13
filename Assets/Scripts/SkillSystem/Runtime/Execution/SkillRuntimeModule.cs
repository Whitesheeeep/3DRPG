using System;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 托管单个角色执行通道，并由外部显式驱动普通帧和 Late 帧处理。
    /// </summary>
    public sealed class SkillRuntimeModule : ISkillRuntimeModule
    {
        #region 字段与事件

        // 稳定配置
        private SkillActorContext actorContext;
        private SkillAttackSettings attackSettings;

        // 执行状态
        private SkillExecution execution;
        private ulong nextExecutionId;
        private bool initialized;
        private bool disposed;

        /// <inheritdoc />
        public event Action<SkillHitEventArgs> HitDetected;

        /// <inheritdoc />
        public event Action<SkillCompletedEventArgs> Completed;

        /// <inheritdoc />
        public event Action<SkillActionPhaseChangedEventArgs> ActionPhaseChanged;

        #endregion

        #region 状态查询

        /// <inheritdoc />
        public bool IsPlaying => execution != null;

        /// <inheritdoc />
        public int CurrentFrame => execution?.CurrentFrame ?? 0;

        /// <inheritdoc />
        public ActionPhaseType CurrentPhase => execution?.CurrentPhase ?? ActionPhaseType.None;

        /// <inheritdoc />
        public bool CanBeInterrupted => execution?.CanBeInterrupted ?? false;

        #endregion

        #region 配置与播放

        /// <summary>
        /// 创建尚未绑定角色上下文的外部驱动技能运行时模块。
        /// </summary>
        public SkillRuntimeModule()
        {
        }

        /// <inheritdoc />
        public void Initialize(SkillActorContext actor, SkillAttackSettings attack)
        {
            ThrowIfDisposed();
            if (execution != null)
                throw new InvalidOperationException("不能在技能执行期间重新初始化 SkillRuntimeModule。");

            actorContext = actor;
            attackSettings = attack;
            initialized = true;
        }

        /// <inheritdoc />
        public void SetAttackTargetFilter(ISkillAttackTargetFilter filter)
        {
            ThrowIfDisposed();
            attackSettings = attackSettings.WithTargetFilter(filter);
        }

        /// <inheritdoc />
        public void SetAttackLayerMask(LayerMask layerMask)
        {
            ThrowIfDisposed();
            attackSettings = attackSettings.WithLayerMask(layerMask);
        }

        /// <inheritdoc />
        public SkillStartResult TryPlay(in SkillPlayRequest request)
        {
            ThrowIfDisposed();
            if (!initialized) return SkillStartResult.Failure("SkillRuntimeModule 尚未 Initialize。");
            if (execution != null) return SkillStartResult.Failure("SkillRuntimeModule 当前已有活动技能。");
            if (request.Config == null) return SkillStartResult.Failure("SkillPlayRequest 缺少 SkillConfig。");
            if (request.Config.FrameRate <= 0 || request.Config.DurationFrames <= 0)
                return SkillStartResult.Failure("SkillConfig 的 FPS 与总帧必须大于零。");

            ulong executionId = ++nextExecutionId;
            SkillRuntimeContext context = new(
                executionId,
                actorContext,
                request,
                attackSettings,
                PublishHit,
                PublishActionPhaseChanged);
            execution = new SkillExecution(context);

            // 必须先保存当前引用再处理第 0 帧，使帧零回调可以同步 Stop 或 Cancel。
            execution.Start();
            return SkillStartResult.Success();
        }

        /// <inheritdoc />
        public void Tick(float deltaTime)
        {
            ThrowIfDisposed();
            execution?.Advance(deltaTime);
        }

        /// <inheritdoc />
        public void LateTick()
        {
            ThrowIfDisposed();
            if (execution == null) return;

            SkillExecution activeExecution = execution;
            activeExecution.ProcessLateFrames();

            // 命中回调允许同步 Stop/Cancel；只允许仍归 Module 所有的原执行自然结束。
            if (execution == activeExecution && activeExecution.CanCompleteNaturally)
                CompleteExecution(SkillCompletionReason.Natural);
        }

        /// <inheritdoc />
        public void Stop()
        {
            ThrowIfDisposed();
            if (execution != null) CompleteExecution(SkillCompletionReason.Stopped);
        }

        /// <inheritdoc />
        public void Cancel()
        {
            ThrowIfDisposed();
            if (execution != null) CompleteExecution(SkillCompletionReason.Cancelled);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;

            // 先封闭公开入口，避免 Completed 回调在销毁过程中启动新的执行。
            disposed = true;
            if (execution != null) CompleteExecution(SkillCompletionReason.Cancelled);
        }

        #endregion

        #region 事件与结束

        /// <summary>
        /// 将完成过滤与 Clip 内去重的命中快照发送给当前 Module 监听者。
        /// </summary>
        /// <param name="args">本次命中的不可变运行时快照。</param>
        private void PublishHit(SkillHitEventArgs args)
        {
            HitDetected?.Invoke(args);
        }

        /// <summary>将当前执行同帧生效的动作阶段快照发送给 Module 监听者。</summary>
        /// <param name="args">阶段、帧和可打断状态快照。</param>
        private void PublishActionPhaseChanged(SkillActionPhaseChangedEventArgs args)
        {
            ActionPhaseChanged?.Invoke(args);
        }

        /// <summary>
        /// 结束当前执行并在清理完成后发布一次完成事件。
        /// </summary>
        /// <param name="reason">自然结束、主动停止或立即取消。</param>
        private void CompleteExecution(SkillCompletionReason reason)
        {
            SkillExecution completedExecution = execution;

            // 先释放活动槽位；完成事件回调因此可以立即启动下一技能。
            execution = null;
            completedExecution.Complete(reason);

            // 动画退出由外部状态机负责，Module 只发送已结束执行的不可变上下文。
            Completed?.Invoke(new SkillCompletedEventArgs(
                completedExecution.ExecutionId,
                completedExecution.Config,
                completedExecution.Owner,
                reason,
                completedExecution.CurrentFrame));
        }

        #endregion

        #region 生命周期校验

        /// <summary>
        /// 阻止已释放 Module 再次接收配置、播放或帧驱动调用。
        /// </summary>
        /// <exception cref="ObjectDisposedException">Module 已完成释放。</exception>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SkillRuntimeModule));
        }

        #endregion
    }
}
