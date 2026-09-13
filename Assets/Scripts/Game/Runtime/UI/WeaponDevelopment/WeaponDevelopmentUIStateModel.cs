using System;
using System.Collections.Generic;
using RPG.ItemSystem;

namespace RPG.Game.UI.WeaponDevelopment
{
    /// <summary>保存武器培养窗口的页面、升级素材数量和精炼临时选择状态。</summary>
    public sealed class WeaponDevelopmentUIStateModel
    {
        #region 状态

        private readonly HashSet<EquipmentInstanceId> selectedMaterialIds = new();
        // key：武器强化素材 ItemId；value：本次培养会话选择的堆叠数量。
        private readonly Dictionary<ItemId, int> selectedQuantityByItemIdMap = new();
        private WeaponDevelopmentPage currentPage = WeaponDevelopmentPage.Growth;
        private bool selectionPanelVisible;

        #endregion

        #region 属性与事件

        /// <summary>页面发生变化时触发。</summary>
        public event Action Changed;

        /// <summary>获取当前页面。</summary>
        public WeaponDevelopmentPage CurrentPage => currentPage;

        /// <summary>获取选择面板是否展开。</summary>
        public bool SelectionPanelVisible => selectionPanelVisible;

        /// <summary>获取当前选中的材料实例。</summary>
        public IReadOnlyCollection<EquipmentInstanceId> SelectedMaterialIds => selectedMaterialIds;

        /// <summary>获取当前选中的武器强化素材数量。</summary>
        public IReadOnlyDictionary<ItemId, int> SelectedEnhancementQuantities => selectedQuantityByItemIdMap;

        #endregion

        #region 状态操作

        /// <summary>切换当前页面并关闭选择面板。</summary>
        /// <param name="page">目标页面。</param>
        public void SetPage(WeaponDevelopmentPage page)
        {
            currentPage = page;
            selectionPanelVisible = false;
            Changed?.Invoke();
        }

        /// <summary>设置选择面板显隐。</summary>
        /// <param name="visible">目标显隐。</param>
        public void SetSelectionPanelVisible(bool visible)
        {
            selectionPanelVisible = visible;
            Changed?.Invoke();
        }

        /// <summary>切换一个精炼材料实例的选择状态。</summary>
        /// <param name="instanceId">材料实例。</param>
        /// <param name="maxCount">允许的最大数量。</param>
        public void ToggleMaterial(EquipmentInstanceId instanceId, int maxCount)
        {
            if (selectedMaterialIds.Contains(instanceId)) selectedMaterialIds.Remove(instanceId);
            else if (selectedMaterialIds.Count < maxCount) selectedMaterialIds.Add(instanceId);
            Changed?.Invoke();
        }

        /// <summary>按用户输入增减一种武器强化素材的临时选择数量。</summary>
        /// <param name="itemId">强化素材标识。</param>
        /// <param name="delta">本次数量变化，正数为增加，负数为减少。</param>
        /// <param name="ownedQuantity">背包当前拥有数量。</param>
        public void AdjustEnhancementMaterial(ItemId itemId, int delta, int ownedQuantity)
        {
            if (!itemId.IsValid || delta == 0 || ownedQuantity < 0) return;
            int previous = selectedQuantityByItemIdMap.TryGetValue(itemId, out int value) ? value : 0;
            long requested = (long)previous + delta;
            long clamped = requested < 0L ? 0L : requested > ownedQuantity ? ownedQuantity : requested;
            int next = (int)clamped;
            if (next == 0) selectedQuantityByItemIdMap.Remove(itemId);
            else selectedQuantityByItemIdMap[itemId] = next;
            if (next != previous) Changed?.Invoke();
        }

        /// <summary>用自动添加结果替换当前武器强化素材选择。</summary>
        /// <param name="sourceQuantityByItemIdMap">按素材 ID 聚合的数量映射。</param>
        public void ReplaceEnhancementMaterials(IReadOnlyDictionary<ItemId, int> sourceQuantityByItemIdMap)
        {
            if (sourceQuantityByItemIdMap == null)
                throw new ArgumentNullException(nameof(sourceQuantityByItemIdMap));
            selectedQuantityByItemIdMap.Clear();
            foreach (KeyValuePair<ItemId, int> pair in sourceQuantityByItemIdMap)
                if (pair.Key.IsValid && pair.Value > 0) selectedQuantityByItemIdMap[pair.Key] = pair.Value;
            Changed?.Invoke();
        }

        /// <summary>清空当前会话内的武器强化素材选择。</summary>
        public void ClearEnhancementMaterials()
        {
            if (selectedQuantityByItemIdMap.Count == 0) return;
            selectedQuantityByItemIdMap.Clear();
            Changed?.Invoke();
        }

        /// <summary>清空页面临时选择并恢复成长页。</summary>
        public void Reset()
        {
            currentPage = WeaponDevelopmentPage.Growth;
            selectionPanelVisible = false;
            selectedMaterialIds.Clear();
            selectedQuantityByItemIdMap.Clear();
            Changed?.Invoke();
        }

        #endregion
    }
}
