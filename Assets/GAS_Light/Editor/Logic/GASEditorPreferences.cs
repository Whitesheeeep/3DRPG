#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WS_Modules.GAS.Editor
{
    /// <summary>标识 GAS Editor 各类资产创建面板的最近目录。</summary>
    internal enum GASEditorAssetFolderKind
    {
        /// <summary>Gameplay Effect Data 资产。</summary>
        GameplayEffect,
        /// <summary>Gameplay Ability Data 资产。</summary>
        GameplayAbility,
        /// <summary>Gameplay Cue Data 资产。</summary>
        GameplayCue,
        /// <summary>Gameplay Cue Database 资产。</summary>
        GameplayCueDatabase
    }

    /// <summary>保存 GAS Editor 的项目级用户偏好，不把 Editor 状态写入运行时资产。</summary>
    internal static class GASEditorPreferences
    {
        #region 常量与字段

        private const string PreferenceKeyPrefix = "WSFrame.GAS.Editor.CreateFolder.";
        private const string DefaultCreateFolder = "Assets";

        #endregion

        #region 最近目录

        /// <summary>读取指定 GAS 资产类型上次创建时使用的项目文件夹。</summary>
        /// <param name="kind">需要读取的资产类型。</param>
        /// <returns>有效的 Unity Asset 文件夹路径；没有可用记录时返回 Assets。</returns>
        internal static string GetLastCreateFolder(GASEditorAssetFolderKind kind)
        {
            string folder = EditorPrefs.GetString(BuildPreferenceKey(kind), DefaultCreateFolder);
            folder = NormalizeAssetPath(folder);
            return AssetDatabase.IsValidFolder(folder) ? folder : DefaultCreateFolder;
        }

        /// <summary>记录保存面板确认后的资产父文件夹。</summary>
        /// <param name="kind">需要记录的资产类型。</param>
        /// <param name="assetPath">保存面板返回的 Unity Asset 路径。</param>
        internal static void RecordCreateAssetPath(
            GASEditorAssetFolderKind kind,
            string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;

            // 只在用户确认有效项目路径后记录父目录，取消面板不会覆盖上次偏好。
            string folder = Path.GetDirectoryName(assetPath);
            folder = NormalizeAssetPath(folder);
            if (!AssetDatabase.IsValidFolder(folder)) return;
            EditorPrefs.SetString(BuildPreferenceKey(kind), folder);
        }

        #endregion

        #region 内部辅助

        /// <summary>构造包含项目身份的 EditorPrefs Key，避免多个 Unity 项目共享目录记录。</summary>
        /// <param name="kind">需要构造 Key 的资产类型。</param>
        /// <returns>项目隔离后的偏好 Key。</returns>
        private static string BuildPreferenceKey(GASEditorAssetFolderKind kind)
        {
            // Application.dataPath 在项目之间稳定区分，直接纳入 Key 可避免额外哈希和碰撞处理。
            string projectIdentity = Application.dataPath.Replace('\\', '/');
            return PreferenceKeyPrefix + projectIdentity + "." + kind;
        }

        /// <summary>将路径转换为 Unity 统一使用的正斜杠格式。</summary>
        /// <param name="path">待规范化的项目路径。</param>
        /// <returns>规范化后的路径；空输入保持为空。</returns>
        private static string NormalizeAssetPath(string path) =>
            string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');

        #endregion
    }
}
#endif
