using UnityEngine;
using WS_Modules.Singleton;

namespace WS_Modules.ConfigInstaller
{
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
