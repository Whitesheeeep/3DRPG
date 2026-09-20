using System;
using System.Collections.Generic;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;
using WS_Modules;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 将动作阶段轨道解析为当前阶段和 RuntimeTag/阻断策略，不把窗口投影为 ASC Owner Tag。
    /// </summary>
    internal sealed class ActionPhaseRuntimeHandler : TrackRuntimeHandler<ActionPhaseTrackConfig>, IActionPhaseRuntimeState
    {
        private bool hasPublishedState;
        private GameplayTag[] runtimeTags = Array.Empty<GameplayTag>();
        private GameplayTag[] blockAbilityTags = Array.Empty<GameplayTag>();

        /// <summary>获取当前动作阶段。</summary>
        public ActionPhaseType CurrentPhase { get; private set; } = ActionPhaseType.None;
        /// <summary>获取当前是否存在 Clip 策略。</summary>
        public bool HasPhasePolicy { get; private set; }
        /// <summary>获取当前 Clip 的普通取消权限。</summary>
        public bool IsCancelable { get; private set; }
        /// <summary>获取当前 Clip 的 RuntimeTags 快照。</summary>
        public IReadOnlyList<GameplayTag> RuntimeTags => runtimeTags;
        /// <summary>获取当前 Clip 的完整 BlockAbilityTags 快照。</summary>
        public IReadOnlyList<GameplayTag> BlockAbilityTags => blockAbilityTags;

        /// <summary>
        /// 在技能执行创建时校验动作阶段配置，避免非法 Tag、重复 Tag、窗口权限和重叠区间进入逐帧状态。
        /// </summary>
        /// <exception cref="InvalidOperationException">配置存在非法 Tag、重复 Tag、窗口权限冲突或重叠区间。</exception>
        protected override void OnInitialized()
        {
            for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
            {
                ActionPhaseTrackConfig track = Tracks[trackIndex];
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    ActionPhaseSkillClipConfig clip = track.Clips[clipIndex];
                    ValidateClipTags(clip);
                    if (clip.Phase == ActionPhaseType.None &&
                        (clip.IsCancelable || clip.RuntimeTags.Count > 0 || clip.BlockAbilityTags.Count > 0))
                        throw new InvalidOperationException(
                            $"SkillConfig '{Context.Request.Config.name}' 的动作阶段 Clip '{clip.Id}' 不能在 Phase.None 中配置策略。");

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

        /// <summary>查找覆盖当前帧的动作阶段；空白区间恢复初始策略。</summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessFrame(int frame)
        {
            ActionPhaseSkillClipConfig selectedClip = FindClip(frame);
            ActionPhaseType nextPhase = selectedClip?.Phase ?? ActionPhaseType.None;
            bool nextHasPolicy = selectedClip != null;
            bool nextCancelable = selectedClip?.IsCancelable ?? false;
            GameplayTag[] nextRuntimeTags = selectedClip == null
                ? Array.Empty<GameplayTag>()
                : CopyTags(selectedClip.RuntimeTags);
            GameplayTag[] nextBlockTags = selectedClip == null
                ? Array.Empty<GameplayTag>()
                : CopyTags(selectedClip.BlockAbilityTags);

            if (hasPublishedState && CurrentPhase == nextPhase && HasPhasePolicy == nextHasPolicy &&
                IsCancelable == nextCancelable && AreEqual(runtimeTags, nextRuntimeTags) &&
                AreEqual(blockAbilityTags, nextBlockTags))
                return;

            CurrentPhase = nextPhase;
            HasPhasePolicy = nextHasPolicy;
            IsCancelable = nextCancelable;
            runtimeTags = nextRuntimeTags;
            blockAbilityTags = nextBlockTags;
            hasPublishedState = true;

            // 阶段处理器位于管线首位，因此同帧后续动画、命中和事件回调都能观察到新策略。
            Context.PhasePublisher?.Invoke(new SkillActionPhaseChangedEventArgs(
                Context.ExecutionId,
                Context.Request.Config,
                frame,
                CurrentPhase,
                HasPhasePolicy,
                IsCancelable,
                runtimeTags,
                blockAbilityTags));
        }

        /// <summary>动作阶段不依赖 LateUpdate 姿态。</summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessLateFrame(int frame)
        {
        }

        /// <summary>结束后清空阶段查询状态并发布一次空白策略。</summary>
        /// <param name="reason">技能结束原因。</param>
        public override void Complete(SkillCompletionReason reason)
        {
            if (!hasPublishedState) return;
            CurrentPhase = ActionPhaseType.None;
            HasPhasePolicy = false;
            IsCancelable = false;
            runtimeTags = Array.Empty<GameplayTag>();
            blockAbilityTags = Array.Empty<GameplayTag>();
            hasPublishedState = false;
            Context.PhasePublisher?.Invoke(new SkillActionPhaseChangedEventArgs(
                Context.ExecutionId,
                Context.Request.Config,
                -1,
                CurrentPhase,
                false,
                false,
                runtimeTags,
                blockAbilityTags));
        }

        /// <summary>查找覆盖指定帧的第一个动作阶段 Clip。</summary>
        /// <param name="frame">要查询的逻辑帧。</param>
        /// <returns>覆盖该帧的 Clip；空白区间返回 null。</returns>
        private ActionPhaseSkillClipConfig FindClip(int frame)
        {
            for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
            {
                ActionPhaseTrackConfig track = Tracks[trackIndex];
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    ActionPhaseSkillClipConfig clip = track.Clips[clipIndex];
                    if (frame >= clip.StartFrame && frame < clip.EndFrame) return clip;
                }
            }

            return null;
        }

        /// <summary>校验一个 Clip 的 RuntimeTags 和 BlockAbilityTags 数据契约。</summary>
        /// <param name="clip">待校验的动作阶段 Clip。</param>
        private void ValidateClipTags(ActionPhaseSkillClipConfig clip)
        {
            ValidateTags(clip.RuntimeTags, clip, "RuntimeTags");
            ValidateTags(clip.BlockAbilityTags, clip, "BlockAbilityTags");
            for (int i = 0; i < clip.RuntimeTags.Count; i++)
            {
                GameplayTag tag = clip.RuntimeTags[i];
                bool isWindowTag = tag == GameplayTags.Tag_Skill_Window_CancelBy_Ability ||
                                   tag == GameplayTags.Tag_Skill_Window_CancelBy_Jump ||
                                   tag == GameplayTags.Tag_Skill_Window_CancelBy_Move;
                if (isWindowTag && !clip.IsCancelable)
                    throw new InvalidOperationException(
                        $"SkillConfig '{Context.Request.Config.name}' 的动作阶段 Clip '{clip.Id}' 含转换窗口 TagId={tag.Id}，但 IsCancelable=false。");
            }
        }

        /// <summary>校验数组中的 Tag 均有效且不存在重复显式值。</summary>
        /// <param name="tags">待校验的标签集合。</param>
        /// <param name="clip">所属 Clip。</param>
        /// <param name="fieldName">字段名称。</param>
        private void ValidateTags(IReadOnlyList<GameplayTag> tags,
            ActionPhaseSkillClipConfig clip, string fieldName)
        {
            var uniqueTags = new HashSet<GameplayTag>();
            for (int i = 0; i < tags.Count; i++)
            {
                GameplayTag tag = tags[i];
                if (!GameplayTagManager.Instance.IsValidTag(tag))
                    throw new InvalidOperationException(
                        $"SkillConfig '{Context.Request.Config.name}' 的动作阶段 Clip '{clip.Id}' 的 {fieldName} 包含未 Bake TagId={tag.Id}。");
                if (!uniqueTags.Add(tag))
                    throw new InvalidOperationException(
                        $"SkillConfig '{Context.Request.Config.name}' 的动作阶段 Clip '{clip.Id}' 的 {fieldName} 存在重复 TagId={tag.Id}。");
            }
        }

        /// <summary>复制只读 Tag 列表，隔离作者配置后续修改。</summary>
        /// <param name="source">作者配置标签。</param>
        /// <returns>稳定数组快照。</returns>
        private static GameplayTag[] CopyTags(IReadOnlyList<GameplayTag> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<GameplayTag>();
            var copy = new GameplayTag[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }

        /// <summary>比较两个 Tag 快照的显式顺序和值。</summary>
        /// <param name="left">左侧快照。</param>
        /// <param name="right">右侧快照。</param>
        /// <returns>两组快照完全一致时返回 true。</returns>
        private static bool AreEqual(IReadOnlyList<GameplayTag> left, IReadOnlyList<GameplayTag> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
                if (left[i] != right[i]) return false;
            return true;
        }
    }
}
