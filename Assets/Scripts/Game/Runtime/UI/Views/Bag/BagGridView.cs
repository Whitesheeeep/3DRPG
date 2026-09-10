using System;
using System.Collections.Generic;
using RPG.Game.UI.Bag;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.Pooling;

namespace RPG.Game.UI.Views.Bag
{
    /// <summary>
    /// 背包手动虚拟化网格。
    /// 只为当前可见行及上下缓冲行创建池化 Item View，不随总数据量线性增加 GameObject。
    /// </summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 ScrollRect、Viewport、Content、ItemBK 池化模板和已初始化 PoolManager。")]
    public sealed class BagGridView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform content;
        [SerializeField] private GameObject itemPrefab;
        // 采用旧 ItemBK/GridLayoutGroup 的 50×50 单元和 5 像素间距，保持现有背包的信息密度。
        [SerializeField, MinValue(1f)] private float cellWidth = 50f;
        [SerializeField, MinValue(1f)] private float cellHeight = 50f;
        [SerializeField, MinValue(0f)] private float horizontalSpacing = 5f;
        [SerializeField, MinValue(0f)] private float verticalSpacing = 5f;
        [SerializeField, MinValue(0f)] private float paddingLeft = 5f;
        [SerializeField, MinValue(0f)] private float paddingTop = 5f;
        [SerializeField, MinValue(0f)] private float paddingBottom = 5f;
        [SerializeField, MinValue(0)] private int bufferRows = 2;

        #endregion

        #region 状态字段

        private readonly Dictionary<BagEntryKey, BagItemView> viewByEntryKeyMap = new();
        private readonly Dictionary<int, BagItemView> viewByIndexMap = new();
        private IReadOnlyList<BagItemViewData> entries = Array.Empty<BagItemViewData>();
        private Action<BagEntryKey> selectionCallback;
        private BagEntryKey? selectedEntryKey;
        private int columnCount = 1;
        private bool isInteractable = true;

        #endregion

        #region Unity 生命周期

        /// <summary>校验 Prefab 依赖并监听滚动变化；窗口销毁时解绑回调。</summary>
        private void Awake()
        {
            if (scrollRect == null) throw new InvalidOperationException("[BagGridView] 未绑定 ScrollRect。 ");
            if (viewport == null) throw new InvalidOperationException("[BagGridView] 未绑定 Viewport。 ");
            if (content == null) throw new InvalidOperationException("[BagGridView] 未绑定 Content。 ");
            if (itemPrefab == null) throw new InvalidOperationException("[BagGridView] 未绑定 ItemBK 池化模板。 ");
            scrollRect.onValueChanged.AddListener(OnScrollValueChanged);
        }

        /// <summary>清理滚动监听并回收所有当前可见格子。</summary>
        private void OnDestroy()
        {
            if (scrollRect != null) scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
            RecycleAllViews();
        }

        /// <summary>
        /// 视口尺寸改变时重新计算列数和可见范围。
        /// Unity 首帧布局完成后通常才得到最终宽度，因此不能只在 Bind 时计算一次。
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            if (entries == null || content == null || viewport == null) return;
            RecalculateLayout();
            RefreshVisibleViews();
        }

        #endregion

        #region 数据绑定

        /// <summary>绑定排序后的列表数据并计算内容高度。</summary>
        /// <param name="newEntries">当前分类的稳定条目快照。</param>
        /// <param name="onSelected">点击条目后的回调。</param>
        public void Bind(IReadOnlyList<BagItemViewData> newEntries, Action<BagEntryKey> onSelected)
        {
            // 列表快照更换后旧 View 的 EntryKey 已不再属于当前集合，先完整回收可见对象，
            // 避免虚拟化索引复用时残留旧键映射和旧点击回调。
            RecycleAllViews();
            entries = newEntries ?? throw new ArgumentNullException(nameof(newEntries));
            selectionCallback = onSelected;
            RecalculateLayout();
            RefreshVisibleViews();
        }

        /// <summary>设置当前稳定选择并同步可见格子的选中背景。</summary>
        /// <param name="entryKey">当前选择，空值表示清空。</param>
        public void SetSelection(BagEntryKey? entryKey)
        {
            selectedEntryKey = entryKey;
            foreach (KeyValuePair<BagEntryKey, BagItemView> pair in viewByEntryKeyMap)
                pair.Value.SetSelected(entryKey.HasValue && pair.Key == entryKey.Value);
        }

        /// <summary>启用或禁用网格中所有可见格子的点击。</summary>
        /// <param name="interactable">是否允许点击。</param>
        public void SetInteractable(bool interactable)
        {
            isInteractable = interactable;
            foreach (KeyValuePair<BagEntryKey, BagItemView> pair in viewByEntryKeyMap)
                pair.Value.SetInteractable(interactable);
        }

        /// <summary>把滚动位置恢复到列表顶部。</summary>
        public void ScrollToTop()
        {
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
        }

        #endregion

        #region 虚拟化布局

        /// <summary>按视口宽度计算列数和 Content 高度。</summary>
        private void RecalculateLayout()
        {
            float availableWidth = Mathf.Max(1f, viewport.rect.width);
            columnCount = Mathf.Max(1, Mathf.FloorToInt(
                (availableWidth + horizontalSpacing) / (cellWidth + horizontalSpacing)));
            int rowCount = entries.Count == 0 ? 0 : Mathf.CeilToInt(entries.Count / (float)columnCount);
            float contentHeight = Mathf.Max(viewport.rect.height,
                paddingTop + rowCount * cellHeight + Mathf.Max(0, rowCount - 1) * verticalSpacing + paddingBottom);
            content.sizeDelta = new Vector2(content.sizeDelta.x, contentHeight);
        }

        /// <summary>只刷新当前可见行和上下缓冲行的池化格子。</summary>
        private void RefreshVisibleViews()
        {
            if (viewport == null || content == null || itemPrefab == null) return;
            int rowCount = entries.Count == 0 ? 0 : Mathf.CeilToInt(entries.Count / (float)columnCount);
            float scrollOffset = Mathf.Max(0f, content.rect.height - viewport.rect.height) *
                                 (1f - (scrollRect?.verticalNormalizedPosition ?? 1f));
            int firstVisibleRow = Mathf.Max(0, Mathf.FloorToInt(scrollOffset / (cellHeight + verticalSpacing)) - bufferRows);
            int visibleRowCount = Mathf.CeilToInt(viewport.rect.height / (cellHeight + verticalSpacing)) + bufferRows * 2 + 1;
            int lastVisibleRow = Mathf.Min(rowCount - 1, firstVisibleRow + visibleRowCount - 1);
            var requiredIndices = new HashSet<int>();
            for (int row = firstVisibleRow; row <= lastVisibleRow; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    int index = row * columnCount + column;
                    if (index >= 0 && index < entries.Count) requiredIndices.Add(index);
                }
            }

            var staleIndices = new List<int>();
            foreach (KeyValuePair<int, BagItemView> pair in viewByIndexMap)
                if (!requiredIndices.Contains(pair.Key)) staleIndices.Add(pair.Key);
            for (int index = 0; index < staleIndices.Count; index++) RecycleIndex(staleIndices[index]);

            foreach (int index in requiredIndices)
            {
                if (!viewByIndexMap.TryGetValue(index, out BagItemView view))
                {
                    GameObject instance = PoolManager.Instance.Get(itemPrefab, content);
                    view = instance.GetComponent<BagItemView>();
                    if (view == null) throw new InvalidOperationException("ItemBK.prefab 缺少 BagItemView 组件。");
                    viewByIndexMap.Add(index, view);
                    viewByEntryKeyMap[entries[index].EntryKey] = view;
                }

                RectTransform rectTransform = view.transform as RectTransform;
                if (rectTransform != null)
                {
                    // 池对象可能从 PoolRoot 以 worldPositionStays=true 重新挂载；每次显示前恢复
                    // Content 左上角坐标契约，确保首次实例化和复用对象都使用同一局部坐标系。
                    rectTransform.anchorMin = new Vector2(0f, 1f);
                    rectTransform.anchorMax = new Vector2(0f, 1f);
                    rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    rectTransform.localScale = Vector3.one;
                    rectTransform.localRotation = Quaternion.identity;
                    // 运行时尺寸以网格配置为准，避免模板保留旧布局尺寸后点击区域与背景错位。
                    rectTransform.sizeDelta = new Vector2(cellWidth, cellHeight);
                    rectTransform.anchoredPosition = GetPosition(index);
                }
                view.Bind(entries[index], selectionCallback);
                view.SetInteractable(isInteractable);
                view.SetSelected(selectedEntryKey.HasValue && selectedEntryKey.Value == entries[index].EntryKey);
            }
        }

        /// <summary>根据条目索引计算 Content 顶部坐标系中的锚点位置。</summary>
        private Vector2 GetPosition(int index)
        {
            int row = index / columnCount;
            int column = index % columnCount;
            float x = paddingLeft + column * (cellWidth + horizontalSpacing) + cellWidth * 0.5f;
            float y = -(paddingTop + row * (cellHeight + verticalSpacing) + cellHeight * 0.5f);
            return new Vector2(x, y);
        }

        /// <summary>滚动时只更新可见范围，不重新排序或加载资源。</summary>
        private void OnScrollValueChanged(Vector2 _)
        {
            RefreshVisibleViews();
        }

        /// <summary>回收一个离开缓冲区的条目。</summary>
        private void RecycleIndex(int index)
        {
            if (!viewByIndexMap.TryGetValue(index, out BagItemView view)) return;
            viewByIndexMap.Remove(index);
            viewByEntryKeyMap.Remove(view.EntryKey);
            PoolManager.Instance.Recycle(view.gameObject);
        }

        /// <summary>回收当前所有格子，隐藏窗口或销毁时释放动态 UI。</summary>
        private void RecycleAllViews()
        {
            var indices = new List<int>(viewByIndexMap.Keys);
            for (int index = 0; index < indices.Count; index++) RecycleIndex(indices[index]);
            viewByEntryKeyMap.Clear();
        }

        #endregion
    }
}
