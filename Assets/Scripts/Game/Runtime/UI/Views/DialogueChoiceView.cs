using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.DialogueSystemModule;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WS_Modules.ResLoadModule;

namespace WS_Modules.UIModule
{
    /// <summary>DialogueWindow 内的 Choice View，负责行复用、置灰、焦点和稳定 NodeId 转发。</summary>
    public sealed class DialogueChoiceView : IDisposable
    {
        #region 资源与状态

        // 资源由 View 自己加载和释放；DialogueWindow 只负责等待初始化和销毁顺序。
        private readonly Transform choiceRoot;
        private readonly string optionPrefabPath;
        private readonly int initialRowCount;
        private readonly List<OptionChoice> rows = new();
        private readonly List<DialogueChoiceSnapShot> pendingChoices = new();
        private GameObject optionPrefab;
        private UniTask initializationTask;
        private bool initializationStarted;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>用户点击可用 Choice 时发送该 Choice 的稳定 NodeId。</summary>
        public event Action<string> ChoiceRequested;

        #endregion

        #region 构造与初始化

        /// <summary>创建绑定 DialogueWindow 选项根节点的 View。</summary>
        /// <param name="choiceRoot">动态选项行的父节点。</param>
        /// <param name="optionPrefabPath">OptionChoice Addressable 地址。</param>
        /// <param name="initialRowCount">首次预创建行数量。</param>
        public DialogueChoiceView(Transform choiceRoot, string optionPrefabPath, int initialRowCount)
        {
            this.choiceRoot = choiceRoot ?? throw new ArgumentNullException(nameof(choiceRoot));
            this.optionPrefabPath = string.IsNullOrWhiteSpace(optionPrefabPath)
                ? throw new ArgumentException("OptionChoice prefab 地址不能为空。", nameof(optionPrefabPath))
                : optionPrefabPath;
            this.initialRowCount = Mathf.Max(0, initialRowCount);
        }

        /// <summary>启动 Addressable 行资源加载，并返回可重复等待的初始化任务。</summary>
        /// <returns>行资源和初始实例初始化任务。</returns>
        public UniTask InitializeAsync()
        {
            if (!initializationStarted)
            {
                initializationStarted = true;
                initializationTask = InitializeCoreAsync().Preserve();
            }

            return initializationTask;
        }

        /// <summary>加载 OptionChoice 并应用初始化前缓存的最新 Choice 状态。</summary>
        /// <returns>异步资源加载流程。</returns>
        private async UniTask InitializeCoreAsync()
        {
            GameObject loadedPrefab = await ResSystem.Instance.LoadAsync<GameObject>(optionPrefabPath);
            if (disposed)
            {
                if (loadedPrefab != null) ResSystem.Instance.UnLoad<GameObject>(optionPrefabPath);
                return;
            }

            optionPrefab = loadedPrefab ?? throw new InvalidOperationException(
                $"无法加载 OptionChoice 资源：{optionPrefabPath}。");
            EnsureRowCount(initialRowCount);
            ApplyPendingState();
        }

        #endregion

        #region 状态刷新

        /// <summary>刷新 Choice 快照；资源未完成加载时缓存最新一次状态。</summary>
        /// <param name="choices">按 DialogueAsset 顺序排列的 Choice 展示快照。</param>
        public void RefreshChoices(IReadOnlyList<DialogueChoiceSnapShot> choices)
        {
            ThrowIfDisposed();
            string selectedNodeId = GetSelectedNodeId();
            pendingChoices.Clear();
            if (choices != null)
            {
                for (int index = 0; index < choices.Count; index++)
                    pendingChoices.Add(choices[index]);
            }

            ApplyPendingState(selectedNodeId);
        }

        /// <summary>设置整个 Choice 区域显隐。</summary>
        /// <param name="visible">是否显示 Choice 区域。</param>
        public void SetVisible(bool visible)
        {
            ThrowIfDisposed();
            choiceRoot.gameObject.SetActive(visible);
            if (!visible) ClearSelectionIfOwned();
        }

        /// <summary>清空当前行并隐藏 Choice 区域。</summary>
        public void Clear()
        {
            ThrowIfDisposed();
            pendingChoices.Clear();
            ApplyPendingState();
            SetVisible(false);
        }

        /// <summary>确保行实例数量满足当前展示数据，不销毁可复用行。</summary>
        /// <param name="requiredCount">需要的最小行数。</param>
        private void EnsureRowCount(int requiredCount)
        {
            while (rows.Count < requiredCount)
            {
                GameObject rowObject = UnityEngine.Object.Instantiate(optionPrefab, choiceRoot, false);
                OptionChoice row = rowObject.GetComponent<OptionChoice>();
                if (row == null)
                    throw new InvalidOperationException("OptionChoice prefab 必须包含 OptionChoice 组件。");
                // Dialogue Choice 不维护第二份颜色状态；按钮的 Selected 视觉直接由 EventSystem 驱动。
                row.Initialize(HandleChoiceRequested, null, HandlePointerSelectionRequested);
                rows.Add(row);
            }
        }

        /// <summary>把缓存 Choice 状态应用到所有行并聚焦第一个可用选项。</summary>
        private void ApplyPendingState(string preferredNodeId = null)
        {
            if (optionPrefab == null || disposed) return;

            EnsureRowCount(Mathf.Max(initialRowCount, pendingChoices.Count));
            int firstAvailableIndex = -1;
            int preferredIndex = -1;
            for (int index = 0; index < rows.Count; index++)
            {
                OptionChoice row = rows[index];
                bool visible = index < pendingChoices.Count;
                row.gameObject.SetActive(visible);
                if (!visible)
                {
                    row.ClearOption();
                    continue;
                }

                DialogueChoiceSnapShot choice = pendingChoices[index];
                row.SetOption(index, choice.Text, choice.IsAvailable);
                if (choice.IsAvailable)
                {
                    if (firstAvailableIndex < 0) firstAvailableIndex = index;
                    if (!string.IsNullOrEmpty(preferredNodeId) && choice.NodeId == preferredNodeId)
                        preferredIndex = index;
                }
            }

            ConfigureNavigation();
            int selectedIndex = preferredIndex >= 0 ? preferredIndex : firstAvailableIndex;

            if (pendingChoices.Count == 0)
            {
                ClearSelectionIfOwned();
                choiceRoot.gameObject.SetActive(false);
            }
            else
            {
                choiceRoot.gameObject.SetActive(true);
                if (selectedIndex >= 0)
                    TrySelectRow(rows[selectedIndex].gameObject);
                else
                    ClearSelectionIfOwned();
            }
        }

        /// <summary>为当前可用 Choice 建立只允许上下移动的循环 UI 导航。</summary>
        private void ConfigureNavigation()
        {
            List<int> availableIndices = new();
            for (int index = 0; index < pendingChoices.Count; index++)
                if (pendingChoices[index].IsAvailable) availableIndices.Add(index);

            for (int index = 0; index < rows.Count; index++)
            {
                OptionChoice row = rows[index];
                Navigation navigation = row.Button.navigation;
                if (index >= pendingChoices.Count || !pendingChoices[index].IsAvailable)
                {
                    navigation.mode = Navigation.Mode.None;
                    navigation.selectOnUp = null;
                    navigation.selectOnDown = null;
                    navigation.selectOnLeft = null;
                    navigation.selectOnRight = null;
                }
                else
                {
                    navigation.mode = Navigation.Mode.Explicit;
                    navigation.selectOnLeft = null;
                    navigation.selectOnRight = null;
                    if (availableIndices.Count <= 1)
                    {
                        navigation.selectOnUp = row.Button;
                        navigation.selectOnDown = row.Button;
                    }
                    else
                    {
                        int currentPosition = availableIndices.IndexOf(index);
                        int upPosition = (currentPosition - 1 + availableIndices.Count) % availableIndices.Count;
                        int downPosition = (currentPosition + 1) % availableIndices.Count;
                        navigation.selectOnUp = rows[availableIndices[upPosition]].Button;
                        navigation.selectOnDown = rows[availableIndices[downPosition]].Button;
                    }
                }

                row.Button.navigation = navigation;
            }
        }

        #endregion

        #region 点击与释放

        /// <summary>把行索引转换为当前快照中的稳定 NodeId。</summary>
        /// <param name="index">点击行索引。</param>
        private void HandleChoiceRequested(int index)
        {
            if (index < 0 || index >= pendingChoices.Count) return;
            DialogueChoiceSnapShot choice = pendingChoices[index];
            if (choice.IsAvailable) ChoiceRequested?.Invoke(choice.NodeId);
        }

        /// <summary>
        /// 响应 OptionChoice 的鼠标移动选中请求；只有当前快照中的可用行可以取得焦点。
        /// </summary>
        /// <param name="index">请求切换焦点的行索引。</param>
        private void HandlePointerSelectionRequested(int index)
        {
            if (index < 0 || index >= pendingChoices.Count) return;
            DialogueChoiceSnapShot choice = pendingChoices[index];
            OptionChoice row = rows[index];
            if (!choice.IsAvailable || row == null || !row.gameObject.activeInHierarchy || !row.Button.IsInteractable()) return;
            TrySelectRow(row.gameObject);
        }

        /// <summary>
        /// 在 EventSystem 未处理另一轮 Selection 时同步 Choice 焦点，避免 Selection 回调嵌套选择。
        /// </summary>
        /// <param name="target">需要聚焦的 Choice 行。</param>
        private static void TrySelectRow(GameObject target)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || target == null || !target.activeInHierarchy) return;

            // SetSelectedGameObject 的选择保护先于目标比较执行，重入时必须完全跳过调用。
            if (eventSystem.alreadySelecting || eventSystem.currentSelectedGameObject == target) return;
            eventSystem.SetSelectedGameObject(target);
        }

        /// <summary>读取当前 View 所属 EventSystem Selection 对应的 NodeId。</summary>
        /// <returns>当前 Choice NodeId；没有属于本 View 的 Selection 时返回 null。</returns>
        private string GetSelectedNodeId()
        {
            GameObject selected = EventSystem.current?.currentSelectedGameObject;
            if (selected == null) return null;
            for (int index = 0; index < rows.Count && index < pendingChoices.Count; index++)
                if (rows[index].gameObject == selected && pendingChoices[index].IsAvailable)
                    return pendingChoices[index].NodeId;
            return null;
        }

        /// <summary>仅当 EventSystem 当前焦点属于本 View 时清除焦点。</summary>
        private void ClearSelectionIfOwned()
        {
            EventSystem eventSystem = EventSystem.current;
            GameObject selected = eventSystem?.currentSelectedGameObject;
            if (eventSystem == null || eventSystem.alreadySelecting) return;
            if (selected != null && selected.transform.IsChildOf(choiceRoot))
                eventSystem.SetSelectedGameObject(null);
        }

        /// <summary>释放行对象、资源引用和用户意图事件。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ChoiceRequested = null;
            ClearSelectionIfOwned();

            for (int index = 0; index < rows.Count; index++)
                if (rows[index] != null) UnityEngine.Object.Destroy(rows[index].gameObject);
            rows.Clear();
            pendingChoices.Clear();
            if (optionPrefab != null)
            {
                ResSystem.Instance.UnLoad<GameObject>(optionPrefabPath);
                optionPrefab = null;
            }
        }

        /// <summary>拒绝在 View 释放后继续刷新状态。</summary>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(DialogueChoiceView));
        }

        #endregion
    }
}
