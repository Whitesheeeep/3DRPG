namespace RPG.SkillSystem
{
    /// <summary>
    /// 将动作阶段轨道投影为当前阶段和可打断状态，不直接干预 Runner 的停止决策。
    /// </summary>
    internal sealed class ActionPhaseRuntimeHandler : TrackRuntimeHandler<ActionPhaseTrackConfig>, IActionPhaseRuntimeState
    {
        public ActionPhaseType CurrentPhase { get; private set; } = ActionPhaseType.None;
        public bool CanBeInterrupted { get; private set; }

        /// <summary>
        /// 查找覆盖当前帧的动作阶段；空白区间恢复 None 和不可打断。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessFrame(int frame)
        {
            CurrentPhase = ActionPhaseType.None;
            CanBeInterrupted = false;
            for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
            {
                ActionPhaseTrackConfig track = Tracks[trackIndex];
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    ActionPhaseSkillClipConfig clip = track.Clips[clipIndex];
                    if (frame < clip.StartFrame || frame >= clip.EndFrame) continue;

                    // 首个有效 Clip 按统一轨道物理顺序获得阶段查询优先权。
                    CurrentPhase = clip.Phase;
                    CanBeInterrupted = clip.CanBeInterrupted;
                    return;
                }
            }
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
            CurrentPhase = ActionPhaseType.None;
            CanBeInterrupted = false;
        }
    }
}
