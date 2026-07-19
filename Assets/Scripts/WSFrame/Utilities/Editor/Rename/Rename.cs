using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WS_Modules
{
    public sealed class Rename : EditorWindow
    {
        private const string WindowTitle = "Batch Rename";

        private string _prefix = string.Empty;
        private string _suffix = string.Empty;
        private string _removePrefix = string.Empty;
        private string _removeSuffix = string.Empty;
        private string _separator = "_";
        private int _sequenceStart = 1;
        private string _sequenceSuffix = string.Empty;
        private bool _autoRemovePrefixSegment;
        private bool _autoRemoveSuffixSegment;

        private Label _selectionLabel;
        private HelpBox _errorBox;

        #region Menu Items
        [MenuItem("Tools/WSFrame/Rename Tool", priority = 2000)]
        private static void ShowWindow()
        {
            var window = GetWindow<Rename>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(360f, 320f);
        }

        [MenuItem("Assets/WSFrame/批量添加前缀后缀重命名工具", priority = 2000)]
        private static void ShowRenameWithAffix()
        {
            ShowWindow();
        }

        [MenuItem("Assets/WSFrame/批量添加序列数字重命名工具", true)]
        private static bool ValidateShowRenameWithSequence()
        {
            return Selection.assetGUIDs.Length > 0;
        }

        [MenuItem("GameObject/WSFrame/批量添加序列数字重命名工具")]
        private static void ShowGameObjectRename()
        {
            ShowWindow();
        }
        #endregion

        #region 生命周期
        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }
        #endregion

        #region 创建窗口
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 12f;
            rootVisualElement.style.paddingRight = 12f;
            rootVisualElement.style.paddingTop = 10f;
            rootVisualElement.style.paddingBottom = 10f;
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            var header = new Label("Rename Selected Assets")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14,
                    marginBottom = 4
                }
            };
            rootVisualElement.Add(header);

            _selectionLabel = new Label();
            rootVisualElement.Add(_selectionLabel);

            _errorBox = new HelpBox(string.Empty, HelpBoxMessageType.Error)
            {
                style =
                {
                    display = DisplayStyle.None,
                    marginTop = 4,
                    marginBottom = 4
                }
            };
            rootVisualElement.Add(_errorBox);

            rootVisualElement.Add(BuildConnectorSection());
            rootVisualElement.Add(BuildAffixSection());
            rootVisualElement.Add(BuildRemoveAffixSection());
            rootVisualElement.Add(BuildSequenceSection());

            RefreshSelectionLabel();
            ClearError();
        }

        private VisualElement BuildConnectorSection()
        {
            var box = new Box { style = { marginTop = 8 } };
            var title = new Label("Affix Connector")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold }
            };
            box.Add(title);

            var connectorField = new TextField("Connector") { value = _separator, isDelayed = true };
            connectorField.RegisterValueChangedCallback(evt => _separator = evt.newValue ?? string.Empty);
            box.Add(connectorField);

            return box;
        }

        private VisualElement BuildAffixSection()
        {
            var box = new Box { style = { marginTop = 10 } };
            var title = new Label("Add Prefix or Suffix")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold }
            };
            box.Add(title);

            var prefixField = new TextField("Prefix") { value = _prefix, isDelayed = true };
            prefixField.RegisterValueChangedCallback(evt => _prefix = evt.newValue.Trim());
            box.Add(prefixField);

            var suffixField = new TextField("Suffix") { value = _suffix, isDelayed = true };
            suffixField.RegisterValueChangedCallback(evt => _suffix = evt.newValue.Trim());
            box.Add(suffixField);

            var applyButton = new Button(ApplyAffixRename)
            {
                text = "Apply Prefix/Suffix",
                style = { marginTop = 6 }
            };
            box.Add(applyButton);

            return box;
        }

        private VisualElement BuildRemoveAffixSection()
        {
            var box = new Box { style = { marginTop = 12 } };
            var title = new Label("Remove Prefix or Suffix")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold }
            };

            box.Add(title);

            var hint = new HelpBox("留空并启用自动移除选项时，会根据第一个/最后一个分隔符裁剪名称片段，例如 Player_01 -> Player 或 01。", HelpBoxMessageType.Info)
            {
                style = { marginTop = 4, marginBottom = 4 }
            };
            box.Add(hint);

            var removePrefixField = new TextField("Remove Prefix") { value = _removePrefix, isDelayed = true };
            removePrefixField.RegisterValueChangedCallback(evt => _removePrefix = evt.newValue.Trim());
            box.Add(removePrefixField);

            var autoPrefixToggle = new Toggle("Auto remove leading segment") { value = _autoRemovePrefixSegment };
            autoPrefixToggle.RegisterValueChangedCallback(evt => _autoRemovePrefixSegment = evt.newValue);
            box.Add(autoPrefixToggle);

            var removeSuffixField = new TextField("Remove Suffix") { value = _removeSuffix, isDelayed = true };
            removeSuffixField.RegisterValueChangedCallback(evt => _removeSuffix = evt.newValue.Trim());
            box.Add(removeSuffixField);

            var autoSuffixToggle = new Toggle("Auto remove trailing segment") { value = _autoRemoveSuffixSegment };
            autoSuffixToggle.RegisterValueChangedCallback(evt => _autoRemoveSuffixSegment = evt.newValue);
            box.Add(autoSuffixToggle);

            var applyButton = new Button(ApplyRemoveAffixRename)
            {
                text = "Apply Remove Prefix/Suffix",
                style = { marginTop = 6 }
            };
            box.Add(applyButton);

            return box;
        }

        private VisualElement BuildSequenceSection()
        {
            var box = new Box { style = { marginTop = 12 } };
            var title = new Label("Append Sequential Numbers")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold }
            };
            box.Add(title);

            var startField = new IntegerField("Starting Number")
            {
                value = _sequenceStart,
                isDelayed = true
            };
            startField.RegisterValueChangedCallback(evt => _sequenceStart = Mathf.Max(0, evt.newValue));
            box.Add(startField);

            var sequenceSuffixField = new TextField("Number Suffix (Optional)") { value = _sequenceSuffix, isDelayed = true };
            sequenceSuffixField.RegisterValueChangedCallback(evt => _sequenceSuffix = evt.newValue.Trim());
            box.Add(sequenceSuffixField);

            var applyButton = new Button(ApplySequentialRename)
            {
                text = "Apply Numbering",
                style = { marginTop = 6 }
            };
            box.Add(applyButton);

            return box;
        }
        #endregion

        #region 重命名逻辑
        private void ApplyAffixRename()
        {
            if (string.IsNullOrEmpty(_prefix) && string.IsNullOrEmpty(_suffix))
            {
                ShowError("Enter a prefix or suffix before applying.");
                return;
            }

            if (!TryGetRenameContext(out var context))
            {
                return;
            }

            var renamedCount = context.Target switch
            {
                RenameTarget.Assets => BatchRename(
                    context.Assets,
                    info => BuildAffixedName(info.Name),
                    info => info.Name,
                    info => info.Path,
                    new AssetDatabaseRenamer()),
                RenameTarget.GameObjects => BatchRename(
                    context.GameObjects,
                    go => BuildAffixedName(go.name),
                    go => go.name,
                    GetGameObjectDescriptor,
                    new GameObjectRenamer()),
                _ => 0
            };

            ShowCompletionDialog(renamedCount, context.Target);
        }

        private void ApplyRemoveAffixRename()
        {
            if (string.IsNullOrEmpty(_removePrefix) && string.IsNullOrEmpty(_removeSuffix) && string.IsNullOrEmpty(_separator))
            {
                ShowError("Provide a prefix/suffix to remove or enable an automatic segment removal option.");
                return;
            }

            var hasManualInput = !string.IsNullOrEmpty(_removePrefix) || !string.IsNullOrEmpty(_removeSuffix);
            var needsSeparator = (_autoRemovePrefixSegment || _autoRemoveSuffixSegment) && string.IsNullOrEmpty(_separator);
            if (!hasManualInput && !_autoRemovePrefixSegment && !_autoRemoveSuffixSegment)
            {
                ShowError("Provide a prefix/suffix to remove or enable an automatic segment removal option.");
                return;
            }

            if (needsSeparator)
            {
                ShowError("Automatic segment removal requires a connector separator.");
                return;
            }

            if (!TryGetRenameContext(out var context))
            {
                return;
            }

            var renamedRemovalCount = context.Target switch
            {
                RenameTarget.Assets => BatchRename(
                    context.Assets,
                    info => BuildRemovedAffixName(info.Name),
                    info => info.Name,
                    info => info.Path,
                    new AssetDatabaseRenamer()),
                RenameTarget.GameObjects => BatchRename(
                    context.GameObjects,
                    go => BuildRemovedAffixName(go.name),
                    go => go.name,
                    GetGameObjectDescriptor,
                    new GameObjectRenamer()),
                _ => 0
            };

            ShowCompletionDialog(renamedRemovalCount, context.Target);
        }

        private void ApplySequentialRename()
        {
            Func<string, string> numberingBuilder = CreateSequenceBuilder();

            if (!TryGetRenameContext(out var context))
            {
                return;
            }

            var renamedCount = context.Target switch
            {
                RenameTarget.Assets => BatchRename(
                    context.Assets,
                    info => numberingBuilder(info.Name),
                    info => info.Name,
                    info => info.Path,
                    new AssetDatabaseRenamer()),
                RenameTarget.GameObjects => BatchRename(
                    context.GameObjects,
                    go => numberingBuilder(go.name),
                    go => go.name,
                    GetGameObjectDescriptor,
                    new GameObjectRenamer()),
                _ => 0
            };

            ShowCompletionDialog(renamedCount, context.Target);
        }

        private bool TryGetRenameContext(out RenameContext context)
        {
            var assets = GetSelectedAssets();
            var objects = GetSelectedGameObjects();

            if (assets.Count > 0 && objects.Count > 0)
            {
                context = default;
                ShowError("Mixed selections detected. Please select only assets or only GameObjects.");
                return false;
            }

            if (assets.Count > 0)
            {
                context = new RenameContext(RenameTarget.Assets, assets, objects);
                ClearError();
                return true;
            }

            if (objects.Count > 0)
            {
                context = new RenameContext(RenameTarget.GameObjects, assets, objects);
                ClearError();
                return true;
            }

            context = default;
            ShowError("Select one or more assets or GameObjects first.");
            return false;
        }

        // 前缀的重命名方法
        private string BuildAffixedName(string currentName)
        {
            return CombineWithSeparator(_separator, _prefix, currentName, _suffix);
        }

        private string BuildRemovedAffixName(string currentName)
        {
            var renamed = currentName;
            if (!string.IsNullOrWhiteSpace(_removePrefix))
            {
                renamed = RemoveLeadingAffix(renamed, _removePrefix, _separator);
            }
            else if (_autoRemovePrefixSegment)
            {
                renamed = RemoveLeadingSegmentBySeparator(renamed, _separator);
            }

            if (!string.IsNullOrWhiteSpace(_removeSuffix))
            {
                renamed = RemoveTrailingAffix(renamed, _removeSuffix, _separator);
            }
            else if (_autoRemoveSuffixSegment)
            {
                renamed = RemoveTrailingSegmentBySeparator(renamed, _separator);
            }

            return TrimConnectorEdges(renamed, _separator);
        }

        private Func<string, string> CreateSequenceBuilder()
        {
            var currentIndex = Mathf.Max(0, _sequenceStart);
            return name =>
            {
                var numbered = CombineWithSeparator(_separator, name, currentIndex.ToString(), _sequenceSuffix);
                currentIndex++;
                return numbered;
            };
        }


        // 使用连接符连接非空部分，避免出现多余的连接符
        private static string CombineWithSeparator(string separator, params string[] parts)
        {
            var tokens = parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
            if (tokens.Length == 0)
            {
                return string.Empty;
            }

            return string.IsNullOrEmpty(separator)
                ? string.Concat(tokens)
                : string.Join(separator, tokens);
        }

        private static string RemoveLeadingSegmentBySeparator(string value, string separator)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(separator))
            {
                return value;
            }

            var index = value.IndexOf(separator, StringComparison.Ordinal);
            if (index < 0)
            {
                return value;
            }

            var nextIndex = index + separator.Length;
            return nextIndex >= value.Length ? string.Empty : value.Substring(nextIndex);
        }

        private static string RemoveTrailingSegmentBySeparator(string value, string separator)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(separator))
            {
                return value;
            }

            var index = value.LastIndexOf(separator, StringComparison.Ordinal);
            if (index < 0)
            {
                return value;
            }

            return index <= 0 ? string.Empty : value.Substring(0, index);
        }

        private static string RemoveLeadingAffix(string value, string affix, string separator)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrWhiteSpace(affix))
            {
                return value;
            }

            if (string.IsNullOrEmpty(separator))
            {
                return value.StartsWith(affix, StringComparison.Ordinal)
                    ? value.Substring(affix.Length)
                    : value;
            }

            if (value.Equals(affix, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var token = affix + separator;
            return value.StartsWith(token, StringComparison.Ordinal)
                ? value.Substring(token.Length)
                : value;
        }

        [CanBeNull]
        private static string RemoveTrailingAffix(string value, string affix, string separator)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrWhiteSpace(affix))
            {
                return value;
            }

            if (string.IsNullOrEmpty(separator))
            {
                return value.EndsWith(affix, StringComparison.Ordinal)
                    ? value.Substring(0, value.Length - affix.Length)
                    : value;
            }

            if (value.Equals(affix, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var token = separator + affix;
            return value.EndsWith(token, StringComparison.Ordinal)
                ? value.Substring(0, value.Length - token.Length)
                : value;
        }

        private static string TrimConnectorEdges(string value, string separator)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(separator))
            {
                return value;
            }

            var result = value;
            while (result.StartsWith(separator, StringComparison.Ordinal))
            {
                result = result.Substring(separator.Length);
            }

            while (result.EndsWith(separator, StringComparison.Ordinal))
            {
                result = result.Substring(0, result.Length - separator.Length);
            }

            return result;
        }

        /// <summary>
        /// 批量重命名方法，接受一个对象列表、生成名称的委托、获取当前名称及描述的委托以及实际执行命名的策略。
        /// </summary>
        private int BatchRename<T>(IEnumerable<T> targets, Func<T, string> nameBuilder, Func<T, string> currentNameGetter, Func<T, string> descriptorGetter, IRename<T> renamer)
        {
            if (targets == null)
            {
                return 0;
            }

            var list = targets.ToList();
            if (list.Count == 0 || nameBuilder == null || currentNameGetter == null || renamer == null)
            {
                return 0;
            }

            var renamedCount = 0;
            var errors = new List<string>();

            renamer.Begin();
            try
            {
                foreach (var target in list)
                {
                    var currentName = currentNameGetter(target);
                    var targetName = nameBuilder(target);
                    if (string.IsNullOrWhiteSpace(targetName) || string.Equals(targetName, currentName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (renamer.TryRename(target, targetName, out var error))
                    {
                        renamedCount++;
                    }
                    else if (!string.IsNullOrEmpty(error))
                    {
                        var descriptor = descriptorGetter?.Invoke(target) ?? currentName ?? string.Empty;
                        errors.Add($"{descriptor}: {error}");
                    }
                }
            }
            finally
            {
                renamer.End();
            }

            if (errors.Count > 0)
            {
                ShowError(string.Join("\n", errors));
            }

            return renamedCount;
        }


        private void ShowError(string message)
        {
            if (_errorBox == null)
            {
                return;
            }

            _errorBox.text = message;
            _errorBox.style.display = DisplayStyle.Flex;
        }

        private void ClearError()
        {
            if (_errorBox == null)
            {
                return;
            }

            _errorBox.text = string.Empty;
            _errorBox.style.display = DisplayStyle.None;
        }

        #endregion

        #region UI 逻辑
        private void OnSelectionChanged()
        {
            RefreshSelectionLabel();
        }

        private void RefreshSelectionLabel()
        {
            if (_selectionLabel == null)
            {
                return;
            }

            var assets = GetSelectedAssets();
            var objects = GetSelectedGameObjects();

            if (assets.Count > 0 && objects.Count > 0)
            {
                _selectionLabel.text = "Mixed selection (assets + GameObjects).";
                return;
            }

            if (assets.Count > 0)
            {
                _selectionLabel.text = $"Selected assets: {assets.Count}";
                return;
            }

            if (objects.Count > 0)
            {
                _selectionLabel.text = $"Selected GameObjects: {objects.Count}";
                return;
            }

            _selectionLabel.text = "No items selected.";
        }
        #endregion

        #region 确认窗口
        private void ShowCompletionDialog(int renamedCount, RenameTarget target)
        {
            if (renamedCount <= 0)
            {
                ShowError("Nothing was renamed. Names might already match the requested format.");
                return;
            }

            ClearError();
            var label = target == RenameTarget.Assets ? "asset" : "GameObject";
            EditorUtility.DisplayDialog(WindowTitle, $"Renamed {renamedCount} {label}(s).", "OK");
            RefreshSelectionLabel();
        }
        #endregion

        #region 获取资源
        private static List<AssetInfo> GetSelectedAssets()
        {
            var infos = new List<AssetInfo>();
            foreach (var guid in Selection.assetGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                infos.Add(new AssetInfo(guid, path, name));
            }

            return infos;
        }

        private static List<GameObject> GetSelectedGameObjects()
        {
            var objects = Selection.gameObjects;
            return objects == null
                ? new List<GameObject>()
                : objects.Where(go => go != null).Distinct().ToList();
        }

        private static string GetGameObjectDescriptor(GameObject go)
        {
            if (go == null)
            {
                return "<Missing GameObject>";
            }

            if (!go.scene.IsValid())
            {
                return go.name;
            }

            var sceneName = string.IsNullOrEmpty(go.scene.name) ? go.scene.path : go.scene.name;
            if (string.IsNullOrEmpty(sceneName))
            {
                return go.name;
            }

            return $"{sceneName}/{go.name}";
        }

        private enum RenameTarget
        {
            Assets,
            GameObjects
        }

        private readonly struct RenameContext
        {
            public RenameContext(RenameTarget target, List<AssetInfo> assets, List<GameObject> objects)
            {
                Target = target;
                Assets = assets;
                GameObjects = objects;
            }

            public RenameTarget Target { get; }
            public List<AssetInfo> Assets { get; }
            public List<GameObject> GameObjects { get; }
        }
        #endregion
    }

    public struct AssetInfo
    {
        public AssetInfo(string guid, string path, string name)
        {
            Guid = guid;
            Path = path;
            Name = name;
        }

        public string Guid { get; }
        public string Path { get; }
        public string Name { get; }
    }
}

