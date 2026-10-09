#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WS_Modules.SceneModule.Editor
{
    /// <summary>从场景数据库选择稳定 SceneId，并在数据库变动时刷新 Inspector 候选项。</summary>
    [CustomPropertyDrawer(typeof(SceneIdDropdownAttribute))]
    internal sealed class SceneIdDropdownPropertyDrawer : PropertyDrawer
    {
        #region 常量与缓存

        private const string SelectedDatabaseSessionKey = "WSFrame.SceneSystem.SceneIdDropdown.SelectedDatabaseGuid";
        private static SceneLoadDatabase[] sceneLoadDatabases;
        private static readonly HashSet<string> invalidPropertyPathSet = new(StringComparer.Ordinal);

        #endregion

        #region UI Toolkit 绘制

        /// <summary>创建数据库选择框、SceneId 下拉框和配置错误提示。</summary>
        /// <param name="property">标记为 SceneId 引用的序列化字符串。</param>
        /// <returns>场景引用控件，或字段类型不匹配时的错误提示。</returns>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (!IsStringProperty(property))
            {
                LogInvalidProperty(property);
                return new Label($"{property.displayName}：SceneIdDropdown 仅支持 string 字段");
            }

            UnityEngine.Object[] targets = property.serializedObject.targetObjects;
            string propertyPath = property.propertyPath;
            var root = new VisualElement();
            var databaseField = new ObjectField("场景数据库")
            {
                objectType = typeof(SceneLoadDatabase),
                allowSceneObjects = false
            };
            var sceneIdField = new DropdownField(property.displayName, new List<string> { "读取中" }, 0);
            var warningBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            root.Add(databaseField);
            root.Add(sceneIdField);
            root.Add(warningBox);

            Action refresh = () => RefreshToolkitFields(
                databaseField,
                sceneIdField,
                warningBox,
                targets,
                propertyPath);

            databaseField.RegisterValueChangedCallback(change =>
            {
                if (change.newValue != null && change.newValue is not SceneLoadDatabase)
                {
                    refresh();
                    return;
                }

                SetSelectedDatabase((SceneLoadDatabase)change.newValue);
                refresh();
            });
            sceneIdField.RegisterValueChangedCallback(change =>
            {
                SceneLoadDatabase database = GetSelectedDatabase();
                SceneIdDropdownOptionSet optionSet = BuildOptions(
                    database,
                    GetCurrentValue(targets, propertyPath),
                    HasMultipleValues(targets, propertyPath));
                SceneIdDropdownOption selectedOption = optionSet.FindByLabel(change.newValue);
                if (selectedOption != null && selectedOption.CanSelect)
                    ApplyValue(targets, propertyPath, selectedOption.SceneId);

                refresh();
            });

            IVisualElementScheduledItem refreshSchedule = null;
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                EditorApplication.projectChanged += refresh;
                Undo.undoRedoPerformed += refresh.Invoke;
                refreshSchedule = root.schedule.Execute(refresh).Every(750);
                refresh();
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                EditorApplication.projectChanged -= refresh;
                Undo.undoRedoPerformed -= refresh.Invoke;
                refreshSchedule?.Pause();
            });

            refresh();
            return root;
        }

        #endregion

        #region IMGUI 绘制

        /// <summary>绘制传统 Inspector 和 Odin Inspector 使用的两行场景引用字段。</summary>
        /// <param name="position">字段绘制区域。</param>
        /// <param name="property">标记为 SceneId 引用的序列化字符串。</param>
        /// <param name="label">Inspector 字段标签。</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!IsStringProperty(property))
            {
                LogInvalidProperty(property);
                EditorGUI.LabelField(position, label, new GUIContent("SceneIdDropdown 仅支持 string 字段"));
                return;
            }

            SceneLoadDatabase[] databases = GetSceneLoadDatabases();
            SceneLoadDatabase database = GetSelectedDatabase(databases);
            SceneIdDropdownOptionSet optionSet = BuildOptions(
                database,
                property.hasMultipleDifferentValues ? null : property.stringValue,
                property.hasMultipleDifferentValues);
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            Rect databaseRect = new(position.x, position.y, position.width, lineHeight);
            Rect sceneIdRect = new(position.x, position.y + lineHeight + spacing, position.width, lineHeight);

            EditorGUI.BeginProperty(position, label, property);
            try
            {
                DrawDatabaseField(databaseRect, databases, database);
                DrawSceneIdField(sceneIdRect, property, label, database, optionSet);

                if (!string.IsNullOrEmpty(optionSet.Warning))
                {
                    Rect warningRect = new(
                        position.x,
                        sceneIdRect.yMax + spacing,
                        position.width,
                        EditorGUIUtility.singleLineHeight * 1.8f);
                    EditorGUI.HelpBox(warningRect, optionSet.Warning, MessageType.Warning);
                }
            }
            finally
            {
                EditorGUI.EndProperty();
            }
        }

        /// <summary>为数据库选择行、SceneId 行和可选警告计算 Inspector 高度。</summary>
        /// <param name="property">待绘制的序列化字符串。</param>
        /// <param name="label">Inspector 字段标签。</param>
        /// <returns>完整绘制区域高度。</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            if (!IsStringProperty(property)) return lineHeight;

            SceneLoadDatabase database = GetSelectedDatabase();
            SceneIdDropdownOptionSet optionSet = BuildOptions(
                database,
                property.hasMultipleDifferentValues ? null : property.stringValue,
                property.hasMultipleDifferentValues);
            float height = lineHeight * 2f + spacing;
            if (!string.IsNullOrEmpty(optionSet.Warning))
                height += spacing + lineHeight * 1.8f;
            return height;
        }

        #endregion

        #region 数据库发现与会话选择

        /// <summary>在项目资产变动或撤销重做后使数据库资产发现缓存失效。</summary>
        private static void InvalidateDatabaseCache()
        {
            sceneLoadDatabases = null;
        }

        /// <summary>从 AssetDatabase 发现并按资产路径稳定排序场景数据库。</summary>
        /// <returns>项目内全部可加载场景数据库。</returns>
        private static SceneLoadDatabase[] GetSceneLoadDatabases()
        {
            if (sceneLoadDatabases != null) return sceneLoadDatabases;

            string[] databaseGuids = AssetDatabase.FindAssets("t:SceneLoadDatabase");
            var databases = new List<SceneLoadDatabase>(databaseGuids.Length);
            for (int index = 0; index < databaseGuids.Length; index++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(databaseGuids[index]);
                SceneLoadDatabase database = AssetDatabase.LoadAssetAtPath<SceneLoadDatabase>(assetPath);
                if (database != null) databases.Add(database);
            }

            databases.Sort((left, right) => string.Compare(
                AssetDatabase.GetAssetPath(left),
                AssetDatabase.GetAssetPath(right),
                StringComparison.Ordinal));
            sceneLoadDatabases = databases.ToArray();
            Debug.Log($"[SceneIdDropdownPropertyDrawer] 已发现场景数据库，count={sceneLoadDatabases.Length}。");
            return sceneLoadDatabases;
        }

        /// <summary>按自动唯一选择或当前编辑器会话选择获取候选数据库。</summary>
        /// <returns>唯一或明确选择的数据库；多个数据库尚未选择时返回 null。</returns>
        private static SceneLoadDatabase GetSelectedDatabase() => GetSelectedDatabase(GetSceneLoadDatabases());

        /// <summary>从候选数据库集合中解析自动唯一项或会话中保存的 GUID。</summary>
        /// <param name="databases">AssetDatabase 发现的候选项。</param>
        /// <returns>当前会话选中的数据库。</returns>
        private static SceneLoadDatabase GetSelectedDatabase(IReadOnlyList<SceneLoadDatabase> databases)
        {
            if (databases.Count == 1) return databases[0];
            if (databases.Count == 0) return null;

            string selectedGuid = SessionState.GetString(SelectedDatabaseSessionKey, string.Empty);
            if (string.IsNullOrEmpty(selectedGuid)) return null;

            for (int index = 0; index < databases.Count; index++)
            {
                SceneLoadDatabase database = databases[index];
                if (database != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(database)) == selectedGuid)
                    return database;
            }

            SessionState.EraseString(SelectedDatabaseSessionKey);
            return null;
        }

        /// <summary>仅在编辑器会话内记住用户明确选择的数据库资产。</summary>
        /// <param name="database">选中的候选数据库；传 null 清除会话选择。</param>
        private static void SetSelectedDatabase(SceneLoadDatabase database)
        {
            if (database == null)
            {
                SessionState.EraseString(SelectedDatabaseSessionKey);
                Debug.Log("[SceneIdDropdownPropertyDrawer] 已清除场景数据库会话选择。");
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(database);
            string databaseGuid = AssetDatabase.AssetPathToGUID(assetPath);
            IReadOnlyList<SceneLoadDatabase> databases = GetSceneLoadDatabases();
            bool isProjectCandidate = false;
            for (int index = 0; index < databases.Count; index++)
            {
                if (databases[index] == database)
                {
                    isProjectCandidate = true;
                    break;
                }
            }

            if (!isProjectCandidate || string.IsNullOrEmpty(databaseGuid))
            {
                Debug.LogWarning($"[SceneIdDropdownPropertyDrawer] 拒绝未登记的数据库选择，asset={database.name}。");
                return;
            }

            SessionState.SetString(SelectedDatabaseSessionKey, databaseGuid);
            Debug.Log($"[SceneIdDropdownPropertyDrawer] 已选择场景数据库，asset={database.name}，guid={databaseGuid}。");
        }

        #endregion

        #region 候选项生成与绘制

        /// <summary>从数据库列表建立可选择项，并标记缺失 ID 或重复 ID。</summary>
        /// <param name="database">当前会话明确选择的数据库。</param>
        /// <param name="currentSceneId">字段当前保存的 SceneId。</param>
        /// <returns>显示标签、可写入值、当前选中项及配置警告。</returns>
        private static SceneIdDropdownOptionSet BuildOptions(
            SceneLoadDatabase database,
            string currentSceneId,
            bool hasMultipleValues = false)
        {
            var optionSet = new SceneIdDropdownOptionSet();
            if (database == null)
            {
                optionSet.Add("请先选择场景数据库", null);
                optionSet.Warning = GetDatabaseSelectionWarning();
                return optionSet;
            }

            IReadOnlyList<SceneLoadConfig> sceneConfigs = database.SceneConfigs;
            var sceneConfigByIdMap = new Dictionary<string, SceneLoadConfig>(StringComparer.Ordinal);
            var duplicateSceneIdSet = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; sceneConfigs != null && index < sceneConfigs.Count; index++)
            {
                SceneLoadConfig config = sceneConfigs[index];
                if (config == null || string.IsNullOrWhiteSpace(config.SceneId)) continue;
                if (!sceneConfigByIdMap.TryAdd(config.SceneId, config))
                    duplicateSceneIdSet.Add(config.SceneId);
            }

            if (hasMultipleValues)
                optionSet.SelectedIndex = optionSet.Add("多个对象值不同", null);

            int noneOptionIndex = optionSet.Add("无", string.Empty);
            if (!hasMultipleValues && string.IsNullOrEmpty(currentSceneId))
                optionSet.SelectedIndex = 0;

            var validConfigs = new List<SceneLoadConfig>();
            foreach (KeyValuePair<string, SceneLoadConfig> sceneConfigByIdEntry in sceneConfigByIdMap)
            {
                if (!duplicateSceneIdSet.Contains(sceneConfigByIdEntry.Key))
                    validConfigs.Add(sceneConfigByIdEntry.Value);
            }

            validConfigs.Sort((left, right) => string.Compare(
                BuildOptionLabel(left),
                BuildOptionLabel(right),
                StringComparison.Ordinal));
            for (int index = 0; index < validConfigs.Count; index++)
            {
                SceneLoadConfig config = validConfigs[index];
                int optionIndex = optionSet.Add(BuildOptionLabel(config), config.SceneId);
                if (!hasMultipleValues && string.Equals(config.SceneId, currentSceneId, StringComparison.Ordinal))
                    optionSet.SelectedIndex = optionIndex;
            }

            if (!hasMultipleValues && !string.IsNullOrEmpty(currentSceneId))
            {
                if (duplicateSceneIdSet.Contains(currentSceneId))
                {
                    optionSet.SelectedIndex = optionSet.Add(
                        $"重复 ID，保留原值：{currentSceneId}",
                        null);
                }
                else if (optionSet.Find(currentSceneId) == null)
                {
                    optionSet.SelectedIndex = optionSet.Add($"未找到：{currentSceneId}", null);
                }
            }

            if (hasMultipleValues && optionSet.Options.Count > 0)
                optionSet.SelectedIndex = 0;

            // 空字符串始终对应“无”；变量保留该项下标，便于字段值为空时明确选中。
            if (!hasMultipleValues && string.IsNullOrEmpty(currentSceneId))
                optionSet.SelectedIndex = noneOptionIndex;

            optionSet.Warning = BuildDatabaseWarning(database, sceneConfigs, duplicateSceneIdSet);
            return optionSet;
        }

        /// <summary>生成包含友好名称与稳定 ID 的候选显示标签。</summary>
        /// <param name="config">场景数据库中的配置项。</param>
        /// <returns>用于 Inspector 下拉框的标签。</returns>
        private static string BuildOptionLabel(SceneLoadConfig config)
        {
            string displayName = string.IsNullOrWhiteSpace(config.DisplayName) ? config.name : config.DisplayName;
            return $"{displayName}（{config.SceneId}）";
        }

        /// <summary>汇总未配置场景和重复 SceneId，提醒使用者修复数据库。</summary>
        /// <param name="database">被查看的数据库。</param>
        /// <param name="sceneConfigs">数据库中的序列化配置列表。</param>
        /// <param name="duplicateSceneIdSet">出现多次且不可作为候选项的 ID。</param>
        /// <returns>可显示在 Inspector 下方的警告文本。</returns>
        private static string BuildDatabaseWarning(
            SceneLoadDatabase database,
            IReadOnlyList<SceneLoadConfig> sceneConfigs,
            HashSet<string> duplicateSceneIdSet)
        {
            var warnings = new List<string>();
            if (sceneConfigs == null || sceneConfigs.Count == 0)
                warnings.Add("数据库没有场景配置。");

            int shownDuplicateCount = 0;
            foreach (string duplicateSceneId in duplicateSceneIdSet)
            {
                if (shownDuplicateCount == 3) break;
                warnings.Add($"SceneId '{duplicateSceneId}' 重复，已从候选项中排除。");
                shownDuplicateCount++;
            }
            if (duplicateSceneIdSet.Count > shownDuplicateCount)
                warnings.Add($"另有 {duplicateSceneIdSet.Count - shownDuplicateCount} 个重复 ID。");

            if (warnings.Count == 0) return string.Empty;
            return $"{database.name}：{string.Join(" ", warnings)}";
        }

        /// <summary>描述数据库不存在、唯一自动选择或多个数据库未选择的状态。</summary>
        /// <returns>数据库选择提示。</returns>
        private static string GetDatabaseSelectionWarning()
        {
            SceneLoadDatabase[] databases = GetSceneLoadDatabases();
            if (databases.Length == 0) return "项目中没有 SceneLoadDatabase 资产，当前 SceneId 保持不变。";
            if (databases.Length > 1) return "项目存在多个 SceneLoadDatabase，请在上方明确选择一个。";
            return "场景数据库不可用，当前 SceneId 保持不变。";
        }

        /// <summary>绘制会话级数据库选择；唯一候选项自动选用并锁定。</summary>
        /// <param name="position">数据库选择行区域。</param>
        /// <param name="databases">项目内数据库候选项。</param>
        /// <param name="database">当前选中的数据库。</param>
        private static void DrawDatabaseField(
            Rect position,
            IReadOnlyList<SceneLoadDatabase> databases,
            SceneLoadDatabase database)
        {
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(databases.Count <= 1))
            {
                SceneLoadDatabase nextDatabase = (SceneLoadDatabase)EditorGUI.ObjectField(
                    position,
                    new GUIContent("场景数据库"),
                    database,
                    typeof(SceneLoadDatabase),
                    false);
                if (EditorGUI.EndChangeCheck() && nextDatabase != database)
                    SetSelectedDatabase(nextDatabase);
            }
        }

        /// <summary>绘制 SceneId 候选项并只在用户主动选择有效项时写入序列化字段。</summary>
        /// <param name="position">SceneId 下拉行区域。</param>
        /// <param name="property">需要修改的序列化字符串。</param>
        /// <param name="label">Inspector 字段标签。</param>
        /// <param name="database">当前选中的数据库。</param>
        /// <param name="optionSet">当前字段对应的可选择项。</param>
        private static void DrawSceneIdField(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            SceneLoadDatabase database,
            SceneIdDropdownOptionSet optionSet)
        {
            GUIContent displayLabel = label ?? new GUIContent(property.displayName);
            bool previousMixedValue = EditorGUI.showMixedValue;
            try
            {
                EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
                using (new EditorGUI.DisabledScope(database == null || optionSet.Options.Count == 0))
                {
                    GUIContent[] displayOptions = new GUIContent[optionSet.Options.Count];
                    for (int index = 0; index < displayOptions.Length; index++)
                        displayOptions[index] = new GUIContent(optionSet.Options[index].Label);

                    EditorGUI.BeginChangeCheck();
                    int selectedIndex = EditorGUI.Popup(position, displayLabel, optionSet.SelectedIndex, displayOptions);
                    if (EditorGUI.EndChangeCheck() &&
                        selectedIndex >= 0 && selectedIndex < optionSet.Options.Count)
                    {
                        SceneIdDropdownOption selectedOption = optionSet.Options[selectedIndex];
                        if (selectedOption.CanSelect)
                            ApplyValue(property.serializedObject.targetObjects, property.propertyPath, selectedOption.SceneId);
                    }
                }
            }
            finally
            {
                EditorGUI.showMixedValue = previousMixedValue;
            }
        }

        /// <summary>刷新 UI Toolkit 字段及其状态提示，不保留失效的 SerializedProperty。</summary>
        /// <param name="databaseField">会话级数据库选择控件。</param>
        /// <param name="sceneIdField">SceneId 选择控件。</param>
        /// <param name="warningBox">数据库配置警告控件。</param>
        /// <param name="targets">字段所属 Unity 对象。</param>
        /// <param name="propertyPath">字段序列化路径。</param>
        private static void RefreshToolkitFields(
            ObjectField databaseField,
            DropdownField sceneIdField,
            HelpBox warningBox,
            UnityEngine.Object[] targets,
            string propertyPath)
        {
            if (!TryReadValue(targets, propertyPath, out string currentSceneId, out bool hasMultipleValues))
            {
                sceneIdField.choices = new List<string> { "对象已不可用" };
                sceneIdField.SetValueWithoutNotify("对象已不可用");
                sceneIdField.SetEnabled(false);
                warningBox.text = "字段所属对象已不可用；刷新 Inspector 后重新绑定。";
                warningBox.style.display = DisplayStyle.Flex;
                return;
            }

            SceneLoadDatabase[] databases = GetSceneLoadDatabases();
            SceneLoadDatabase database = GetSelectedDatabase(databases);
            databaseField.SetValueWithoutNotify(database);
            databaseField.SetEnabled(databases.Length > 1);

            SceneIdDropdownOptionSet optionSet = BuildOptions(database, hasMultipleValues ? null : currentSceneId);
            var choices = new List<string>(optionSet.Options.Count);
            for (int index = 0; index < optionSet.Options.Count; index++)
                choices.Add(optionSet.Options[index].Label);
            sceneIdField.choices = choices;
            sceneIdField.SetValueWithoutNotify(optionSet.Options[optionSet.SelectedIndex].Label);
            sceneIdField.showMixedValue = hasMultipleValues;
            sceneIdField.SetEnabled(database != null && optionSet.Options.Count > 0);

            string warning = optionSet.Warning;
            if (hasMultipleValues)
                warning = string.IsNullOrEmpty(warning)
                    ? "选中对象的 SceneId 不同；选择后会将新 ID 应用于全部对象。"
                    : $"{warning} 选中对象的 SceneId 不同；选择后会将新 ID 应用于全部对象。";
            warningBox.text = warning;
            warningBox.style.display = string.IsNullOrEmpty(warning) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>验证属性确为 string，并避免同一错误字段在 Inspector 重绘时重复记录。</summary>
        /// <param name="property">待绘制序列化属性。</param>
        /// <returns>属性类型正确时返回 true。</returns>
        private static bool IsStringProperty(SerializedProperty property) =>
            property != null && property.propertyType == SerializedPropertyType.String;

        /// <summary>记录错误的 Attribute 用法，避免每次 Inspector 重绘重复输出。</summary>
        /// <param name="property">使用 Attribute 的字段。</param>
        private static void LogInvalidProperty(SerializedProperty property)
        {
            string propertyPath = property == null ? "<null>" : property.propertyPath;
            if (!invalidPropertyPathSet.Add(propertyPath)) return;
            Debug.LogError($"[SceneIdDropdownPropertyDrawer] SceneIdDropdown 仅支持 string 字段，property={propertyPath}。");
        }

        /// <summary>判断多对象 Inspector 中该 SceneId 是否存在不同值。</summary>
        /// <param name="targets">字段所属的 Unity 对象。</param>
        /// <param name="propertyPath">字段序列化路径。</param>
        /// <returns>目标属性仍有效且值不同时返回 true。</returns>
        private static bool HasMultipleValues(UnityEngine.Object[] targets, string propertyPath)
        {
            return TryReadValue(targets, propertyPath, out _, out bool hasMultipleValues) && hasMultipleValues;
        }

        /// <summary>通过临时 SerializedObject 获取字段值，安全支持多对象编辑。</summary>
        /// <param name="targets">属性所属的 Unity 对象。</param>
        /// <param name="propertyPath">字段序列化路径。</param>
        /// <param name="currentSceneId">唯一当前值；混合值时为空。</param>
        /// <param name="hasMultipleValues">是否存在不同的选中值。</param>
        /// <returns>对象和属性仍有效时返回 true。</returns>
        private static bool TryReadValue(
            UnityEngine.Object[] targets,
            string propertyPath,
            out string currentSceneId,
            out bool hasMultipleValues)
        {
            currentSceneId = string.Empty;
            hasMultipleValues = false;
            if (targets == null || targets.Length == 0) return false;
            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index] == null) return false;
            }

            using var serializedTargets = new SerializedObject(targets);
            serializedTargets.UpdateIfRequiredOrScript();
            SerializedProperty property = serializedTargets.FindProperty(propertyPath);
            if (!IsStringProperty(property)) return false;

            hasMultipleValues = property.hasMultipleDifferentValues;
            currentSceneId = hasMultipleValues ? string.Empty : property.stringValue;
            return true;
        }

        /// <summary>读取单字段显示值，用于 UI Toolkit 在选择前构造候选视图。</summary>
        /// <param name="targets">属性所属的 Unity 对象。</param>
        /// <param name="propertyPath">字段序列化路径。</param>
        /// <returns>当前值；对象失效或存在混合值时返回 null。</returns>
        private static string GetCurrentValue(UnityEngine.Object[] targets, string propertyPath)
        {
            return TryReadValue(targets, propertyPath, out string currentValue, out bool hasMultipleValues) &&
                   !hasMultipleValues
                ? currentValue
                : null;
        }

        /// <summary>通过 Unity 序列化系统写入用户选择，保留 Undo 和 Prefab Override 行为。</summary>
        /// <param name="targets">需要修改的 Unity 对象。</param>
        /// <param name="propertyPath">字段序列化路径。</param>
        /// <param name="sceneId">用户主动选择的稳定场景 ID。</param>
        private static void ApplyValue(UnityEngine.Object[] targets, string propertyPath, string sceneId)
        {
            if (targets == null || targets.Length == 0) return;
            using var serializedTargets = new SerializedObject(targets);
            serializedTargets.UpdateIfRequiredOrScript();
            SerializedProperty property = serializedTargets.FindProperty(propertyPath);
            if (!IsStringProperty(property)) return;

            property.stringValue = sceneId;
            if (serializedTargets.ApplyModifiedProperties())
                Debug.Log($"[SceneIdDropdownPropertyDrawer] 已设置 SceneId，property={propertyPath}，sceneId={sceneId}，targetCount={targets.Length}。");
        }

        #endregion

        #region 嵌套数据类型

        /// <summary>表示一个下拉项；SceneId 为空引用的项只展示状态且不会写入字段。</summary>
        private sealed class SceneIdDropdownOption
        {
            /// <summary>创建一个显示项及其可选写入值。</summary>
            /// <param name="label">Inspector 展示文本。</param>
            /// <param name="sceneId">可写入的 SceneId；空引用表示占位或错误状态。</param>
            public SceneIdDropdownOption(string label, string sceneId)
            {
                Label = label;
                SceneId = sceneId;
            }

            /// <summary>获取 Inspector 展示文本。</summary>
            public string Label { get; }

            /// <summary>获取用户选择后写入的 SceneId。</summary>
            public string SceneId { get; }

            /// <summary>判断该项是否对应明确、可写入的场景配置或清空动作。</summary>
            public bool CanSelect => SceneId != null;
        }

        /// <summary>保存一次字段绘制所需选项、当前下标和数据库校验提示。</summary>
        private sealed class SceneIdDropdownOptionSet
        {
            private readonly List<SceneIdDropdownOption> options = new();

            /// <summary>获取本次绘制的候选项。</summary>
            public IReadOnlyList<SceneIdDropdownOption> Options => options;

            /// <summary>获取当前字段在候选项中的显示下标。</summary>
            public int SelectedIndex { get; set; }

            /// <summary>获取数据库配置问题提示。</summary>
            public string Warning { get; set; }

            /// <summary>追加一项并返回其下标。</summary>
            /// <param name="label">显示文本。</param>
            /// <param name="sceneId">写入值；空引用表示不可写的状态项。</param>
            /// <returns>新选项下标。</returns>
            public int Add(string label, string sceneId)
            {
                options.Add(new SceneIdDropdownOption(label, sceneId));
                return options.Count - 1;
            }

            /// <summary>按稳定 SceneId 查找有效选项。</summary>
            /// <param name="sceneId">需要查询的场景 ID。</param>
            /// <returns>匹配选项；不存在时返回 null。</returns>
            public SceneIdDropdownOption Find(string sceneId)
            {
                for (int index = 0; index < options.Count; index++)
                {
                    SceneIdDropdownOption option = options[index];
                    if (option.CanSelect && string.Equals(option.SceneId, sceneId, StringComparison.Ordinal))
                        return option;
                }

                return null;
            }

            /// <summary>按 UI Toolkit 下拉框展示的标签查找用户刚选中的候选项。</summary>
            /// <param name="label">下拉框当前显示文本。</param>
            /// <returns>匹配选项；占位标签或不存在时返回 null。</returns>
            public SceneIdDropdownOption FindByLabel(string label)
            {
                for (int index = 0; index < options.Count; index++)
                {
                    SceneIdDropdownOption option = options[index];
                    if (option.CanSelect && string.Equals(option.Label, label, StringComparison.Ordinal))
                        return option;
                }

                return null;
            }
        }

        #endregion

        #region 静态生命周期

        /// <summary>订阅资产和撤销变化，使数据库发现缓存不跨编辑器变更使用。</summary>
        static SceneIdDropdownPropertyDrawer()
        {
            EditorApplication.projectChanged += InvalidateDatabaseCache;
            Undo.undoRedoPerformed += InvalidateDatabaseCache;
            Debug.Log("[SceneIdDropdownPropertyDrawer] 已订阅项目资产与 Undo 变化。");
        }

        #endregion
    }
}
#endif
