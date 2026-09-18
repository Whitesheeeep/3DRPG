using System;
using System.Collections.Generic;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Views.Bag;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.Pooling;

namespace RPG.Game.UI.Views.Common
{
    /// <summary>使用横向布局和对象池渲染少量培养素材的条目列表。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖横向 ScrollRect、Viewport、Content、HorizontalLayoutGroup、ContentSizeFitter、BagItem.prefab 和已初始化 PoolManager。")]
    public sealed class HorizontalBagItemListView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform content;
        [SerializeField] private HorizontalLayoutGroup horizontalLayout;
        [SerializeField] private ContentSizeFitter contentSizeFitter;
        [SerializeField] private GameObject itemPrefab;

        #endregion

        #region 状态字段

        private readonly List<BagItemView> activeItemViews = new();

        #endregion

        #region 生命周期与校验

        /// <summary>校验横向列表的显式序列化依赖。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            scrollRect.horizontal = true;
            scrollRect.vertical = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            horizontalLayout.enabled = true;
            horizontalLayout.childAlignment = TextAnchor.MiddleLeft;
            horizontalLayout.childForceExpandWidth = false;
            horizontalLayout.childForceExpandHeight = false;
            // 条目宽高由列表按模板比例直接计算，禁止布局组用 LayoutElement 覆盖高度。
            horizontalLayout.childControlWidth = false;
            horizontalLayout.childControlHeight = false;
            contentSizeFitter.enabled = true;
            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        /// <summary>销毁时回收当前横向列表中的池对象。</summary>
        private void OnDestroy()
        {
            RecycleAll();
        }

        /// <summary>校验横向 ScrollView、Content 和条目模板。</summary>
        public void ValidateConfiguration()
        {
            if (scrollRect == null || viewport == null || content == null || horizontalLayout == null ||
                contentSizeFitter == null || itemPrefab == null)
                throw new InvalidOperationException("[HorizontalBagItemListView] 横向素材列表存在未绑定控件。");
        }

        #endregion

        #region 展示

        /// <summary>替换横向素材条目；空列表只保留空 Content，不显示占位文字。</summary>
        /// <param name="entries">需要显示的条目快照。</param>
        public void Bind(IReadOnlyList<BagItemViewData> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            // 绑定会重建 Content，先停止旧惯性并记录当前偏移，避免刷新时列表突然回到左端。
            scrollRect.StopMovement();
            float previousContentOffset = content.anchoredPosition.x;
            RecycleAll();
            Vector2 itemSize = CalculateItemSize();
            for (int index = 0; index < entries.Count; index++)
            {
                BagItemViewData entry = entries[index] ?? throw new InvalidOperationException(
                    $"[HorizontalBagItemListView] 条目 {index} 为空。");
                GameObject instance = PoolManager.Instance.Get(itemPrefab, content);
                BagItemView itemView = instance.GetComponent<BagItemView>();
                if (itemView == null)
                    throw new InvalidOperationException("[HorizontalBagItemListView] BagItem.prefab 缺少 BagItemView。");

                RectTransform rectTransform = instance.transform as RectTransform;
                if (rectTransform == null)
                    throw new InvalidOperationException("[HorizontalBagItemListView] BagItem.prefab 根节点缺少 RectTransform。");

                // PoolManager 取出对象时保持 worldPositionStays，必须在布局前同步覆盖池根节点留下的坐标。
                rectTransform.SetParent(content, false);
                rectTransform.anchorMin = new Vector2(0f, 0.5f);
                rectTransform.anchorMax = new Vector2(0f, 0.5f);
                rectTransform.pivot = new Vector2(0f, 0.5f);
                rectTransform.anchoredPosition = Vector2.zero;
                rectTransform.localScale = Vector3.one;
                rectTransform.localRotation = Quaternion.identity;
                rectTransform.sizeDelta = itemSize;

                itemView.Bind(entry, null);
                itemView.SetInteractable(false);
                itemView.SetSelected(false);
                activeItemViews.Add(itemView);
            }

            // 立即完成 HorizontalLayoutGroup 与 ContentSizeFitter，避免对象先以池中旧位置绘制一帧。
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            float restoredContentOffset = entries.Count == 0
                ? 0f
                : ClampContentOffset(previousContentOffset);
            Vector2 contentPosition = content.anchoredPosition;
            contentPosition.x = restoredContentOffset;
            content.anchoredPosition = contentPosition;
            scrollRect.StopMovement();

            Debug.Log(
                $"[HorizontalBagItemListView] 重新绑定横向素材列表：Count={entries.Count}，ItemSize={itemSize}，Offset={restoredContentOffset}。",
                this);
        }

        /// <summary>根据条目模板比例和 Content 当前可用高度计算列表条目尺寸。</summary>
        /// <returns>保持 BagItem.prefab 原始宽高比例的条目尺寸。</returns>
        private Vector2 CalculateItemSize()
        {
            RectTransform prefabRectTransform = itemPrefab.transform as RectTransform;
            if (prefabRectTransform == null)
                throw new InvalidOperationException("[HorizontalBagItemListView] BagItem.prefab 根节点缺少 RectTransform。");

            float prefabWidth = Mathf.Abs(prefabRectTransform.rect.width);
            float prefabHeight = Mathf.Abs(prefabRectTransform.rect.height);
            if (prefabWidth <= Mathf.Epsilon || prefabHeight <= Mathf.Epsilon)
                throw new InvalidOperationException("[HorizontalBagItemListView] BagItem.prefab 根节点宽高必须大于零。");

            float availableHeight = Mathf.Abs(content.rect.height);
            if (availableHeight <= Mathf.Epsilon)
                availableHeight = Mathf.Abs(viewport.rect.height);
            availableHeight -= horizontalLayout.padding.top + horizontalLayout.padding.bottom;
            if (availableHeight <= Mathf.Epsilon)
                availableHeight = prefabHeight;

            float width = availableHeight * (prefabWidth / prefabHeight);
            return new Vector2(width, availableHeight);
        }

        /// <summary>将 Content 的横向偏移限制在当前 Viewport 可见范围内。</summary>
        /// <param name="requestedOffset">绑定前保存的 Content 横向偏移。</param>
        /// <returns>不超出左右滚动边界的横向偏移。</returns>
        private float ClampContentOffset(float requestedOffset)
        {
            float maxScrollOffset = Mathf.Max(0f, content.rect.width - viewport.rect.width);
            return Mathf.Clamp(requestedOffset, -maxScrollOffset, 0f);
        }

        #endregion

        #region 池生命周期

        /// <summary>回收当前显示的所有素材条目。</summary>
        private void RecycleAll()
        {
            for (int index = 0; index < activeItemViews.Count; index++)
            {
                BagItemView itemView = activeItemViews[index];
                if (itemView != null) PoolManager.Instance.Recycle(itemView.gameObject);
            }

            activeItemViews.Clear();
        }

        #endregion
    }
}
