using System;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 为一个角色托管唯一活动 SkillExecution，并通过实例事件向外部状态机发布命中与结束通知。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkillRunner : MonoBehaviour
    {
        #region 字段与事件

        private SkillActorContext actorContext;
        private SkillAttackSettings attackSettings;
        private SkillExecution execution;
        private ulong nextExecutionId;
        private bool initialized;

        public event Action<SkillHitEventArgs> HitDetected;
        public event Action<SkillCompletedEventArgs> Completed;

        #endregion

        #region 状态查询

        public bool IsPlaying => execution != null;
        public int CurrentFrame => execution?.CurrentFrame ?? 0;
        public ActionPhaseType CurrentPhase => execution?.CurrentPhase ?? ActionPhaseType.None;
        public bool CanBeInterrupted => execution?.CanBeInterrupted ?? false;

        #endregion

        #region 生命周期

        /// <summary>
        /// 推进当前技能的普通帧处理；动画姿态相关检测延后到 LateUpdate。
        /// </summary>
        private void Update()
        {
            execution?.Advance(Time.deltaTime);
        }

        /// <summary>
        /// 在 Animator 本帧姿态稳定后执行攻击检测，并在最后有效帧检测完成后自然结束。
        /// </summary>
        private void LateUpdate()
        {
            if (execution == null) return;
            SkillExecution activeExecution = execution;
            activeExecution.ProcessLateFrames();
            // 命中回调允许同步 Stop/Cancel；只有原执行仍归 Runner 所有时才能继续自然结束判断。
            if (execution == activeExecution && activeExecution.CanCompleteNaturally)
                CompleteExecution(SkillCompletionReason.Natural);
        }

        /// <summary>
        /// 组件销毁时立即取消活动技能，确保池对象、音频和事件生命周期完整收口。
        /// </summary>
        private void OnDestroy()
        {
            if (execution != null) CompleteExecution(SkillCompletionReason.Cancelled);
        }

        #endregion

        #region 公开操作

        /// <summary>
        /// 初始化 Runner 的稳定角色依赖与攻击筛选设置。
        /// </summary>
        /// <param name="actor">角色、动画层、坐标根和 Marker Provider。</param>
        /// <param name="attack">LayerMask、Trigger 和可选业务过滤器。</param>
        public void Initialize(SkillActorContext actor, SkillAttackSettings attack)
        {
            if (execution != null)
                throw new InvalidOperationException("不能在技能执行期间重新初始化 SkillRunner。");
            actorContext = actor;
            attackSettings = attack;
            initialized = true;
        }

        /// <summary>
        /// 替换后续技能执行使用的业务目标过滤器；当前执行使用开始时冻结的设置快照。
        /// </summary>
        /// <param name="filter">新的过滤器；为空表示不做额外业务筛选。</param>
        public void SetAttackTargetFilter(ISkillAttackTargetFilter filter)
        {
            attackSettings = attackSettings.WithTargetFilter(filter);
        }

        /// <summary>
        /// 替换后续技能执行使用的 Physics LayerMask。
        /// </summary>
        /// <param name="layerMask">新的目标层掩码。</param>
        public void SetAttackLayerMask(LayerMask layerMask)
        {
            attackSettings = attackSettings.WithLayerMask(layerMask);
        }

        /// <summary>
        /// 尝试启动技能；Runner 忙碌时不会替换或中断现有执行。
        /// </summary>
        /// <param name="request">技能配置与当前武器节点。</param>
        /// <returns>成功状态或明确失败原因。</returns>
        public SkillStartResult TryPlay(in SkillPlayRequest request)
        {
            if (!initialized) return SkillStartResult.Failure("SkillRunner 尚未 Initialize。");
            if (execution != null) return SkillStartResult.Failure("SkillRunner 当前已有活动技能。");
            if (request.Config == null) return SkillStartResult.Failure("SkillPlayRequest 缺少 SkillConfig。");
            if (request.Config.FrameRate <= 0 || request.Config.DurationFrames <= 0)
                return SkillStartResult.Failure("SkillConfig 的 FPS 与总帧必须大于零。");

            ulong executionId = ++nextExecutionId;
            SkillRuntimeContext context = new(executionId, actorContext, request, attackSettings, PublishHit);
            execution = new SkillExecution(context);
            execution.Start();
            return SkillStartResult.Success();
        }

        /// <summary>
        /// 主动正常停止技能；VFX 尾迹与已开始音频允许自然结束。
        /// </summary>
        public void Stop()
        {
            if (execution != null) CompleteExecution(SkillCompletionReason.Stopped);
        }

        /// <summary>
        /// 立即取消技能并回收本次执行仍持有的动态资源。
        /// </summary>
        public void Cancel()
        {
            if (execution != null) CompleteExecution(SkillCompletionReason.Cancelled);
        }

        #endregion

        #region 事件与结束

        /// <summary>
        /// 将攻击处理器发布的实例命中转发给当前 Runner 监听者。
        /// </summary>
        /// <param name="args">已经完成全部过滤和去重的命中快照。</param>
        private void PublishHit(SkillHitEventArgs args)
        {
            HitDetected?.Invoke(args);
        }

        /// <summary>
        /// 先释放 Runner 当前执行引用，再发送结束事件，允许回调立即启动下一技能。
        /// </summary>
        /// <param name="reason">结束原因。</param>
        private void CompleteExecution(SkillCompletionReason reason)
        {
            SkillExecution completedExecution = execution;
            completedExecution.Complete(reason);
            execution = null;

            // 动画退出完全由监听者负责；Runner 只提供已结束技能的不可变上下文。
            Completed?.Invoke(new SkillCompletedEventArgs(
                completedExecution.ExecutionId,
                completedExecution.Config,
                completedExecution.Owner,
                reason,
                completedExecution.CurrentFrame));
        }

        #endregion
    }
}
