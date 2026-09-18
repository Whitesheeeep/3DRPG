#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using WS_Modules;

namespace RPG.RedDotSystemNS.Editor
{
    /// <summary>
    /// 保存红点节点设置页的项目级编辑偏好，不参与运行时红点数据。
    /// </summary>
    [FilePath("ProjectSettings/RedDotEditorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class RedDotEditorSettings : ScriptableSingleton<RedDotEditorSettings>
    {
        #region 常量与字段

        /// <summary>默认节点 Asset 保存目录。</summary>
        internal const string DefaultNodeAssetFolder =
            "Assets/Scripts/RedDotSystem/Runtime/Config/Assets/Nodes";

        [SerializeField, WSFolderPath]
        private string newNodeAssetFolder = DefaultNodeAssetFolder;

        #endregion

        #region 公开编辑状态

        /// <summary>获取新建节点 Asset 的默认项目相对目录。</summary>
        internal string NewNodeAssetFolder =>
            string.IsNullOrEmpty(newNodeAssetFolder)
                ? DefaultNodeAssetFolder
                : newNodeAssetFolder;

        /// <summary>
        /// 更新并保存新建节点 Asset 的默认目录。
        /// </summary>
        /// <param name="folderPath">以 Assets/ 开头的项目相对目录。</param>
        /// <exception cref="ArgumentException">目录路径格式无效时抛出。</exception>
        internal void SetNewNodeAssetFolder(string folderPath)
        {
            string normalizedPath = NormalizeAssetFolder(folderPath);
            if (!IsValidAssetFolder(normalizedPath))
            {
                throw new ArgumentException(
                    $"[RedDotEditorSettings] 节点 Asset 目录必须是 Assets/... 路径：{folderPath}。",
                    nameof(folderPath));
            }

            if (string.Equals(newNodeAssetFolder, normalizedPath, StringComparison.Ordinal))
            {
                return;
            }

            newNodeAssetFolder = normalizedPath;
            Save(true);
        }

        /// <summary>保存当前编辑器设置对象，供 UI Toolkit 原生属性绑定提交后的持久化。</summary>
        internal void SaveSettings()
        {
            Save(true);
        }

        #endregion

        #region 路径校验

        /// <summary>规范化 Unity 项目相对目录。</summary>
        /// <param name="folderPath">待规范化目录。</param>
        /// <returns>使用正斜杠且不包含尾部斜杠的目录。</returns>
        internal static string NormalizeAssetFolder(string folderPath)
        {
            string normalizedPath = (folderPath ?? string.Empty)
                .Trim()
                .Replace('\\', '/');
            while (normalizedPath.EndsWith("/", StringComparison.Ordinal))
            {
                normalizedPath = normalizedPath.Substring(0, normalizedPath.Length - 1);
            }

            return normalizedPath;
        }

        /// <summary>判断目录是否为合法的 Assets 项目相对路径。</summary>
        /// <param name="folderPath">待检查目录。</param>
        /// <returns>路径格式正确时返回 true。</returns>
        internal static bool IsValidAssetFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) ||
                (!string.Equals(folderPath, "Assets", StringComparison.Ordinal) &&
                 !folderPath.StartsWith("Assets/", StringComparison.Ordinal)) ||
                folderPath.Contains("//", StringComparison.Ordinal))
            {
                return false;
            }

            string[] segments = folderPath.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(segments[index]) ||
                    string.Equals(segments[index], ".", StringComparison.Ordinal) ||
                    string.Equals(segments[index], "..", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
#endif
