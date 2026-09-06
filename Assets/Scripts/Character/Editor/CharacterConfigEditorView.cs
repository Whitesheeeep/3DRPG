#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIToolkitExtensions.Editor;

namespace RPG.Character.Editor
{
    /// <summary>角色配置窗口组合根，负责 Toolbar、状态栏和两个子 View 的事件转发。</summary>
    public sealed class CharacterConfigEditorView : IDisposable
    {
        #region 依赖字段
        private readonly ObjectField databaseField;
        private readonly Label statusLabel;
        private readonly Button createButton;
        private readonly Button duplicateButton;
        private readonly Button removeButton;
        private readonly Button deleteButton;
        private readonly Button validateCurrentButton;
        private readonly Button validateDatabaseButton;
        private readonly Button pingButton;
        private readonly CharacterConfigListView listView;
        private readonly CharacterConfigDetailsView detailsView;
        private bool databaseAvailable;
        private bool suppressCallbacks;
        private bool disposed;
        #endregion

        #region 事件
        /// <summary>数据库字段变化事件。</summary>
        public event Action<CharacterDatabase> DatabaseChanged;
        /// <summary>搜索文本变化事件。</summary>
        public event Action<string> SearchChanged;
        /// <summary>角色配置选择事件。</summary>
        public event Action<CharacterConfig> CharacterSelected;
        /// <summary>列表右键角色操作事件。</summary>
        internal event Action<CharacterConfig, CharacterConfigCommand> CharacterCommandRequested;
        /// <summary>列表空白区域新建角色事件。</summary>
        public event Action NewCharacterRequested;
        /// <summary>侧面头像变化事件。</summary>
        public event Action<CharacterConfig, Sprite> SideIconChanged;
        /// <summary>角色头像变化事件。</summary>
        public event Action<CharacterConfig, Sprite> AvatarChanged;
        /// <summary>角色配置序列化字段变化事件。</summary>
        internal event Action<CharacterConfig, string> PropertiesChanged;
        /// <summary>新建按钮事件。</summary>
        public event Action CreateRequested;
        /// <summary>复制按钮事件。</summary>
        public event Action DuplicateRequested;
        /// <summary>移出数据库按钮事件。</summary>
        public event Action RemoveRequested;
        /// <summary>删除资产按钮事件。</summary>
        public event Action DeleteRequested;
        /// <summary>验证当前按钮事件。</summary>
        public event Action ValidateCurrentRequested;
        /// <summary>验证数据库按钮事件。</summary>
        public event Action ValidateDatabaseRequested;
        /// <summary>定位资产按钮事件。</summary>
        public event Action PingRequested;
        #endregion

        #region 生命周期
        /// <summary>从窗口根节点取得固定控件并创建列表、详情子 View。</summary>
        /// <param name="root">已克隆的窗口 VisualTree。</param>
        public CharacterConfigEditorView(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            // 角色窗口与物品窗口共用 WSFrame 的 CustomTwoPanelSplitView；必须在构造阶段配置固定 Pane，
            // 这样自定义控件才能把拖拽线锚点同步到左侧面板真实边界，并恢复本次 Editor 会话的宽度。
            CustomTwoPanelSplitView splitView = Require<CustomTwoPanelSplitView>(root, "CharacterConfigSplitView");
            splitView.ConfigureFixedPane(320f, 360f, 360f, "RPG.CharacterConfigEditor.SidebarWidth");
            databaseField = Require<ObjectField>(root, "DatabaseField");
            statusLabel = Require<Label>(root, "StatusLabel");
            createButton = Require<Button>(root, "CreateButton");
            duplicateButton = Require<Button>(root, "DuplicateButton");
            removeButton = Require<Button>(root, "RemoveButton");
            deleteButton = Require<Button>(root, "DeleteButton");
            validateCurrentButton = Require<Button>(root, "ValidateCurrentButton");
            validateDatabaseButton = Require<Button>(root, "ValidateDatabaseButton");
            pingButton = Require<Button>(root, "PingButton");
            databaseField.objectType = typeof(CharacterDatabase);
            databaseField.RegisterValueChangedCallback(OnDatabaseChanged);
            listView = new CharacterConfigListView(root);
            detailsView = new CharacterConfigDetailsView(root);
            listView.SearchChanged += OnSearchChanged;
            listView.CharacterSelected += OnCharacterSelected;
            listView.CharacterCommandRequested += OnCharacterCommandRequested;
            listView.NewCharacterRequested += OnNewCharacterRequested;
            detailsView.SideIconChanged += OnSideIconChanged;
            detailsView.AvatarChanged += OnAvatarChanged;
            detailsView.PropertiesChanged += OnPropertiesChanged;
            createButton.clicked += OnCreateClicked;
            duplicateButton.clicked += OnDuplicateClicked;
            removeButton.clicked += OnRemoveClicked;
            deleteButton.clicked += OnDeleteClicked;
            validateCurrentButton.clicked += OnValidateCurrentClicked;
            validateDatabaseButton.clicked += OnValidateDatabaseClicked;
            pingButton.clicked += OnPingClicked;
            SetButtonsEnabled(false, false);
        }

        /// <summary>解除所有回调并释放列表和详情子 View。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            databaseField.UnregisterValueChangedCallback(OnDatabaseChanged);
            listView.SearchChanged -= OnSearchChanged;
            listView.CharacterSelected -= OnCharacterSelected;
            listView.CharacterCommandRequested -= OnCharacterCommandRequested;
            listView.NewCharacterRequested -= OnNewCharacterRequested;
            detailsView.SideIconChanged -= OnSideIconChanged;
            detailsView.AvatarChanged -= OnAvatarChanged;
            detailsView.PropertiesChanged -= OnPropertiesChanged;
            createButton.clicked -= OnCreateClicked;
            duplicateButton.clicked -= OnDuplicateClicked;
            removeButton.clicked -= OnRemoveClicked;
            deleteButton.clicked -= OnDeleteClicked;
            validateCurrentButton.clicked -= OnValidateCurrentClicked;
            validateDatabaseButton.clicked -= OnValidateDatabaseClicked;
            pingButton.clicked -= OnPingClicked;
            listView.Dispose();
            detailsView.Dispose();
        }
        #endregion

        #region 数据刷新
        /// <summary>绑定数据库、按搜索词筛选角色并恢复选中项。</summary>
        /// <param name="database">目标数据库。</param>
        /// <param name="search">搜索文本。</param>
        public void SetDatabase(CharacterDatabase database, string search)
        {
            suppressCallbacks = true;
            try
            {
                databaseField.SetValueWithoutNotify(database);
                databaseAvailable = database != null;
                listView.SetSearch(search);
                var filtered = new List<CharacterConfig>();
                string normalizedSearch = search?.Trim() ?? string.Empty;
                if (database != null)
                {
                    for (int index = 0; index < database.Characters.Count; index++)
                    {
                        CharacterConfig config = database.Characters[index];
                        if (config == null) continue;
                        if (normalizedSearch.Length > 0 &&
                            (config.Name ?? string.Empty).IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                            config.CharacterId.ToString().IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                            (config.PrefabAddress ?? string.Empty).IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        filtered.Add(config);
                    }
                }
                listView.RenderCharacters(filtered, detailsView.SelectedConfig);
                SetButtonsEnabled(databaseAvailable, detailsView.SelectedConfig != null);
            }
            finally { suppressCallbacks = false; }
        }

        /// <summary>绑定右侧详情和摘要。</summary>
        /// <param name="config">当前角色配置，可为空。</param>
        public void SetSelectedConfig(CharacterConfig config)
        {
            detailsView.Bind(config);
            SetButtonsEnabled(databaseAvailable, config != null);
        }

        /// <summary>刷新当前角色详情、摘要和列表行。</summary>
        public void RefreshSelectedConfig()
        {
            detailsView.Refresh();
            listView.RefreshItems();
        }

        /// <summary>恢复当前详情中的头像预览字段。</summary>
        /// <param name="sideIcon">是否恢复侧面头像。</param>
        public void RestorePreview(bool sideIcon) => detailsView.RestorePreview(sideIcon);

        /// <summary>显示状态或错误消息。</summary>
        /// <param name="message">状态文本。</param>
        public void SetStatus(string message) => statusLabel.text = message ?? string.Empty;
        #endregion

        #region 事件处理
        /// <summary>转发数据库字段变化。</summary>
        private void OnDatabaseChanged(ChangeEvent<UnityEngine.Object> eventData) { if (!suppressCallbacks) DatabaseChanged?.Invoke(eventData.newValue as CharacterDatabase); }
        /// <summary>转发列表搜索变化。</summary>
        private void OnSearchChanged(string search) { if (!suppressCallbacks) SearchChanged?.Invoke(search); }
        /// <summary>转发列表角色选择。</summary>
        private void OnCharacterSelected(CharacterConfig config) { if (!suppressCallbacks) CharacterSelected?.Invoke(config); }
        /// <summary>转发列表右键角色操作。</summary>
        private void OnCharacterCommandRequested(CharacterConfig config, CharacterConfigCommand command) { if (!suppressCallbacks) CharacterCommandRequested?.Invoke(config, command); }
        /// <summary>转发列表空白区域新建角色。</summary>
        private void OnNewCharacterRequested() { if (!suppressCallbacks) NewCharacterRequested?.Invoke(); }
        /// <summary>转发侧面头像变化。</summary>
        private void OnSideIconChanged(CharacterConfig config, Sprite sprite) { if (!suppressCallbacks) SideIconChanged?.Invoke(config, sprite); }
        /// <summary>转发角色头像变化。</summary>
        private void OnAvatarChanged(CharacterConfig config, Sprite sprite) { if (!suppressCallbacks) AvatarChanged?.Invoke(config, sprite); }
        /// <summary>转发角色序列化字段变化。</summary>
        private void OnPropertiesChanged(CharacterConfig config, string propertyPath) { if (!suppressCallbacks) PropertiesChanged?.Invoke(config, propertyPath); }
        /// <summary>转发新建按钮。</summary>
        private void OnCreateClicked() => CreateRequested?.Invoke();
        /// <summary>转发复制按钮。</summary>
        private void OnDuplicateClicked() => DuplicateRequested?.Invoke();
        /// <summary>转发移出按钮。</summary>
        private void OnRemoveClicked() => RemoveRequested?.Invoke();
        /// <summary>转发删除按钮。</summary>
        private void OnDeleteClicked() => DeleteRequested?.Invoke();
        /// <summary>转发当前验证按钮。</summary>
        private void OnValidateCurrentClicked() => ValidateCurrentRequested?.Invoke();
        /// <summary>转发数据库验证按钮。</summary>
        private void OnValidateDatabaseClicked() => ValidateDatabaseRequested?.Invoke();
        /// <summary>转发定位按钮。</summary>
        private void OnPingClicked() => PingRequested?.Invoke();
        #endregion

        #region 内部辅助
        /// <summary>根据当前选择启用窗口操作按钮。</summary>
        /// <param name="enabled">是否启用。</param>
        private void SetButtonsEnabled(bool hasDatabase, bool hasSelection)
        {
            createButton.SetEnabled(hasDatabase);
            duplicateButton.SetEnabled(hasSelection);
            removeButton.SetEnabled(hasSelection);
            deleteButton.SetEnabled(hasSelection);
            validateCurrentButton.SetEnabled(hasSelection);
            validateDatabaseButton.SetEnabled(hasDatabase);
            pingButton.SetEnabled(hasSelection);
        }

        /// <summary>从窗口根节点取得指定控件。</summary>
        /// <typeparam name="T">控件类型。</typeparam>
        /// <param name="root">根节点。</param>
        /// <param name="name">控件名称。</param>
        /// <returns>目标控件。</returns>
        private static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null) throw new InvalidOperationException($"角色配置窗口缺少控件：{name}。");
            return element;
        }
        #endregion
    }
}
#endif
