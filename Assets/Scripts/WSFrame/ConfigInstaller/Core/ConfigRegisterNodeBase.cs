using UnityEngine;

namespace WS_Modules.ConfigInstaller
{
    public abstract class ConfigRegisterNodeBase : ScriptableObject, IConfigRegisterNode
    {
        public abstract void Register();
    }
}
