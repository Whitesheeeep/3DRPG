#if UNITY_EDITOR
using System.Collections.Generic;

namespace RPG.SkillSystem.Editor
{
    internal static class SkillTimelineValidator
    {
        /// <summary>
        /// 校验当前配置并返回全部数据问题。
        /// </summary>
        public static IReadOnlyList<string> Validate(SkillConfig config)
        {
            List<string> errors = new();
            if (config == null)
            {
                errors.Add("未选择 SkillConfig。");
                return errors;
            }

            if (config.FrameRate < 1) errors.Add("FPS 必须大于 0。");
            if (config.DurationFrames < 1) errors.Add("总帧数必须大于 0。");
            HashSet<string> ids = new();
            CheckId(config.Id, "SkillConfig", ids, errors);

            foreach (AnimationTrackConfig track in config.AnimationTracks)
            {
                CheckId(track.Header.Id, track.Header.DisplayName, ids, errors);
                int previousEnd = 0;
                foreach (AnimationSkillClipConfig clip in track.Clips)
                {
                    CheckId(clip.Id, "Animation Clip", ids, errors);
                    ValidateInterval(track.Header.DisplayName, clip.StartFrame, clip.DurationFrames, ref previousEnd, errors);
                    if (clip.PlaybackSpeed <= 0f) errors.Add($"{track.Header.DisplayName} 中动画播放速度必须大于 0。");
                }
            }

            foreach (VfxTrackConfig track in config.VfxTracks)
            {
                CheckId(track.Header.Id, track.Header.DisplayName, ids, errors);
                int previousEnd = 0;
                foreach (VfxSkillClipConfig clip in track.Clips)
                {
                    CheckId(clip.Id, "VFX Clip", ids, errors);
                    ValidateInterval(track.Header.DisplayName, clip.StartFrame, clip.DurationFrames, ref previousEnd, errors);
                }
            }

            foreach (EventTrackConfig track in config.EventTracks)
            {
                CheckId(track.Header.Id, track.Header.DisplayName, ids, errors);
                foreach (SkillEventMarkerConfig marker in track.Markers)
                {
                    CheckId(marker.Id, "Event Marker", ids, errors);
                    if (marker.Frame < 0 || marker.Frame >= config.DurationFrames)
                        errors.Add($"事件 {marker.DisplayName} 位于时间轴范围外。");
                }
            }

            return errors;
        }

        /// <summary>
        /// 检查稳定标识符是否为空或重复，并记录校验错误。
        /// </summary>
        private static void CheckId(string id, string label, ISet<string> ids, ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(id)) errors.Add($"{label} 缺少稳定 ID。");
            else if (!ids.Add(id)) errors.Add($"{label} 的 ID 重复：{id}");
        }

        /// <summary>
        /// 校验片段半开区间、排序和同轨不重叠约束。
        /// </summary>
        private static void ValidateInterval(string trackName, int start, int duration, ref int previousEnd,
            ICollection<string> errors)
        {
            if (start < 0 || duration < 1) errors.Add($"{trackName} 包含非法帧区间。");
            if (start < previousEnd) errors.Add($"{trackName} 中存在重叠或未排序的 Clip。");
            previousEnd = start + duration;
        }
    }
}
#endif
