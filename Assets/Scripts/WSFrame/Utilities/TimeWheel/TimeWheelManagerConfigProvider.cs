using UnityEngine;
using WS_Modules.ConfigInstaller;
using WS_Modules.Singleton;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// TimeWheelManager 的配置 Provider。作为配置树叶子节点，将 TimeWheelConfig 注册给 AutoSingletonConfigRegistry。
    /// </summary>
    [CreateAssetMenu(fileName = "TimeWheelManagerConfigProvider", menuName = "WSFrame/AutoConfig/TimeWheelManager", order = 0)]
    public sealed class TimeWheelManagerConfigProvider : ConfigRegisterNodeBase
    {
        [SerializeField, Tooltip("注册给 TimeWheelManager 的配置。Provider 执行时会创建运行时副本，避免直接修改资产实例。")]
        private TimeWheelConfig config = new TimeWheelConfig();

        public override void Register()
        {
            AutoSingletonConfigRegistry.Register<TimeWheelManager, TimeWheelConfig>(config.CreateRuntimeCopy());
        }
    }
}
