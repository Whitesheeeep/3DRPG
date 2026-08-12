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
        private int nextFrame;
        private bool reachedDurationBoundary;
        private bool completed;

        public ulong ExecutionId => context.ExecutionId;
        public SkillConfig Config => context.Request.Config;
        public GameObject Owner => context.Actor.Owner;
        public int CurrentFrame { get; private set; } = -1;
        public ActionPhaseType CurrentPhase => actionPhaseState?.CurrentPhase ?? ActionPhaseType.None;
        public bool CanBeInterrupted => actionPhaseState?.CanBeInterrupted ?? false;
        public bool CanCompleteNaturally => reachedDurationBoundary && pendingLateFrames.Count == 0;

        #endregion

        #region 创建

        /// <summary>
        /// 创建执行对象，并按固定类型顺序初始化本次执行独占的聚合轨道处理器。
        /// </summary>
        /// <param name="context">本次执行共享上下文。</param>
        public SkillExecution(SkillRuntimeContext context)
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
        /// <param name="deltaTime">本帧 Time.deltaTime。</param>
        public void Advance(float deltaTime)
        {
            if (completed || reachedDurationBoundary) return;

            elapsedSeconds += Mathf.Max(0f, deltaTime);
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
        /// 结束全部轨道处理器并冻结当前执行对象，动画轨道不会在这里停止 Animancer。
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
