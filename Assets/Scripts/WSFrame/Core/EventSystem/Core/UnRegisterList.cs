using System.Collections.Generic;

namespace WS_Modules.CustomEventSystem
{
    /// <summary>
    /// 保存一组事件注销句柄，供订阅者在同一生命周期边界统一调用 UnRegisterAll。
    /// </summary>
    public sealed class UnRegisterListContainer : IUnRegisterList
    {
        /// <summary>当前由同一所有者集中管理的事件注销句柄。</summary>
        public List<IUnRegister> UnRegisterList { get; } = new();
    }
}
