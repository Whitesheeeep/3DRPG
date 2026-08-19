using System;

namespace RPG.TaskSystem
{
    #region 标识值对象

    /// <summary>
    /// 表示任务配置和运行时状态共用的稳定任务标识。
    /// </summary>
    public readonly struct TaskId : IEquatable<TaskId>, IComparable<TaskId>
    {
        /// <summary>
        /// 稳定标识允许的最大字符数。
        /// </summary>
        public const int MaxLength = 128;

        /// <summary>
        /// 创建任务标识。
        /// </summary>
        /// <param name="value">任务稳定字符串。</param>
        /// <exception cref="ArgumentException">标识为空或格式非法时抛出。</exception>
        public TaskId(string value)
        {
            if (!TaskIdentifierRules.IsValid(value))
            {
                throw new ArgumentException("任务 ID 必须是非空且不超过 128 个字符的稳定标识。", nameof(value));
            }

            Value = value;
        }

        /// <summary>
        /// 获取稳定字符串值。
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// 获取当前值是否有效。
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Value);

        /// <summary>
        /// 尝试创建任务标识。
        /// </summary>
        /// <param name="value">待校验字符串。</param>
        /// <param name="taskId">创建成功后的任务标识。</param>
        /// <returns>输入有效时返回 true。</returns>
        public static bool TryCreate(string value, out TaskId taskId)
        {
            if (TaskIdentifierRules.IsValid(value))
            {
                taskId = new TaskId(value);
                return true;
            }

            taskId = default;
            return false;
        }

        /// <summary>
        /// 按 Ordinal 规则比较任务标识。
        /// </summary>
        /// <param name="other">待比较标识。</param>
        /// <returns>比较结果。</returns>
        public int CompareTo(TaskId other) =>
            string.Compare(Value ?? string.Empty, other.Value ?? string.Empty, StringComparison.Ordinal);

        /// <summary>
        /// 判断两个任务标识是否相等。
        /// </summary>
        /// <param name="other">待比较标识。</param>
        /// <returns>相等时返回 true。</returns>
        public bool Equals(TaskId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <summary>
        /// 判断对象是否为相同任务标识。
        /// </summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>对象为相同任务标识时返回 true。</returns>
        public override bool Equals(object obj) => obj is TaskId other && Equals(other);

        /// <summary>
        /// 获取与 Ordinal 相等规则一致的哈希值。
        /// </summary>
        /// <returns>标识哈希值。</returns>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <summary>
        /// 返回稳定字符串值。
        /// </summary>
        /// <returns>稳定字符串。</returns>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>
        /// 判断两个任务标识是否相等。
        /// </summary>
        /// <param name="left">左侧标识。</param>
        /// <param name="right">右侧标识。</param>
        /// <returns>相等时返回 true。</returns>
        public static bool operator ==(TaskId left, TaskId right) => left.Equals(right);

        /// <summary>
        /// 判断两个任务标识是否不相等。
        /// </summary>
        /// <param name="left">左侧标识。</param>
        /// <param name="right">右侧标识。</param>
        /// <returns>不相等时返回 true。</returns>
        public static bool operator !=(TaskId left, TaskId right) => !left.Equals(right);
    }

    /// <summary>
    /// 表示任务内部稳定且唯一的目标标识。
    /// </summary>
    public readonly struct ObjectiveId : IEquatable<ObjectiveId>, IComparable<ObjectiveId>
    {
        /// <summary>
        /// 创建目标标识。
        /// </summary>
        /// <param name="value">目标稳定字符串。</param>
        /// <exception cref="ArgumentException">标识为空或格式非法时抛出。</exception>
        public ObjectiveId(string value)
        {
            if (!TaskIdentifierRules.IsValid(value))
            {
                throw new ArgumentException("目标 ID 必须是非空且不超过 128 个字符的稳定标识。", nameof(value));
            }

            Value = value;
        }

        /// <summary>
        /// 获取稳定字符串值。
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// 获取当前值是否有效。
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Value);

        /// <summary>
        /// 按 Ordinal 规则比较目标标识。
        /// </summary>
        /// <param name="other">待比较标识。</param>
        /// <returns>比较结果。</returns>
        public int CompareTo(ObjectiveId other) =>
            string.Compare(Value ?? string.Empty, other.Value ?? string.Empty, StringComparison.Ordinal);

        /// <summary>
        /// 判断两个目标标识是否相等。
        /// </summary>
        /// <param name="other">待比较标识。</param>
        /// <returns>相等时返回 true。</returns>
        public bool Equals(ObjectiveId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <summary>
        /// 判断对象是否为相同目标标识。
        /// </summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>对象为相同目标标识时返回 true。</returns>
        public override bool Equals(object obj) => obj is ObjectiveId other && Equals(other);

        /// <summary>
        /// 获取与 Ordinal 相等规则一致的哈希值。
        /// </summary>
        /// <returns>标识哈希值。</returns>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <summary>
        /// 返回稳定字符串值。
        /// </summary>
        /// <returns>稳定字符串。</returns>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>
        /// 判断两个目标标识是否相等。
        /// </summary>
        /// <param name="left">左侧标识。</param>
        /// <param name="right">右侧标识。</param>
        /// <returns>相等时返回 true。</returns>
        public static bool operator ==(ObjectiveId left, ObjectiveId right) => left.Equals(right);

        /// <summary>
        /// 判断两个目标标识是否不相等。
        /// </summary>
        /// <param name="left">左侧标识。</param>
        /// <param name="right">右侧标识。</param>
        /// <returns>不相等时返回 true。</returns>
        public static bool operator !=(ObjectiveId left, ObjectiveId right) => !left.Equals(right);
    }

    /// <summary>
    /// 表示可扩展的任务分类标识。
    /// </summary>
    public readonly struct TaskCategoryId : IEquatable<TaskCategoryId>, IComparable<TaskCategoryId>
    {
        /// <summary>
        /// 创建任务分类标识。
        /// </summary>
        /// <param name="value">分类稳定字符串。</param>
        /// <exception cref="ArgumentException">标识为空或格式非法时抛出。</exception>
        public TaskCategoryId(string value)
        {
            if (!TaskIdentifierRules.IsValid(value))
            {
                throw new ArgumentException("任务分类 ID 必须是非空且不超过 128 个字符的稳定标识。", nameof(value));
            }

            Value = value;
        }

        /// <summary>
        /// 获取稳定字符串值。
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// 获取当前值是否有效。
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Value);

        /// <summary>
        /// 按 Ordinal 规则比较分类标识。
        /// </summary>
        /// <param name="other">待比较标识。</param>
        /// <returns>比较结果。</returns>
        public int CompareTo(TaskCategoryId other) =>
            string.Compare(Value ?? string.Empty, other.Value ?? string.Empty, StringComparison.Ordinal);

        /// <summary>
        /// 判断两个分类标识是否相等。
        /// </summary>
        /// <param name="other">待比较标识。</param>
        /// <returns>相等时返回 true。</returns>
        public bool Equals(TaskCategoryId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <summary>
        /// 判断对象是否为相同分类标识。
        /// </summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>对象为相同分类标识时返回 true。</returns>
        public override bool Equals(object obj) => obj is TaskCategoryId other && Equals(other);

        /// <summary>
        /// 获取与 Ordinal 相等规则一致的哈希值。
        /// </summary>
        /// <returns>标识哈希值。</returns>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <summary>
        /// 返回稳定字符串值。
        /// </summary>
        /// <returns>稳定字符串。</returns>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>
        /// 判断两个分类标识是否相等。
        /// </summary>
        /// <param name="left">左侧标识。</param>
        /// <param name="right">右侧标识。</param>
        /// <returns>相等时返回 true。</returns>
        public static bool operator ==(TaskCategoryId left, TaskCategoryId right) => left.Equals(right);

        /// <summary>
        /// 判断两个分类标识是否不相等。
        /// </summary>
        /// <param name="left">左侧标识。</param>
        /// <param name="right">右侧标识。</param>
        /// <returns>不相等时返回 true。</returns>
        public static bool operator !=(TaskCategoryId left, TaskCategoryId right) => !left.Equals(right);
    }

    /// <summary>
    /// 提供任务模型共用的稳定字符串校验规则。
    /// </summary>
    internal static class TaskIdentifierRules
    {
        /// <summary>
        /// 判断输入是否可作为稳定任务模型标识。
        /// </summary>
        /// <param name="value">待校验字符串。</param>
        /// <returns>有效时返回 true。</returns>
        public static bool IsValid(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Length <= TaskId.MaxLength &&
                   value.IndexOfAny(new[] { '/', '\\', '\r', '\n', '\t' }) < 0;
        }
    }

    #endregion
}
