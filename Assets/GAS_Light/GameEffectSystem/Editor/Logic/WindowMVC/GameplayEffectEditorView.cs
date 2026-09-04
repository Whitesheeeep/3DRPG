#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>组合 GE Editor 顶栏、资产列表和独立右侧详情 View。</summary>
    public sealed class GameplayEffectEditorView : IGameplayEffectEditorView
    {
        #region 常量与字段

        private const string EffectRowUxmlPath =
            "Assets/GAS_Light/GameEffectSystem/Editor/Style/GameplayEffectAssetRow.uxml";

        private readonly VisualElement root;
        private readonly VisualTreeAsset effectRowAsset;
        private readonly ToolbarSearchField searchField;
        private readonly Button createEffectButton;
        private readonly Button duplicateEffectButton;
        private readonly Button deleteEffectButton;
        private readonly ListView effectList;
        private readonly IGameplayEffectDetailsView detailsView;
        private readonly List<GameplayEffectData> renderedEffects = new();
        private readonly List<EffectRowState> effectRows = new();

        private IReadOnlyDictionary<GameplayEffectData, GameplayEffectValidationSeverity>
            effectValidationStates;
        private GameplayEffectData renamingEffect;
        private string pendingRenameValue = string.Empty;
        private int renameRequestVersion;
        private bool disposed;

        #endregion

        #region 事件

        /// <inheritdoc />
        public event Action<string> SearchChanged;
        /// <inheritdoc />
        public event Action<GameplayEffectData> EffectSelectionChanged;
        /// <inheritdoc />
        public event Action<GameplayEffectData> PingEffectRequested;
        /// <inheritdoc />
        public event Action<GameplayEffectRenameRequest> RenameEffectSubmitted;
        /// <inheritdoc />
        public event Action<string> CreateEffectRequested;
        /// <inheritdoc />
        public event Action DuplicateEffectRequested;
        /// <inheritdoc />
        public event Action DeleteEffectRequested;
        /// <inheritdoc />
        public event Action EffectSerializedChanged
        {
            add => detailsView.EffectSerializedChanged += value;
            remove => detailsView.EffectSerializedChanged -= value;
        }
        /// <inheritdoc />
        public event Action<int> ModifierSelectionChanged
        {
            add => detailsView.ModifierSelectionChanged += value;
            remove => detailsView.ModifierSelectionChanged -= value;
        }
        /// <inheritdoc />
        public event Action<Type> AddModifierRequested
        {
            add => detailsView.AddModifierRequested += value;
            remove => detailsView.AddModifierRequested -= value;
        }
        /// <inheritdoc />
        public event Action RemoveModifierRequested
        {
            add => detailsView.RemoveModifierRequested += value;
            remove => detailsView.RemoveModifierRequested -= value;
        }
        /// <inheritdoc />
        public event Action<GameplayEffectModifierMoveRequest> MoveModifierRequested
        {
            add => detailsView.MoveModifierRequested += value;
            remove => detailsView.MoveModifierRequested -= value;
        }
        /// <inheritdoc />
        public event Action ValidateRequested
        {
            add => detailsView.ValidateRequested += value;
            remove => detailsView.ValidateRequested -= value;
        }
        /// <inheritdoc />
        public event Action BakeCurvePreviewRequested
        {
            add => detailsView.BakeCurvePreviewRequested += value;
            remove => detailsView.BakeCurvePreviewRequested -= value;
        }
        /// <inheritdoc />
        public event Action ViewBakedResultRequested
        {
            add => detailsView.ViewBakedResultRequested += value;
            remove => detailsView.ViewBakedResultRequested -= value;
        }

        #endregion

        #region 生命周期

        /// <summary>查询顶栏与资产列表控件，并在右侧宿主中创建独立 Details View。</summary>
        /// <param name="root">已实例化 GE 主 UXML 的页面根元素。</param>
        /// <exception cref="ArgumentNullException">root 为 null。</exception>
        /// <exception cref="InvalidOperationException">主 UXML、右侧 UXML 或行模板缺失。</exception>
        public GameplayEffectEditorView(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            effectRowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(EffectRowUxmlPath);
            if (effectRowAsset == null)
                throw new InvalidOperationException("Gameplay Effect asset row UXML is missing.");

            searchField = Require<ToolbarSearchField>("SearchField");
            createEffectButton = Require<Button>("CreateEffectButton");
            duplicateEffectButton = Require<Button>("DuplicateEffectButton");
            deleteEffectButton = Require<Button>("DeleteEffectButton");
            effectList = Require<ListView>("EffectList");
            VisualElement detailsHost = Require<VisualElement>("DetailsHost");
            detailsView = new GameplayEffectDetailsView(detailsHost);

            ConfigureEffectList();
            RegisterCallbacks();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            UnregisterCallbacks();
            UnregisterEffectRows();
            effectList.itemsSource = null;
            detailsView.Dispose();
        }

        #endregion

        #region 状态与渲染

        /// <inheritdoc />
        public void SetSearch(string search) =>
            searchField.SetValueWithoutNotify(search ?? string.Empty);

        /// <inheritdoc />
        public void RenderEffects(
            IReadOnlyList<GameplayEffectData> effects,
            GameplayEffectData selected)
        {
            renderedEffects.Clear();
            if (effects != null) renderedEffects.AddRange(effects);
            if (renamingEffect != null && !renderedEffects.Contains(renamingEffect))
                CancelEffectRename(false);
            effectList.RefreshItems();
            int index = renderedEffects.IndexOf(selected);
            effectList.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index });
        }

        /// <inheritdoc />
        public void RenderEffectValidationStates(
            IReadOnlyDictionary<GameplayEffectData, GameplayEffectValidationSeverity> states)
        {
            effectValidationStates = states;
            effectList.RefreshItems();
        }

        /// <inheritdoc />
        public void RestoreEffectRename(GameplayEffectData effect, string attemptedName) =>
            QueueEffectRename(effect, attemptedName);

        /// <inheritdoc />
        public void SetAttributeRegistry(
            GameplayAttributeRegistry registry,
            string unavailableReason) =>
            detailsView.SetAttributeRegistry(registry, unavailableReason);

        /// <inheritdoc />
        public void BindEffect(GameplayEffectData effect)
        {
            if (renamingEffect != null && !ReferenceEquals(renamingEffect, effect))
                CancelEffectRename();
            bool hasEffect = effect != null;
            duplicateEffectButton.SetEnabled(hasEffect);
            deleteEffectButton.SetEnabled(hasEffect);
            detailsView.BindEffect(effect);
        }

        /// <inheritdoc />
        public void BindModifier(int selectedModifierIndex) =>
            detailsView.BindModifier(selectedModifierIndex);

        /// <inheritdoc />
        public void RenderModifiers(
            IReadOnlyList<GameplayEffectModifier> modifiers,
            int selectedIndex,
            IReadOnlyList<Type> availableTypes) =>
            detailsView.RenderModifiers(modifiers, selectedIndex, availableTypes);

        /// <inheritdoc />
        public void RefreshPolicyVisibility(GameplayEffectData effect) =>
            detailsView.RefreshPolicyVisibility(effect);

        /// <inheritdoc />
        public void RenderValidation(IReadOnlyList<GameplayEffectValidationIssue> issues) =>
            detailsView.RenderValidation(issues);

        /// <inheritdoc />
        public bool Confirm(string title, string message) =>
            EditorUtility.DisplayDialog(title, message, "Confirm", "Cancel");

        /// <inheritdoc />
        public void ShowError(string title, string message) =>
            EditorUtility.DisplayDialog(title, message, "OK");

        #endregion

        #region 资产列表

        // 资产 ListView 只保存真实 Model 引用，虚拟化行负责显示和交互状态。
        private void ConfigureEffectList()
        {
            effectList.selectionType = SelectionType.Single;
            effectList.fixedItemHeight = 38f;
            effectList.makeItem = CreateEffectRow;
            effectList.bindItem = BindEffectRow;
            effectList.itemsSource = renderedEffects;
        }

        // 创建虚拟化资产行状态并只注册一次回调，后续 bindItem 仅切换对应 Model。
        private VisualElement CreateEffectRow()
        {
            VisualElement row = effectRowAsset.Instantiate();
            var state = new EffectRowState(this, row);
            state.Register();
            row.userData = state;
            effectRows.Add(state);
            return row;
        }

        // 资产行绑定真实 Model，并根据当前重命名目标切换 Label 与 TextField。
        private void BindEffectRow(VisualElement element, int index)
        {
            var state = element.userData as EffectRowState;
            if (state == null)
                throw new InvalidOperationException("Gameplay Effect asset row state is missing.");

            GameplayEffectData item = renderedEffects[index];
            GameplayEffectValidationSeverity? severity = null;
            if (effectValidationStates != null &&
                effectValidationStates.TryGetValue(item, out GameplayEffectValidationSeverity stateSeverity))
                severity = stateSeverity;
            state.Bind(
                item,
                ReferenceEquals(renamingEffect, item),
                pendingRenameValue,
                severity);
        }

        #endregion

        #region UI 事件

        // 搜索框直接转发用户输入。
        private void OnSearchChanged(ChangeEvent<string> evt) => SearchChanged?.Invoke(evt.newValue);

        // 创建路径对话属于 View 交互，取消时不发送意图。
        private void OnCreateEffectClicked()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Gameplay Effect",
                "GameplayEffectData",
                "asset",
                "Choose a project path for the GameplayEffectData asset.");
            if (!string.IsNullOrEmpty(path)) CreateEffectRequested?.Invoke(path);
        }

        // 复制意图不携带 UI 状态。
        private void OnDuplicateEffectClicked() => DuplicateEffectRequested?.Invoke();

        // 删除确认由 Controller 通过 View.Confirm 统一执行。
        private void OnDeleteEffectClicked() => DeleteEffectRequested?.Invoke();

        // 将 ListView 选择转换为稳定 Model 引用。
        private void OnEffectSelectionChanged(IEnumerable<object> selection) =>
            EffectSelectionChanged?.Invoke(selection.OfType<GameplayEffectData>().FirstOrDefault());

        // 双击释放后延迟到当前 Pointer 生命周期结束，再切换行内输入和焦点。
        private void QueueEffectRename(GameplayEffectData effect, string value)
        {
            if (effect == null || disposed) return;
            renamingEffect = effect;
            pendingRenameValue = value ?? effect.name;
            int requestVersion = ++renameRequestVersion;
            root.schedule.Execute(() =>
            {
                if (disposed || requestVersion != renameRequestVersion) return;
                effectList.RefreshItems();
            });
        }

        // 行内输入先退出可视状态，再把稳定资产引用和名称提交给 Controller。
        private void SubmitEffectRename(EffectRowState state)
        {
            if (state?.Effect == null || !ReferenceEquals(renamingEffect, state.Effect)) return;
            GameplayEffectData effect = state.Effect;
            string value = state.RenameField.value;
            CancelEffectRename();
            RenameEffectSubmitted?.Invoke(new GameplayEffectRenameRequest(effect, value));
        }

        // 取消当前或尚未执行的重命名请求，并按需恢复所有可见行。
        private void CancelEffectRename(bool refresh = true)
        {
            renameRequestVersion++;
            renamingEffect = null;
            pendingRenameValue = string.Empty;
            if (refresh && !disposed) effectList.RefreshItems();
        }

        #endregion

        #region 回调与辅助

        // 注册顶栏与资产列表回调，由 Dispose 对称解除。
        private void RegisterCallbacks()
        {
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            createEffectButton.clicked += OnCreateEffectClicked;
            duplicateEffectButton.clicked += OnDuplicateEffectClicked;
            deleteEffectButton.clicked += OnDeleteEffectClicked;
            effectList.selectionChanged += OnEffectSelectionChanged;
        }

        // 释放时解除顶栏与资产列表回调。
        private void UnregisterCallbacks()
        {
            searchField.UnregisterValueChangedCallback(OnSearchChanged);
            createEffectButton.clicked -= OnCreateEffectClicked;
            duplicateEffectButton.clicked -= OnDuplicateEffectClicked;
            deleteEffectButton.clicked -= OnDeleteEffectClicked;
            effectList.selectionChanged -= OnEffectSelectionChanged;
        }

        // View 释放时解除全部虚拟化资产行回调和待执行焦点任务。
        private void UnregisterEffectRows()
        {
            renameRequestVersion++;
            for (int i = 0; i < effectRows.Count; i++) effectRows[i].Unregister();
            effectRows.Clear();
            renamingEffect = null;
            pendingRenameValue = string.Empty;
        }

        // 查询主 UXML 必需控件，缺失时立即暴露资源与代码契约不一致。
        private T Require<T>(string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null)
                throw new InvalidOperationException(
                    $"Gameplay Effect Editor UXML is missing required element '{name}'.");
            return element;
        }

        #endregion
        #region 嵌套类型

        /// <summary>保存单个虚拟化 GE 资产行的绑定、重命名和 Pointer 状态。</summary>
        private sealed class EffectRowState
        {
            private readonly GameplayEffectEditorView owner;
            private readonly VisualElement root;
            private readonly VisualElement visualRoot;
            private readonly Label nameLabel;
            private readonly Label pathLabel;
            private readonly Button pingButton;
            private bool suppressNextFocusCommit;
            private bool wasRenaming;
            private int bindVersion;
            private int pendingFocusVersion = -1;
            private int pendingRenamePointerId = -1;
            private GameplayEffectData pendingRenameEffect;

            /// <summary>获取当前虚拟化行绑定的 GE 资产。</summary>
            public GameplayEffectData Effect { get; private set; }
            /// <summary>获取行内重命名输入框。</summary>
            public TextField RenameField { get; }

            // 缓存行控件，确保虚拟化复用时只改变绑定数据而不重复注册回调。
            internal EffectRowState(GameplayEffectEditorView owner, VisualElement root)
            {
                this.owner = owner;
                this.root = root;
                visualRoot = root.Q<VisualElement>(className: "ge-effect-row") ??
                    throw new InvalidOperationException(
                        "Gameplay Effect asset row UXML is missing the 'ge-effect-row' root class.");
                nameLabel = RequireRowElement<Label>("NameLabel");
                pathLabel = RequireRowElement<Label>("PathLabel");
                pingButton = RequireRowElement<Button>("PingButton");
                RenameField = RequireRowElement<TextField>("RenameField");
            }

            // 注册当前虚拟化行全部输入回调，由 View.Dispose 对称解除。
            internal void Register()
            {
                nameLabel.RegisterCallback<PointerDownEvent>(OnNamePointerDown);
                nameLabel.RegisterCallback<PointerUpEvent>(OnNamePointerUp);
                nameLabel.RegisterCallback<PointerCaptureOutEvent>(OnNamePointerCaptureOut);
                RenameField.RegisterCallback<KeyDownEvent>(OnRenameKeyDown);
                RenameField.RegisterCallback<FocusOutEvent>(OnRenameFocusOut);
                RenameField.RegisterCallback<FocusInEvent>(OnRenameFocusIn);
                RenameField.RegisterCallback<GeometryChangedEvent>(OnRenameGeometryChanged);
                pingButton.RegisterCallback<PointerDownEvent>(OnPingPointerDown);
                pingButton.RegisterCallback<ClickEvent>(OnPingClicked);
            }

            // 解除行回调、Pointer 捕获和焦点任务，防止页面重建后残留旧 View 引用。
            internal void Unregister()
            {
                ClearPendingRenamePointer();
                CancelPendingFocus();
                nameLabel.UnregisterCallback<PointerDownEvent>(OnNamePointerDown);
                nameLabel.UnregisterCallback<PointerUpEvent>(OnNamePointerUp);
                nameLabel.UnregisterCallback<PointerCaptureOutEvent>(OnNamePointerCaptureOut);
                RenameField.UnregisterCallback<KeyDownEvent>(OnRenameKeyDown);
                RenameField.UnregisterCallback<FocusOutEvent>(OnRenameFocusOut);
                RenameField.UnregisterCallback<FocusInEvent>(OnRenameFocusIn);
                RenameField.UnregisterCallback<GeometryChangedEvent>(OnRenameGeometryChanged);
                pingButton.UnregisterCallback<PointerDownEvent>(OnPingPointerDown);
                pingButton.UnregisterCallback<ClickEvent>(OnPingClicked);
                Effect = null;
            }

            // 绑定当前资产，并在进入重命名时安全聚焦；普通复用不会覆盖正在输入的文本。
            internal void Bind(
                GameplayEffectData effect,
                bool renaming,
                string renameValue,
                GameplayEffectValidationSeverity? severity)
            {
                bool bindingChanged = !ReferenceEquals(Effect, effect);
                if (bindingChanged)
                {
                    ClearPendingRenamePointer();
                    CancelPendingFocus();
                    suppressNextFocusCommit = false;
                }

                bindVersion++;
                Effect = effect;
                nameLabel.text = effect.name;
                pathLabel.text = AssetDatabase.GetAssetPath(effect);
                if (!renaming || bindingChanged || !wasRenaming)
                    RenameField.SetValueWithoutNotify(renaming ? renameValue : effect.name);
                nameLabel.EnableInClassList("is-hidden", renaming);
                RenameField.EnableInClassList("is-hidden", !renaming);
                visualRoot.EnableInClassList(
                    "has-validation-error",
                    severity == GameplayEffectValidationSeverity.Error);
                visualRoot.EnableInClassList(
                    "has-validation-warning",
                    severity == GameplayEffectValidationSeverity.Warning);
                wasRenaming = renaming;
                if (renaming) FocusRename();
                else
                {
                    CancelPendingFocus();
                    suppressNextFocusCommit = false;
                }
            }

            // 第二次按下只记录名称目标，并等待 PointerUp 后再请求切换 UI。
            private void OnNamePointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || evt.clickCount != 2 || Effect == null) return;
                ClearPendingRenamePointer();
                pendingRenameEffect = Effect;
                pendingRenamePointerId = evt.pointerId;
                nameLabel.CapturePointer(pendingRenamePointerId);
                evt.StopImmediatePropagation();
            }

            // PointerUp 后释放捕获并排队重命名，避免当前点击事件夺回输入焦点。
            private void OnNamePointerUp(PointerUpEvent evt)
            {
                if (evt.pointerId != pendingRenamePointerId || pendingRenameEffect == null) return;
                GameplayEffectData effect = pendingRenameEffect;
                ClearPendingRenamePointer();
                evt.StopImmediatePropagation();
                owner.QueueEffectRename(effect, effect.name);
            }

            // Pointer 捕获意外丢失时取消未完成的双击请求。
            private void OnNamePointerCaptureOut(PointerCaptureOutEvent evt)
            {
                if (evt.pointerId != pendingRenamePointerId) return;
                pendingRenamePointerId = -1;
                pendingRenameEffect = null;
            }

            // Enter 提交当前输入，Escape 取消且不发送资产操作。
            private void OnRenameKeyDown(KeyDownEvent evt)
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    suppressNextFocusCommit = true;
                    owner.SubmitEffectRename(this);
                    evt.StopImmediatePropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    suppressNextFocusCommit = true;
                    owner.CancelEffectRename();
                    evt.StopImmediatePropagation();
                }
            }

            // 正常失焦提交；Enter/Escape 引发的单次 FocusOut 已被显式抑制。
            private void OnRenameFocusOut(FocusOutEvent evt)
            {
                if (suppressNextFocusCommit)
                {
                    suppressNextFocusCommit = false;
                    return;
                }

                if (Effect != null && ReferenceEquals(owner.renamingEffect, Effect))
                    owner.SubmitEffectRename(this);
            }

            // 输入框实际获得焦点后全选文本，并完成本次待聚焦任务。
            private void OnRenameFocusIn(FocusInEvent evt)
            {
                suppressNextFocusCommit = false;
                CompletePendingFocus();
            }

            // 隐藏状态切换并完成布局后重试聚焦。
            private void OnRenameGeometryChanged(GeometryChangedEvent evt) => TryFocusRename();

            // Ping 只定位当前绑定资产，不让 Pointer 继续触发 ListView 选择。
            private void OnPingPointerDown(PointerDownEvent evt) => evt.StopPropagation();

            // 虚拟化复用后通过当前 Effect 引用发送正确的 Ping 意图。
            private void OnPingClicked(ClickEvent evt)
            {
                evt.StopPropagation();
                if (Effect != null) owner.PingEffectRequested?.Invoke(Effect);
            }

            // 保存当前绑定版本，等待输入框附着并完成样式布局后聚焦。
            private void FocusRename()
            {
                pendingFocusVersion = bindVersion;
                RenameField.schedule.Execute(TryFocusRename);
            }

            // 只有行仍绑定同一重命名资产时才允许旧调度任务获取焦点。
            private void TryFocusRename()
            {
                if (pendingFocusVersion != bindVersion ||
                    Effect == null ||
                    !ReferenceEquals(owner.renamingEffect, Effect))
                {
                    CancelPendingFocus();
                    return;
                }

                if (RenameField.panel == null || RenameField.ClassListContains("is-hidden")) return;
                RenameField.Focus();
                if (RenameField.panel?.focusController?.focusedElement == RenameField)
                    CompletePendingFocus();
            }

            // 输入框获得焦点后全选文本并使旧任务失效。
            private void CompletePendingFocus()
            {
                if (pendingFocusVersion < 0) return;
                RenameField.SelectAll();
                CancelPendingFocus();
            }

            // 取消尚未完成的聚焦任务。
            private void CancelPendingFocus() => pendingFocusVersion = -1;

            // 清除双击 Pointer 状态；先清标记再释放捕获，避免 CaptureOut 重入。
            private void ClearPendingRenamePointer()
            {
                int pointerId = pendingRenamePointerId;
                pendingRenamePointerId = -1;
                pendingRenameEffect = null;
                if (pointerId >= 0 && nameLabel.HasPointerCapture(pointerId))
                    nameLabel.ReleasePointer(pointerId);
            }

            // 查询行模板必需控件，缺失时立即暴露 UXML 与代码契约不一致。
            private T RequireRowElement<T>(string name) where T : VisualElement
            {
                T element = root.Q<T>(name);
                if (element == null)
                    throw new InvalidOperationException(
                        $"Gameplay Effect asset row UXML is missing required element '{name}'.");
                return element;
            }
        }

        #endregion
    }
}
#endif
