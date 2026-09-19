using System.Collections.Generic;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 作为纯 C# 单次执行对象推进整数帧、协调轨道处理器并等待 LateUpdate 完成本帧姿态检测。
    /// </summary>
    internal sealed class SkillExecution
    {
        #region 字段与状态

        private readonly SkillRuntimeContext context;
        private readonly List<ISkillTrackRuntimeHandler> handlers = new();
        private readonly List<int> pendingLateFrames = new();

        // 状态
        private IActionPhaseRuntimeState actionPhaseState;
        private float elapsedSeconds;
        // 当前执行的全局播放倍率，0 表示暂停，1 表示正常速度，2 表示两倍速。
        private float playbackSpeed = 1f;
        private int nextFrame;
        private bool reachedDurationBoundary;
        private bool completed;

        public ulong ExecutionId => context.ExecutionId;
        public SkillConfig Config => context.Request.Config;
        public GameObject Owner => context.Actor.Owner;
        public int CurrentFrame { get; private set; } = -1;
        public ActionPhaseType CurrentPhase => actionPhaseState?.CurrentPhase ?? ActionPhaseType.None;
        public SkillTransitionMask AllowedTransitions =>
            actionPhaseState?.AllowedTransitions ?? SkillTransitionMask.None;
        public bool CanCompleteNaturally => reachedDurationBoundary && pendingLateFrames.Count == 0;

        /// <summary>
        /// 获取按缩放后逻辑秒数计算的连续技能进度；整数帧仍是战斗判定的权威时间点。
        /// </summary>
        public float NormalizedTime
        {
            get
            {
                float durationSeconds = Config.DurationFrames / (float)Config.FrameRate;
                return Mathf.Clamp01(elapsedSeconds / durationSeconds);
            }
        }
        #endregion

        #region 创建

        /// <summary>
        /// 创建执行对象，并按固定类型顺序初始化本次执行独占的聚合轨道处理器。
        /// </summary>
        /// <param name="context">本次执行共享上下文。</param>
        /// <param name="playbackSpeed">本次执行开始时采用的全局播放倍率。</param>
        public SkillExecution(SkillRuntimeContext context, float playbackSpeed)
        {
            this.context = context;
            IReadOnlyList<ISkillTrackRuntimeHandler> createdHandlers = SkillRuntimeRegistry.CreateHandlers();
            for (int index = 0; index < createdHandlers.Count; index++)
            {
                ISkillTrackRuntimeHandler handler = createdHandlers[index];
                // 每个 Handler 从同一 Config 中收集自己的全部未静音轨道。
                handler.Initialize(context, context.Request.Config);
                handlers.Add(handler);
                actionPhaseState ??= handler as IActionPhaseRuntimeState;
            }

            // 在处理第 0 帧之前同步倍率，使首帧创建的动画与 VFX 使用正确速度。
            SetPlaybackSpeed(playbackSpeed);
        }

        #endregion

        #region 帧推进

        /// <summary>
        /// 在 Module 保存当前执行引用后同步处理第 0 帧，使帧零事件回调可以安全 Stop 或 Cancel。
        /// </summary>
        public void Start()
        {
            ProcessFrame(0);
            nextFrame = 1;
        }

        /// <summary>
        /// 按缩放时间推进并依次消费所有跨过的整数帧。
        /// </summary>
        /// <param name="deltaTime">外部驱动者提供的未缩放帧间隔；方法内部再乘以全局播放倍率。</param>
        public void Advance(float deltaTime)
        {
            if (completed || reachedDurationBoundary) return;

            elapsedSeconds += Mathf.Max(0f, deltaTime) * playbackSpeed;
            int targetFrame = Mathf.FloorToInt(elapsedSeconds * Config.FrameRate);
            int lastValidFrame = Config.DurationFrames - 1;
            while (!completed && nextFrame <= targetFrame && nextFrame <= lastValidFrame)
            {
                ProcessFrame(nextFrame);
                nextFrame++;
            }

            reachedDurationBoundary = targetFrame >= Config.DurationFrames;
        }

        /// <summary>
        /// 将通道倍率同步给本次执行的全部处理器，以立即更新动画和活动粒子。
        /// </summary>
        /// <param name="playbackSpeed">已经由 Module 校验的 0 到 2 倍率。</param>
        public void SetPlaybackSpeed(float playbackSpeed)
        {
            this.playbackSpeed = playbackSpeed;
            for (int index = 0; index < handlers.Count; index++)
                handlers[index].SetPlaybackSpeed(playbackSpeed);
        }

        /// <summary>
        /// 将攻击区域调试绘制设置同步到本次执行独占的 Physics 服务。
        /// </summary>
        /// <param name="enabled">是否绘制攻击查询形状。</param>
        /// <param name="duration">调试线框保留秒数；零表示当前帧。</param>
        public void SetAttackDetectionDebug(bool enabled, float duration)
        {
            context.AttackDetectionServices.SetDebugDrawing(enabled, duration);
        }

        /// <summary>
        /// 在 LateUpdate 中按原顺序提交所有等待姿态稳定的帧。
        /// </summary>
        public void ProcessLateFrames()
        {
            if (completed) return;

            for (int frameIndex = 0; frameIndex < pendingLateFrames.Count; frameIndex++)
            {
                int frame = pendingLateFrames[frameIndex];
                for (int handlerIndex = 0; handlerIndex < handlers.Count; handlerIndex++)
                {
                    if (completed) break;
                    handlers[handlerIndex].ProcessLateFrame(frame);
                }
                if (completed) break;
            }
            pendingLateFrames.Clear();
        }

        /// <summary>
        /// 结束全部轨道处理器并冻结当前执行对象；动画轨道会通过 IAnimationPlayer 立即停止技能层。
        /// </summary>
        /// <param name="reason">自然结束、Stop 或 Cancel。</param>
        public void Complete(SkillCompletionReason reason)
        {
            if (completed) return;
            completed = true;
            pendingLateFrames.Clear();
            for (int index = 0; index < handlers.Count; index++) handlers[index].Complete(reason);
        }

        /// <summary>
        /// 处理单个整数帧并将该帧排入 LateUpdate 姿态阶段。
        /// </summary>
        /// <param name="frame">需要处理的整数帧。</param>
        private void ProcessFrame(int frame)
        {
            CurrentFrame = frame;
            for (int index = 0; index < handlers.Count; index++) handlers[index].ProcessFrame(frame);
            pendingLateFrames.Add(frame);
        }

        #endregion
    }
}
