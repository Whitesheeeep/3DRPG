#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.Editor
{
    /// <summary>实现 Cue 列表、详情绑定、资源定位和校验显示的 UI Toolkit View。</summary>
    public sealed class GameplayCueEditorView : IGameplayCueEditorView
    {
        #region 字段与状态
        private const string RowUxmlPath =
            "Assets/GAS_Light/GameplayCueSystem/Editor/Style/GameplayCueRow.uxml";

        private readonly VisualElement root;
        private readonly VisualTreeAsset rowAsset;
        private readonly ObjectField databaseField;
        private readonly ToolbarSearchField searchField;
        private readonly Button createDatabaseButton;
        private readonly Button refreshButton;
        private readonly Button createCueButton;
        private readonly Button addCueButton;
        private readonly Button duplicateCueButton;
        private readonly ListView cueList;
        private readonly VisualElement detailsHost;
        private readonly VisualElement validationHost;
        private readonly List<GameplayCueData> renderedCues = new();
        private readonly List<CueRowState> rowStates = new();
        private readonly Dictionary<GameplayCueData, GameplayCueValidationSeverity> validationStates = new();
        private readonly List<GameplayCueValidationIssue> renderedValidationIssues = new();

        private GameplayCueData selectedCue;
        private GameplayTagDatabase tagDatabase;
        private GameplayCueData renamingCue;
        private string pendingRenameValue = string.Empty;
        private int renameVersion;
        private SerializedObject boundObject;
        private Label detailsTitle;
        private Label cueAssetPathLabel;
        private Label addressableKeyLabel;
        private Label fallbackPrefabPathLabel;
        private Label behaviourStateLabel;
        private Button pingPrefabButton;
        private bool bindingDetails;
        private bool disposed;

        #endregion

        #region 用户意图事件

        /// <inheritdoc />
        public event Action<GameplayCueDatabase> DatabaseChanged;
        /// <inheritdoc />
        public event Action<string> SearchChanged;
        /// <inheritdoc />
        public event Action<GameplayCueData> CueSelectionChanged;
        /// <inheritdoc />
        public event Action CreateCueRequested;
        /// <inheritdoc />
        public event Action AddExistingCueRequested;
        /// <inheritdoc />
        public event Action DuplicateCueRequested;
        /// <inheritdoc />
        public event Action<GameplayCueData> RemoveFromDatabaseRequested;
        /// <inheritdoc />
        public event Action<GameplayCueData> DeleteCueRequested;
        /// <inheritdoc />
        public event Action<GameplayCueData> PingCueRequested;
        /// <inheritdoc />
        public event Action<GameplayCueData> PingPrefabRequested;
        /// <inheritdoc />
        public event Action<GameplayCueRenameRequest> RenameCueSubmitted;
        /// <inheritdoc />
        public event Action CueSerializedChanged;
        /// <inheritdoc />
        public event Action RefreshRequested;
        /// <inheritdoc />
        public event Action CreateDatabaseRequested;

        #endregion

        #region 生命周期与绑定

        /// <summary>创建 Cue 编辑 View 并配置列表回调。</summary>
        /// <param name="root">已实例化的 Cue 页面根节点。</param>
        public GameplayCueEditorView(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            rowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RowUxmlPath);
            if (rowAsset == null) throw new InvalidOperationException("Gameplay Cue row UXML asset is missing.");

            databaseField = Require<ObjectField>("DatabaseField");
            databaseField.objectType = typeof(GameplayCueDatabase);
            databaseField.allowSceneObjects = false;
            searchField = Require<ToolbarSearchField>("SearchField");
            createDatabaseButton = Require<Button>("CreateDatabaseButton");
            refreshButton = Require<Button>("RefreshButton");
            createCueButton = Require<Button>("CreateCueButton");
            addCueButton = Require<Button>("AddCueButton");
            duplicateCueButton = Require<Button>("DuplicateCueButton");
            cueList = Require<ListView>("CueList");
            detailsHost = Require<VisualElement>("DetailsHost");
            validationHost = Require<VisualElement>("ValidationHost");
            ConfigureList();
            RegisterCallbacks();
        }

        /// <inheritdoc />
        public void SetDatabase(GameplayCueDatabase database) =>
            databaseField.SetValueWithoutNotify(database);

        /// <inheritdoc />
        public void SetTagDatabase(GameplayTagDatabase database)
        {
            tagDatabase = database;
            if (!disposed) cueList.RefreshItems();
        }

        /// <inheritdoc />
        public void SetSearch(string search) =>
            searchField.SetValueWithoutNotify(search ?? string.Empty);

        /// <inheritdoc />
        public void RenderCues(IReadOnlyList<GameplayCueData> cues, GameplayCueData selected)
        {
            renderedCues.Clear();
            if (cues != null) renderedCues.AddRange(cues);
            selectedCue = selected;
            if (renamingCue != null && !renderedCues.Contains(renamingCue)) CancelRename();
            cueList.RefreshItems();
            int index = renderedCues.IndexOf(selected);
            cueList.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index });
        }

        /// <inheritdoc />
        public void RenderValidationStates(
            IReadOnlyDictionary<GameplayCueData, GameplayCueValidationSeverity> states)
        {
            if (ValidationStatesEqual(states)) return;
            validationStates.Clear();
            if (states != null)
            {
                foreach (KeyValuePair<GameplayCueData, GameplayCueValidationSeverity> pair in states)
                    validationStates[pair.Key] = pair.Value;
            }

            cueList.RefreshItems();
        }

        /// <inheritdoc />
        public void BindCue(GameplayCueData cue)
        {
            if (boundObject != null) detailsHost.Unbind();
            boundObject = null;
            detailsHost.Clear();
            ClearDetailsPresentationReferences();
            selectedCue = cue;
            duplicateCueButton.SetEnabled(cue != null);
            if (cue == null)
            {
                detailsHost.Add(new HelpBox("请选择一个 GameplayCueData。", HelpBoxMessageType.Info));
                return;
            }

            boundObject = new SerializedObject(cue);
            detailsTitle = new Label(cue.name) { name = "CueDetailsTitle" };
            detailsHost.Add(detailsTitle);
            AddProperty("cueTag", "Cue Tag");
            AddProperty("markerKey", "Marker");
            AddProperty("addressableKey", "Addressable Key");
            AddProperty("fallbackPrefab", "Fallback Prefab");
            AddProperty("defaultAnchor", "Default Anchor Mode");
            AddProperty("localPosition", "Local Position");
            AddProperty("localEulerAngles", "Local Euler Angles");
            AddProperty("followAnchor", "Follow Anchor");
            AddResourceInfo(cue);
            bindingDetails = true;
            detailsHost.Bind(boundObject);
            bindingDetails = false;
            detailsHost.schedule.Execute(() =>
            {
                detailsHost.Query<TextField>().ForEach(field => field.isDelayed = true);
            });
        }

        /// <inheritdoc />
        public void RefreshCuePresentation(GameplayCueData cue)
        {
            if (disposed || cue == null) return;
            int index = renderedCues.IndexOf(cue);
            if (index >= 0) cueList.RefreshItem(index);
            if (!ReferenceEquals(selectedCue, cue) || boundObject?.targetObject != cue) return;

            if (detailsTitle != null) detailsTitle.text = cue.name;
            RefreshResourceInfo(cue);
        }

        /// <inheritdoc />
        public void RenderValidation(IReadOnlyList<GameplayCueValidationIssue> issues)
        {
            if (ValidationIssuesEqual(issues)) return;
            renderedValidationIssues.Clear();
            if (issues != null) renderedValidationIssues.AddRange(issues);
            validationHost.Clear();
            if (issues == null || issues.Count == 0) return;

            validationHost.Add(new Label("Validation") { name = "CueValidationTitle" });
            for (int i = 0; i < issues.Count; i++)
            {
                GameplayCueValidationIssue issue = issues[i];
                string prefix = issue.Severity == GameplayCueValidationSeverity.Error
                    ? "Error"
                    : issue.Severity == GameplayCueValidationSeverity.Warning ? "Warning" : "Info";
                HelpBoxMessageType type = issue.Severity == GameplayCueValidationSeverity.Error
                    ? HelpBoxMessageType.Error
                    : issue.Severity == GameplayCueValidationSeverity.Warning
                        ? HelpBoxMessageType.Warning
                        : HelpBoxMessageType.Info;
                validationHost.Add(new HelpBox($"[{prefix}] {issue.Message}", type));
            }
        }

        /// <inheritdoc />
        public void RestoreCueRename(GameplayCueData cue, string attemptedName)
        {
            if (cue == null) return;
            QueueRename(cue, attemptedName);
        }

        /// <inheritdoc />
        public void ShowError(string title, string message) => EditorUtility.DisplayDialog(title, message, "确定");

        /// <inheritdoc />
        public bool Confirm(string title, string message) =>
            EditorUtility.DisplayDialog(title, message, "确定", "取消");

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            UnregisterCallbacks();
            for (int i = 0; i < rowStates.Count; i++) rowStates[i].Unregister();
            rowStates.Clear();
            cueList.itemsSource = null;
            detailsHost.Unbind();
            boundObject = null;
            renderedValidationIssues.Clear();
            ClearDetailsPresentationReferences();
        }

        #endregion

        #region 列表与详情渲染

        // 配置 ListView 稳定数据源，后续只刷新行，不反复替换 itemsSource。
        private void ConfigureList()
        {
            cueList.selectionType = SelectionType.Single;
            cueList.fixedItemHeight = 42f;
            cueList.makeItem = MakeRow;
            cueList.bindItem = BindRow;
            cueList.itemsSource = renderedCues;
        }

        // 创建一次行状态并注册一次事件。
        private VisualElement MakeRow()
        {
            VisualElement row = rowAsset.Instantiate();
            var state = new CueRowState(this, row);
            state.Register();
            row.userData = state;
            rowStates.Add(state);
            return row;
        }

        // 只绑定当前引用和校验状态，清理虚拟化复用留下的 USS 状态。
        private void BindRow(VisualElement element, int index)
        {
            var state = element.userData as CueRowState;
            if (state == null) throw new InvalidOperationException("Gameplay Cue row state is missing.");
            GameplayCueData cue = renderedCues[index];
            validationStates.TryGetValue(cue, out GameplayCueValidationSeverity severity);
            state.Bind(cue, ReferenceEquals(renamingCue, cue), pendingRenameValue, severity);
        }

        // 将稳定 TagId 解析为当前数据库中的完整路径；数据库不可用时保留明确的 ID 文本。
        private string ResolveCueTagName(GameplayTag tag)
        {
            if (tagDatabase != null && tagDatabase.TryGetBakedPath(tag, out string path))
                return path;
            return tag.IsValid ? $"GameplayTag ({tag.Id})" : "GameplayTag.Empty";
        }

        #endregion

        #region 事件处理与内部辅助

        // 监听 UI 控件的用户意图，并将业务操作交给 Controller。
        private void RegisterCallbacks()
        {
            databaseField.RegisterValueChangedCallback(OnDatabaseChanged);
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            createDatabaseButton.clicked += OnCreateDatabaseClicked;
            refreshButton.clicked += OnRefreshClicked;
            createCueButton.clicked += OnCreateCueClicked;
            addCueButton.clicked += OnAddCueClicked;
            duplicateCueButton.clicked += OnDuplicateCueClicked;
            cueList.selectionChanged += OnSelectionChanged;
            detailsHost.RegisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
        }

        // 对称解除所有 UI 回调，避免模块切换后重复发送意图。
        private void UnregisterCallbacks()
        {
            databaseField.UnregisterValueChangedCallback(OnDatabaseChanged);
            searchField.UnregisterValueChangedCallback(OnSearchChanged);
            createDatabaseButton.clicked -= OnCreateDatabaseClicked;
            refreshButton.clicked -= OnRefreshClicked;
            createCueButton.clicked -= OnCreateCueClicked;
            addCueButton.clicked -= OnAddCueClicked;
            duplicateCueButton.clicked -= OnDuplicateCueClicked;
            cueList.selectionChanged -= OnSelectionChanged;
            detailsHost.UnregisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
        }

        // 数据库 ObjectField 只传递用户选择，不直接修改 Model。
        private void OnDatabaseChanged(ChangeEvent<UnityEngine.Object> evt) =>
            DatabaseChanged?.Invoke(evt.newValue as GameplayCueDatabase);

        // 搜索框事件直接转发文本意图。
        private void OnSearchChanged(ChangeEvent<string> evt) => SearchChanged?.Invoke(evt.newValue);

        // 顶部操作只负责收集路径或发送用户意图。
        private void OnCreateDatabaseClicked() => CreateDatabaseRequested?.Invoke();
        private void OnRefreshClicked() => RefreshRequested?.Invoke();
        private void OnCreateCueClicked() => CreateCueRequested?.Invoke();
        private void OnAddCueClicked() => AddExistingCueRequested?.Invoke();
        private void OnDuplicateCueClicked() => DuplicateCueRequested?.Invoke();

        // ListView 选择事件转换为稳定 CueData 引用。
        private void OnSelectionChanged(IEnumerable<object> selection) =>
            CueSelectionChanged?.Invoke(selection.OfType<GameplayCueData>().FirstOrDefault());

        // 原生 PropertyField 写回后延迟通知 Controller，避免在 IMGUI 当前事件中重建绑定。
        private void OnSerializedPropertyChanged(SerializedPropertyChangeEvent evt)
        {
            if (bindingDetails) return;
            if (!disposed) CueSerializedChanged?.Invoke();
        }

        // 双击名称后延迟刷新行，避免当前 Pointer 事件期间重建 TextField。
        private void QueueRename(GameplayCueData cue, string value)
        {
            if (disposed || cue == null) return;
            if (!ReferenceEquals(selectedCue, cue))
            {
                selectedCue = cue;
                int index = renderedCues.IndexOf(cue);
                cueList.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index });
                CueSelectionChanged?.Invoke(cue);
            }
            renamingCue = cue;
            pendingRenameValue = value ?? cue.name;
            int version = ++renameVersion;
            root.schedule.Execute(() =>
            {
                if (disposed || version != renameVersion) return;
                cueList.RefreshItems();
            });
        }

        // 退出行内编辑后提交稳定资产引用，避免 ListView 刷新时旧输入框继续写回。
        private void SubmitRename(CueRowState state)
        {
            if (state?.Cue == null || !ReferenceEquals(renamingCue, state.Cue)) return;
            GameplayCueData cue = state.Cue;
            string name = state.RenameField.value;
            CancelRename();
            RenameCueSubmitted?.Invoke(new GameplayCueRenameRequest(cue, name));
        }

        // 取消行内重命名并清除旧焦点任务。
        private void CancelRename()
        {
            renameVersion++;
            renamingCue = null;
            pendingRenameValue = string.Empty;
            if (!disposed) cueList.RefreshItems();
        }

        // 使用 SerializedProperty 创建详情字段，保持原生 Undo 和绑定语义。
        private void AddProperty(string path, string label)
        {
            SerializedProperty property = boundObject.FindProperty(path);
            if (property == null) return;
            detailsHost.Add(new PropertyField(property, label));
        }

        // 显示静态资源信息，不调用 Addressable 或 PoolManager。
        private void AddResourceInfo(GameplayCueData cue)
        {
            var box = new VisualElement { name = "CueResourceInfo" };
            box.AddToClassList("cue-resource-box");
            cueAssetPathLabel = new Label();
            addressableKeyLabel = new Label();
            fallbackPrefabPathLabel = new Label();
            behaviourStateLabel = new Label();
            box.Add(cueAssetPathLabel);
            box.Add(addressableKeyLabel);
            box.Add(fallbackPrefabPathLabel);
            box.Add(behaviourStateLabel);

            var pingCue = new Button(() => PingCueRequested?.Invoke(selectedCue)) { text = "Ping Cue" };
            pingPrefabButton = new Button(() => PingPrefabRequested?.Invoke(selectedCue)) { text = "Ping Prefab" };
            box.Add(pingCue);
            box.Add(pingPrefabButton);
            detailsHost.Add(box);
            RefreshResourceInfo(cue);
        }

        /// <summary>刷新不参与 SerializedObject 绑定的资源摘要，失效 Prefab 引用只显示诊断文本。</summary>
        /// <param name="cue">当前选中的 CueData。</param>
        private void RefreshResourceInfo(GameplayCueData cue)
        {
            if (cue == null || cueAssetPathLabel == null) return;
            cueAssetPathLabel.text = $"Cue 资产：{AssetDatabase.GetAssetPath(cue)}";
            addressableKeyLabel.text = $"Addressable Key：{(string.IsNullOrWhiteSpace(cue.AddressableKey) ? "未配置" : cue.AddressableKey)}";

            // Unity 对已销毁对象保留托管包装，所有 Prefab 访问必须在同一个边界内完成，避免后续 UI 刷新再次访问失效引用。
            string prefabPath = "未配置";
            bool invalidPrefabReference = false;
            bool hasFallbackPrefab = false;
            bool hasBehaviour = false;
            try
            {
                GameObject fallbackPrefab = cue.FallbackPrefab;
                hasFallbackPrefab = fallbackPrefab != null;
                if (hasFallbackPrefab)
                {
                    prefabPath = AssetDatabase.GetAssetPath(fallbackPrefab);
                    hasBehaviour = fallbackPrefab.TryGetComponent<GameplayCueBehaviour>(out _);
                }
            }
            catch (MissingReferenceException)
            {
                invalidPrefabReference = true;
                prefabPath = "引用已失效";
            }

            fallbackPrefabPathLabel.text = $"Fallback Prefab：{prefabPath}";
            behaviourStateLabel.text = invalidPrefabReference
                ? "GameplayCueBehaviour：Fallback Prefab 引用已失效"
                : !hasFallbackPrefab
                    ? "GameplayCueBehaviour：未检查"
                    : $"GameplayCueBehaviour：{(hasBehaviour ? "已配置" : "缺失")}";
            pingPrefabButton?.SetEnabled(!invalidPrefabReference && hasFallbackPrefab);
        }

        // 清除旧详情控件引用，确保目标切换后不会更新已经脱离 Panel 的元素。
        private void ClearDetailsPresentationReferences()
        {
            detailsTitle = null;
            cueAssetPathLabel = null;
            addressableKeyLabel = null;
            fallbackPrefabPathLabel = null;
            behaviourStateLabel = null;
            pingPrefabButton = null;
        }

        // 比较列表着色状态，内容未变化时不触发全部可见行重新绑定。
        private bool ValidationStatesEqual(
            IReadOnlyDictionary<GameplayCueData, GameplayCueValidationSeverity> states)
        {
            int count = states?.Count ?? 0;
            if (validationStates.Count != count) return false;
            if (states == null) return true;
            foreach (KeyValuePair<GameplayCueData, GameplayCueValidationSeverity> pair in states)
            {
                if (!validationStates.TryGetValue(pair.Key, out GameplayCueValidationSeverity current) ||
                    current != pair.Value)
                    return false;
            }

            return true;
        }

        // 比较当前详情校验内容，结果相同时保留现有 HelpBox 控件树。
        private bool ValidationIssuesEqual(IReadOnlyList<GameplayCueValidationIssue> issues)
        {
            int count = issues?.Count ?? 0;
            if (renderedValidationIssues.Count != count) return false;
            for (int i = 0; i < count; i++)
            {
                GameplayCueValidationIssue left = renderedValidationIssues[i];
                GameplayCueValidationIssue right = issues[i];
                if (left.Severity != right.Severity ||
                    !ReferenceEquals(left.Cue, right.Cue) ||
                    !string.Equals(left.Message, right.Message, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        // 查询必需控件，UXML 契约破坏时立即抛出而不是静默显示空页面。
        private T Require<T>(string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null)
                throw new InvalidOperationException($"Gameplay Cue Editor UXML 缺少控件：{name}");
            return element;
        }

        #endregion

        #region 虚拟化行状态

        /// <summary>保存虚拟化行的当前 Cue 引用和交互状态。</summary>
        private sealed class CueRowState
        {
            private readonly GameplayCueEditorView owner;
            private readonly VisualElement visualRoot;
            private readonly Label nameLabel;
            private readonly Label tagLabel;
            private readonly Button pingButton;
            private readonly Button removeButton;
            private readonly Button deleteButton;
            private bool suppressFocusCommit;

            /// <summary>创建一行的控件状态。</summary>
            /// <param name="owner">所属 View。</param>
            /// <param name="root">行根节点。</param>
            public CueRowState(GameplayCueEditorView owner, VisualElement root)
            {
                this.owner = owner;
                visualRoot = root.Q<VisualElement>("RowRoot") ?? root;
                nameLabel = root.Q<Label>("NameLabel") ?? throw new InvalidOperationException("Cue 行缺少 NameLabel。");
                tagLabel = root.Q<Label>("TagLabel") ?? throw new InvalidOperationException("Cue 行缺少 TagLabel。");
                pingButton = root.Q<Button>("PingButton") ?? throw new InvalidOperationException("Cue 行缺少 PingButton。");
                removeButton = root.Q<Button>("RemoveButton") ?? throw new InvalidOperationException("Cue 行缺少 RemoveButton。");
                deleteButton = root.Q<Button>("DeleteButton") ?? throw new InvalidOperationException("Cue 行缺少 DeleteButton。");
                RenameField = root.Q<TextField>("RenameField") ?? throw new InvalidOperationException("Cue 行缺少 RenameField。");
            }

            /// <summary>获取当前行的 CueData。</summary>
            public GameplayCueData Cue { get; private set; }

            /// <summary>获取行内重命名输入框。</summary>
            public TextField RenameField { get; }

            // 注册双击、重命名和 Ping 回调。
            public void Register()
            {
                nameLabel.RegisterCallback<PointerDownEvent>(OnNamePointerDown);
                RenameField.RegisterCallback<KeyDownEvent>(OnRenameKeyDown);
                RenameField.RegisterCallback<FocusOutEvent>(OnRenameFocusOut);
                pingButton.RegisterCallback<PointerDownEvent>(OnPingPointerDown);
                pingButton.RegisterCallback<ClickEvent>(OnPingClicked);
                removeButton.RegisterCallback<PointerDownEvent>(OnActionPointerDown);
                removeButton.RegisterCallback<ClickEvent>(OnRemoveClicked);
                deleteButton.RegisterCallback<PointerDownEvent>(OnActionPointerDown);
                deleteButton.RegisterCallback<ClickEvent>(OnDeleteClicked);
            }

            // 释放虚拟化行的所有回调。
            public void Unregister()
            {
                nameLabel.UnregisterCallback<PointerDownEvent>(OnNamePointerDown);
                RenameField.UnregisterCallback<KeyDownEvent>(OnRenameKeyDown);
                RenameField.UnregisterCallback<FocusOutEvent>(OnRenameFocusOut);
                pingButton.UnregisterCallback<PointerDownEvent>(OnPingPointerDown);
                pingButton.UnregisterCallback<ClickEvent>(OnPingClicked);
                removeButton.UnregisterCallback<PointerDownEvent>(OnActionPointerDown);
                removeButton.UnregisterCallback<ClickEvent>(OnRemoveClicked);
                deleteButton.UnregisterCallback<PointerDownEvent>(OnActionPointerDown);
                deleteButton.UnregisterCallback<ClickEvent>(OnDeleteClicked);
                Cue = null;
            }

            // 绑定当前模型并清理行复用残留的状态 class。
            public void Bind(
                GameplayCueData cue,
                bool renaming,
                string renameValue,
                GameplayCueValidationSeverity severity)
            {
                Cue = cue;
                suppressFocusCommit = false;
                nameLabel.text = cue.name;
                tagLabel.text = owner.ResolveCueTagName(cue.CueTag);
                tagLabel.tooltip = cue.CueTag.IsValid
                    ? $"GameplayTag: {cue.CueTag.Id}"
                    : "非法 CueTag";
                RenameField.SetValueWithoutNotify(renaming ? renameValue : cue.name);
                visualRoot.EnableInClassList("cue-row--renaming", renaming);
                visualRoot.EnableInClassList("has-validation-error", severity == GameplayCueValidationSeverity.Error);
                visualRoot.EnableInClassList("has-validation-warning", severity == GameplayCueValidationSeverity.Warning);
                if (renaming)
                {
                    RenameField.schedule.Execute(() =>
                    {
                        if (Cue != cue || !ReferenceEquals(owner.renamingCue, cue)) return;
                        RenameField.Focus();
                        RenameField.SelectAll();
                    });
                }
            }

            // 名称文字双击进入行内重命名，其他区域不触发。
            private void OnNamePointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || evt.clickCount != 2 || Cue == null) return;
                owner.QueueRename(Cue, Cue.name);
                evt.StopImmediatePropagation();
            }

            // Enter 提交，Escape 取消。
            private void OnRenameKeyDown(KeyDownEvent evt)
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    suppressFocusCommit = true;
                    owner.SubmitRename(this);
                    evt.StopImmediatePropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    suppressFocusCommit = true;
                    owner.CancelRename();
                    evt.StopImmediatePropagation();
                }
            }

            // 失焦时提交，Enter/Escape 的显式操作只抑制本次 FocusOut。
            private void OnRenameFocusOut(FocusOutEvent evt)
            {
                if (suppressFocusCommit)
                {
                    suppressFocusCommit = false;
                    return;
                }

                if (Cue != null && ReferenceEquals(owner.renamingCue, Cue)) owner.SubmitRename(this);
            }

            // Ping 按钮不改变 ListView 选择。
            private void OnPingPointerDown(PointerDownEvent evt) => evt.StopPropagation();

            // Ping 当前行绑定的 CueData。
            private void OnPingClicked(ClickEvent evt)
            {
                evt.StopPropagation();
                if (Cue != null) owner.PingCueRequested?.Invoke(Cue);
            }

            // 删除按钮不改变 ListView 的当前选择。
            private void OnActionPointerDown(PointerDownEvent evt) => evt.StopPropagation();

            // 请求仅解除注册，不删除资产。
            private void OnRemoveClicked(ClickEvent evt)
            {
                evt.StopPropagation();
                if (Cue != null) owner.RemoveFromDatabaseRequested?.Invoke(Cue);
            }

            // 请求解除注册并删除资产。
            private void OnDeleteClicked(ClickEvent evt)
            {
                evt.StopPropagation();
                if (Cue != null) owner.DeleteCueRequested?.Invoke(Cue);
            }
        }

        #endregion
    }
}
#endif
