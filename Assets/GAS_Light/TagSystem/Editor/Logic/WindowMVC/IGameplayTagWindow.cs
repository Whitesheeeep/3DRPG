#if UNITY_EDITOR
using System;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.Editor
{
    /// <summary>定义嵌入式 Gameplay Tag 页面的数据库与资源生命周期能力。</summary>
    public interface IGameplayTagWindow : IDisposable
    {
        /// <summary>获取当前通过 SessionState 选中的数据库。</summary>
        GameplayTagDatabase CurrentDatabase { get; }

        /// <summary>切换 Tag 数据库，并指定是否恢复该数据库的节点选择状态。</summary>
        /// <param name="database">需要编辑的数据库；null 表示清空选择。</param>
        /// <param name="restoreSelection">是否从 SessionState 恢复节点选择。</param>
        void SetDatabase(GameplayTagDatabase database, bool restoreSelection);
    }
}
#endif
