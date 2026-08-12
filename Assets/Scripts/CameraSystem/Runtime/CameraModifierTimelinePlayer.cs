using System;
using RPG.SkillSystem;

namespace RPG.CameraSystem
{
    /// <summary>
    /// 代表一个技能执行实例，将时间轴帧求值结果提交给 Camera Modifier Service。
    /// </summary>
    public sealed class CameraModifierTimelinePlayer : IDisposable
    {
        private readonly ICameraModifierService service;
        private readonly SkillConfig config;
        private readonly CameraModifierHandle handle;
        private bool disposed;

        /// <summary>创建一个层级固定的技能摄像机修饰播放器。</summary>
        public CameraModifierTimelinePlayer(ICameraModifierService service,
            SkillConfig config, string debugName)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            handle = service.CreateModifier(debugName);
        }

        /// <summary>求值并提交一个整数帧；空结果会停用请求但保留创建层级。</summary>
        public void SampleFrame(int frame)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CameraModifierTimelinePlayer));
            CameraModifierState state = CameraModifierEvaluator.Evaluate(config, frame);
            if (state.AffectedChannels == CameraModifierChannel.None)
                service.DeactivateModifier(handle);
            else
                service.UpdateModifier(handle, state);
        }

        /// <summary>永久释放请求；技能结束和中断均应调用此方法。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            service.ReleaseModifier(handle);
        }
    }
}
