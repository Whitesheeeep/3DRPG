using Cinemachine;
using RPG.SkillSystem;
using UnityEngine;

namespace RPG.CameraSystem
{
    /// <summary>
    /// 保存单个持续 Shake 请求的确定性采样相位、强度和停止生命周期。
    /// </summary>
    internal sealed class CameraShakeRuntime
    {
        #region 常量与字段

        private const float MinimumDirectionMagnitude = 0.000001f;

        private readonly CameraShakeProfile profile;
        private readonly double startTime;
        private readonly Vector3 positionOffsets;
        private readonly Vector3 rotationOffsets;
        private float strength;
        private double stopTime = double.PositiveInfinity;
        private bool immediateStop;

        #endregion

        #region 属性

        internal long Sequence { get; }
        internal CameraModifierBlendMode BlendMode => profile.BlendMode;

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建一条以缩放游戏时间为时钟的独立 Shake 请求。
        /// </summary>
        /// <param name="profile">持续噪声预设。</param>
        /// <param name="sequence">与其他 Camera Modifier 共用的创建序号。</param>
        /// <param name="startTime">请求创建时的缩放游戏时间。</param>
        /// <param name="strength">本次播放强度倍率。</param>
        /// <param name="seed">稳定噪声相位种子。</param>
        internal CameraShakeRuntime(CameraShakeProfile profile, long sequence,
            double startTime, float strength, int seed)
        {
            this.profile = profile;
            Sequence = sequence;
            this.startTime = startTime;
            this.strength = Mathf.Max(0f, strength);

            // 位置和旋转使用不同盐值，避免两组通道从完全相同的噪声相位开始。
            positionOffsets = CreateOffsets(seed, 0x51ED270Bu);
            rotationOffsets = CreateOffsets(seed, 0xA2C79B3Du);
        }

        #endregion

        #region 状态操作

        /// <summary>
        /// 修改仍在活动中的播放强度。
        /// </summary>
        /// <param name="value">新的非负倍率。</param>
        internal void SetStrength(float value) => strength = Mathf.Max(0f, value);

        /// <summary>
        /// 请求停止；普通停止从当前时间进入预设淡出，立即停止则当帧失效。
        /// </summary>
        /// <param name="time">停止发生时的缩放游戏时间。</param>
        /// <param name="immediate">是否跳过淡出。</param>
        internal void Stop(double time, bool immediate)
        {
            if (time >= stopTime) return;
            stopTime = time;
            immediateStop = immediate;
        }

        /// <summary>
        /// 判断请求是否已完成淡出，可以从 Manager 请求表移除。
        /// </summary>
        /// <param name="time">当前缩放游戏时间。</param>
        /// <returns>不再产生任何输出时返回 true。</returns>
        internal bool IsExpired(double time)
        {
            ResolveAutomaticStop();
            if (immediateStop) return true;
            if (double.IsPositiveInfinity(stopTime)) return false;
            return time >= stopTime + profile.FadeOutDuration;
        }

        /// <summary>
        /// 在绝对经过时间上确定性采样本请求的局部位置与欧拉旋转偏移。
        /// </summary>
        /// <param name="time">当前缩放游戏时间。</param>
        /// <param name="position">输出局部位置偏移。</param>
        /// <param name="rotation">输出局部欧拉旋转偏移。</param>
        internal void Sample(double time, out Vector3 position, out Vector3 rotation)
        {
            ResolveAutomaticStop();
            float elapsed = Mathf.Max(0f, (float)(time - startTime));
            float weight = EvaluateFadeWeight(time, elapsed);
            float gain = profile.AmplitudeGain * strength * weight;
            NoiseSettings noise = profile.NoiseSettings;
            if (noise == null || gain <= 0f)
            {
                position = Vector3.zero;
                rotation = Vector3.zero;
                return;
            }

            float sampleTime = elapsed * profile.FrequencyGain;
            position = NoiseSettings.GetCombinedFilterResults(
                noise.PositionNoise, sampleTime, positionOffsets) * gain;
            rotation = NoiseSettings.GetCombinedFilterResults(
                noise.OrientationNoise, sampleTime, rotationOffsets) * gain;
        }

        #endregion

        #region 内部计算

        /// <summary>
        /// 为定时请求建立自动停止边界；持续请求只响应显式 Stop。
        /// </summary>
        private void ResolveAutomaticStop()
        {
            if (profile.Lifetime != CameraShakeLifetime.Timed ||
                !double.IsPositiveInfinity(stopTime)) return;
            stopTime = startTime + profile.Duration;
        }

        /// <summary>
        /// 合并淡入与淡出包络，保证停止时从当时强度平滑下降。
        /// </summary>
        /// <param name="time">当前缩放游戏时间。</param>
        /// <param name="elapsed">从请求创建开始经过的秒数。</param>
        /// <returns>范围为 0..1 的包络权重。</returns>
        private float EvaluateFadeWeight(double time, float elapsed)
        {
            if (immediateStop) return 0f;
            float fadeIn = profile.FadeInDuration <= 0f
                ? 1f
                : Mathf.Clamp01(elapsed / profile.FadeInDuration);
            if (double.IsPositiveInfinity(stopTime)) return fadeIn;
            float fadeOut = profile.FadeOutDuration <= 0f
                ? 0f
                : Mathf.Clamp01((float)((stopTime + profile.FadeOutDuration - time) /
                                        profile.FadeOutDuration));
            return Mathf.Min(fadeIn, fadeOut);
        }

        /// <summary>
        /// 使用整数 Hash 创建三个稳定且彼此分离的 Perlin 相位偏移。
        /// </summary>
        /// <param name="seed">调用方稳定种子。</param>
        /// <param name="salt">区分位置与旋转通道的盐值。</param>
        /// <returns>三个噪声轴使用的时间偏移。</returns>
        private static Vector3 CreateOffsets(int seed, uint salt)
        {
            uint state = unchecked((uint)seed) ^ salt;
            return new Vector3(NextOffset(ref state), NextOffset(ref state), NextOffset(ref state));
        }

        /// <summary>
        /// 推进 Xorshift 状态并映射为远离零点的 Perlin 偏移。
        /// </summary>
        /// <param name="state">可变 Hash 状态。</param>
        /// <returns>0..1024 范围内的稳定偏移。</returns>
        private static float NextOffset(ref uint state)
        {
            if (state == 0u) state = 0x9E3779B9u;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            float result = (state & 0x00FFFFFFu) / 16777215f * 1024f;
            return result < MinimumDirectionMagnitude ? 0.5f : result;
        }

        #endregion
    }

    /// <summary>
    /// 保存 Manager 发射的一次 Cinemachine Impulse 事件及其安全持有期限。
    /// </summary>
    internal sealed class CameraImpulseRuntime
    {
        #region 属性

        internal CinemachineImpulseManager.ImpulseEvent Event { get; }
        internal double ExpireTime { get; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建只在事件有效期内保留 Cinemachine 回收对象引用的运行时记录。
        /// </summary>
        /// <param name="impulseEvent">Cinemachine 创建的事件。</param>
        /// <param name="expireTime">事件在缩放游戏时间上的预计结束点。</param>
        internal CameraImpulseRuntime(CinemachineImpulseManager.ImpulseEvent impulseEvent,
            double expireTime)
        {
            Event = impulseEvent;
            ExpireTime = expireTime;
        }

        #endregion
    }
}
