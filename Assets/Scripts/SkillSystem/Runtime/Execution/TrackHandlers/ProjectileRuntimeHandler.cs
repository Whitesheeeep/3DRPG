using System.Collections.Generic;
using RPG.Markers;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>在 Projectile Clip 的发射帧解析挂点并发布一次性投射物生成请求。</summary>
    internal sealed class ProjectileRuntimeHandler : TrackRuntimeHandler<ProjectileTrackConfig>
    {
        #region 执行状态

        private readonly HashSet<ProjectileSkillClipConfig> firedClips = new();

        #endregion

        #region 处理器契约

        /// <summary>按轨道和 Clip 的物理顺序发布当前帧全部一次性发射请求。</summary>
        /// <param name="frame">当前整数逻辑帧。</param>
        public override void ProcessFrame(int frame)
        {
            for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
            {
                ProjectileTrackConfig track = Tracks[trackIndex];
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    ProjectileSkillClipConfig clip = track.Clips[clipIndex];
                    if (clip.StartFrame != frame || !firedClips.Add(clip)) continue;

                    // 在事件发布前解析 Marker，确保 GAS 只接收可直接用于 Spawn 的世界基准。
                    Transform origin = ResolveOrigin(clip.SpawnConfig.MarkerKey);
                    Context.ProjectilePublisher?.Invoke(new SkillProjectileSpawnEventArgs(
                        Context.ExecutionId,
                        Context.Request.Config,
                        clip,
                        origin,
                        frame));
                }
            }
        }

        /// <summary>Projectile 发射不依赖 Animator 最终姿态，Marker 在普通逻辑帧直接读取。</summary>
        /// <param name="frame">此前已经处理过的整数逻辑帧。</param>
        public override void ProcessLateFrame(int frame)
        {
        }

        /// <summary>结束时清除未复用的 Clip 身份，已成功生成的投射物继续独立运行。</summary>
        /// <param name="reason">自然结束、正常停止或立即取消。</param>
        public override void Complete(SkillCompletionReason reason)
        {
            firedClips.Clear();
        }

        #endregion

        #region 挂点解析

        /// <summary>解析 Projectile 的发射 Marker；非空 Marker 解析失败时回退角色 Origin。</summary>
        /// <param name="markerKey">Clip 作者配置的可选 Marker。</param>
        /// <returns>可直接计算世界 Spawn Pose 的 Transform。</returns>
        private Transform ResolveOrigin(MarkerKey markerKey)
        {
            if (markerKey == null) return Context.Actor.Origin;
            IMarkerProvider provider = Context.Actor.MarkerProvider;
            if (provider != null && provider.TryGetMarker(markerKey, out Transform marker))
                return marker;

            Debug.LogWarning(
                $"SkillConfig '{Context.Request.Config.name}' 无法解析 Projectile Marker " +
                $"'{markerKey.name}'，将回退角色 Origin。",
                Context.Actor.Owner);
            return Context.Actor.Origin;
        }

        #endregion
    }
}
