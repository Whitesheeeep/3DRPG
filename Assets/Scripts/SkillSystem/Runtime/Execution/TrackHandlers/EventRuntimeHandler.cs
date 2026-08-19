using WS_Modules.CustomEventSystem;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 在 Event Marker 帧通过 WSFrame 全局类型事件发布完整 SkillConfigEventArgs 快照。
    /// </summary>
    internal sealed class EventRuntimeHandler : TrackRuntimeHandler<EventTrackConfig>
    {
        /// <summary>
        /// 发布当前帧全部 Marker；跨帧追赶时由 SkillExecution 保证逐帧顺序。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessFrame(int frame)
        {
            for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
            {
                EventTrackConfig track = Tracks[trackIndex];
                for (int markerIndex = 0; markerIndex < track.Markers.Count; markerIndex++)
                {
                    SkillEventMarkerConfig marker = track.Markers[markerIndex];
                    if (marker.Frame != frame) continue;

                    // 按轨道物理顺序发布同帧 Marker，使跨轨事件顺序保持稳定。
                    SkillConfigEventArgs args = new(Context.Request.Config, Context.Actor.Owner, marker);
                    EventSystem.EventTrigger_Type(typeof(SkillConfigEventArgs), args);
                }
            }
        }

        /// <summary>
        /// 配置事件不依赖 LateUpdate 姿态。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessLateFrame(int frame)
        {
        }

        /// <summary>
        /// Event Marker 是单帧事件，结束时没有动态资源需要释放。
        /// </summary>
        /// <param name="reason">技能结束原因。</param>
        public override void Complete(SkillCompletionReason reason)
        {
        }
    }
}
