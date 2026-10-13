namespace WS_Modules.AudioSystem
{
    /// <summary>
    /// 提供对单次直接 AudioClip 播放实例的版本安全控制。
    /// </summary>
    public interface IAudioPlaybackHandle
    {
        /// <summary>
        /// 获取句柄所拥有的 Voice 是否仍处于活动状态。
        /// </summary>
        bool IsPlaying { get; }

        /// <summary>
        /// 停止当前句柄仍然拥有的播放实例；实例结束或被复用后不产生操作。
        /// </summary>
        void Stop();
    }
}
