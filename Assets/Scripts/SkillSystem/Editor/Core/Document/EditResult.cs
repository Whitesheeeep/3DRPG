#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace RPG.SkillSystem.Editor
{
    #region Edit results

    /// <summary>
    /// 表示一次 Document 编辑操作的成功状态和失败原因。
    /// </summary>
    internal readonly struct EditResult
    {
        public bool Succeeded { get; }
        public string Message { get; }

        // 统一限制结果只能通过成功或失败工厂创建。
        private EditResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message;
        }

        /// <summary>
        /// 创建表示编辑成功的结果。
        /// </summary>
        public static EditResult Success() => new(true, string.Empty);

        /// <summary>
        /// 创建表示编辑失败及其原因的结果。
        /// </summary>
        public static EditResult Failure(string message) => new(false, message);
    }

    /// <summary>
    /// 表示一次批量内容创建的编辑结果，以及成功创建后用于恢复选择的稳定 Item GUID。
    /// </summary>
    internal readonly struct ItemsCreateResult
    {
        public EditResult EditResult { get; }
        public IReadOnlyList<string> ItemIds { get; }
        public bool Succeeded => EditResult.Succeeded;

        // 统一限制结果只能通过成功或失败工厂创建，避免失败结果携带部分 GUID。
        private ItemsCreateResult(EditResult editResult, IReadOnlyList<string> itemIds)
        {
            EditResult = editResult;
            ItemIds = itemIds ?? Array.Empty<string>();
        }

        /// <summary>
        /// 创建包含全部新 Item GUID 的成功结果。
        /// </summary>
        public static ItemsCreateResult Success(IReadOnlyList<string> itemIds) =>
            new(EditResult.Success(), itemIds);

        /// <summary>
        /// 创建不携带任何部分创建数据的失败结果。
        /// </summary>
        public static ItemsCreateResult Failure(string message) =>
            new(EditResult.Failure(message), Array.Empty<string>());
    }

    #endregion
}
#endif
