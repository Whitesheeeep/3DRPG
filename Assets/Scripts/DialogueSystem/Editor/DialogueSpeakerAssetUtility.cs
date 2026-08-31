#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RPG.DialogueSystemModule.Editor
{
    /// <summary>
    /// 提供 DialogueSpeaker 资产的创建、查询、选择和重命名操作，不缓存第二份身份数据。
    /// </summary>
    internal static class DialogueSpeakerAssetUtility
    {
        #region 资产查询

        /// <summary>
        /// 查询项目中全部 DialogueSpeaker 资产，并按显示名称稳定排序。
        /// </summary>
        /// <returns>当前项目中的 Speaker 资产集合。</returns>
        internal static IReadOnlyList<DialogueSpeaker> FindAll()
        {
            List<DialogueSpeaker> speakers = new List<DialogueSpeaker>();
            string[] assetGuids = AssetDatabase.FindAssets("t:DialogueSpeaker");
            for (int index = 0; index < assetGuids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(assetGuids[index]);
                DialogueSpeaker speaker = AssetDatabase.LoadAssetAtPath<DialogueSpeaker>(path);
                if (speaker != null) speakers.Add(speaker);
            }

            speakers.Sort(CompareByName);
            return speakers;
        }

        /// <summary>
        /// 在编辑器中选中并定位一个 Speaker 资产。
        /// </summary>
        /// <param name="speaker">待选中的 Speaker 资产。</param>
        internal static void Select(DialogueSpeaker speaker)
        {
            if (speaker == null) return;
            Selection.activeObject = speaker;
            EditorGUIUtility.PingObject(speaker);
        }

        #endregion

        #region 资产编辑

        /// <summary>
        /// 在指定项目路径创建一个 DialogueSpeaker 资产，并将其对象名设置为文件名。
        /// </summary>
        /// <param name="assetPath">Project 相对资产路径。</param>
        /// <returns>创建后的 Speaker 资产；路径为空时返回空。</returns>
        internal static DialogueSpeaker Create(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;

            DialogueSpeaker speaker = ScriptableObject.CreateInstance<DialogueSpeaker>();
            speaker.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(speaker, assetPath);
            EditorUtility.SetDirty(speaker);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Select(speaker);
            return speaker;
        }

        /// <summary>
        /// 重命名 Speaker 的资产文件，并保持主对象与文件名一致。
        /// </summary>
        /// <param name="speaker">待重命名的 Speaker 资产。</param>
        /// <param name="newName">新的 SO.name。</param>
        /// <returns>文件名或主对象名发生变化并保存成功时返回 true。</returns>
        internal static bool Rename(DialogueSpeaker speaker, string newName)
        {
            string normalized = newName?.Trim() ?? string.Empty;
            if (speaker == null || string.IsNullOrEmpty(normalized)) return false;
            if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                string.Equals(normalized, ".", StringComparison.Ordinal) ||
                string.Equals(normalized, "..", StringComparison.Ordinal))
            {
                Debug.LogError($"DialogueSpeaker 名称不是有效的资产文件名：{normalized}", speaker);
                return false;
            }

            // 只有已经保存的独立主资产才能安全改名；场景对象、临时对象和嵌套子资产不能改写外层文件。
            if (!EditorUtility.IsPersistent(speaker) || !AssetDatabase.IsMainAsset(speaker))
            {
                Debug.LogError("DialogueSpeaker 必须是已保存到项目中的独立主资产才能重命名。", speaker);
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(speaker);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("DialogueSpeaker 必须是已保存到项目中的主资产才能重命名。", speaker);
                return false;
            }

            string currentFileName = Path.GetFileNameWithoutExtension(assetPath);
            string extension = Path.GetExtension(assetPath);
            string directory = Path.GetDirectoryName(assetPath);
            string targetPath = string.IsNullOrEmpty(directory)
                ? normalized + extension
                : Path.Combine(directory, normalized + extension).Replace('\\', '/');

            // 文件名已经正确时，只修复可能残留的主对象名称不一致，不触发无意义的资产操作。
            if (string.Equals(currentFileName, normalized, StringComparison.Ordinal))
            {
                if (string.Equals(speaker.name, normalized, StringComparison.Ordinal)) return false;
                speaker.name = normalized;
                EditorUtility.SetDirty(speaker);
                AssetDatabase.SaveAssets();
                return true;
            }

            // 先拒绝同目录下的目标文件，避免覆盖其他 Speaker 或产生引用歧义。
            UnityEngine.Object targetAsset = AssetDatabase.LoadMainAssetAtPath(targetPath);
            if (targetAsset != null || File.Exists(targetPath))
            {
                Debug.LogError($"目标 DialogueSpeaker 资产已存在：{targetPath}", targetAsset ?? speaker);
                return false;
            }

            // 必须先重命名文件，再同步主对象名称，避免 Unity 在导入期间报告名称不匹配。
            string renameError = AssetDatabase.RenameAsset(assetPath, normalized);
            if (!string.IsNullOrEmpty(renameError))
            {
                Debug.LogError($"DialogueSpeaker 重命名失败：{renameError}", speaker);
                return false;
            }

            if (!string.Equals(speaker.name, normalized, StringComparison.Ordinal))
            {
                speaker.name = normalized;
                EditorUtility.SetDirty(speaker);
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        #endregion

        #region 内部辅助

        /// <summary>按 SpeakerName 和资产路径进行稳定排序。</summary>
        /// <param name="left">左侧 Speaker。</param>
        /// <param name="right">右侧 Speaker。</param>
        /// <returns>排序结果。</returns>
        private static int CompareByName(DialogueSpeaker left, DialogueSpeaker right)
        {
            int nameComparison = string.Compare(left?.SpeakerName, right?.SpeakerName,
                StringComparison.Ordinal);
            if (nameComparison != 0) return nameComparison;
            string leftPath = left == null ? string.Empty : AssetDatabase.GetAssetPath(left);
            string rightPath = right == null ? string.Empty : AssetDatabase.GetAssetPath(right);
            return string.Compare(leftPath, rightPath, StringComparison.Ordinal);
        }

        #endregion
    }
}
#endif
