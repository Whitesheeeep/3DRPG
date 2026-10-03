#if UNITY_EDITOR
using RPG.TaskSystemNS;
using UnityEditor;

namespace RPG.TaskSystemNS.Editor
{
    /// <summary>保存任务编辑器读取的原始资产字段，允许界面展示尚未通过运行时校验的草稿。</summary>
    internal sealed class TaskDefinitionEditorEntry
    {
        #region 属性

        /// <summary>获取任务定义资产。</summary>
        internal TaskDefinition Definition { get; }

        /// <summary>获取序列化的原始任务 ID。</summary>
        internal string TaskId { get; }

        /// <summary>获取序列化的原始分类 ID。</summary>
        internal string CategoryId { get; }

        /// <summary>获取任务资产路径。</summary>
        internal string AssetPath { get; }

        /// <summary>获取任务是否已登记在当前数据库。</summary>
        internal bool IsRegistered { get; }

        /// <summary>获取任务 ID 是否符合运行时标识规则。</summary>
        internal bool HasValidTaskId { get; }

        /// <summary>获取分类是否存在于静态分类表。</summary>
        internal bool HasValidCategory { get; }

        /// <summary>获取当前资产可直接展示的配置问题。</summary>
        internal string ConfigurationIssue { get; }

        #endregion

        #region 构造

        /// <summary>从 Unity 序列化边界读取原始字段并生成列表所需校验摘要。</summary>
        /// <param name="definition">任务定义资产。</param>
        /// <param name="isRegistered">任务是否登记在当前数据库。</param>
        internal TaskDefinitionEditorEntry(TaskDefinition definition, bool isRegistered)
        {
            Definition = definition;
            IsRegistered = isRegistered;
            AssetPath = AssetDatabase.GetAssetPath(definition);

            // 编辑器读取原始字符串，避免无效草稿通过 TaskId 或 TaskCategoryId 构造时提前抛错。
            using var serializedDefinition = new SerializedObject(definition);
            serializedDefinition.UpdateIfRequiredOrScript();
            TaskId = serializedDefinition.FindProperty("taskId").stringValue;
            CategoryId = serializedDefinition.FindProperty("categoryId").stringValue;

            HasValidTaskId = RPG.TaskSystemNS.TaskId.TryCreate(TaskId, out _);
            HasValidCategory = TaskCategoryCatalog.IsDefined(CategoryId);
            ConfigurationIssue = BuildConfigurationIssue();
        }

        #endregion

        #region 校验

        /// <summary>组合列表中需要提示的标识配置问题。</summary>
        /// <returns>有问题时返回易读摘要，否则返回空文本。</returns>
        private string BuildConfigurationIssue()
        {
            if (!HasValidTaskId && !HasValidCategory)
            {
                return "TaskId 为空或格式无效，分类未登记";
            }

            if (!HasValidTaskId)
            {
                return string.IsNullOrWhiteSpace(TaskId)
                    ? "TaskId 为空"
                    : $"TaskId 格式无效：{TaskId}";
            }

            return HasValidCategory
                ? string.Empty
                : $"分类未登记：{(string.IsNullOrWhiteSpace(CategoryId) ? "（空）" : CategoryId)}";
        }

        #endregion
    }
}
#endif
