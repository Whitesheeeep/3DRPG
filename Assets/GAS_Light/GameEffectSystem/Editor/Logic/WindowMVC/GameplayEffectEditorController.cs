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

        private GameplayEffectData effect;
        private GameplayEffectData pendingModifierMoveEffect;
        private GameplayAttributeRegistry attributeRegistry;
        private GameplayEffectModifierMoveRequest pendingModifierMove;
        private string search;
        private int selectedModifierIndex;
        private bool modifierMoveScheduled;
        private bool validationScheduled;
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
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CancelScheduledModifierMove();
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

        // 资产列表已完成视觉选择，只切换详情，避免再次刷新整张 Effect 列表。
        private void OnEffectSelectionChanged(GameplayEffectData selected)
        {
            CancelScheduledModifierMove();
            effect = selected;
            GameplayEffectEditorSession.SetEffect(effect);
            selectedModifierIndex = -1;
            NormalizeModifierSelection();
            RefreshEffectDetails();
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

            if (!service.MoveEffectToTrash(effect, out string error))
            {
                view.ShowError("Delete Gameplay Effect", error);
                return;
            }

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
            ScheduleValidation();
        }

        // 选择索引是纯 UI 状态，使用 SessionState 跨域重载保存。
        private void OnModifierSelectionChanged(int index)
        {
            selectedModifierIndex = index;
            NormalizeModifierSelection();
            if (!modifierMoveScheduled) view.BindModifier(selectedModifierIndex);
        }

        // 添加前先销毁旧 SerializedProperty 控件，避免扩容 SerializeReference 数组后遗留失效句柄。
        private void OnAddModifierRequested(Type type)
        {
            view.BindModifier(-1);
            if (!service.TryAddModifier(effect, type, out int newIndex, out string error))
            {
                view.BindModifier(selectedModifierIndex);
                view.ShowError("Add Gameplay Effect Modifier", error);
                return;
            }

            selectedModifierIndex = newIndex;
            GameplayEffectEditorSession.SelectedModifierIndex = newIndex;
            RefreshModifiers(true);
            ScheduleValidation();
        }

        // 删除前先销毁旧 SerializedProperty 控件，提交后再绑定同一位置或前一项。
        private void OnRemoveModifierRequested()
        {
            view.BindModifier(-1);
            if (!service.RemoveModifier(effect, selectedModifierIndex))
            {
                view.BindModifier(selectedModifierIndex);
                return;
            }

            NormalizeModifierSelection();
            RefreshModifiers(true);
            ScheduleValidation();
        }

        // Drop 时先销毁旧详情；数组移动延迟到当前 Panel/IMGUI 事件完全结束后执行。
        private void OnMoveModifierRequested(GameplayEffectModifierMoveRequest request)
        {
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
            ScheduleValidation();
        }

        // 页面释放或切换 GE 时取消尚未提交的拖放，防止修改已经离开的资产。
        private void CancelScheduledModifierMove()
        {
            if (!modifierMoveScheduled) return;
            EditorApplication.delayCall -= ApplyScheduledModifierMove;
            modifierMoveScheduled = false;
            pendingModifierMoveEffect = null;
        }

        // 手动校验取消待执行请求并立即查询当前 Model。
        private void OnValidateRequested()
        {
            CancelScheduledValidation();
            RefreshValidation();
        }

        // 项目资产变化后重新扫描；当前 GUID 丢失时清空详情。
        private void OnProjectChanged()
        {
            service.InvalidateValidationSetCache();
            RefreshAssets();
            RefreshAttributeRegistry();
            GameplayEffectData sessionEffect = GameplayEffectEditorSession.GetEffect();
            if (sessionEffect != effect) SetEffect(sessionEffect, true);
            else
            {
                RefreshEffectList();
                RefreshModifiers(false);
                ScheduleValidation();
            }
        }

        // Undo/Redo 后重新加载 Session 资产并重建原生绑定。
        private void OnUndoRedo()
        {
            effect = GameplayEffectEditorSession.GetEffect();
            NormalizeModifierSelection();
            RefreshAll();
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

        // 校验从当前 Model 和缓存的项目 Set 引用计算，不保存校验结果。
        private void RefreshValidation() =>
            view.RenderValidation(service.Validate(effect));

        // 把同一 UI 周期内的多个自动校验请求合并到下一次 Editor 更新。
        private void ScheduleValidation()
        {
            if (disposed || validationScheduled) return;
            validationScheduled = true;
            EditorApplication.delayCall += RunScheduledValidation;
        }

        // 延迟回调只消费最新的当前 GE，避免字段提交或 Drop 阻塞当前输入事件。
        private void RunScheduledValidation()
        {
            validationScheduled = false;
            if (!disposed) RefreshValidation();
        }

        // 释放或手动刷新时移除尚未执行的延迟回调。
        private void CancelScheduledValidation()
        {
            if (!validationScheduled) return;
            EditorApplication.delayCall -= RunScheduledValidation;
            validationScheduled = false;
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
            ScheduleValidation();
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

        #region 事件连接

        // 统一建立 View、Undo 与项目资产事件。
        private void RegisterEvents()
        {
            view.SearchChanged += OnSearchChanged;
            view.EffectSelectionChanged += OnEffectSelectionChanged;
            view.PingEffectRequested += service.PingEffect;
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
    }
}
#endif
