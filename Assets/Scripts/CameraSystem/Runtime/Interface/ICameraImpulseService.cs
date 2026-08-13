using UnityEngine;

namespace RPG.CameraSystem
{
    /// <summary>
    /// 定义瞬时 Cinemachine Impulse 的发射与独立取消契约。
    /// </summary>
    public interface ICameraImpulseService
    {
        /// <summary>
        /// 使用预设默认方向发射一次冲击。
        /// </summary>
        /// <param name="profile">Impulse 预设。</param>
        /// <param name="amplitude">叠加到预设默认强度上的倍率。</param>
        /// <returns>只代表本次冲击事件的句柄。</returns>
        CameraImpulseHandle EmitImpulse(CameraImpulseProfile profile, float amplitude = 1f);

        /// <summary>
        /// 使用调用方方向发射一次冲击。
        /// </summary>
        /// <param name="profile">Impulse 预设。</param>
        /// <param name="direction">冲击方向；幅度由独立参数控制。</param>
        /// <param name="amplitude">叠加到预设默认强度上的倍率。</param>
        /// <returns>只代表本次冲击事件的句柄。</returns>
        CameraImpulseHandle EmitImpulse(CameraImpulseProfile profile, Vector3 direction, float amplitude = 1f);

        /// <summary>
        /// 尝试取消由当前 Manager 发射且尚未回收的冲击事件。
        /// </summary>
        /// <param name="handle">目标冲击句柄。</param>
        /// <param name="immediate">是否立即截断，而不是进入事件衰减。</param>
        /// <returns>事件仍由 Manager 持有且成功取消时返回 true。</returns>
        bool TryCancelImpulse(CameraImpulseHandle handle, bool immediate = true);
    }
}
