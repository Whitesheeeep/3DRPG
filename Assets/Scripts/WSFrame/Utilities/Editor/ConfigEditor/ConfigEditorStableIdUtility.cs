#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;

namespace WS_Modules.EditorExtensions
{
    /// <summary>
    /// 为配置编辑器提供类别编号 ID 的统一生成、解析和引用显示格式。
    /// 该工具只处理字符串和序列化计数器，不依赖具体的 Item 或 Character 类型。
    /// </summary>
    public static class ConfigEditorStableIdUtility
    {
        #region 常量

        private const int FirstNumber = 1;
        private const int LastNumber = 9999;

        #endregion

        #region 编号生成与解析

        /// <summary>
        /// 从序列化数据库计数器中分配一个新的类别编号，并推进计数器。
        /// </summary>
        /// <param name="prefix">小写 ASCII 类别前缀。</param>
        /// <param name="counterOwner">保存计数器的序列化数据库。</param>
        /// <param name="counterPropertyPath">计数器的序列化属性路径。</param>
        /// <param name="existingIds">当前数据库已使用的 ID。</param>
        /// <returns>形如 prefix_0001 的新 ID。</returns>
        /// <exception cref="ArgumentException">前缀或参数不合法时抛出。</exception>
        /// <exception cref="InvalidOperationException">计数器缺失或编号耗尽时抛出。</exception>
        public static string AllocateNextId(
            string prefix,
            SerializedObject counterOwner,
            string counterPropertyPath,
            IReadOnlyList<string> existingIds)
        {
            ValidatePrefix(prefix);
            if (counterOwner == null) throw new ArgumentNullException(nameof(counterOwner));
            if (string.IsNullOrWhiteSpace(counterPropertyPath)) throw new ArgumentException("计数器属性路径不能为空。", nameof(counterPropertyPath));
            if (existingIds == null) throw new ArgumentNullException(nameof(existingIds));

            SerializedProperty counterProperty = counterOwner.FindProperty(counterPropertyPath);
            if (counterProperty == null || counterProperty.propertyType != SerializedPropertyType.Integer)
                throw new InvalidOperationException($"数据库缺少整数编号计数器属性“{counterPropertyPath}”。");

            // 计数器是持久化的防回收记录；扫描现有 ID 只允许把它向前修正，不能让删除资产后复用旧编号。
            int nextNumber = Math.Max(FirstNumber, counterProperty.intValue);
            int largestNumber = 0;
            for (int index = 0; index < existingIds.Count; index++)
            {
                if (!TryParseNumber(existingIds[index], prefix, out int number)) continue;
                largestNumber = Math.Max(largestNumber, number);
            }

            nextNumber = Math.Max(nextNumber, largestNumber + 1);
            if (nextNumber > LastNumber)
                throw new InvalidOperationException($"类别 ID 前缀“{prefix}”已分配至 {LastNumber:D4}，无法继续创建。");

            string candidate = FormatId(prefix, nextNumber);
            for (int index = 0; index < existingIds.Count; index++)
                if (string.Equals(existingIds[index], candidate, StringComparison.Ordinal))
                    throw new InvalidOperationException($"数据库中已存在重复 ID“{candidate}”。");

            counterProperty.intValue = nextNumber + 1;
            counterOwner.ApplyModifiedProperties();
            return candidate;
        }

        /// <summary>解析指定前缀的四位编号 ID。</summary>
        /// <param name="id">待解析 ID。</param>
        /// <param name="expectedPrefix">预期的小写类别前缀。</param>
        /// <param name="number">解析出的编号。</param>
        /// <returns>格式和编号均合法时返回 true。</returns>
        public static bool TryParseNumber(string id, string expectedPrefix, out int number)
        {
            number = 0;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(expectedPrefix)) return false;
            string prefix = expectedPrefix + "_";
            if (!id.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string suffix = id.Substring(prefix.Length);
            if (suffix.Length != 4)
            {
                return false;
            }

            // 只接受四个 ASCII 数字，避免 Unicode 数字或带符号文本被误认为稳定 ID。
            for (int index = 0; index < suffix.Length; index++)
            {
                if (suffix[index] < '0' || suffix[index] > '9')
                {
                    number = 0;
                    return false;
                }
            }

            if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out number))
            {
                number = 0;
                return false;
            }

            return number >= FirstNumber && number <= LastNumber && string.Equals(FormatId(expectedPrefix, number), id, StringComparison.Ordinal);
        }

        #endregion

        #region 引用显示

        /// <summary>格式化有效引用的下拉显示文本，保证 ID 位于 Name 前面。</summary>
        /// <param name="id">稳定 ID。</param>
        /// <param name="displayName">用于编辑器显示的名称。</param>
        /// <returns>形如 ID（Name）的文本；空 ID 返回“无”。</returns>
        public static string FormatReferenceLabel(string id, string displayName)
        {
            if (string.IsNullOrWhiteSpace(id)) return "无";
            string name = string.IsNullOrWhiteSpace(displayName) ? "未命名" : displayName.Trim();
            return $"{id}（{name}）";
        }

        /// <summary>格式化失效引用的显示文本并保留原始 ID。</summary>
        /// <param name="id">失效的稳定 ID。</param>
        /// <returns>形如 ID（无效引用）的文本。</returns>
        public static string FormatInvalidReferenceLabel(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? "无" : $"{id}（无效引用）";
        }

        #endregion

        #region 内部辅助

        /// <summary>验证类别前缀只能由小写 ASCII 字母组成。</summary>
        /// <param name="prefix">待验证前缀。</param>
        private static void ValidatePrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("ID 前缀不能为空。", nameof(prefix));
            for (int index = 0; index < prefix.Length; index++)
                if (prefix[index] < 'a' || prefix[index] > 'z')
                    throw new ArgumentException($"ID 前缀“{prefix}”必须只包含小写 ASCII 字母。", nameof(prefix));
        }

        /// <summary>按固定四位格式组合 ID。</summary>
        /// <param name="prefix">类别前缀。</param>
        /// <param name="number">编号。</param>
        /// <returns>格式化后的稳定 ID。</returns>
        private static string FormatId(string prefix, int number) => $"{prefix}_{number:D4}";

        #endregion
    }
}
#endif
