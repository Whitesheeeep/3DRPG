using System.Collections.Generic;
using RPG.SkillSystem;

namespace RPG.CameraSystem
{
    /// <summary>
    /// 按 Handle 创建顺序和每通道 Exclusive 屏障合成全部有效请求。
    /// </summary>
    internal sealed class CameraModifierMixer
    {
        /// <summary>合成请求；输入顺序必须由早到晚。</summary>
        internal CameraModifierState Mix(IReadOnlyList<CameraModifierRequest> requests,
            IReadOnlyList<CameraShakeRuntime> shakes, double time)
        {
            long lensBarrier = FindBarrier(requests, CameraModifierChannel.Lens);
            long shakeBarrier = FindShakeBarrier(requests, shakes);
            float fov = 1f;
            UnityEngine.Vector3 position = UnityEngine.Vector3.zero;
            UnityEngine.Vector3 rotation = UnityEngine.Vector3.zero;
            CameraModifierChannel affected = CameraModifierChannel.None;

            foreach (CameraModifierRequest request in requests)
            {
                if (!request.Active) continue;
                if ((request.State.AffectedChannels & CameraModifierChannel.Lens) != 0 &&
                    request.Sequence >= lensBarrier)
                {
                    fov *= request.State.FovScale;
                    affected |= CameraModifierChannel.Lens;
                }
                if ((request.State.AffectedChannels & CameraModifierChannel.Shake) != 0 &&
                    request.Sequence >= shakeBarrier)
                {
                    position += request.State.LocalPositionOffset;
                    rotation += request.State.LocalRotationOffset;
                    affected |= CameraModifierChannel.Shake;
                }
            }

            foreach (CameraShakeRuntime shake in shakes)
            {
                if (shake.Sequence < shakeBarrier) continue;
                shake.Sample(time, out UnityEngine.Vector3 shakePosition,
                    out UnityEngine.Vector3 shakeRotation);
                position += shakePosition;
                rotation += shakeRotation;
                affected |= CameraModifierChannel.Shake;
            }
            return new CameraModifierState(affected, CameraModifierChannel.None, fov, position, rotation);
        }

        /// <summary>查找某通道最后创建的 Exclusive 请求序号；没有屏障时返回最小值。</summary>
        private static long FindBarrier(IReadOnlyList<CameraModifierRequest> requests,
            CameraModifierChannel channel)
        {
            long result = long.MinValue;
            foreach (CameraModifierRequest request in requests)
            {
                if (request.Active && (request.State.ExclusiveChannels & channel) != 0)
                    result = request.Sequence;
            }
            return result;
        }

        /// <summary>
        /// 在时间轴 Modifier 与 Profile Shake 中查找最后创建的 Exclusive 屏障。
        /// </summary>
        /// <param name="requests">按创建顺序保存的 Modifier 请求。</param>
        /// <param name="shakes">按创建顺序保存的 Profile Shake 请求。</param>
        /// <returns>Shake 通道最后一个 Exclusive 请求的创建序号。</returns>
        private static long FindShakeBarrier(IReadOnlyList<CameraModifierRequest> requests,
            IReadOnlyList<CameraShakeRuntime> shakes)
        {
            long result = FindBarrier(requests, CameraModifierChannel.Shake);
            foreach (CameraShakeRuntime shake in shakes)
            {
                if (shake.BlendMode == CameraModifierBlendMode.Exclusive)
                    result = System.Math.Max(result, shake.Sequence);
            }
            return result;
        }
    }

    /// <summary>
    /// 保存 Manager 内部一个 Handle 的固定创建序号和当前帧状态。
    /// </summary>
    internal sealed class CameraModifierRequest
    {
        internal long Sequence { get; }
        internal string DebugName { get; }
        internal CameraModifierState State { get; set; }
        internal bool Active { get; set; }

        /// <summary>创建内部请求记录。</summary>
        internal CameraModifierRequest(long sequence, string debugName)
        {
            Sequence = sequence;
            DebugName = debugName;
            State = CameraModifierState.Identity;
        }
    }
}
