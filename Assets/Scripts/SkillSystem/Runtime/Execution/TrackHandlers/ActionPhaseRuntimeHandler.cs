using System;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 将动作阶段轨道解析为当前阶段和原生转换窗口，不将状态投影为 ASC GameplayTag。
    /// </summary>
    internal sealed class ActionPhaseRuntimeHandler : TrackRuntimeHandler<ActionPhaseTrackConfig>, IActionPhaseRuntimeState
    {
        private bool hasPublishedState;

        public ActionPhaseType CurrentPhase { get; private set; } = ActionPhaseType.None;
        public SkillTransitionMask AllowedTransitions { get; private set; }

        /// <summary>
        /// 在技能执行创建时校验动作阶段配置，避免无效位或空阶段权限进入逐帧状态。
        /// </summary>
        /// <exception cref="InvalidOperationException">动作阶段包含未知转换位、非法空阶段权限或重叠区间。</exception>
        protected override void OnInitialized()
        {
            const SkillTransitionMask definedMask =
                SkillTransitionMask.Move | SkillTransitionMask.Jump | SkillTransitionMask.Ability;

            for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
            {
                ActionPhaseTrackConfig track = Tracks[trackIndex];
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    ActionPhaseSkillClipConfig clip = track.Clips[clipIndex];
                    SkillTransitionMask unknownBits = clip.AllowedTransitions & ~definedMask;
                    if (unknownBits != SkillTransitionMask.None)
                        throw new InvalidOperationException(
                            $"SkillConfig '{Context.Request.Config.name}' 的动作阶段 Clip '{clip.Id}' 包含未知转换位 {unknownBits}。");
                    if (clip.Phase == ActionPhaseType.None &&
                        clip.AllowedTransitions != SkillTransitionMask.None)
                        throw new InvalidOperationException(
                            $"SkillConfig '{Context.Request.Config.name}' 的动作阶段 Clip '{clip.Id}' 不能在 Phase.None 中配置转换窗口。");

                    for (int otherClipIndex = clipIndex + 1;
                         otherClipIndex < track.Clips.Count;
                         otherClipIndex++)
                    {
                        ActionPhaseSkillClipConfig otherClip = track.Clips[otherClipIndex];
                        bool overlaps = clip.StartFrame < otherClip.EndFrame &&
                                         otherClip.StartFrame < clip.EndFrame;
                        if (overlaps)
                            throw new InvalidOperationException(
                                $"SkillConfig '{Context.Request.Config.name}' 的动作阶段轨道存在重叠区间："
                                + $"'{clip.Id}' 与 '{otherClip.Id}'。");
                    }
                }
            }
        }

        /// <summary>
        /// 查找覆盖当前帧的动作阶段；空白区间恢复 None 和无转换窗口。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessFrame(int frame)
        {
            ActionPhaseType nextPhase = ActionPhaseType.None;
            SkillTransitionMask nextAllowedTransitions = SkillTransitionMask.None;
            for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
            {
                ActionPhaseTrackConfig track = Tracks[trackIndex];
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    ActionPhaseSkillClipConfig clip = track.Clips[clipIndex];
                    if (frame < clip.StartFrame || frame >= clip.EndFrame) continue;

                    // 首个有效 Clip 按统一轨道物理顺序获得阶段查询优先权。
                    nextPhase = clip.Phase;
                    nextAllowedTransitions = clip.AllowedTransitions;
                    trackIndex = Tracks.Count;
                    break;
                }
            }

            if (hasPublishedState && CurrentPhase == nextPhase &&
                AllowedTransitions == nextAllowedTransitions) return;
            CurrentPhase = nextPhase;
            AllowedTransitions = nextAllowedTransitions;
            hasPublishedState = true;

            // 阶段处理器位于管线首位，因此同帧后续动画、命中和事件回调都能观察到新状态。
            Context.PhasePublisher?.Invoke(new SkillActionPhaseChangedEventArgs(
                Context.ExecutionId,
                Context.Request.Config,
                frame,
                CurrentPhase,
                AllowedTransitions));
        }

        /// <summary>
        /// 动作阶段不依赖 LateUpdate 姿态。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessLateFrame(int frame)
        {
        }

        /// <summary>
        /// 结束后清空阶段查询状态。
        /// </summary>
        /// <param name="reason">技能结束原因。</param>
        public override void Complete(SkillCompletionReason reason)
        {
            if (!hasPublishedState) return;
            CurrentPhase = ActionPhaseType.None;
            AllowedTransitions = SkillTransitionMask.None;
            hasPublishedState = false;
            Context.PhasePublisher?.Invoke(new SkillActionPhaseChangedEventArgs(
                Context.ExecutionId,
                Context.Request.Config,
                -1,
                CurrentPhase,
                AllowedTransitions));
        }
    }
}
