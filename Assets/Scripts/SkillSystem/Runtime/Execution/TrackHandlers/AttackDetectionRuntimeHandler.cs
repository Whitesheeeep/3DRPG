using System.Collections.Generic;
using RPG.Markers;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 在 LateUpdate 中执行攻击检测；Detection ID 负责整次技能执行去重，WeaponTrace 姿态仍按 Clip 独立保存。
    /// （因为武器节点在 LateUpdate 才会更新到本帧位置，且攻击检测可能会跨帧采样，所以需要在 LateUpdate 执行检测）
    /// </summary>
    internal sealed class AttackDetectionRuntimeHandler : TrackRuntimeHandler<AttackDetectionTrackConfig>
    {
        #region 运行时状态

        // key：DetectionId；value：本次 SkillExecution 已经结算过的目标实例 ID。
        private readonly Dictionary<int, HashSet<int>> hitTargetIdsByDetectionIdMap = new();
        // key：WeaponTrace Clip；value：该 Clip 上一次有效采样的刀根和刀尖世界位置。
        private readonly Dictionary<AttackDetectionSkillClipConfig, WeaponTraceState>
            weaponTraceStateByClipMap = new();
        // key：KeepWorldPosition Clip；value：Clip 首次采样时冻结的绑定世界矩阵。
        private readonly Dictionary<AttackDetectionSkillClipConfig, Matrix4x4>
            frozenBindingMatrixByClipMap = new();
        // 记录已经报告过缺失绑定的 Clip，避免每个采样帧重复刷错误日志。
        private readonly HashSet<AttackDetectionSkillClipConfig> invalidBindingClips = new();

        #endregion

        #region 处理器契约

        /// <summary>
        /// 普通帧阶段仅清除已经离开半开区间的 Clip 姿态状态，Detection ID 命中集合延后到执行结束清理。
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
                    if (frame != clip.EndFrame) continue;
                    weaponTraceStateByClipMap.Remove(clip);
                    frozenBindingMatrixByClipMap.Remove(clip);
                    invalidBindingClips.Remove(clip);
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

                    HashSet<int> hitTargetIds = GetHitTargetIds(clip.DetectionId);
                    if (clip.DetectionData is WeaponTraceAttackDetectionData weaponTrace)
                    {
                        WeaponTraceState state = GetWeaponTraceState(clip);
                        DetectWeaponTrace(clip, frame, weaponTrace, state, hitTargetIds);
                        continue;
                    }

                    if (!TryResolveBindingMatrix(clip, out Matrix4x4 bindingMatrix)) continue;
                    Context.AttackDetectionServices.DetectVolume(
                        clip, frame, clip.DetectionData, bindingMatrix, hitTargetIds);
                }
            }
        }

        /// <summary>
        /// 技能结束后释放本次执行的 Detection ID 去重、绑定冻结和 WeaponTrace 姿态状态。
        /// </summary>
        /// <param name="reason">技能结束原因。</param>
        public override void Complete(SkillCompletionReason reason)
        {
            hitTargetIdsByDetectionIdMap.Clear();
            weaponTraceStateByClipMap.Clear();
            frozenBindingMatrixByClipMap.Clear();
            invalidBindingClips.Clear();
        }

        #endregion

        #region 绑定解析

        /// <summary>
        /// 解析普通攻击区域当前采样使用的绑定矩阵，并按 Clip 跟随模式决定是否冻结。
        /// </summary>
        /// <param name="clip">需要解析绑定的普通攻击 Clip。</param>
        /// <param name="bindingMatrix">成功时返回用于形状换算的世界矩阵。</param>
        /// <returns>绑定有效时返回 true。</returns>
        private bool TryResolveBindingMatrix(AttackDetectionSkillClipConfig clip,
            out Matrix4x4 bindingMatrix)
        {
            if (clip.FollowMode == AttackDetectionFollowMode.KeepWorldPosition &&
                frozenBindingMatrixByClipMap.TryGetValue(clip, out bindingMatrix))
                return true;

            Transform binding = clip.MarkerKey == null
                ? Context.Actor.Origin
                : ResolveMarker(clip.MarkerKey, clip);
            if (binding == null)
            {
                bindingMatrix = default;
                return false;
            }

            bindingMatrix = binding.localToWorldMatrix;
            if (clip.FollowMode == AttackDetectionFollowMode.KeepWorldPosition)
                frozenBindingMatrixByClipMap[clip] = bindingMatrix;
            return true;
        }

        /// <summary>
        /// 解析非空攻击 Marker；失败时跳过当前 Clip，不静默回退到角色 Origin。
        /// </summary>
        /// <param name="markerKey">待解析的 MarkerKey。</param>
        /// <param name="clip">所属攻击 Clip。</param>
        /// <returns>成功时返回 Marker Transform，失败返回空。</returns>
        private Transform ResolveMarker(MarkerKey markerKey, AttackDetectionSkillClipConfig clip)
        {
            IMarkerProvider provider = Context.Actor.MarkerProvider;
            if (provider != null && provider.TryGetMarker(markerKey, out Transform marker))
                return marker;

            if (invalidBindingClips.Add(clip))
            {
                Debug.LogError(
                    $"SkillConfig '{Context.Request.Config.name}' 无法解析攻击检测 Marker " +
                    $"'{markerKey.name}'，已跳过 Clip '{clip.Id}'。",
                    Context.Actor.Owner);
            }
            return null;
        }

        #endregion

        #region 武器检测

        /// <summary>
        /// 使用播放请求传入的当前武器刀根与刀尖执行单刃轨迹检测。
        /// WeaponTrace 忽略攻击 Clip 的 Marker 和 FollowMode，但仍使用 Detection ID 去重。
        /// </summary>
        /// <param name="clip">WeaponTrace 攻击片段。</param>
        /// <param name="frame">当前采样帧。</param>
        /// <param name="data">武器轨迹插值配置。</param>
        /// <param name="state">该 Clip 独立运行时状态。</param>
        /// <param name="hitTargetIds">该 Detection ID 的整次执行去重集合。</param>
        private void DetectWeaponTrace(AttackDetectionSkillClipConfig clip, int frame,
            WeaponTraceAttackDetectionData data, WeaponTraceState state,
            HashSet<int> hitTargetIds)
        {
            Transform root = Context.Request.WeaponRoot;
            Transform tip = Context.Request.WeaponTip;
            if (root == null || tip == null || root == tip) return;

            Vector3 currentRoot = root.position;
            Vector3 currentTip = tip.position;
            Context.AttackDetectionServices.DetectWeaponTrace(clip, frame, data,
                state.PreviousRoot, state.PreviousTip, currentRoot, currentTip,
                state.HasWeaponPose, hitTargetIds);
            state.PreviousRoot = currentRoot;
            state.PreviousTip = currentTip;
            state.HasWeaponPose = true;
        }

        #endregion

        #region 状态辅助

        /// <summary>
        /// 获取 Detection ID 的共享命中集合；集合生命周期覆盖本次 SkillExecution。
        /// </summary>
        /// <param name="detectionId">攻击检测分组 ID。</param>
        /// <returns>该 ID 对应的目标实例 ID 集合。</returns>
        private HashSet<int> GetHitTargetIds(int detectionId)
        {
            if (!hitTargetIdsByDetectionIdMap.TryGetValue(detectionId, out HashSet<int> hitTargetIds))
            {
                hitTargetIds = new HashSet<int>();
                hitTargetIdsByDetectionIdMap.Add(detectionId, hitTargetIds);
            }
            return hitTargetIds;
        }

        /// <summary>
        /// 获取独立保存上一武器姿态的 WeaponTrace Clip 状态。
        /// </summary>
        /// <param name="clip">需要读取状态的 WeaponTrace Clip。</param>
        /// <returns>该 Clip 的轨迹姿态状态。</returns>
        private WeaponTraceState GetWeaponTraceState(AttackDetectionSkillClipConfig clip)
        {
            if (!weaponTraceStateByClipMap.TryGetValue(clip, out WeaponTraceState state))
            {
                state = new WeaponTraceState();
                weaponTraceStateByClipMap.Add(clip, state);
            }
            return state;
        }

        #endregion

        #region 嵌套状态

        /// <summary>
        /// 保存单个 WeaponTrace Clip 的上一采样刀根和刀尖姿态，不保存命中去重集合。
        /// </summary>
        private sealed class WeaponTraceState
        {
            public bool HasWeaponPose;
            public Vector3 PreviousRoot;
            public Vector3 PreviousTip;
        }

        #endregion
    }
}
