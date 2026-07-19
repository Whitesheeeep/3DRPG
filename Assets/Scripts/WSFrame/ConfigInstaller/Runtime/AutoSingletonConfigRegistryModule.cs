using UnityEngine;
using WS_Modules.ConfigInstaller;

namespace WS_Modules.Singleton
{
    /// <summary>
    /// AutoSingleton 配置注册模块。作为组合节点挂在配置树中，子节点通常是各模块的 ConfigProvider。
    /// </summary>
    [CreateAssetMenu(fileName = "AutoConfigRegistryModule", menuName = "WSFrame/ConfigRegister/AutoConfigRegistryModule", order = 0)]
    public sealed class AutoSingletonConfigRegistryModule : CompositeConfigRegisterNode
    {
    }
}
