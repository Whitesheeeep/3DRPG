using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.DialogueSystemModule;
using UnityEngine;
using UnityEngine.EventSystems;
using WS_Modules.ResLoadModule;

namespace WS_Modules.UIModule
{
    /// <summary>DialogueWindow 内的 Choice View，负责行复用、置灰和稳定 NodeId 转发。</summary>
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
            pendingChoices.Clear();
            if (choices != null)
            {
                for (int index = 0; index < choices.Count; index++)
                    pendingChoices.Add(choices[index]);
            }

            ApplyPendingState();
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
                row.Initialize(HandleChoiceRequested);
                rows.Add(row);
            }
        }

        /// <summary>把缓存 Choice 状态应用到所有行并聚焦第一个可用选项。</summary>
        private void ApplyPendingState()
        {
            if (optionPrefab == null || disposed) return;

            EnsureRowCount(Mathf.Max(initialRowCount, pendingChoices.Count));
            int firstAvailableIndex = -1;
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
                row.SetOption(index, choice.Text, false, choice.IsAvailable);
                if (firstAvailableIndex < 0 && choice.IsAvailable) firstAvailableIndex = index;
            }

            if (pendingChoices.Count > 0)
            {
                SetVisible(true);
                if (firstAvailableIndex >= 0 && EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(rows[firstAvailableIndex].gameObject);
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

        /// <summary>仅当 EventSystem 当前焦点属于本 View 时清除焦点。</summary>
        private void ClearSelectionIfOwned()
        {
            GameObject selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(choiceRoot) && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
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
