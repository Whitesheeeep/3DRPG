using RPG.SkillSystem;
using UnityEngine;
using System;

namespace RPG.CameraSystem
{
    /// <summary>
    /// 将技能配置中的有效 Camera Modifier Clip 确定性求值为单个请求状态。
    /// </summary>
    public static class CameraModifierEvaluator
    {
        /// <summary>
        /// 求值指定整数帧；区间采用 [StartFrame, EndFrame)，静音轨道不参与结果。
        /// </summary>
        public static CameraModifierState Evaluate(SkillConfig config, int frame) =>
            Evaluate(config, frame, null);

        /// <summary>
        /// 使用可选数据解析器求值；Editor 通过它覆盖单个 Clip 草稿，运行时传空。
        /// </summary>
        public static CameraModifierState Evaluate(SkillConfig config, int frame,
            Func<string, CameraModifierDataBase, CameraModifierDataBase> dataResolver)
        {
            CameraModifierChannel affected = CameraModifierChannel.None;
            CameraModifierChannel exclusive = CameraModifierChannel.None;
            float fovScale = 1f;
            Vector3 position = Vector3.zero;
            Vector3 rotation = Vector3.zero;

            foreach (TrackConfigBase track in config.Tracks)
            {
                if (track is not CameraModifierTrackConfig modifierTrack || track.Muted) continue;
                foreach (CameraModifierSkillClipConfig clip in modifierTrack.Clips)
                {
                    if (frame < clip.StartFrame || frame >= clip.EndFrame || clip.ModifierData == null) continue;
                    CameraModifierDataBase modifierData = dataResolver?.Invoke(clip.Id, clip.ModifierData) ??
                                                          clip.ModifierData;
                    float normalizedTime = clip.DurationFrames <= 1
                        ? 1f
                        : Mathf.Clamp01((frame - clip.StartFrame) / (float)(clip.DurationFrames - 1));

                    // 每种配置只产生自身通道；同一技能的贡献在提交 Manager 前先完成汇总。
                    switch (modifierData)
                    {
                        case FovCameraModifierData fov:
                            float weight = fov.WeightCurve?.Evaluate(normalizedTime) ?? normalizedTime;
                            fovScale *= Mathf.LerpUnclamped(1f, Mathf.Max(0.01f, fov.TargetScale), weight);
                            affected |= CameraModifierChannel.Lens;
                            if (fov.BlendMode == CameraModifierBlendMode.Exclusive)
                                exclusive |= CameraModifierChannel.Lens;
                            break;
                        case ShakeCameraModifierData shake:
                            float intensity = shake.IntensityCurve?.Evaluate(normalizedTime) ?? 1f;
                            Vector3 noise = EvaluateNoise(clip.Id, shake.Seed,
                                frame - clip.StartFrame, config.FrameRate, shake.Frequency);
                            position += Vector3.Scale(noise, shake.LocalPositionAmplitude) * intensity;
                            rotation += Vector3.Scale(noise, shake.LocalRotationAmplitude) * intensity;
                            affected |= CameraModifierChannel.Shake;
                            if (shake.BlendMode == CameraModifierBlendMode.Exclusive)
                                exclusive |= CameraModifierChannel.Shake;
                            break;
                    }
                }
            }

            return new CameraModifierState(affected, exclusive, fovScale, position, rotation);
        }

        /// <summary>
        /// 使用稳定 FNV-1a Hash 与 PerlinNoise 生成不依赖运行顺序的三轴噪声。
        /// </summary>
        private static Vector3 EvaluateNoise(string clipId, int seed, int localFrame,
            int frameRate, float frequency)
        {
            uint hash = StableHash(clipId) ^ unchecked((uint)seed * 16777619u);
            float time = localFrame / (float)Mathf.Max(1, frameRate) * Mathf.Max(0f, frequency);
            float offset = (hash & 0x00FFFFFFu) / 16777215f * 1024f;
            return new Vector3(
                Mathf.PerlinNoise(offset + 11.3f, time) * 2f - 1f,
                Mathf.PerlinNoise(offset + 37.7f, time) * 2f - 1f,
                Mathf.PerlinNoise(offset + 73.1f, time) * 2f - 1f);
        }

        /// <summary>计算跨进程和 Domain Reload 稳定的 UTF-16 FNV-1a Hash。</summary>
        private static uint StableHash(string value)
        {
            uint hash = 2166136261u;
            if (value == null) return hash;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
