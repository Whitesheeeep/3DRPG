using UnityEngine;
using WS_Modules.Singleton;

namespace WS_Modules.ConfigInstaller
{
    /// <summary>
    /// 负责注册配置的系统，确保在游戏运行时可以正确加载和使用配置数据。
    /// 为全局<c>最高优先级</c>的配置注册系统，确保在游戏运行时可以正确加载和使用配置数据。
    /// </summary>
    public sealed class ConfigRegisterSystem : SingletonBase<ConfigRegisterSystem>
    {
        private ConfigRegisterNodeBase rootNode;

        private ConfigRegisterSystem()
        {
        }

        public bool Registered { get; private set; }

        public void Initialize(ConfigRegisterSetting setting)
        {
            Register(setting);
        }

        public void Register(ConfigRegisterSetting setting)
        {
            ResetState();

            if (setting == null)
            {
                Debug.LogWarning("[ConfigRegisterSystem] ConfigRegisterSetting is null, skip register.");
                Registered = true;
                return;
            }

            rootNode = setting.rootNode;
            rootNode?.Register();
            Registered = true;

            if (setting.clearRootNodeAfterRegister)
            {
                ClearRuntimeReferences();
            }
        }

        public void ClearRuntimeReferences()
        {
            rootNode = null;
        }

        public void ResetState()
        {
            Registered = false;
            ClearRuntimeReferences();
        }
    }
}
