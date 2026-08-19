using Animancer;
using RPG.Character.Animation;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 在动画 Clip 起始帧播放并淡入 Animancer 状态；结束时不停止动画或恢复 Locomotion。
    /// </summary>
    internal sealed class AnimationRuntimeHandler : TrackRuntimeHandler<AnimationTrackConfig>
    {
        private AnimationSkillClipConfig currentClip;

        /// <summary>
        /// 按轨道物理顺序选择当前帧最上方有效 Clip；权威 Clip 变化时从正确源时间播放并淡入。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessFrame(int frame)
        {
            IAnimationPlayer animationPlayer = Context.Actor.AnimationPlayer;
            if (animationPlayer == null) return;
            AnimationSkillClipConfig selected = SelectClip(frame);
            if (selected == null || selected == currentClip) return;
            currentClip = selected;

            AnimancerState state = animationPlayer.Play(
                Context.Actor.SkillAnimationLayer,
                selected.AnimationClip,
                selected.FadeDuration,
                FadeMode.FromStart);
            float sourceFrameRate = selected.AnimationClip.frameRate > 0f
                ? selected.AnimationClip.frameRate
                : Context.Request.Config.FrameRate;
            double sourceStartTime = selected.SourceStartFrame / sourceFrameRate;
            double elapsedTime = (frame - selected.StartFrame) /
                                 (double)Context.Request.Config.FrameRate;
            // 切换到已开始的下层 Clip 时，直接定位到其权威时间，避免只从素材开头重新播放。
            state.TimeD = sourceStartTime + elapsedTime * selected.PlaybackSpeed;
            state.Speed = selected.PlaybackSpeed;
        }

        /// <summary>
        /// 使用与 Editor Preview 相同的轨道和 Clip 顺序选择当前帧权威动画。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        /// <returns>首个未静音、区间有效且素材非空的动画 Clip；没有时返回空。</returns>
        private AnimationSkillClipConfig SelectClip(int frame)
        {
            // 按轨道顺序遍历，找到当前帧最上方的有效 Clip。
            for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
            {
                AnimationTrackConfig animationTrack = Tracks[trackIndex];
                for (int clipIndex = 0; clipIndex < animationTrack.Clips.Count; clipIndex++)
                {
                    AnimationSkillClipConfig clip = animationTrack.Clips[clipIndex];
                    if (clip.AnimationClip != null && frame >= clip.StartFrame && frame < clip.EndFrame)
                        return clip;
                }
            }
            return null;
        }

        /// <summary>
        /// 动画播放由 Animancer 自身图更新，不需要 LateUpdate 处理。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessLateFrame(int frame)
        {
        }

        /// <summary>
        /// 技能结束时有意不停止 Animancer 状态，动画退出完全交由外部状态机。
        /// </summary>
        /// <param name="reason">技能结束原因。</param>
        public override void Complete(SkillCompletionReason reason)
        {
            currentClip = null;
        }
    }
}
