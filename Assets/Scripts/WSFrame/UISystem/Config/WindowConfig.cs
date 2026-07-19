using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WS_Modules.UIModule
{
    [CreateAssetMenu(fileName = "WindowConfig", menuName = "WSFrame/WindowConfig", order = 0)]
    public partial class WindowConfig : ScriptableObject
    {
        public List<WindowConfigData> windowConfigList = new();

        public WindowConfigData GetWindowData(string windowName, bool logError = true)
        {
            return windowConfigList.FirstOrDefault(w => w.windowName == windowName);
        }
    }

    /// <summary>
    /// 配置 UI window 预制体的名字和路径。
    /// </summary>
    [Serializable]
    public class WindowConfigData
    {
        public string windowName;
        public string windowPrefabPath;
    }
}
