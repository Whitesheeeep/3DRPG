#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>协调 GE 资产选择、搜索、Modifier 命令、SessionState 与实时校验。</summary>
    public sealed class GameplayEffectEditorController : IDisposable
    {
        #region 字段

        private readonly IGameplayEffectEditorView view;
        private readonly GameplayEffectEditorService service;
        private readonly List<GameplayEffectData> allEffects = new();
        private readonly List<Type> modifierTypes;
        private readonly Dictionary<GameplayEffectData, GameplayEffectValidationSeverity>
            effectValidationStates = new();

        private GameplayEffectData effect;
        private GameplayEffectData pendingModifierMoveEffect;
        private GameplayEffectData pendingModifierStructureEffect;
        private GameplayAttributeRegistry attributeRegistry;
        private GameplayEffectModifierMoveRequest pendingModifierMove;
        private Type pendingModifierAddType;
        private string search;
        private int selectedModifierIndex;
        private int pendingModifierRemoveIndex = -1;
        private ModifierStructureOperation pendingModifierStructureOperation;
        private bool modifierMoveScheduled;
        private bool modifierStructureApplyScheduled;
        private bool modifierStructureRebindScheduled;
        private bool pendingModifierStructureChanged;
        private ValidationRequestScope scheduledValidationScope;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>创建 Controller、建立对称事件连接并恢复 SessionState。</summary>
        /// <param name="view">GE Editor View 抽象。</param>
        /// <exception cref="ArgumentNullException">view 为 null。</exception>
        public GameplayEffectEditorController(IGameplayEffectEditorView view)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            service = new GameplayEffectEditorService();
            modifierTypes = service.FindModifierTypes();
            search = GameplayEffectEditorSession.Search;
            selectedModifierIndex = GameplayEffectEditorSession.SelectedModifierIndex;
            RegisterEvents();
            RefreshAssets();
            SetEffect(GameplayEffectEditorSession.GetEffect(), true);
            ScheduleValidation(ValidationRequestScope.All);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CancelScheduledModifierMove();
            CancelScheduledModifierStructureChange();
            CancelScheduledValidation();
            UnregisterEvents();
        }

        #endregion

        #region 公开操作

        /// <summary>切换当前 GE，重建原生绑定并保存 Asset GUID。</summary>
        /// <param name="target">目标 GE；null 表示清空。</param>
        /// <param name="restoreSelection">是否使用 SessionState 中的 Modifier 索引。</param>
        public void SetEffect(GameplayEffectData target, bool restoreSelection)
        {
            if (disposed) return;
            CancelScheduledModifierMove();
            CancelScheduledModifierStructureChange();
            effect = target;
            GameplayEffectEditorSession.SetEffect(effect);
            selectedModifierIndex = restoreSelection
                ? GameplayEffectEditorSession.SelectedModifierIndex
                : -1;
            NormalizeModifierSelection();
            RefreshAll();
        }

        #endregion

        #region 事件处理

        // 搜索只影响左侧资产投影，不改变当前 Model 选择。
        private void OnSearchChanged(string value)
        {
            search = value ?? string.Empty;
            GameplayEffectEditorSession.Search = search;
            RefreshEffectList();
        }

        // 资产列表已完成视觉选择，只切换详情，避免再次刷新整个 Effect 列表。
        private void OnEffectSelectionChanged(GameplayEffectData selected)
        {
            CancelScheduledModifierMove();
            CancelScheduledModifierStructureChange();
            effect = selected;
            GameplayEffectEditorSession.SetEffect(effect);
            selectedModifierIndex = -1;
            NormalizeModifierSelection();
            RefreshEffectDetails();
        }

        // 资产重命名成功后重排列表并重绑标题；失败时恢复原行输入和焦点。
        private void OnRenameEffectSubmitted(GameplayEffectRenameRequest request)
        {
            if (!service.TryRenameEffect(request.Effect, request.Name, out string error))
            {
                view.ShowError("Rename Gameplay Effect", error);
                view.RestoreEffectRename(request.Effect, request.Name);
                return;
            }

            effect = request.Effect;
            GameplayEffectEditorSession.SetEffect(effect);
            NormalizeModifierSelection();
            RefreshAssets();
            RefreshAll();
        }

        // View 已完成路径对话，Service 只负责创建资产。
        private void OnCreateEffectRequested(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            GameplayEffectData created = service.CreateEffect(path, out string error);
            if (created == null)
            {
                view.ShowError("Create Gameplay Effect", error);
                return;
            }

            RefreshAssets();
            SetEffect(created, false);
        }

        // 副本创建成功后立即成为当前 GE。
        private void OnDuplicateEffectRequested()
        {
            GameplayEffectData copy = service.DuplicateEffect(effect, out string error);
            if (copy == null)
            {
                view.ShowError("Duplicate Gameplay Effect", error);
                return;
            }

            RefreshAssets();
            SetEffect(copy, false);
        }

        // 删除资产使用可恢复的系统回收站，但不冒充 Unity Undo。
        private void OnDeleteEffectRequested()
        {
            if (effect == null || !view.Confirm(
                    "Delete Gameplay Effect",
                    $"将 '{effect.name}' 移入系统回收站？"))
                return;

            CancelScheduledModifierMove();
            CancelScheduledModifierStructureChange();
            if (!service.MoveEffectToTrash(effect, out string error))
            {
                view.ShowError("Delete Gameplay Effect", error);
                return;
            }

            effectValidationStates.Remove(effect);
            view.RenderEffectValidationStates(effectValidationStates);
            effect = null;
            GameplayEffectEditorSession.SetEffect(null);
            selectedModifierIndex = -1;
            GameplayEffectEditorSession.SelectedModifierIndex = -1;
            RefreshAssets();
            RefreshAll();
        }

        // SerializedObject 绑定已完成写回，Controller 只刷新派生显隐和校验。
        private void OnEffectSerializedChanged()
        {
            if (effect == null) return;
            view.RefreshPolicyVisibility(effect);
            ScheduleValidation(ValidationRequestScope.Current);
        }

        // 选择索引是纯 UI 状态；结构事务期间只记录选择，不创建持有旧属性的详情控件。
        private void OnModifierSelectionChanged(int index)
        {
            selectedModifierIndex = index;
            NormalizeModifierSelection();
            if (!modifierMoveScheduled && !IsModifierStructureChangePending)
                view.BindModifier(selectedModifierIndex);
        }

        // 添加请求先销毁旧详情，下一次 Editor 更新才扩容 SerializeReference 数组。
        private void OnAddModifierRequested(Type type)
        {
            if (disposed || modifierMoveScheduled || IsModifierStructureChangePending) return;
            view.BindModifier(-1);
            pendingModifierStructureEffect = effect;
            pendingModifierAddType = type;
            pendingModifierRemoveIndex = -1;
            pendingModifierStructureOperation = ModifierStructureOperation.Add;
            modifierStructureApplyScheduled = true;
            EditorApplication.delayCall += ApplyScheduledModifierStructureChange;
        }

        // 删除请求先销毁旧详情，下一次 Editor 更新才缩减 SerializeReference 数组。
        private void OnRemoveModifierRequested()
        {
            if (disposed || modifierMoveScheduled || IsModifierStructureChangePending) return;
            view.BindModifier(-1);
            pendingModifierStructureEffect = effect;
            pendingModifierAddType = null;
            pendingModifierRemoveIndex = selectedModifierIndex;
            pendingModifierStructureOperation = ModifierStructureOperation.Remove;
            modifierStructureApplyScheduled = true;
            EditorApplication.delayCall += ApplyScheduledModifierStructureChange;
        }

        // 第一阶段在旧 PropertyField 离开当前 IMGUI 事件后修改数组，但不创建新详情控件。
        private void ApplyScheduledModifierStructureChange()
        {
            modifierStructureApplyScheduled = false;
            GameplayEffectData target = pendingModifierStructureEffect;
            if (disposed || effect != target)
            {
                ResetPendingModifierStructureChange();
                return;
            }

            bool changed;
            if (pendingModifierStructureOperation == ModifierStructureOperation.Add)
            {
                changed = service.TryAddModifier(
                    target,
                    pendingModifierAddType,
                    out int newIndex,
                    out string error);
                if (changed)
                {
                    selectedModifierIndex = newIndex;
                    GameplayEffectEditorSession.SelectedModifierIndex = newIndex;
                }
                else
                {
                    view.ShowError("Add Gameplay Effect Modifier", error);
                }
            }
            else
            {
                changed = service.RemoveModifier(target, pendingModifierRemoveIndex);
                if (changed) NormalizeModifierSelection();
            }

            pendingModifierStructureChanged = changed;
            RefreshModifiers(false);
            modifierStructureRebindScheduled = true;
            EditorApplication.delayCall += RestoreModifierBindingAfterStructureChange;
        }

        // 第二阶段重新创建 PropertyField，确保它只持有数组修改后的新 SerializedProperty。
        private void RestoreModifierBindingAfterStructureChange()
        {
            modifierStructureRebindScheduled = false;
            GameplayEffectData target = pendingModifierStructureEffect;
            bool changed = pendingModifierStructureChanged;
            if (!disposed && effect == target)
            {
                view.BindModifier(selectedModifierIndex);
                if (changed) ScheduleValidation(ValidationRequestScope.Current);
            }

            ResetPendingModifierStructureChange();
        }

        // 页面或数据上下文变化时撤销两个阶段的回调，禁止延迟修改已离开的资产。
        private void CancelScheduledModifierStructureChange()
        {
            if (modifierStructureApplyScheduled)
                EditorApplication.delayCall -= ApplyScheduledModifierStructureChange;
            if (modifierStructureRebindScheduled)
                EditorApplication.delayCall -= RestoreModifierBindingAfterStructureChange;
            ResetPendingModifierStructureChange();
        }

        // 清空一次结构操作携带的目标与参数，供成功、取消及生命周期结束共用。
        private void ResetPendingModifierStructureChange()
        {
            pendingModifierStructureEffect = null;
            pendingModifierAddType = null;
            pendingModifierRemoveIndex = -1;
            pendingModifierStructureOperation = ModifierStructureOperation.None;
            modifierStructureApplyScheduled = false;
            modifierStructureRebindScheduled = false;
            pendingModifierStructureChanged = false;
        }

        // Drop 时先销毁旧详情；数组移动延迟到当前 Panel/IMGUI 事件完全结束后执行。
        private void OnMoveModifierRequested(GameplayEffectModifierMoveRequest request)
        {
            if (IsModifierStructureChangePending) return;
            view.BindModifier(-1);
            pendingModifierMoveEffect = effect;
            pendingModifierMove = request;
            if (modifierMoveScheduled) return;
            modifierMoveScheduled = true;
            EditorApplication.delayCall += ApplyScheduledModifierMove;
        }

        // 在下一次 Editor 更新中移动数组，再以新索引创建全新的 PropertyField 与 IMGUIContainer。
        private void ApplyScheduledModifierMove()
        {
            modifierMoveScheduled = false;
            GameplayEffectData moveEffect = pendingModifierMoveEffect;
            GameplayEffectModifierMoveRequest request = pendingModifierMove;
            pendingModifierMoveEffect = null;
            if (disposed || effect != moveEffect) return;

            if (!service.MoveModifier(moveEffect, request))
            {
                RefreshModifiers(true);
                return;
            }

            selectedModifierIndex = request.ToIndex;
            GameplayEffectEditorSession.SelectedModifierIndex = selectedModifierIndex;
            RefreshModifiers(true);
            ScheduleValidation(ValidationRequestScope.Current);
        }

        // 页面释放或切换 GE 时取消尚未提交的拖放，防止修改已经离开的资产。
        private void CancelScheduledModifierMove()
        {
            if (!modifierMoveScheduled) return;
            EditorApplication.delayCall -= ApplyScheduledModifierMove;
            modifierMoveScheduled = false;
            pendingModifierMoveEffect = null;
        }

        // 手动校验立即刷新当前 GE；已排队的全量校验仍保留，用于更新其他资产行。
        private void OnValidateRequested()
        {
            if (scheduledValidationScope == ValidationRequestScope.Current)
                CancelScheduledValidation();
            RefreshCurrentValidation();
        }

        // 项目资产变化后重新扫描；当前 GUID 丢失时清空详情。
        private void OnProjectChanged()
        {
            CancelScheduledModifierMove();
            CancelScheduledModifierStructureChange();
            service.InvalidateValidationSetCache();
            RefreshAssets();
            RefreshAttributeRegistry();
            GameplayEffectData sessionEffect = GameplayEffectEditorSession.GetEffect();
            if (sessionEffect != effect) SetEffect(sessionEffect, true);
            else
            {
                RefreshEffectList();
                RefreshModifiers(true);
            }

            ScheduleValidation(ValidationRequestScope.All);
        }

        // Undo/Redo 后重新加载 Session 资产并重建原生绑定。
        private void OnUndoRedo()
        {
            CancelScheduledModifierMove();
            CancelScheduledModifierStructureChange();
            effect = GameplayEffectEditorSession.GetEffect();
            NormalizeModifierSelection();
            RefreshAll();
            ScheduleValidation(ValidationRequestScope.All);
        }

        #endregion

        #region 刷新

        // 重新扫描项目资产，不保存额外 ViewData 副本。
        private void RefreshAssets()
        {
            allEffects.Clear();
            allEffects.AddRange(service.FindAllEffects());
        }

        // 将搜索结果作为 Model 引用列表交给 View。
        private void RefreshEffectList()
        {
            var visible = new List<GameplayEffectData>();
            for (int i = 0; i < allEffects.Count; i++)
            {
                GameplayEffectData item = allEffects[i];
                string path = AssetDatabase.GetAssetPath(item);
                if (string.IsNullOrEmpty(search) ||
                    item.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    visible.Add(item);
            }

            view.SetSearch(search);
            view.RenderEffects(visible, effect);
        }

        // 将当前 Model 顺序同步到稳定 ListView 数据源；结构改变时只重绑 Modifier 详情。
        private void RefreshModifiers(bool rebind)
        {
            NormalizeModifierSelection();
            IReadOnlyList<GameplayEffectModifier> modifiers = effect?.Modifiers ??
                Array.Empty<GameplayEffectModifier>();
            view.RenderModifiers(modifiers, selectedModifierIndex, modifierTypes);
            if (rebind) view.BindModifier(selectedModifierIndex);
        }

        // 校验当前 GE，同时更新右侧问题与对应资产行的最高严重程度。
        private void RefreshCurrentValidation()
        {
            List<GameplayEffectValidationIssue> issues = service.Validate(effect);
            view.RenderValidation(issues);
            UpdateValidationState(effect, issues);
            view.RenderEffectValidationStates(effectValidationStates);
        }

        // 批量校验项目中的全部 GE，并以同一轮结果同步当前右侧问题。
        private void RefreshAllValidation()
        {
            effectValidationStates.Clear();
            List<GameplayEffectValidationIssue> currentIssues = null;
            for (int i = 0; i < allEffects.Count; i++)
            {
                GameplayEffectData item = allEffects[i];
                List<GameplayEffectValidationIssue> issues = service.Validate(item);
                UpdateValidationState(item, issues);
                if (ReferenceEquals(item, effect)) currentIssues = issues;
            }

            view.RenderEffectValidationStates(effectValidationStates);
            view.RenderValidation(currentIssues ?? service.Validate(effect));
        }

        // 只缓存会改变列表背景的最高 Error/Warning；Info 与无问题资产不占用条目。
        private void UpdateValidationState(
            GameplayEffectData target,
            IReadOnlyList<GameplayEffectValidationIssue> issues)
        {
            if (target == null) return;
            bool hasWarning = false;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity == GameplayEffectValidationSeverity.Error)
                {
                    effectValidationStates[target] = GameplayEffectValidationSeverity.Error;
                    return;
                }

                if (issues[i].Severity == GameplayEffectValidationSeverity.Warning)
                    hasWarning = true;
            }

            if (hasWarning)
                effectValidationStates[target] = GameplayEffectValidationSeverity.Warning;
            else
                effectValidationStates.Remove(target);
        }

        // 合并同一 UI 周期内的校验请求；All 的覆盖范围高于 Current。
        private void ScheduleValidation(ValidationRequestScope scope)
        {
            if (disposed || scope == ValidationRequestScope.None) return;
            if (scope <= scheduledValidationScope) return;
            bool alreadyScheduled = scheduledValidationScope != ValidationRequestScope.None;
            scheduledValidationScope = scope;
            if (alreadyScheduled) return;
            EditorApplication.delayCall += RunScheduledValidation;
        }

        // 延迟回调消费合并后的最高范围，避免字段提交、Drop 或项目扫描阻塞当前 UI 事件。
        private void RunScheduledValidation()
        {
            ValidationRequestScope scope = scheduledValidationScope;
            scheduledValidationScope = ValidationRequestScope.None;
            if (disposed) return;
            if (scope == ValidationRequestScope.All) RefreshAllValidation();
            else if (scope == ValidationRequestScope.Current) RefreshCurrentValidation();
        }

        // 页面释放或明确消费 Current 请求时移除尚未执行的延迟回调。
        private void CancelScheduledValidation()
        {
            if (scheduledValidationScope == ValidationRequestScope.None) return;
            EditorApplication.delayCall -= RunScheduledValidation;
            scheduledValidationScope = ValidationRequestScope.None;
        }

        // 使用与 Attribute PropertyDrawer 相同的 Session/唯一资产规则解析显示名称。
        private void RefreshAttributeRegistry()
        {
            attributeRegistry =
                GameplayAttributeEditorSession.ResolveSingleRegistry(out string error);
            view.SetAttributeRegistry(attributeRegistry, error);
        }

        // 同步资产列表和当前 GE 详情的完整状态。
        private void RefreshAll()
        {
            RefreshEffectList();
            RefreshEffectDetails();
        }

        // 只切换当前 GE、Modifier 与校验，不触碰已经完成选择的资产列表。
        private void RefreshEffectDetails()
        {
            RefreshAttributeRegistry();
            view.BindEffect(effect);
            RefreshModifiers(false);
            view.BindModifier(selectedModifierIndex);
            view.RefreshPolicyVisibility(effect);
            ScheduleValidation(ValidationRequestScope.Current);
        }

        // Modifier 索引始终与当前列表一致，并同步 SessionState。
        private void NormalizeModifierSelection()
        {
            int count = effect?.Modifiers.Count ?? 0;
            if (count == 0) selectedModifierIndex = -1;
            else if (selectedModifierIndex >= count) selectedModifierIndex = count - 1;
            else if (selectedModifierIndex < -1) selectedModifierIndex = -1;
            GameplayEffectEditorSession.SelectedModifierIndex = selectedModifierIndex;
        }

        #endregion

        #region 状态查询

        // 结构操作覆盖数组修改与下一帧详情重建两个阶段，期间禁止交错其他结构操作。
        private bool IsModifierStructureChangePending =>
            modifierStructureApplyScheduled ||
            modifierStructureRebindScheduled ||
            pendingModifierStructureOperation != ModifierStructureOperation.None;

        #endregion

        #region 事件连接

        // 统一建立 View、Undo 与项目资产事件。
        private void RegisterEvents()
        {
            view.SearchChanged += OnSearchChanged;
            view.EffectSelectionChanged += OnEffectSelectionChanged;
            view.PingEffectRequested += service.PingEffect;
            view.RenameEffectSubmitted += OnRenameEffectSubmitted;
            view.CreateEffectRequested += OnCreateEffectRequested;
            view.DuplicateEffectRequested += OnDuplicateEffectRequested;
            view.DeleteEffectRequested += OnDeleteEffectRequested;
            view.EffectSerializedChanged += OnEffectSerializedChanged;
            view.ModifierSelectionChanged += OnModifierSelectionChanged;
            view.AddModifierRequested += OnAddModifierRequested;
            view.RemoveModifierRequested += OnRemoveModifierRequested;
            view.MoveModifierRequested += OnMoveModifierRequested;
            view.ValidateRequested += OnValidateRequested;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        // Dispose 时对称解除全部连接，防止切换页面后重复刷新。
        private void UnregisterEvents()
        {
            view.SearchChanged -= OnSearchChanged;
            view.EffectSelectionChanged -= OnEffectSelectionChanged;
            view.PingEffectRequested -= service.PingEffect;
            view.RenameEffectSubmitted -= OnRenameEffectSubmitted;
            view.CreateEffectRequested -= OnCreateEffectRequested;
            view.DuplicateEffectRequested -= OnDuplicateEffectRequested;
            view.DeleteEffectRequested -= OnDeleteEffectRequested;
            view.EffectSerializedChanged -= OnEffectSerializedChanged;
            view.ModifierSelectionChanged -= OnModifierSelectionChanged;
            view.AddModifierRequested -= OnAddModifierRequested;
            view.RemoveModifierRequested -= OnRemoveModifierRequested;
            view.MoveModifierRequested -= OnMoveModifierRequested;
            view.ValidateRequested -= OnValidateRequested;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.projectChanged -= OnProjectChanged;
        }

        #endregion

        #region 嵌套类型

        /// <summary>区分待执行的 Modifier 数组扩容与删除操作。</summary>
        private enum ModifierStructureOperation
        {
            None,
            Add,
            Remove
        }

        /// <summary>定义一次延迟校验需要覆盖当前 GE 还是全部 GE。</summary>
        private enum ValidationRequestScope
        {
            None,
            Current,
            All
        }

        #endregion
    }
}
#endif
