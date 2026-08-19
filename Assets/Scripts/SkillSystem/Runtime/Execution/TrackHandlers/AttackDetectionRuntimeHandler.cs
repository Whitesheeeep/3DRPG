using System.Collections.Generic;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 在 LateUpdate 中执行攻击检测，并为每个 Clip 维护独立的目标去重与武器上一姿态。
    /// （因为武器节点在 LateUpdate 才会更新到本帧位置，且攻击检测可能会跨帧采样，所以需要在 LateUpdate 执行检测）
    /// </summary>
    internal sealed class AttackDetectionRuntimeHandler : TrackRuntimeHandler<AttackDetectionTrackConfig>
    {
        #region 运行时状态

        private readonly Dictionary<AttackDetectionSkillClipConfig, ClipState> states = new();

        #endregion

        #region 处理器契约

        /// <summary>
        /// 普通帧阶段仅清除已经离开半开区间的 Clip 状态，查询延后到 LateUpdate。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessFrame(int frame)
        {
            for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
            {
                AttackDetectionTrackConfig track = Tracks[trackIndex];
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    AttackDetectionSkillClipConfig clip = track.Clips[clipIndex];
                    if (frame == clip.EndFrame) states.Remove(clip);
                }
            }
        }

        /// <summary>
        /// 在 Animator 与武器节点完成本帧更新后执行所有命中采样帧。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessLateFrame(int frame)
        {
            for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
            {
                AttackDetectionTrackConfig track = Tracks[trackIndex];
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    AttackDetectionSkillClipConfig clip = track.Clips[clipIndex];
                    if (frame < clip.StartFrame || frame >= clip.EndFrame) continue;
                    if ((frame - clip.StartFrame) % Mathf.Max(1, clip.SampleIntervalFrames) != 0) continue;

                    if (!states.TryGetValue(clip, out ClipState state))
                    {
                        state = new ClipState();
                        states.Add(clip, state);
                    }

                    if (clip.DetectionData is WeaponTraceAttackDetectionData weaponTrace)
                        DetectWeaponTrace(clip, frame, weaponTrace, state);
                    else
                        Context.AttackDetectionServices.DetectVolume(clip, frame, clip.DetectionData, state.HitTargets);
                }
            }
        }

        /// <summary>
        /// 技能结束后释放所有 Clip 去重和上一武器姿态状态。
        /// </summary>
        /// <param name="reason">技能结束原因。</param>
        public override void Complete(SkillCompletionReason reason)
        {
            states.Clear();
        }

        #endregion

        #region 武器检测

        /// <summary>
        /// 使用播放请求传入的当前武器刀根与刀尖执行单刃轨迹检测。
        /// </summary>
        /// <param name="clip">WeaponTrace 攻击片段。</param>
        /// <param name="frame">当前采样帧。</param>
        /// <param name="data">武器轨迹插值配置。</param>
        /// <param name="state">该 Clip 独立运行时状态。</param>
        private void DetectWeaponTrace(AttackDetectionSkillClipConfig clip, int frame,
            WeaponTraceAttackDetectionData data, ClipState state)
        {
            Transform root = Context.Request.WeaponRoot;
            Transform tip = Context.Request.WeaponTip;
            if (root == null || tip == null || root == tip) return;

            Vector3 currentRoot = root.position;
            Vector3 currentTip = tip.position;
            Context.AttackDetectionServices.DetectWeaponTrace(clip, frame, data,
                state.PreviousRoot, state.PreviousTip, currentRoot, currentTip,
                state.HasWeaponPose, state.HitTargets);
            state.PreviousRoot = currentRoot;
            state.PreviousTip = currentTip;
            state.HasWeaponPose = true;
        }

        #endregion

        #region 嵌套状态

        /// <summary>
        /// 保存单个 AttackDetection Clip 生命周期内的命中去重与上一刀刃姿态。
        /// </summary>
        private sealed class ClipState
        {
            public readonly HashSet<int> HitTargets = new();
            public bool HasWeaponPose;
            public Vector3 PreviousRoot;
            public Vector3 PreviousTip;
        }

        #endregion
    }
}
