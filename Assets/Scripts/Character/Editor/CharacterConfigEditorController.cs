#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace RPG.Character.Editor
{
    /// <summary>编排角色配置窗口的数据库选择、列表过滤、编辑提交和 Undo 刷新。</summary>
    public sealed class CharacterConfigEditorController : IDisposable
    {
        #region 依赖与状态

        private readonly CharacterConfigEditorService service = new();
        private CharacterConfigEditorView view;
        private CharacterDatabase database;
        private CharacterConfig selectedConfig;
        private bool disposed;
        private bool undoRefreshScheduled;

        #endregion

        #region 生命周期

        /// <summary>绑定 View、订阅事件并恢复数据库与角色选择。</summary>
        /// <param name="targetView">窗口 View。</param>
        /// <param name="initialDatabase">显式数据库，可为空。</param>
        /// <param name="initialConfig">显式配置，可为空。</param>
        public void Bind(CharacterConfigEditorView targetView, CharacterDatabase initialDatabase, CharacterConfig initialConfig)
        {
            view = targetView ?? throw new ArgumentNullException(nameof(targetView));
            view.DatabaseChanged += OnDatabaseChanged;
            view.SearchChanged += OnSearchChanged;
            view.CharacterSelected += OnCharacterSelected;
            view.CharacterCommandRequested += OnCharacterCommandRequested;
            view.NewCharacterRequested += OnNewCharacterRequested;
            view.SideIconChanged += OnSideIconChanged;
            view.AvatarChanged += OnAvatarChanged;
            view.PropertiesChanged += OnPropertiesChanged;
            view.CreateRequested += OnCreateRequested;
            view.DuplicateRequested += OnDuplicateRequested;
            view.RemoveRequested += OnRemoveRequested;
            view.DeleteRequested += OnDeleteRequested;
            view.ValidateCurrentRequested += OnValidateCurrentRequested;
            view.ValidateDatabaseRequested += OnValidateDatabaseRequested;
            view.PingRequested += OnPingRequested;
            Undo.undoRedoEvent += OnUndoRedo;

            CharacterDatabase resolvedDatabase = initialDatabase ?? service.ResolveDatabaseForConfig(initialConfig) ?? CharacterConfigEditorSession.ResolveDatabase();
            SetDatabase(resolvedDatabase, false);
            CharacterConfig restoredConfig = initialConfig ?? CharacterConfigEditorSession.ResolveConfig();
            if (restoredConfig != null && (resolvedDatabase == null || ContainsConfig(resolvedDatabase, restoredConfig)))
                SelectConfig(restoredConfig);
        }

        /// <summary>释放 View 事件、Undo 监听和延迟刷新状态。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Undo.undoRedoEvent -= OnUndoRedo;
            EditorApplication.delayCall -= ExecuteUndoRefresh;
            if (view != null)
            {
                view.DatabaseChanged -= OnDatabaseChanged;
                view.SearchChanged -= OnSearchChanged;
                view.CharacterSelected -= OnCharacterSelected;
                view.CharacterCommandRequested -= OnCharacterCommandRequested;
                view.NewCharacterRequested -= OnNewCharacterRequested;
                view.SideIconChanged -= OnSideIconChanged;
                view.AvatarChanged -= OnAvatarChanged;
                view.PropertiesChanged -= OnPropertiesChanged;
                view.CreateRequested -= OnCreateRequested;
                view.DuplicateRequested -= OnDuplicateRequested;
                view.RemoveRequested -= OnRemoveRequested;
                view.DeleteRequested -= OnDeleteRequested;
                view.ValidateCurrentRequested -= OnValidateCurrentRequested;
                view.ValidateDatabaseRequested -= OnValidateDatabaseRequested;
                view.PingRequested -= OnPingRequested;
            }
            view?.Dispose();
            view = null;
        }

        #endregion

        #region 外部打开与数据库

        /// <summary>切换窗口目标数据库和可选配置。</summary>
        /// <param name="targetDatabase">目标数据库。</param>
        /// <param name="targetConfig">目标角色配置。</param>
        public void Open(CharacterDatabase targetDatabase, CharacterConfig targetConfig)
        {
            SetDatabase(targetDatabase ?? service.ResolveDatabaseForConfig(targetConfig) ?? CharacterConfigEditorSession.ResolveDatabase(), false);
            if (targetConfig != null) SelectConfig(targetConfig);
        }

        /// <summary>设置当前数据库并重建角色列表。</summary>
        /// <param name="targetDatabase">目标数据库。</param>
        /// <param name="writeSession">是否写入会话路径。</param>
        private void SetDatabase(CharacterDatabase targetDatabase, bool writeSession = true)
        {
            // 切换数据库前显式清空详情，避免 View 保留旧配置而 Controller 已经没有对应选择。
            view?.SetSelectedConfig(null);
            database = targetDatabase;
            selectedConfig = null;
            if (writeSession) CharacterConfigEditorSession.SetDatabase(database);
            string search = CharacterConfigEditorSession.Search;
            view.SetDatabase(database, search);
            view.SetStatus(database == null ? "请选择 CharacterDatabase。" : $"当前数据库：{database.name}，角色数：{database.Characters.Count}");
        }

        /// <summary>选择列表中的角色配置。</summary>
        /// <param name="config">目标配置。</param>
        private void SelectConfig(CharacterConfig config)
        {
            selectedConfig = config;
            CharacterConfigEditorSession.SetConfig(config);
            view.SetSelectedConfig(config);
        }

        /// <summary>判断配置是否属于当前数据库，避免会话恢复时跨库误选。</summary>
        /// <param name="targetDatabase">当前数据库。</param>
        /// <param name="config">待判断配置。</param>
        /// <returns>属于当前数据库时返回 true。</returns>
        private static bool ContainsConfig(CharacterDatabase targetDatabase, CharacterConfig config)
        {
            for (int index = 0; index < targetDatabase.Characters.Count; index++)
                if (targetDatabase.Characters[index] == config) return true;
            return false;
        }

        #endregion

        #region 事件处理

        /// <summary>处理数据库 ObjectField 变更。</summary>
        private void OnDatabaseChanged(CharacterDatabase targetDatabase) => SetDatabase(targetDatabase);

        /// <summary>处理列表搜索变化。</summary>
        private void OnSearchChanged(string search)
        {
            CharacterConfigEditorSession.SetSearch(search);
            selectedConfig = null;
            view.SetSelectedConfig(null);
            view.SetDatabase(database, search);
        }

        /// <summary>处理列表选择。</summary>
        private void OnCharacterSelected(CharacterConfig config) => SelectConfig(config);

        /// <summary>处理详情原生绑定字段变化并即时刷新列表投影。</summary>
        /// <param name="config">发生变化的角色配置。</param>
        /// <param name="propertyPath">发生变化的序列化路径。</param>
        private void OnPropertiesChanged(CharacterConfig config, string propertyPath)
        {
            if (disposed || config == null || config != selectedConfig || database == null) return;
            // DetailsView 已经在 SerializedPropertyChangeEvent 中刷新摘要；这里仅刷新虚拟化列表，
            // 避免在绑定事件期间重新 Bind 当前详情，同时让 Name、Rarity 和计数元信息立即更新。
            view.SetDatabase(database, CharacterConfigEditorSession.Search);
        }

        /// <summary>处理列表右键菜单中的角色命令。</summary>
        /// <param name="config">右键打开时捕获的角色配置。</param>
        /// <param name="command">命令类型。</param>
        private void OnCharacterCommandRequested(CharacterConfig config, CharacterConfigCommand command)
        {
            if (config == null || database == null) return;
            selectedConfig = config;
            try
            {
                switch (command)
                {
                    case CharacterConfigCommand.Duplicate:
                        CharacterConfig copy = service.Duplicate(database, config);
                        SetDatabase(database);
                        SelectConfig(copy);
                        break;
                    case CharacterConfigCommand.Validate:
                        service.ValidateConfig(config);
                        view.SetStatus("当前 CharacterConfig 验证通过。");
                        break;
                    case CharacterConfigCommand.PingAsset:
                        EditorGUIUtility.PingObject(config);
                        Selection.activeObject = config;
                        break;
                    case CharacterConfigCommand.RemoveFromDatabase:
                        service.RemoveFromDatabase(database, config);
                        selectedConfig = null;
                        SetDatabase(database);
                        break;
                    case CharacterConfigCommand.DeleteAsset:
                        if (!EditorUtility.DisplayDialog("删除角色配置", $"确定删除“{config.Name}”吗？", "删除", "取消")) return;
                        service.Delete(database, config);
                        selectedConfig = null;
                        SetDatabase(database);
                        break;
                }
            }
            catch (Exception exception)
            {
                view.SetStatus($"角色操作失败：{exception.Message}");
            }
        }

        /// <summary>处理列表空白区域的新建角色请求。</summary>
        private void OnNewCharacterRequested() => OnCreateRequested();

        /// <summary>提交侧面头像预览。</summary>
        private void OnSideIconChanged(CharacterConfig config, Sprite sprite) => SetPreview(config, sprite, true);

        /// <summary>提交角色头像预览。</summary>
        private void OnAvatarChanged(CharacterConfig config, Sprite sprite) => SetPreview(config, sprite, false);

        /// <summary>调用 Service 同步 SpriteName，失败时恢复预览控件。</summary>
        private void SetPreview(CharacterConfig config, Sprite sprite, bool sideIcon)
        {
            if (config == null || config != selectedConfig) return;
            try
            {
                string message = service.SetPreviewSprite(config, sprite, sideIcon);
                view.RefreshSelectedConfig();
                view.SetStatus(message);
            }
            catch (Exception exception)
            {
                view.RestorePreview(sideIcon);
                view.SetStatus($"Sprite 设置失败：{exception.Message}");
            }
        }

        /// <summary>创建角色配置并加入数据库。</summary>
        private void OnCreateRequested()
        {
            if (database == null) { view.SetStatus("创建角色前必须选择 CharacterDatabase。"); return; }
            try
            {
                CharacterConfig created = service.Create(database);
                SetDatabase(database);
                SelectConfig(created);
            }
            catch (Exception exception) { view.SetStatus($"创建失败：{exception.Message}"); }
        }

        /// <summary>复制当前角色配置。</summary>
        private void OnDuplicateRequested()
        {
            if (database == null || selectedConfig == null) return;
            try
            {
                CharacterConfig copy = service.Duplicate(database, selectedConfig);
                SetDatabase(database);
                SelectConfig(copy);
            }
            catch (Exception exception) { view.SetStatus($"复制失败：{exception.Message}"); }
        }

        /// <summary>从数据库移除当前角色但保留资产。</summary>
        private void OnRemoveRequested()
        {
            if (database == null || selectedConfig == null) return;
            service.RemoveFromDatabase(database, selectedConfig);
            selectedConfig = null;
            SetDatabase(database);
        }

        /// <summary>删除当前角色资产。</summary>
        private void OnDeleteRequested()
        {
            if (database == null || selectedConfig == null) return;
            if (!EditorUtility.DisplayDialog("删除角色配置", $"确定删除“{selectedConfig.Name}”吗？", "删除", "取消")) return;
            service.Delete(database, selectedConfig);
            selectedConfig = null;
            SetDatabase(database);
        }

        /// <summary>验证当前角色配置。</summary>
        private void OnValidateCurrentRequested()
        {
            try { service.ValidateConfig(selectedConfig); view.SetStatus("当前 CharacterConfig 验证通过。"); }
            catch (Exception exception) { view.SetStatus($"当前配置验证失败：{exception.Message}"); }
        }

        /// <summary>验证整个角色数据库。</summary>
        private void OnValidateDatabaseRequested()
        {
            try { service.ValidateDatabase(database); view.SetStatus("CharacterDatabase 验证通过。"); }
            catch (Exception exception) { view.SetStatus($"数据库验证失败：{exception.Message}"); }
        }

        /// <summary>定位当前 CharacterConfig 资产。</summary>
        private void OnPingRequested()
        {
            if (selectedConfig != null) EditorGUIUtility.PingObject(selectedConfig);
        }

        /// <summary>Undo/Redo 后只安排一次 SerializedObject 和列表刷新。</summary>
        private void OnUndoRedo(in UndoRedoInfo _)
        {
            if (disposed || undoRefreshScheduled) return;
            undoRefreshScheduled = true;
            EditorApplication.delayCall += ExecuteUndoRefresh;
        }

        /// <summary>执行延迟 Undo/Redo 刷新，避免回调期间重复写入资产。</summary>
        private void ExecuteUndoRefresh()
        {
            undoRefreshScheduled = false;
            if (disposed || view == null) return;
            view.RefreshSelectedConfig();
            view.SetDatabase(database, CharacterConfigEditorSession.Search);
            if (selectedConfig != null) SelectConfig(selectedConfig);
        }

        #endregion
    }
}
#endif
