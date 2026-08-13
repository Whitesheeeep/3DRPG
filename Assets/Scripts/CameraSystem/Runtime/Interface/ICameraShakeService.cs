namespace RPG.CameraSystem
{
    /// <summary>
    /// 定义持续摄像机噪声的播放、强度调整与停止契约。
    /// </summary>
    public interface ICameraShakeService
    {
        /// <summary>
        /// 使用预设创建一个独立 Shake 请求。
        /// </summary>
        /// <param name="profile">描述波形与生命周期的 Shake 预设。</param>
        /// <param name="strength">本次播放叠加到预设幅度上的倍率。</param>
        /// <param name="seed">决定各轴噪声相位的稳定种子。</param>
        /// <returns>用于后续调整或停止本次请求的句柄。</returns>
        CameraShakeHandle PlayShake(CameraShakeProfile profile, float strength = 1f, int seed = 0);

        /// <summary>
        /// 尝试修改尚未结束的 Shake 请求强度。
        /// </summary>
        /// <param name="handle">目标请求句柄。</param>
        /// <param name="strength">新的非负播放倍率。</param>
        /// <returns>请求仍然有效且修改成功时返回 true。</returns>
        bool TrySetShakeStrength(CameraShakeHandle handle, float strength);

        /// <summary>
        /// 尝试停止 Shake；普通停止使用预设淡出，立即停止则当帧移除。
        /// </summary>
        /// <param name="handle">目标请求句柄。</param>
        /// <param name="immediate">是否跳过淡出立即停止。</param>
        /// <returns>请求仍然有效且接受停止操作时返回 true。</returns>
        bool TryStopShake(CameraShakeHandle handle, bool immediate = false);
    }
}
