using UnityEditor;
using UnityEngine;

namespace WS_Modules
{
    /// <summary>保存项目范围内的 SceneSystem 编辑器资产目录偏好。</summary>
    [FilePath("ProjectSettings/SceneLoadingEditorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class SceneLoadingEditorSettings : ScriptableSingleton<SceneLoadingEditorSettings>
    {
        #region 项目编辑器设置

        [SerializeField, WSFolderPath]
        private string nodeAssetFolder = "Assets/SceneLoadAssets";

        /// <summary>获取新建 SceneLoadConfig 和任务节点的默认目录。</summary>
        public string NodeAssetFolder => nodeAssetFolder;

        /// <summary>保存经过资产路径校验的目录值到当前 Unity 项目的 ProjectSettings。</summary>
        /// <param name="folder">Assets 下的项目相对目录。</param>
        public void SetNodeAssetFolder(string folder)
        {
            nodeAssetFolder = folder;
            Save(true);
        }

        #endregion
    }
}
