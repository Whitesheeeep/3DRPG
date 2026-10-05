using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace RPG.Game.UI.Views.HUD
{
    /// <summary>在静态 Horizontal 容器中创建并管理 GE 持续效果图标项。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖本节点的 RectTransform、GEDurationItem Prefab 及其根节点 GEDurationItemView；运行时仅在效果增加或移除时创建、销毁条目。")]
    public sealed class HUDGEListView : MonoBehaviour
    {
        #region 配置字段

        [SerializeField, Required, LabelText("持续效果项 Prefab")]
        private GameObject itemPrefab;

        #endregion

        #region 状态字段

        private readonly List<GEDurationItemView> activeItems = new();

        #endregion

        #region 配置与条目操作

        /// <summary>验证列表 Prefab 与方形图标尺寸。</summary>
        public void ValidateConfiguration()
        {
            if (itemPrefab == null)
                throw new InvalidOperationException($"[HUDGEListView] '{name}' 未绑定 GEDurationItem Prefab。");
            GEDurationItemView prefabView = itemPrefab.GetComponent<GEDurationItemView>();
            if (prefabView == null)
                throw new InvalidOperationException($"[HUDGEListView] Prefab '{itemPrefab.name}' 缺少 GEDurationItemView。");
            prefabView.ValidateConfiguration();
            if (transform is not RectTransform)
                throw new InvalidOperationException($"[HUDGEListView] '{name}' 必须挂在 RectTransform 横向容器上。");
        }

        /// <summary>添加新效果图标，或更新同一 Runtime 与 Tag 的既有图标。</summary>
        /// <param name="tag">该条目代表的 GrantedTag。</param>
        /// <param name="runtime">持续 GE Runtime。</param>
        /// <param name="icon">配置的 Sprite。</param>
        /// <param name="duration">用于进度比例的总持续时间。</param>
        /// <param name="isInfinite">是否为 Infinite GE。</param>
        public void AddOrUpdate(
            GameplayTag tag,
            GameEffectRuntime runtime,
            Sprite icon,
            float duration,
            bool isInfinite)
        {
            GEDurationItemView itemView = FindItem(runtime, tag);
            if (itemView == null)
            {
                var itemObject = Instantiate(itemPrefab, transform, false);
                RectTransform itemTransform = (RectTransform)itemObject.transform;
                // 模板根节点是拉伸锚点，列表项覆盖为固定正方形并保留原始比例。
                float itemRatio =  itemTransform.rect.width / itemTransform.rect.height;
                itemTransform.anchorMin = new Vector2(0.5f, 0.5f);
                itemTransform.anchorMax = new Vector2(0.5f, 0.5f);
                itemTransform.pivot = new Vector2(0.5f, 0.5f);
                itemTransform.anchoredPosition = Vector2.zero;
                itemTransform.localScale = Vector3.one;
                itemTransform.localRotation = Quaternion.identity;
                itemTransform.sizeDelta = new Vector2(itemTransform.rect.height * itemRatio, itemTransform.rect.height);

                itemView = itemObject.GetComponent<GEDurationItemView>();
                activeItems.Add(itemView);
            }

            itemView.Bind(tag, runtime, icon, duration, isInfinite);
        }

        /// <summary>移除指定 Runtime 对应的全部 Tag 图标。</summary>
        /// <param name="runtime">生命周期结束的持续 GE Runtime。</param>
        public int RemoveRuntime(GameEffectRuntime runtime)
        {
            int removedCount = 0;
            for (int itemIndex = activeItems.Count - 1; itemIndex >= 0; itemIndex--)
            {
                GEDurationItemView itemView = activeItems[itemIndex];
                if (itemView == null || !ReferenceEquals(itemView.Runtime, runtime)) continue;
                itemView.Clear();
                Destroy(itemView.gameObject);
                activeItems.RemoveAt(itemIndex);
                removedCount++;
            }

            return removedCount;
        }

        /// <summary>清除容器内所有运行时创建的图标条目。</summary>
        public void Clear()
        {
            for (int itemIndex = activeItems.Count - 1; itemIndex >= 0; itemIndex--)
            {
                GEDurationItemView itemView = activeItems[itemIndex];
                if (itemView == null) continue;
                itemView.Clear();
                Destroy(itemView.gameObject);
            }
            activeItems.Clear();
        }

        #endregion

        #region 内部查询

        /// <summary>在线性条目序列中查找 Runtime 与 Tag 完全相同的既有视图。</summary>
        /// <param name="runtime">目标 GE Runtime。</param>
        /// <param name="tag">目标 GrantedTag。</param>
        /// <returns>已存在的 View；没有匹配项时返回 null。</returns>
        private GEDurationItemView FindItem(GameEffectRuntime runtime, GameplayTag tag)
        {
            for (int itemIndex = 0; itemIndex < activeItems.Count; itemIndex++)
            {
                GEDurationItemView itemView = activeItems[itemIndex];
                if (itemView != null && itemView.Matches(runtime, tag)) return itemView;
            }

            return null;
        }

        #endregion
    }
}
