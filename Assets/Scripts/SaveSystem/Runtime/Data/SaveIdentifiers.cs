using System;

namespace RPG.SaveSystem
{
    #region 槽位标识

    /// <summary>
    /// 表示一个长期稳定的存档槽位标识，内部使用规范化的 GUID N 格式字符串。
    /// </summary>
    public readonly struct SaveSlotId : IEquatable<SaveSlotId>, IComparable<SaveSlotId>
    {
        /// <summary>
        /// 获取规范化的 GUID 字符串；默认值返回空字符串。
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// 获取当前标识是否包含有效 GUID。
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Value);

        /// <summary>
        /// 使用 GUID 创建槽位标识。
        /// </summary>
        /// <param name="value">用于构造槽位标识的 GUID。</param>
        /// <exception cref="ArgumentException">传入空 GUID 时抛出。</exception>
        public SaveSlotId(Guid value)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("存档槽位 ID 不能使用空 GUID。", nameof(value));
            }

            Value = value.ToString("N");
        }

        /// <summary>
        /// 使用可解析为 GUID 的字符串创建槽位标识，并将其规范化为 N 格式。
        /// </summary>
        /// <param name="value">GUID 字符串。</param>
        /// <exception cref="ArgumentException">字符串为空或空白时抛出。</exception>
        /// <exception cref="FormatException">字符串不是有效 GUID 或表示空 GUID 时抛出。</exception>
        public SaveSlotId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("存档槽位 ID 不能为空。", nameof(value));
            }

            if (!Guid.TryParse(value, out Guid guid) || guid == Guid.Empty)
            {
                throw new FormatException($"存档槽位 ID 不是有效的非空 GUID：{value}");
            }

            Value = guid.ToString("N");
        }

        /// <summary>
        /// 创建一个全新的随机槽位标识。
        /// </summary>
        /// <returns>基于新 GUID 的槽位标识。</returns>
        public static SaveSlotId CreateNew() => new SaveSlotId(Guid.NewGuid());

        /// <summary>
        /// 严格解析槽位标识。
        /// </summary>
        /// <param name="value">待解析的 GUID 字符串。</param>
        /// <returns>规范化后的槽位标识。</returns>
        /// <exception cref="ArgumentException">字符串为空或空白时抛出。</exception>
        /// <exception cref="FormatException">字符串不是有效 GUID 或表示空 GUID 时抛出。</exception>
        public static SaveSlotId Parse(string value) => new SaveSlotId(value);

        /// <summary>
        /// 尝试解析槽位标识，不在外部输入无效时抛出异常。
        /// </summary>
        /// <param name="value">待解析的 GUID 字符串。</param>
        /// <param name="slotId">解析成功时返回规范化槽位标识。</param>
        /// <returns>输入是有效非空 GUID 时返回 <see langword="true"/>。</returns>
        public static bool TryParse(string value, out SaveSlotId slotId)
        {
            if (!string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out Guid guid) && guid != Guid.Empty)
            {
                slotId = new SaveSlotId(guid);
                return true;
            }

            slotId = default;
            return false;
        }

        /// <summary>
        /// 将槽位标识转换回 GUID。
        /// </summary>
        /// <returns>与当前槽位标识对应的 GUID。</returns>
        /// <exception cref="InvalidOperationException">当前值是默认无效标识时抛出。</exception>
        public Guid ToGuid()
        {
            if (!IsValid)
            {
                throw new InvalidOperationException("默认 SaveSlotId 不能转换为 GUID。");
            }

            return Guid.ParseExact(Value, "N");
        }

        /// <summary>
        /// 比较两个槽位标识的规范字符串顺序。
        /// </summary>
        /// <param name="other">另一个槽位标识。</param>
        /// <returns>Ordinal 比较结果。</returns>
        public int CompareTo(SaveSlotId other) =>
            string.Compare(Value ?? string.Empty, other.Value ?? string.Empty, StringComparison.Ordinal);

        /// <summary>
        /// 判断两个槽位标识是否相等。
        /// </summary>
        /// <param name="other">另一个槽位标识。</param>
        /// <returns>规范字符串完全相同时返回 <see langword="true"/>。</returns>
        public bool Equals(SaveSlotId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <summary>
        /// 判断对象是否是相等的槽位标识。
        /// </summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>对象表示相同槽位标识时返回 <see langword="true"/>。</returns>
        public override bool Equals(object obj) => obj is SaveSlotId other && Equals(other);

        /// <summary>
        /// 获取与 Ordinal 相等性一致的哈希值。
        /// </summary>
        /// <returns>槽位标识哈希值。</returns>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <summary>
        /// 获取规范化字符串。
        /// </summary>
        /// <returns>有效时返回 N 格式 GUID，默认值返回空字符串。</returns>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>
        /// 判断两个槽位标识是否相等。
        /// </summary>
        /// <param name="left">左侧标识。</param>
        /// <param name="right">右侧标识。</param>
        /// <returns>两个标识相等时返回 <see langword="true"/>。</returns>
        public static bool operator ==(SaveSlotId left, SaveSlotId right) => left.Equals(right);

        /// <summary>
        /// 判断两个槽位标识是否不相等。
        /// </summary>
        /// <param name="left">左侧标识。</param>
        /// <param name="right">右侧标识。</param>
        /// <returns>两个标识不相等时返回 <see langword="true"/>。</returns>
        public static bool operator !=(SaveSlotId left, SaveSlotId right) => !left.Equals(right);
    }

    #endregion

    #region 模块标识

    /// <summary>
    /// 表示一个稳定、可读且区分大小写的存档模块标识。
    /// </summary>
    public readonly struct SaveModuleId : IEquatable<SaveModuleId>, IComparable<SaveModuleId>
    {
        /// <summary>
        /// 模块标识允许的最大字符数量。
        /// </summary>
        public const int MaxLength = 64;

        /// <summary>
        /// 获取模块标识字符串；默认值返回空字符串。
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// 获取当前标识是否有效。
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Value);

        /// <summary>
        /// 创建模块标识。
        /// </summary>
        /// <param name="value">由小写字母开头，可包含小写字母、数字、点、下划线和连字符的标识。</param>
        /// <exception cref="ArgumentException">标识为空或格式非法时抛出。</exception>
        public SaveModuleId(string value)
        {
            if (!IsValidValue(value))
            {
                throw new ArgumentException(
                    "存档模块 ID 必须由小写字母开头，且只能包含小写字母、数字、点、下划线和连字符，长度不能超过 64。",
                    nameof(value));
            }

            Value = value;
        }

        /// <summary>
        /// 尝试创建模块标识。
        /// </summary>
        /// <param name="value">待校验的模块标识字符串。</param>
        /// <param name="moduleId">校验成功时返回模块标识。</param>
        /// <returns>字符串符合模块 ID 规则时返回 <see langword="true"/>。</returns>
        public static bool TryCreate(string value, out SaveModuleId moduleId)
        {
            if (IsValidValue(value))
            {
                moduleId = new SaveModuleId(value);
                return true;
            }

            moduleId = default;
            return false;
        }

        /// <summary>
        /// 比较两个模块标识的 Ordinal 顺序。
        /// </summary>
        /// <param name="other">另一个模块标识。</param>
        /// <returns>Ordinal 比较结果。</returns>
        public int CompareTo(SaveModuleId other) =>
            string.Compare(Value ?? string.Empty, other.Value ?? string.Empty, StringComparison.Ordinal);

        /// <summary>
        /// 判断两个模块标识是否相等。
        /// </summary>
        /// <param name="other">另一个模块标识。</param>
        /// <returns>字符串完全相同时返回 <see langword="true"/>。</returns>
        public bool Equals(SaveModuleId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <summary>
        /// 判断对象是否是相等的模块标识。
        /// </summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>对象表示相同模块标识时返回 <see langword="true"/>。</returns>
        public override bool Equals(object obj) => obj is SaveModuleId other && Equals(other);

        /// <summary>
        /// 获取与 Ordinal 相等性一致的哈希值。
        /// </summary>
        /// <returns>模块标识哈希值。</returns>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <summary>
        /// 获取模块标识字符串。
        /// </summary>
        /// <returns>有效标识字符串，默认值返回空字符串。</returns>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>
        /// 判断两个模块标识是否相等。
        /// </summary>
        /// <param name="left">左侧标识。</param>
        /// <param name="right">右侧标识。</param>
        /// <returns>两个标识相等时返回 <see langword="true"/>。</returns>
        public static bool operator ==(SaveModuleId left, SaveModuleId right) => left.Equals(right);

        /// <summary>
        /// 判断两个模块标识是否不相等。
        /// </summary>
        /// <param name="left">左侧标识。</param>
        /// <param name="right">右侧标识。</param>
        /// <returns>两个标识不相等时返回 <see langword="true"/>。</returns>
        public static bool operator !=(SaveModuleId left, SaveModuleId right) => !left.Equals(right);

        /// <summary>
        /// 校验模块标识是否符合稳定标识规则。
        /// </summary>
        /// <param name="value">待校验字符串。</param>
        /// <returns>符合规则时返回 <see langword="true"/>。</returns>
        private static bool IsValidValue(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxLength || value[0] < 'a' || value[0] > 'z')
            {
                return false;
            }

            for (int index = 1; index < value.Length; index++)
            {
                char character = value[index];
                bool isLowerLetter = character >= 'a' && character <= 'z';
                bool isDigit = character >= '0' && character <= '9';
                bool isSeparator = character == '.' || character == '_' || character == '-';
                if (!isLowerLetter && !isDigit && !isSeparator)
                {
                    return false;
                }
            }

            return true;
        }
    }

    #endregion
}
