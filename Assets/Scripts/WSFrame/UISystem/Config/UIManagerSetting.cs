using System;
using UnityEngine;

namespace WS_Modules.UIModule
{
    [Serializable]
    public class UIManagerSetting
    {
        [Tooltip("UI 根节点预制体的资源加载路径。")]
        public string uiRootPath;

        [Tooltip("UI Camera 预制体的资源加载路径。")]
        public string uiCameraPrefabPath;

        [Tooltip("UI EventSystem 预制体的资源加载路径。")]
        public string uiEventSystemPrefabPath;

        [Tooltip("窗口配置表，记录窗口名称和窗口预制体加载路径。")]
        public WindowConfig windowConfig;

        [Tooltip("是否使用单遮罩模式。启用后 UIManager 会在当前顶层窗口上显示唯一遮罩。")]
        public bool isSingleMask;

        [Tooltip("组件绑定脚本生成路径。")]
        [WSFolderPath]
        public string BindComponentGeneratorPath = "";

        [Tooltip("组件绑定脚本生成时使用的命名空间。")]
        public string BindComponentNameSpace = "";

        [Tooltip("窗口交互脚本生成路径。")]
        [WSFolderPath]
        public string WindowGeneratorPath = "";

        [Tooltip("Item 脚本生成路径。")]
        [WSFolderPath]
        public string ItemScriptsGeneratorPath = "";

        [Tooltip("窗口预制体存放路径。框架会根据这些路径自动计算窗口加载路径，新增窗口无需手动配置。")]
        [WSFolderPath]
        public string[] WindowPrefabFolderPathArr;

        [Tooltip("自动生成脚本时需要额外引入的命名空间。")]
        public string[] UsingNameSpaceArr;
    }
}
