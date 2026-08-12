#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>协调 GA 资产查询、Session、详情绑定、校验与 Editor 资产命令。</summary>
    public sealed class GameplayAbilityEditorController : IDisposable
    {
        #region 字段
        private readonly IGameplayAbilityEditorView view;
        private readonly GameplayAbilityEditorService service;
        private readonly List<GameplayAbilityData> allAbilities = new();
        private readonly List<GameplayAbilityData> filteredAbilities = new();
        private readonly Dictionary<GameplayAbilityData, GameplayAbilityValidationSeverity>
            validationStates = new();
        private GameplayAbilityDatabase database;
        private GameplayAbilityData currentAbility;
        private string search;
        private bool disposed;
        #endregion

        #region 生命周期
        /// <summary>连接 View 与 Service，并恢复 Session 中的资产和搜索状态。</summary>
        public GameplayAbilityEditorController(IGameplayAbilityEditorView view)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            service = new GameplayAbilityEditorService();
            search = GameplayAbilityEditorSession.Search;
            database = service.ResolveDatabase();
            Subscribe();
            view.SetCreatableAbilityTypes(service.FindCreatableAbilityTypes());
            view.SetDatabase(database);
            view.SetSearch(search);
            RefreshAssets(GameplayAbilityEditorSession.GetAbility());
        }

        /// <summary>解除全部 View、Undo 和项目变化回调。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Unsubscribe();
        }
        #endregion

        #region 公开操作
        /// <summary>切换当前 GA，并按需恢复 Session 资产。</summary>
        public void SetAbility(GameplayAbilityData ability, bool restoreSelection)
        {
            GameplayAbilityData target = ability;
            if (target == null && restoreSelection)
                target = GameplayAbilityEditorSession.GetAbility();
            if (target != null && !allAbilities.Contains(target))
                RefreshAssetCache();
            SelectAbility(target);
            RenderList();
        }

        /// <summary>切换当前 Ability Database，并立即刷新 Bake 状态与 Session。</summary>
        /// <param name="value">同时保存稳定 ID 历史和运行时索引的 Database。</param>
        public void SetDatabase(GameplayAbilityDatabase value)
        {
            database = value;
            GameplayAbilityEditorSession.SetDatabase(value);
            view.SetDatabase(value);
            RefreshBakeStatus();
        }
        #endregion

        #region 事件订阅
        /// <summary>在同一页面生命周期内对称连接 View、Undo 与 Project 回调。</summary>
        private void Subscribe()
        {
            view.DatabaseChanged += OnDatabaseChanged;
            view.BakeRequested += OnBakeRequested;
            view.SearchChanged += OnSearchChanged;
            view.AbilitySelected += OnAbilitySelected;
            view.CreateRequested += OnCreateRequested;
            view.DuplicateRequested += OnDuplicateRequested;
            view.DeleteRequested += OnDeleteRequested;
            view.PingRequested += service.PingAbility;
            view.RenameSubmitted += OnRenameSubmitted;
            view.AbilityChanged += OnAbilityChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        /// <summary>页面释放前解除外部事件，防止旧 Controller 继续刷新。</summary>
        private void Unsubscribe()
        {
            view.DatabaseChanged -= OnDatabaseChanged;
            view.BakeRequested -= OnBakeRequested;
            view.SearchChanged -= OnSearchChanged;
            view.AbilitySelected -= OnAbilitySelected;
            view.CreateRequested -= OnCreateRequested;
            view.DuplicateRequested -= OnDuplicateRequested;
            view.DeleteRequested -= OnDeleteRequested;
            view.PingRequested -= service.PingAbility;
            view.RenameSubmitted -= OnRenameSubmitted;
            view.AbilityChanged -= OnAbilityChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.projectChanged -= OnProjectChanged;
        }
        #endregion

        #region 事件处理
        /// <summary>处理 Database ObjectField 切换意图。</summary>
        /// <param name="value">用户选择的 Database。</param>
        private void OnDatabaseChanged(GameplayAbilityDatabase value) => SetDatabase(value);

        /// <summary>执行当前 Database 的 Bake，并在成功后重新绑定 ID 显示。</summary>
        private void OnBakeRequested()
        {
            List<string> errors = service.ValidateForBake(database);
            if (errors.Count > 0)
            {
                view.ShowError(string.Join("\n", errors));
                RefreshBakeStatus();
                return;
            }

            string summary = service.Bake(database);
            view.RenderBakeStatus(summary, false);
            RefreshAssets(currentAbility);
        }

        // 搜索即时更新过滤列表并持久化到 SessionState。
        private void OnSearchChanged(string value)
        {
            search = value ?? string.Empty;
            GameplayAbilityEditorSession.Search = search;
            RenderList();
        }

        // 选择变化统一更新 Session、详情绑定与校验。
        private void OnAbilitySelected(GameplayAbilityData ability) => SelectAbility(ability);

        /// <summary>创建新 GA 资产，标记 Bake Dirty 并切换到新资产。</summary>
        /// <param name="abilityType">用户选择的具体 Ability Data 类型。</param>
        private void OnCreateRequested(Type abilityType)
        {
            GameplayAbilityData created = service.CreateAbility(abilityType);
            if (created == null) return;
            service.MarkBakeDirty(database);
            RefreshAssets(created);
        }

        /// <summary>复制当前 GA 资产，标记 Bake Dirty 并恢复对新资产的选择。</summary>
        private void OnDuplicateRequested()
        {
            GameplayAbilityData duplicate = service.DuplicateAbility(currentAbility);
            if (duplicate == null) return;
            service.MarkBakeDirty(database);
            RefreshAssets(duplicate);
        }

        /// <summary>将当前 GA 移入回收站，标记 Bake Dirty 并选择邻近资产。</summary>
        private void OnDeleteRequested()
        {
            if (currentAbility == null || !view.ConfirmDelete(currentAbility)) return;
            int oldIndex = filteredAbilities.IndexOf(currentAbility);
            if (!service.DeleteAbility(currentAbility))
            {
                view.ShowError("Unable to move the Gameplay Ability asset to the recycle bin.");
                return;
            }

            service.MarkBakeDirty(database);

            RefreshAssetCache();
            ApplyFilter();
            GameplayAbilityData next = filteredAbilities.Count == 0
                ? null
                : filteredAbilities[Math.Min(Math.Max(oldIndex, 0), filteredAbilities.Count - 1)];
            SelectAbility(next);
            view.RenderAbilities(filteredAbilities, currentAbility);
        }

        // 重命名失败时恢复输入；成功时保持对象身份并重新排序。
        private void OnRenameSubmitted(GameplayAbilityRenameRequest request)
        {
            if (!service.TryRenameAbility(request.Ability, request.Name, out string error))
            {
                view.ShowError(error);
                view.RestoreRename(request.Ability, request.Name);
                return;
            }

            RefreshAssets(request.Ability);
        }

        // 原生 SerializedObject 写回后重新校验当前资产。
        private void OnAbilityChanged() => RenderValidation();

        // Undo/Redo 后重新绑定，避免继续使用旧 SerializedProperty。
        private void OnUndoRedo() => RefreshAssets(currentAbility);

        /// <summary>项目资产变化后重建列表、可创建类型和 Bake 状态。</summary>
        private void OnProjectChanged()
        {
            if (database == null) SetDatabase(service.ResolveDatabase());
            service.MarkBakeDirtyIfAssetSetChanged(database);
            view.SetCreatableAbilityTypes(service.FindCreatableAbilityTypes());
            RefreshAssets(currentAbility);
        }
        #endregion

        #region 状态刷新
        /// <summary>扫描、过滤、恢复目标选择，并刷新资产列表与 Bake 状态。</summary>
        /// <param name="preferred">刷新后优先恢复的 GA 资产。</param>
        private void RefreshAssets(GameplayAbilityData preferred)
        {
            RefreshAssetCache();
            ApplyFilter();
            GameplayAbilityData target = preferred != null && allAbilities.Contains(preferred)
                ? preferred
                : GameplayAbilityEditorSession.GetAbility();
            if (target != null && !allAbilities.Contains(target)) target = null;
            SelectAbility(target);
            view.RenderAbilities(filteredAbilities, currentAbility);
            RefreshBakeStatus();
        }

        // Service 返回真实 Model 引用，Controller 不创建资产 ViewData。
        private void RefreshAssetCache()
        {
            allAbilities.Clear();
            allAbilities.AddRange(service.FindAllAbilities());
            RefreshAllValidationStates();
        }

        // 搜索只匹配资产名与路径，并保持 Service 排序。
        private void ApplyFilter()
        {
            filteredAbilities.Clear();
            string query = search?.Trim();
            for (int i = 0; i < allAbilities.Count; i++)
            {
                GameplayAbilityData ability = allAbilities[i];
                if (string.IsNullOrEmpty(query) ||
                    ability.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    AssetDatabase.GetAssetPath(ability).IndexOf(
                        query, StringComparison.OrdinalIgnoreCase) >= 0)
                    filteredAbilities.Add(ability);
            }
        }

        // 只刷新列表内容和合法选择，不重建 ListView。
        private void RenderList()
        {
            ApplyFilter();
            view.RenderAbilities(filteredAbilities, currentAbility);
        }

        // 当前对象是唯一详情绑定来源。
        private void SelectAbility(GameplayAbilityData ability)
        {
            currentAbility = ability;
            GameplayAbilityEditorSession.SetAbility(ability);
            view.BindAbility(ability);
            RenderValidation();
        }

        // 当前校验同时更新详情与列表背景缓存。
        private void RenderValidation()
        {
            List<GameplayAbilityValidationIssue> issues = service.Validate(currentAbility);
            view.RenderValidation(issues);
            if (currentAbility != null)
            {
                if (ContainsError(issues))
                    validationStates[currentAbility] = GameplayAbilityValidationSeverity.Error;
                else
                    validationStates.Remove(currentAbility);
            }

            view.RenderAbilityValidationStates(validationStates);
        }

        /// <summary>根据 Data、稳定 ID 历史与 Database 运行时索引的一致性渲染 Bake 摘要。</summary>
        private void RefreshBakeStatus()
        {
            List<string> errors = service.ValidateBakeState(database);
            if (errors.Count > 0)
            {
                view.RenderBakeStatus(errors[0], true);
                return;
            }

            int count = database?.Count ?? 0;
            view.RenderBakeStatus($"Baked: {count}", false);
        }

        // 全量资产刷新时重建列表着色缓存。
        private void RefreshAllValidationStates()
        {
            validationStates.Clear();
            for (int i = 0; i < allAbilities.Count; i++)
            {
                GameplayAbilityData ability = allAbilities[i];
                if (ContainsError(service.Validate(ability)))
                    validationStates.Add(ability, GameplayAbilityValidationSeverity.Error);
            }
        }

        // Info 不改变列表背景，仅 Error 进入缓存。
        private static bool ContainsError(IReadOnlyList<GameplayAbilityValidationIssue> issues)
        {
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].Severity == GameplayAbilityValidationSeverity.Error)
                    return true;
            return false;
        }
        #endregion
    }
}
#endif
