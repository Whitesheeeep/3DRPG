using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using WS_Modules.LogModule;

namespace WS_Modules
{
    /// <summary>
    /// Draws Addressables address selectors for string fields and string arrays/lists.
    /// </summary>
    [InitializeOnLoad]
    [CustomPropertyDrawer(typeof(WSAddressableKeyAttribute))]
    internal sealed class WSAddressableKeyDrawer : PropertyDrawer
    {
        #region 常量

        private const float VerticalSpacing = 2f;
        private const float ArrayElementIndent = 16f;
        private const string NoneLabel = "None";
        private const string UnsupportedTypeMessage = "[WSAddressableKey] only supports string, string[], or List<string> fields.";
        private const string MissingSettingsMessage = "Addressables Settings have not been created.";
        private const string EmptyOptionsMessage = "No Addressables entries match the current group and label filters.";
        private const string PingButtonLabel = "Ping";
        private const string PingButtonTooltip = "在 Project 窗口中定位当前 Addressable 资源。";
        private const float PingButtonWidth = 38f;
        private const float ObjectFieldRatio = 0.32f;
        private const float FieldSpacing = 2f;

        #endregion

        #region 依赖字段

        // Addressables 筛选缓存：Key 包含 Settings 实例和原始筛选表达式，Value 为不可变的筛选结果快照。
        private static readonly Dictionary<
            AddressableKeyFilterCacheKey,
            IReadOnlyList<AddressableKeyOption>> optionsByFilterKeyMap =
            new Dictionary<AddressableKeyFilterCacheKey, IReadOnlyList<AddressableKeyOption>>();

        // 用于检测当前 Addressables Settings 是否已经切换，避免旧 Settings 的结果继续被复用。
        private static int cachedSettingsInstanceId = int.MinValue;

        #endregion

        #region 过滤缓存生命周期

        /// <summary>
        /// 注册用于失效内存 Addressables 筛选缓存的编辑器回调。
        /// </summary>
        static WSAddressableKeyDrawer()
        {
            AddressableAssetSettings.OnModificationGlobal -= OnAddressableSettingsModified;
            AddressableAssetSettings.OnModificationGlobal += OnAddressableSettingsModified;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        /// <summary>
        /// 在任意 Addressables Settings 修改后清空筛选结果缓存。
        /// </summary>
        /// <param name="settings">发生修改的 Addressables Settings 实例。</param>
        /// <param name="modificationEvent">Addressables 修改事件类别。</param>
        /// <param name="eventData">与修改事件关联的数据对象。</param>
        private static void OnAddressableSettingsModified(
            AddressableAssetSettings settings,
            AddressableAssetSettings.ModificationEvent modificationEvent,
            object eventData)
        {
            // 修改事件可能在一次 Inspector 重绘期间触发；这里只失效缓存，不在回调中扫描资源。
            InvalidateAddressableOptionsCache();
        }

        /// <summary>
        /// 在 Unity 报告项目资产变化后清空筛选结果缓存。
        /// </summary>
        private static void OnProjectChanged()
        {
            // Project 变更只负责让下一次访问重新构建，避免在高频编辑器事件中执行全量遍历。
            InvalidateAddressableOptionsCache();
        }

        /// <summary>
        /// 清除所有 Addressables 筛选结果，但不访问或修改 Addressable 资源。
        /// </summary>
        private static void InvalidateAddressableOptionsCache()
        {
            optionsByFilterKeyMap.Clear();
        }

        #endregion

        #region 属性绘制生命周期

        /// <summary>
        /// Draws an Addressables selector for a supported string field or collection.
        /// </summary>
        /// <param name="position">The rectangle allocated by Unity for the property.</param>
        /// <param name="property">The serialized property being drawn.</param>
        /// <param name="label">The label displayed beside the property.</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            WSAddressableKeyAttribute keyAttribute = (WSAddressableKeyAttribute)attribute;

            if (property.propertyType == SerializedPropertyType.String)
            {
                DrawAddressableKeyField(position, property, label, keyAttribute);
                return;
            }

            if (IsStringCollection(property))
            {
                DrawAddressableKeyArray(position, property, label, keyAttribute);
                return;
            }

            DrawUnsupportedProperty(position, property, label);
        }

        /// <summary>
        /// Calculates the height required by the selector and any warning message.
        /// </summary>
        /// <param name="property">The serialized property being measured.</param>
        /// <param name="label">The label associated with the property.</param>
        /// <returns>The height in GUI points required to draw the property.</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType == SerializedPropertyType.String)
            {
                return GetAddressableKeyFieldHeight((WSAddressableKeyAttribute)attribute);
            }

            if (IsStringCollection(property))
            {
                return GetAddressableKeyArrayHeight(property, (WSAddressableKeyAttribute)attribute);
            }

            return EditorGUI.GetPropertyHeight(property, label, true) +
                   VerticalSpacing +
                   EditorGUIUtility.singleLineHeight * 2f;
        }

        #endregion

        #region 类型与集合绘制

        /// <summary>
        /// Determines whether a serialized property is a string array or a List&lt;string&gt;.
        /// </summary>
        /// <param name="property">The serialized property to inspect.</param>
        /// <returns><see langword="true"/> when the property is a supported string collection.</returns>
        private bool IsStringCollection(SerializedProperty property)
        {
            if (!property.isArray || property.propertyType != SerializedPropertyType.Generic)
            {
                return false;
            }

            Type fieldType = fieldInfo?.FieldType;
            if (fieldType == typeof(string[]))
            {
                return true;
            }

            return fieldType != null &&
                   fieldType.IsGenericType &&
                   fieldType.GetGenericTypeDefinition() == typeof(List<>) &&
                   fieldType.GetGenericArguments()[0] == typeof(string);
        }

        /// <summary>
        /// Draws the foldout, size field, and Addressables selector for each string element.
        /// </summary>
        /// <param name="position">The rectangle allocated by Unity for the collection.</param>
        /// <param name="property">The serialized array or list property.</param>
        /// <param name="label">The collection label.</param>
        /// <param name="keyAttribute">The filter configuration attached to the field.</param>
        private static void DrawAddressableKeyArray(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            WSAddressableKeyAttribute keyAttribute)
        {
            float currentY = position.y;
            Rect foldoutRect = new Rect(position.x, currentY, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
            currentY += EditorGUIUtility.singleLineHeight + VerticalSpacing;

            if (!property.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            SerializedProperty sizeProperty = property.FindPropertyRelative("Array.size");
            Rect sizeRect = new Rect(position.x, currentY, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(sizeRect, sizeProperty);
            currentY += EditorGUIUtility.singleLineHeight + VerticalSpacing;

            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty elementProperty = property.GetArrayElementAtIndex(i);
                Rect elementRect = new Rect(
                    position.x + ArrayElementIndent,
                    currentY,
                    position.width - ArrayElementIndent,
                    GetAddressableKeyFieldHeight(keyAttribute));

                DrawAddressableKeyField(elementRect, elementProperty, new GUIContent($"Element {i}"), keyAttribute);
                currentY += elementRect.height + VerticalSpacing;
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Calculates the height of a collapsed or expanded string collection.
        /// </summary>
        /// <param name="property">The serialized array or list property.</param>
        /// <param name="keyAttribute">The filter configuration attached to the field.</param>
        /// <returns>The height in GUI points required to draw the collection.</returns>
        private static float GetAddressableKeyArrayHeight(
            SerializedProperty property,
            WSAddressableKeyAttribute keyAttribute)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            height += VerticalSpacing;
            height += EditorGUIUtility.singleLineHeight + VerticalSpacing;

            float fieldHeight = GetAddressableKeyFieldHeight(keyAttribute);
            height += property.arraySize * (fieldHeight + VerticalSpacing);
            return height;
        }

        #endregion

        #region 单值绘制

        /// <summary>
        /// Draws one Addressables address selector, including settings and empty-result warnings.
        /// </summary>
        /// <param name="position">The rectangle allocated by Unity for the field.</param>
        /// <param name="property">The serialized string property.</param>
        /// <param name="label">The field label.</param>
        /// <param name="keyAttribute">The filter configuration attached to the field.</param>
        private static void DrawAddressableKeyField(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            WSAddressableKeyAttribute keyAttribute)
        {
            if (!AddressableAssetSettingsDefaultObject.SettingsExists)
            {
                DrawDisabledPopupWithHelp(position, label, property.stringValue, MissingSettingsMessage);
                return;
            }

            IReadOnlyList<AddressableKeyOption> options = GetAddressableKeyOptions(keyAttribute);
            if (options.Count == 0)
            {
                DrawDisabledPopupWithHelp(position, label, property.stringValue, EmptyOptionsMessage);
                return;
            }

            DrawAddressableKeyRow(position, property, label, options);
        }

        /// <summary>
        /// Calculates the height of one Addressables selector and its warning message when needed.
        /// </summary>
        /// <param name="keyAttribute">The filter configuration attached to the field.</param>
        /// <returns>The height in GUI points required to draw the field.</returns>
        private static float GetAddressableKeyFieldHeight(WSAddressableKeyAttribute keyAttribute)
        {
            if (!AddressableAssetSettingsDefaultObject.SettingsExists ||
                GetAddressableKeyOptions(keyAttribute).Count == 0)
            {
                return EditorGUIUtility.singleLineHeight +
                       VerticalSpacing +
                       EditorGUIUtility.singleLineHeight * 2f;
            }

            return EditorGUIUtility.singleLineHeight;
        }

        /// <summary>
        /// Draws the popup, the Addressable object preview, and the Ping action in one row.
        /// </summary>
        /// <param name="position">The rectangle allocated by Unity for the complete selector row.</param>
        /// <param name="property">The serialized string property.</param>
        /// <param name="label">The field label.</param>
        /// <param name="options">The filtered and sorted Addressables options.</param>
        private static void DrawAddressableKeyRow(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            IReadOnlyList<AddressableKeyOption> options)
        {
            List<string> values = new List<string>(options.Count + 2) { string.Empty };
            List<GUIContent> labels = new List<GUIContent>(options.Count + 2) { new GUIContent(NoneLabel) };

            for (int i = 0; i < options.Count; i++)
            {
                AddressableKeyOption option = options[i];
                values.Add(option.Address);
                labels.Add(new GUIContent($"{option.Address} ({option.GroupName})", option.Tooltip));
            }

            string currentValue = property.stringValue ?? string.Empty;
            int currentIndex = values.IndexOf(currentValue);
            if (!string.IsNullOrWhiteSpace(currentValue) && currentIndex < 0)
            {
                values.Insert(1, currentValue);
                labels.Insert(1, new GUIContent($"Missing: {currentValue}"));
                currentIndex = 1;
            }
            else if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            TryGetOptionForAddress(options, currentValue, out AddressableKeyOption selectedOption);
            UnityEngine.Object selectedAsset = selectedOption.Entry?.TargetAsset;

            EditorGUI.BeginProperty(position, label, property);
            Rect contentRect = EditorGUI.PrefixLabel(position, label);
            float pingButtonWidth = Mathf.Min(PingButtonWidth, contentRect.width);
            float objectFieldWidth = Mathf.Max(
                0f,
                (contentRect.width - pingButtonWidth - FieldSpacing * 2f) * ObjectFieldRatio);
            float popupWidth = Mathf.Max(
                0f,
                contentRect.width - objectFieldWidth - pingButtonWidth - FieldSpacing * 2f);
            Rect popupRect = new Rect(contentRect.x, contentRect.y, popupWidth, contentRect.height);
            Rect objectFieldRect = new Rect(
                popupRect.xMax + FieldSpacing,
                contentRect.y,
                objectFieldWidth,
                contentRect.height);
            Rect pingButtonRect = new Rect(
                objectFieldRect.xMax + FieldSpacing,
                contentRect.y,
                pingButtonWidth,
                contentRect.height);

            EditorGUI.BeginChangeCheck();
            int selectedIndex = EditorGUI.Popup(
                popupRect,
                GUIContent.none,
                currentIndex,
                labels.ToArray());

            if (EditorGUI.EndChangeCheck() && selectedIndex >= 0 && selectedIndex < values.Count)
            {
                property.stringValue = values[selectedIndex];
                currentValue = property.stringValue;
                TryGetOptionForAddress(options, property.stringValue, out selectedOption);
                selectedAsset = selectedOption.Entry?.TargetAsset;
            }

            EditorGUI.BeginChangeCheck();
            UnityEngine.Object droppedAsset = EditorGUI.ObjectField(
                objectFieldRect,
                selectedAsset,
                typeof(UnityEngine.Object),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                HandleObjectFieldChanged(property, options, droppedAsset);
            }

            using (new EditorGUI.DisabledScope(selectedAsset == null))
            {
                if (GUI.Button(
                        pingButtonRect,
                        new GUIContent(PingButtonLabel, PingButtonTooltip),
                        EditorStyles.miniButton))
                {
                    PingAddressableAsset(selectedAsset, currentValue);
                }
            }

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// Draws a disabled popup and explains why no selectable Addressables options are available.
        /// </summary>
        /// <param name="position">The rectangle allocated by Unity for the field.</param>
        /// <param name="label">The field label.</param>
        /// <param name="currentValue">The currently serialized address.</param>
        /// <param name="message">The warning text shown below the disabled popup.</param>
        private static void DrawDisabledPopupWithHelp(
            Rect position,
            GUIContent label,
            string currentValue,
            string message)
        {
            Rect popupRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            string popupLabel = string.IsNullOrWhiteSpace(currentValue) ? NoneLabel : $"Current: {currentValue}";
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.Popup(popupRect, label, 0, new[] { new GUIContent(popupLabel) });
            }

            Rect helpRect = new Rect(
                position.x,
                popupRect.yMax + VerticalSpacing,
                position.width,
                EditorGUIUtility.singleLineHeight * 2f);
            EditorGUI.HelpBox(helpRect, message, MessageType.Warning);
        }

        /// <summary>
        /// Draws unsupported properties using Unity's default field and an explanatory error.
        /// </summary>
        /// <param name="position">The rectangle allocated by Unity for the property.</param>
        /// <param name="property">The unsupported serialized property.</param>
        /// <param name="label">The field label.</param>
        private static void DrawUnsupportedProperty(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect fieldRect = new Rect(
                position.x,
                position.y,
                position.width,
                EditorGUI.GetPropertyHeight(property, label, true));
            EditorGUI.PropertyField(fieldRect, property, label, true);

            Rect helpRect = new Rect(
                position.x,
                fieldRect.yMax + VerticalSpacing,
                position.width,
                EditorGUIUtility.singleLineHeight * 2f);
            EditorGUI.HelpBox(helpRect, UnsupportedTypeMessage, MessageType.Error);
        }

        #endregion

        #region 对象预览与拖拽绑定

        /// <summary>
        /// Finds the popup option that owns the serialized Address value.
        /// </summary>
        /// <param name="options">The filtered Addressables options.</param>
        /// <param name="address">The serialized Address value.</param>
        /// <param name="option">The matching option when one exists.</param>
        /// <returns><see langword="true"/> when the Address has a matching option.</returns>
        private static bool TryGetOptionForAddress(
            IReadOnlyList<AddressableKeyOption> options,
            string address,
            out AddressableKeyOption option)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].Address, address, StringComparison.Ordinal))
                {
                    option = options[i];
                    return true;
                }
            }

            option = default;
            return false;
        }

        /// <summary>
        /// Resolves a changed ObjectField value to one unique filtered Addressables entry.
        /// </summary>
        /// <param name="property">The serialized string property that stores the Address.</param>
        /// <param name="options">The filtered Addressables options available to the field.</param>
        /// <param name="selectedAsset">The object selected or dropped by the user.</param>
        private static void HandleObjectFieldChanged(
            SerializedProperty property,
            IReadOnlyList<AddressableKeyOption> options,
            UnityEngine.Object selectedAsset)
        {
            if (selectedAsset == null)
            {
                // ObjectField 的清空操作不能伪造一个空 Address，保留原字符串作为唯一数据源。
                WSLog.LogWarning("[WSAddressableKeyDrawer] 拒绝清空 ObjectField：请通过 Popup 选择 None 来清空 Address。");
                return;
            }

            int matchCount = 0;
            AddressableKeyOption matchedOption = default;
            for (int i = 0; i < options.Count; i++)
            {
                AddressableKeyOption option = options[i];
                if (option.Entry != null && option.Entry.TargetAsset == selectedAsset)
                {
                    matchCount++;
                    matchedOption = option;
                }
            }

            if (matchCount == 0)
            {
                WSLog.LogWarning(
                    $"[WSAddressableKeyDrawer] 拒绝对象 {selectedAsset.name}：它不是当前 Group/Label 筛选范围内的 Addressable 资源。");
                return;
            }

            if (matchCount > 1)
            {
                WSLog.LogWarning(
                    $"[WSAddressableKeyDrawer] 拒绝对象 {selectedAsset.name}：匹配到 {matchCount} 个 Addressable Entry，无法唯一确定 Address。");
                return;
            }

            // 只写回 Address 字符串；ObjectField 对象本身不进入序列化数据，避免产生双重状态。
            property.stringValue = matchedOption.Address;
            WSLog.Log(
                $"[WSAddressableKeyDrawer] 已通过 ObjectField 写入 Address：{matchedOption.Address}，资源={selectedAsset.name}。");
        }

        /// <summary>
        /// Selects and pings the Addressable object in the Project window.
        /// </summary>
        /// <param name="asset">The Addressable object to locate.</param>
        /// <param name="address">The Address shown in the serialized field.</param>
        private static void PingAddressableAsset(UnityEngine.Object asset, string address)
        {
            if (asset == null)
            {
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            WSLog.Log($"[WSAddressableKeyDrawer] 定位 Addressable 资源：{address}，资源={asset.name}。");
        }

        #endregion

        #region Addressables 筛选

        /// <summary>
        /// 获取满足 Group 和 Label 表达式的 Addressables 选项，并复用当前编辑器缓存。
        /// </summary>
        /// <param name="keyAttribute">字段上的筛选配置。</param>
        /// <returns>满足筛选条件的排序后只读 Addressables 选项。</returns>
        private static IReadOnlyList<AddressableKeyOption> GetAddressableKeyOptions(WSAddressableKeyAttribute keyAttribute)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                if (cachedSettingsInstanceId != 0)
                {
                    // Settings 被删除后不能保留旧结果，否则重新创建 Settings 前会显示过期 Address。
                    InvalidateAddressableOptionsCache();
                    cachedSettingsInstanceId = 0;
                }

                return Array.Empty<AddressableKeyOption>();
            }

            int settingsInstanceId = settings.GetInstanceID();
            if (cachedSettingsInstanceId != settingsInstanceId)
            {
                // Settings 资产切换时清除旧实例的快照，防止相同表达式命中错误项目数据。
                InvalidateAddressableOptionsCache();
                cachedSettingsInstanceId = settingsInstanceId;
            }

            AddressableKeyFilterCacheKey cacheKey = CreateFilterCacheKey(settingsInstanceId, keyAttribute);
            if (optionsByFilterKeyMap.TryGetValue(cacheKey, out IReadOnlyList<AddressableKeyOption> cachedOptions))
            {
                return cachedOptions;
            }

            IReadOnlyList<AddressableKeyOption> options = BuildAddressableKeyOptions(settings, keyAttribute);
            optionsByFilterKeyMap.Add(cacheKey, options);
            return options;
        }

        /// <summary>
        /// 在缓存未命中时构建一份排序后的 Addressables 筛选结果快照。
        /// </summary>
        /// <param name="settings">当前生效的 Addressables Settings 实例。</param>
        /// <param name="keyAttribute">字段上的筛选配置。</param>
        /// <returns>满足筛选条件的排序后只读结果。</returns>
        private static IReadOnlyList<AddressableKeyOption> BuildAddressableKeyOptions(
            AddressableAssetSettings settings,
            WSAddressableKeyAttribute keyAttribute)
        {
            List<AddressableKeyOption> options = new List<AddressableKeyOption>();

            // 预先解析表达式，避免为每个 Addressables 条目重复拆分字符串。
            List<string> groupFilters = ParseGroupFilters(keyAttribute.GroupName);
            List<LabelFilterCondition> labelFilters = ParseLabelFilters(keyAttribute.Labels);

            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null || !MatchesGroup(group, groupFilters))
                {
                    continue;
                }

                foreach (AddressableAssetEntry entry in group.entries)
                {
                    if (entry == null ||
                        string.IsNullOrWhiteSpace(entry.address) ||
                        !MatchesLabels(entry, labelFilters))
                    {
                        continue;
                    }

                    options.Add(new AddressableKeyOption(entry.address, group.Name, CreateTooltip(entry), entry));
                }
            }

            return options
                .OrderBy(option => option.GroupName)
                .ThenBy(option => option.Address)
                .ToList();
        }

        /// <summary>
        /// 根据当前 Settings 实例和原始筛选表达式创建缓存键。
        /// </summary>
        /// <param name="settingsInstanceId">当前 Addressables Settings 的实例 ID。</param>
        /// <param name="keyAttribute">Drawer 使用的筛选配置。</param>
        /// <returns>能够区分所有筛选组合且不会因分隔符产生碰撞的值键。</returns>
        private static AddressableKeyFilterCacheKey CreateFilterCacheKey(
            int settingsInstanceId,
            WSAddressableKeyAttribute keyAttribute)
        {
            return new AddressableKeyFilterCacheKey(
                settingsInstanceId,
                keyAttribute.GroupName,
                CreateLabelExpressionSignature(keyAttribute.Labels));
        }

        /// <summary>
        /// 为全部原始 Label 参数创建带长度前缀的签名。
        /// </summary>
        /// <param name="labelExpressions">Attribute 中的原始 Label 表达式。</param>
        /// <returns>保留参数数量与顺序且不会发生分隔符碰撞的签名。</returns>
        private static string CreateLabelExpressionSignature(IReadOnlyList<string> labelExpressions)
        {
            StringBuilder signatureBuilder = new StringBuilder();
            int expressionCount = labelExpressions?.Count ?? 0;
            signatureBuilder.Append(expressionCount).Append(':');

            for (int i = 0; i < expressionCount; i++)
            {
                string expression = labelExpressions[i] ?? string.Empty;
                signatureBuilder.Append(expression.Length).Append(':').Append(expression);
            }

            return signatureBuilder.ToString();
        }

        /// <summary>
        /// Parses a group expression into trimmed OR alternatives.
        /// </summary>
        /// <param name="groupExpression">The raw group expression.</param>
        /// <returns>Valid group names, or an empty list when Group is unrestricted.</returns>
        private static List<string> ParseGroupFilters(string groupExpression)
        {
            return ParseExpressionParts(groupExpression, '|');
        }

        /// <summary>
        /// Parses label arguments into OR alternatives, each containing its required AND labels.
        /// </summary>
        /// <param name="labelExpressions">The raw label expression arguments.</param>
        /// <returns>Valid label conditions; an empty list means Label is unrestricted.</returns>
        private static List<LabelFilterCondition> ParseLabelFilters(IReadOnlyList<string> labelExpressions)
        {
            List<LabelFilterCondition> conditions = new List<LabelFilterCondition>();
            if (labelExpressions == null)
            {
                return conditions;
            }

            // 每个参数中的 | 产生 OR 分支，分支中的 & 产生必须同时存在的标签。
            for (int i = 0; i < labelExpressions.Count; i++)
            {
                List<string> alternatives = ParseExpressionParts(labelExpressions[i], '|');
                for (int j = 0; j < alternatives.Count; j++)
                {
                    List<string> requiredLabels = ParseExpressionParts(alternatives[j], '&');
                    if (requiredLabels.Count > 0)
                    {
                        conditions.Add(new LabelFilterCondition(requiredLabels));
                    }
                }
            }

            return conditions;
        }

        /// <summary>
        /// Splits one expression by an operator and removes whitespace-only parts.
        /// </summary>
        /// <param name="expression">The raw expression to split.</param>
        /// <param name="separator">The expression operator.</param>
        /// <returns>Trimmed, non-empty expression parts.</returns>
        private static List<string> ParseExpressionParts(string expression, char separator)
        {
            List<string> parts = new List<string>();
            if (string.IsNullOrWhiteSpace(expression))
            {
                return parts;
            }

            string[] values = expression.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(value);
                }
            }

            return parts;
        }

        /// <summary>
        /// Determines whether a group satisfies at least one configured group alternative.
        /// </summary>
        /// <param name="group">The Addressables group to test.</param>
        /// <param name="groupFilters">The parsed group alternatives.</param>
        /// <returns><see langword="true"/> when Group is unrestricted or matches an alternative.</returns>
        private static bool MatchesGroup(AddressableAssetGroup group, IReadOnlyList<string> groupFilters)
        {
            if (groupFilters == null || groupFilters.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < groupFilters.Count; i++)
            {
                if (group.Name == groupFilters[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether an entry satisfies at least one complete label condition.
        /// </summary>
        /// <param name="entry">The Addressables entry to test.</param>
        /// <param name="labelFilters">The parsed OR conditions whose labels are ANDed within each condition.</param>
        /// <returns><see langword="true"/> when Label is unrestricted or one condition is fully satisfied.</returns>
        private static bool MatchesLabels(AddressableAssetEntry entry, IReadOnlyList<LabelFilterCondition> labelFilters)
        {
            if (labelFilters == null || labelFilters.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < labelFilters.Count; i++)
            {
                LabelFilterCondition condition = labelFilters[i];
                bool matchesCondition = true;
                for (int j = 0; j < condition.RequiredLabels.Count; j++)
                {
                    if (entry.labels == null || !entry.labels.Contains(condition.RequiredLabels[j]))
                    {
                        matchesCondition = false;
                        break;
                    }
                }

                if (matchesCondition)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates the tooltip shown for one Addressables entry.
        /// </summary>
        /// <param name="entry">The Addressables entry represented by the option.</param>
        /// <returns>A tooltip containing path, GUID, and labels.</returns>
        private static string CreateTooltip(AddressableAssetEntry entry)
        {
            string labels = entry.labels == null || entry.labels.Count == 0
                ? "None"
                : string.Join(", ", entry.labels);

            return $"Path: {entry.AssetPath}\nGUID: {entry.guid}\nLabels: {labels}";
        }

        #endregion

        #region 筛选缓存键

        /// <summary>
        /// 标识某个 Settings 实例上的一份 Addressables 筛选缓存结果。
        /// </summary>
        private readonly struct AddressableKeyFilterCacheKey : IEquatable<AddressableKeyFilterCacheKey>
        {
            /// <summary>
            /// 根据 Settings 实例和原始筛选表达式初始化缓存键。
            /// </summary>
            /// <param name="settingsInstanceId">当前 Addressables Settings 的实例 ID。</param>
            /// <param name="groupExpression">原始 Group 表达式。</param>
            /// <param name="labelExpressionSignature">带长度前缀的 Label 签名。</param>
            public AddressableKeyFilterCacheKey(
                int settingsInstanceId,
                string groupExpression,
                string labelExpressionSignature)
            {
                SettingsInstanceId = settingsInstanceId;
                GroupExpression = groupExpression ?? string.Empty;
                LabelExpressionSignature = labelExpressionSignature ?? string.Empty;
            }

            /// <summary>
            /// 获取该键对应的 Addressables Settings 实例 ID。
            /// </summary>
            public int SettingsInstanceId { get; }

            /// <summary>
            /// 获取该键对应的原始 Group 表达式。
            /// </summary>
            public string GroupExpression { get; }

            /// <summary>
            /// 获取原始 Label 表达式的无碰撞签名。
            /// </summary>
            public string LabelExpressionSignature { get; }

            /// <summary>
            /// 使用序数表达式比较两个缓存键。
            /// </summary>
            /// <param name="other">要比较的缓存键。</param>
            /// <returns>当两个键标识同一份筛选快照时返回 <see langword="true"/>。</returns>
            public bool Equals(AddressableKeyFilterCacheKey other)
            {
                return SettingsInstanceId == other.SettingsInstanceId &&
                       string.Equals(GroupExpression, other.GroupExpression, StringComparison.Ordinal) &&
                       string.Equals(
                           LabelExpressionSignature,
                           other.LabelExpressionSignature,
                           StringComparison.Ordinal);
            }

            /// <summary>
            /// 将当前缓存键与其他对象进行比较。
            /// </summary>
            /// <param name="obj">要比较的对象。</param>
            /// <returns>当 <paramref name="obj"/> 是相同缓存键时返回 <see langword="true"/>。</returns>
            public override bool Equals(object obj)
            {
                return obj is AddressableKeyFilterCacheKey other && Equals(other);
            }

            /// <summary>
            /// 计算筛选缓存字典使用的哈希值。
            /// </summary>
            /// <returns>由 Settings 和筛选表达式共同计算出的哈希值。</returns>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = SettingsInstanceId;
                    hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(GroupExpression);
                    hashCode = (hashCode * 397) ^
                               StringComparer.Ordinal.GetHashCode(LabelExpressionSignature);
                    return hashCode;
                }
            }
        }

        #endregion

        #region 筛选结果数据

        /// <summary>
        /// Stores the labels that must all exist for one Label OR branch.
        /// </summary>
        private readonly struct LabelFilterCondition
        {
            /// <summary>
            /// Initializes a label condition with its required labels.
            /// </summary>
            /// <param name="requiredLabels">The labels that must all be present.</param>
            public LabelFilterCondition(IReadOnlyList<string> requiredLabels)
            {
                RequiredLabels = requiredLabels;
            }

            /// <summary>
            /// Gets the labels required by this condition.
            /// </summary>
            public IReadOnlyList<string> RequiredLabels { get; }
        }

        /// <summary>
        /// Stores the display data for one selectable Addressables entry.
        /// </summary>
        private readonly struct AddressableKeyOption
        {
            /// <summary>
            /// Initializes an Addressables popup option.
            /// </summary>
            /// <param name="address">The entry address.</param>
            /// <param name="groupName">The owning group name.</param>
            /// <param name="tooltip">The tooltip shown for the option.</param>
            /// <param name="entry">The Addressables entry used to resolve the preview object.</param>
            public AddressableKeyOption(
                string address,
                string groupName,
                string tooltip,
                AddressableAssetEntry entry)
            {
                Address = address;
                GroupName = groupName;
                Tooltip = tooltip;
                Entry = entry;
            }

            /// <summary>
            /// Gets the entry address written to the serialized field.
            /// </summary>
            public string Address { get; }

            /// <summary>
            /// Gets the owning Addressables group name.
            /// </summary>
            public string GroupName { get; }

            /// <summary>
            /// Gets the tooltip text for the option.
            /// </summary>
            public string Tooltip { get; }

            /// <summary>
            /// Gets the Addressables entry used to resolve the selected preview object.
            /// </summary>
            public AddressableAssetEntry Entry { get; }
        }

        #endregion
    }
}
