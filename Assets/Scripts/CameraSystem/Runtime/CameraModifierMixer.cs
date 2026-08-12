using System.Collections.Generic;

namespace RPG.CameraSystem
{
    /// <summary>
    /// 按 Handle 创建顺序和每通道 Exclusive 屏障合成全部有效请求。
    /// </summary>
    internal sealed class CameraModifierMixer
    {
        /// <summary>合成请求；输入顺序必须由早到晚。</summary>
        internal CameraModifierState Mix(IReadOnlyList<CameraModifierRequest> requests)
        {
            long lensBarrier = FindBarrier(requests, CameraModifierChannel.Lens);
            long shakeBarrier = FindBarrier(requests, CameraModifierChannel.Shake);
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
