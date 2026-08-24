#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;

namespace RPG.DialogueSystemModule.Editor
{
    /// <summary>
    /// 仅供 Dialogue Graph Editor 使用的预定义 SpeakerId 持久化设置。
    /// </summary>
    [FilePath("ProjectSettings/DialogueSpeakerIdSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class DialogueSpeakerIdSettings : ScriptableSingleton<DialogueSpeakerIdSettings>
    {
        #region 序列化字段

        private List<string> speakerIds = new List<string>();

        #endregion

        #region 属性

        /// <summary>获取当前预定义 SpeakerId 的只读集合。</summary>
        internal IReadOnlyList<string> SpeakerIds => speakerIds;

        #endregion

        #region 公开编辑操作

        /// <summary>
        /// 添加一个唯一的 SpeakerId 并立即持久化 Editor-only 设置。
        /// </summary>
        /// <param name="speakerId">待添加的非空 SpeakerId。</param>
        /// <returns>添加成功时返回 true。</returns>
        internal bool AddSpeakerId(string speakerId)
        {
            string normalized = Normalize(speakerId);
            if (string.IsNullOrEmpty(normalized) || speakerIds.Contains(normalized)) return false;
            speakerIds.Add(normalized);
            Save(true);
            return true;
        }

        /// <summary>
        /// 删除指定 SpeakerId 并立即持久化 Editor-only 设置。
        /// </summary>
        /// <param name="speakerId">待删除的 SpeakerId。</param>
        /// <returns>删除成功时返回 true。</returns>
        internal bool RemoveSpeakerId(string speakerId)
        {
            int index = speakerIds.IndexOf(speakerId);
            if (index < 0) return false;
            speakerIds.RemoveAt(index);
            Save(true);
            return true;
        }

        /// <summary>
        /// 重命名 SpeakerId，同时保持列表唯一并持久化设置。
        /// </summary>
        /// <param name="oldSpeakerId">旧 SpeakerId。</param>
        /// <param name="newSpeakerId">新 SpeakerId。</param>
        /// <returns>重命名成功时返回 true。</returns>
        internal bool RenameSpeakerId(string oldSpeakerId, string newSpeakerId)
        {
            string normalized = Normalize(newSpeakerId);
            int index = speakerIds.IndexOf(oldSpeakerId);
            if (index < 0 || string.IsNullOrEmpty(normalized) ||
                (speakerIds.Contains(normalized) && !string.Equals(oldSpeakerId, normalized, StringComparison.Ordinal)))
                return false;

            speakerIds[index] = normalized;
            Save(true);
            return true;
        }

        /// <summary>
        /// 保存设置资产，供窗口关闭和编辑操作复用。
        /// </summary>
        internal void SaveSettings() => Save(true);

        #endregion

        #region 内部辅助

        /// <summary>去除 SpeakerId 首尾空白，确保比较使用序数字符串。</summary>
        /// <param name="speakerId">原始 SpeakerId。</param>
        /// <returns>规范化结果。</returns>
        private static string Normalize(string speakerId) => speakerId?.Trim() ?? string.Empty;

        #endregion
    }
}
#endif
