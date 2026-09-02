using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WS_Modules.ResLoadModule;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// ChoiceWindow 的纯 C# View，负责选项行资源、文本、显隐和用户点击转发。
    /// </summary>
    public sealed class ChoiceWindowView : IDisposable
    {
        #region 资源与状态

        // View 依赖生成的 ChoiceRoot Transform 与 Addressable 选项行资源。
        // 依赖数据：pendingOptionNames、pendingSelectedIndex。
        private readonly Transform choiceRoot;
        private readonly string optionPrefabPath;
        private readonly int initialRowCount;
        // rows 和 pendingOptionNames index 对应，pendingSelectedIndex 为 -1 时表示无选中项。
        private readonly List<OptionChoice> rows = new();
        private readonly List<string> pendingOptionNames = new();

        private int pendingSelectedIndex = -1;
        private GameObject optionPrefab;
        private UniTask initializationTask;
        private bool initializationStarted;
        private bool applyingState;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>用户点击选项行时发送当前行索引。</summary>
        public event Action<int> ChoiceRequested;

        /// <summary>EventSystem 通过键盘或手柄选中选项时发送当前行索引。</summary>
        public event Action<int> SelectionRequested;

        #endregion

        #region 构造与初始化

        /// <summary>
        /// 创建 ChoiceWindow View。
        /// </summary>
        /// <param name="choiceRoot">选项行的父节点。</param>
        /// <param name="optionPrefabPath">OptionChoice prefab 的资源地址。</param>
        /// <param name="initialRowCount">首次预创建的行数量。</param>
        public ChoiceWindowView(Transform choiceRoot, string optionPrefabPath, int initialRowCount)
        {
            this.choiceRoot = choiceRoot ?? throw new ArgumentNullException(nameof(choiceRoot));
            this.optionPrefabPath = string.IsNullOrWhiteSpace(optionPrefabPath)
                ? throw new ArgumentException("OptionChoice prefab 地址不能为空。", nameof(optionPrefabPath))
                : optionPrefabPath;
            this.initialRowCount = Mathf.Max(0, initialRowCount);
        }

        /// <summary>
        /// 开始一次可重复等待的 View 初始化，并预创建初始行。
        /// </summary>
        /// <returns>资源和初始行初始化任务。</returns>
        public UniTask InitializeAsync()
        {
            if (!initializationStarted)
            {
                initializationStarted = true;
                initializationTask = InitializeCoreAsync().Preserve();
            }

            return initializationTask;
        }

        /// <summary>
        /// 异步加载行 prefab，完成后将缓存的最新展示状态应用到行实例。
        /// </summary>
        /// <returns>View 初始化任务。</returns>
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

        /// <summary>
        /// 刷新选项名称和选中索引；资源未完成加载时缓存最新一次展示状态。
        /// </summary>
        /// <param name="optionNames">按交互排序排列的选项名称。</param>
        /// <param name="selectedIndex">当前选中项索引；无有效选择时为 -1。</param>
        public void RefreshOptions(IReadOnlyList<string> optionNames, int selectedIndex)
        {
            pendingOptionNames.Clear();
            if (optionNames != null)
            {
                for (int index = 0; index < optionNames.Count; index++)
                    pendingOptionNames.Add(optionNames[index]);
            }

            pendingSelectedIndex = selectedIndex;
            ApplyPendingState();
        }

        /// <summary>
        /// 确保行实例数量满足当前选项数量；已存在的行只复用不销毁。
        /// </summary>
        /// <param name="requiredCount">需要的最小行数量。</param>
        private void EnsureRowCount(int requiredCount)
        {
            while (rows.Count < requiredCount)
            {
                GameObject rowObject = UnityEngine.Object.Instantiate(optionPrefab, choiceRoot, false);
                OptionChoice row = rowObject.GetComponent<OptionChoice>();
                if (row == null)
                    throw new InvalidOperationException("OptionChoice prefab 必须包含 OptionChoice 组件。");

                row.Initialize(HandleChoiceRequested, HandleSelectionRequested,
                    HandlePointerSelectionRequested);
                rows.Add(row);
            }
        }

        /// <summary>
        /// 将缓存状态应用到所有行，并隐藏当前列表之外的多余行。
        /// </summary>
        private void ApplyPendingState()
        {
            if (optionPrefab == null || disposed) return;

            applyingState = true;
            try
            {
                EnsureRowCount(Mathf.Max(initialRowCount, pendingOptionNames.Count));
                for (int index = 0; index < rows.Count; index++)
                {
                    OptionChoice row = rows[index];
                    bool visible = index < pendingOptionNames.Count;
                    row.gameObject.SetActive(visible);
                    if (visible)
                        row.SetOption(index, pendingOptionNames[index], true);
                    else
                        row.ClearOption();
                }

                ConfigureNavigation();
                if (pendingSelectedIndex >= 0 && pendingSelectedIndex < pendingOptionNames.Count)
                    TrySelectRow(rows[pendingSelectedIndex].gameObject);
                else
                    ClearSelectionIfOwned();
            }
            finally
            {
                applyingState = false;
            }
        }

        /// <summary>为当前可见 Option 建立只允许上下移动的显式 UI 导航。</summary>
        private void ConfigureNavigation()
        {
            List<int> visibleIndices = new();
            for (int index = 0; index < pendingOptionNames.Count; index++)
                visibleIndices.Add(index);

            for (int index = 0; index < rows.Count; index++)
            {
                OptionChoice row = rows[index];
                Navigation navigation = row.Button.navigation;
                if (index >= pendingOptionNames.Count)
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
                    if (visibleIndices.Count <= 1)
                    {
                        navigation.selectOnUp = row.Button;
                        navigation.selectOnDown = row.Button;
                    }
                    else
                    {
                        int currentPosition = visibleIndices.IndexOf(index);
                        int upPosition = (currentPosition - 1 + visibleIndices.Count) % visibleIndices.Count;
                        int downPosition = (currentPosition + 1) % visibleIndices.Count;
                        navigation.selectOnUp = rows[visibleIndices[upPosition]].Button;
                        navigation.selectOnDown = rows[visibleIndices[downPosition]].Button;
                    }
                }

                row.Button.navigation = navigation;
            }
        }

        #endregion

        #region 点击与释放

        /// <summary>将行级点击转换为 View 的选项请求事件。</summary>
        /// <param name="index">被点击行的索引。</param>
        private void HandleChoiceRequested(int index) => ChoiceRequested?.Invoke(index);

        /// <summary>将 EventSystem 选中结果转换为 View 的 Selection 请求事件。</summary>
        /// <param name="index">被选中行的索引。</param>
        private void HandleSelectionRequested(int index)
        {
            if (applyingState || index < 0 || index >= pendingOptionNames.Count) return;
            SelectionRequested?.Invoke(index);
        }

        /// <summary>
        /// 响应 OptionChoice 的鼠标移动选中请求；View 负责验证行状态并复用统一的安全焦点入口。
        /// </summary>
        /// <param name="index">请求切换焦点的行索引。</param>
        private void HandlePointerSelectionRequested(int index)
        {
            if (applyingState || index < 0 || index >= pendingOptionNames.Count) return;
            OptionChoice row = rows[index];
            if (row == null || !row.gameObject.activeInHierarchy || !row.Button.IsInteractable()) return;
            TrySelectRow(row.gameObject);
        }

        /// <summary>
        /// 在 EventSystem 未处理另一轮 Selection 时同步焦点；Selection 回调重入期间只保留当前焦点。
        /// </summary>
        /// <param name="target">需要聚焦的选项行。</param>
        private static void TrySelectRow(GameObject target)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || target == null || !target.activeInHierarchy) return;

            // EventSystem 先检查选择保护，再判断目标是否相同；因此相同目标也必须提前返回。
            if (eventSystem.alreadySelecting || eventSystem.currentSelectedGameObject == target) return;
            eventSystem.SetSelectedGameObject(target);
        }

        /// <summary>仅当 EventSystem 当前焦点属于本 View 时清除焦点，避免影响其他窗口。</summary>
        private void ClearSelectionIfOwned()
        {
            EventSystem eventSystem = EventSystem.current;
            GameObject selected = eventSystem?.currentSelectedGameObject;
            if (eventSystem == null || eventSystem.alreadySelecting) return;
            if (selected != null && selected.transform.IsChildOf(choiceRoot))
                eventSystem.SetSelectedGameObject(null);
        }

        /// <summary>
        /// 释放行实例、事件和异步资源；异步加载尚未完成时由完成回调补偿释放资源。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ChoiceRequested = null;
            SelectionRequested = null;
            ClearSelectionIfOwned();

            for (int index = 0; index < rows.Count; index++)
            {
                if (rows[index] != null) UnityEngine.Object.Destroy(rows[index].gameObject);
            }

            rows.Clear();
            pendingOptionNames.Clear();
            if (optionPrefab != null)
            {
                ResSystem.Instance.UnLoad<GameObject>(optionPrefabPath);
                optionPrefab = null;
            }
        }

        #endregion
    }
}
