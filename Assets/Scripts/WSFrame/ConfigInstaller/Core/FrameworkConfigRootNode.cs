using UnityEngine;

namespace WS_Modules.ConfigInstaller
{
    /// <summary>
    /// 框架配置树的根节点资产，通常由 WSFrameSetting.configRegisterSetting 引用。
    /// </summary>
    [CreateAssetMenu(fileName = "FrameworkConfigRootNode", menuName = "WSFrame/ConfigInstaller/Root Node", order = 0)]
    public sealed class FrameworkConfigRootNode : CompositeConfigRegisterNode
    {
    }
}
