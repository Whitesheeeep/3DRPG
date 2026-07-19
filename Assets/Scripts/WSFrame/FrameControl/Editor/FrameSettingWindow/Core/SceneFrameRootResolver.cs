using UnityEngine;
using WS_Modules.Extensions;

namespace WS_Modules
{
    internal sealed class SceneFrameRootResolver : IFrameRootResolver
    {
        public WSFrameRoot Resolve()
        {
            var globalSettingObj = GameObject.Find("WSFrameRoot");
            if (globalSettingObj == null)
            {
                return null;
            }

            return globalSettingObj.GetOrAddComponent<WSFrameRoot>();
        }
    }
}

