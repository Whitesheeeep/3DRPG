using System;
using System.Collections.Generic;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Views.Bag;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Common
{
    /// <summary>武器培养窗口内可复用的候选物品选择面板。</summary>
    public sealed class ItemSelectionPanelView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private BagGridView gridView;
        [SerializeField] private Button returnButton;
        [SerializeField] private GameObject emptyText;
        private Action<BagEntryKey> boundEntryCallback;

        #endregion

        #region 事件

        /// <summary>候选条目被点击时触发。</summary>
        public event Action<BagEntryKey> EntryClicked;
        /// <summary>数量模式候选条目请求调整数量时触发。</summary>
        public event Action<BagItemQuantityIntent> QuantityChangeRequested;
        /// <summary>用户请求退出选择面板时触发。</summary>
        public event Action ReturnRequested;

        #endregion

        #region 生命周期与绑定

        /// <summary>绑定返回按钮和网格点击转发。</summary>
        private void Awake()
        {
            returnButton?.onClick.AddListener(HandleReturnClicked);
        }

        /// <summary>销毁时移除返回按钮监听。</summary>
        private void OnDestroy()
        {
            returnButton?.onClick.RemoveListener(HandleReturnClicked);
        }

        /// <summary>校验正式 Prefab 的候选网格、返回按钮和空状态提示绑定。</summary>
        public void ValidateConfiguration()
        {
            if (gridView == null || returnButton == null || emptyText == null)
                throw new InvalidOperationException("[ItemSelectionPanelView] 选择面板存在未绑定控件。");
        }

        /// <summary>绑定候选列表。</summary>
        /// <param name="entries">候选条目。</param>
        /// <param name="selectedEntryKeys">当前已选中的候选条目标识。</param>
        /// <param name="onEntryClicked">条目点击回调。</param>
        public void Bind(IReadOnlyList<BagItemViewData> entries,
            IReadOnlyCollection<BagEntryKey> selectedEntryKeys, Action<BagEntryKey> onEntryClicked)
        {
            boundEntryCallback = onEntryClicked;
            // 先写入多选稳定键，再创建当前可见池对象，避免列表重绑时短暂套用上一批选择。
            gridView?.SetSelectedEntries(selectedEntryKeys ?? Array.Empty<BagEntryKey>());
            gridView?.Bind(entries ?? Array.Empty<BagItemViewData>(), HandleEntryClicked);
            emptyText.SetActive(entries == null || entries.Count == 0);
        }

        /// <summary>绑定支持数量调整的候选列表，并在创建可见池对象前投影选择键。</summary>
        /// <param name="entries">候选条目。</param>
        /// <param name="selectedEntryKeys">当前数量大于零的条目标识。</param>
        public void BindQuantitySelection(IReadOnlyList<BagItemViewData> entries,
            IReadOnlyCollection<BagEntryKey> selectedEntryKeys)
        {
            boundEntryCallback = null;
            gridView?.SetSelectedEntries(selectedEntryKeys ?? Array.Empty<BagEntryKey>());
            gridView?.BindQuantitySelection(entries ?? Array.Empty<BagItemViewData>(),
                intent => QuantityChangeRequested?.Invoke(intent));
            emptyText.SetActive(entries == null || entries.Count == 0);
        }

        /// <summary>设置面板网格是否可交互。</summary>
        /// <param name="interactable">是否可交互。</param>
        public void SetInteractable(bool interactable) => gridView?.SetInteractable(interactable);

        /// <summary>发送返回面板请求。</summary>
        private void HandleReturnClicked() => ReturnRequested?.Invoke();

        /// <summary>把网格条目点击转发为面板级用户意图。</summary>
        /// <param name="entryKey">被点击的候选条目标识。</param>
        private void HandleEntryClicked(BagEntryKey entryKey)
        {
            EntryClicked?.Invoke(entryKey);
            boundEntryCallback?.Invoke(entryKey);
        }

        #endregion
    }
}
