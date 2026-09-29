using System;
using System.Collections.Generic;
using RPG.Game.UI.WeaponDevelopment;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.LogModule;
using WS_Modules.Pooling;

namespace RPG.Game.UI.Views.WeaponDevelopment
{
    /// <summary>通过 WSFrame 对象池和 ScrollRect 管理升级页的动态属性对比行。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖显式绑定的 ScrollRect、带 VerticalLayoutGroup 与 ContentSizeFitter 的 Content，以及含 LayoutElement 和 PoolObjectIdentity 的升级属性行 Prefab。")]
    public sealed class EquipmentAttributeUpgradeListView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private EquipmentAttributeUpgradeLineView linePrefab;
        private readonly List<EquipmentAttributeUpgradeLineView> activeLineViews = new();

        #endregion

        #region 生命周期

        /// <summary>校验 ScrollRect、Content 与池化行 Prefab 的显式依赖。</summary>
        private void Awake()
        {
            ValidateConfiguration();
        }

        /// <summary>窗口销毁时归还所有动态行，避免对象池实例继续引用已销毁的 Content。</summary>
        private void OnDestroy()
        {
            ReleaseAll();
        }

        /// <summary>校验列表所需序列化引用及对象池身份配置。</summary>
        public void ValidateConfiguration()
        {
            if (scrollRect == null || contentRoot == null || linePrefab == null)
                throw new InvalidOperationException("[EquipmentAttributeUpgradeListView] ScrollRect、Content 或行 Prefab 未绑定。");
            if (scrollRect.content != contentRoot || scrollRect.viewport == null)
                throw new InvalidOperationException("[EquipmentAttributeUpgradeListView] ScrollRect 的 Viewport／Content 引用与列表配置不一致。");
            if (linePrefab.GetComponent<PoolObjectIdentity>() == null)
                throw new InvalidOperationException("[EquipmentAttributeUpgradeListView] 升级属性行 Prefab 根节点缺少 PoolObjectIdentity。");
        }

        #endregion

        #region 列表刷新

        /// <summary>按 Attribute 数量复用或补充对象池实例，由 Content 布局组件排列属性行。</summary>
        /// <param name="lines">按 Attribute ID 对齐的当前与预计属性数据。</param>
        public void Bind(IReadOnlyList<EquipmentAttributeUpgradeLineViewData> lines)
        {
            int count = lines?.Count ?? 0;
            int acquiredCount = 0;
            int recycledCount = 0;
            while (activeLineViews.Count < count)
            {
                AcquireLine();
                acquiredCount++;
            }
            while (activeLineViews.Count > count)
            {
                RecycleLastLine();
                recycledCount++;
            }

            // 布局组按兄弟顺序放置行，并从行 Prefab 的 LayoutElement 读取尺寸；脚本不再写入坐标或 Content 高度。
            for (int index = 0; index < count; index++)
            {
                activeLineViews[index].Bind(lines[index]);
                RectTransform lineRect = activeLineViews[index].transform as RectTransform;
                if (lineRect == null)
                    throw new InvalidOperationException("[EquipmentAttributeUpgradeListView] 升级行不是 UGUI RectTransform。");
                lineRect.SetSiblingIndex(index);
            }

            // 在复位滚动位置前让 LayoutGroup 与 ContentSizeFitter 同步完成本次高度计算。
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 1f;

            // 聚合记录一次列表刷新，避免多条属性通过对象池取回时产生逐行日志。
            if (acquiredCount > 0 || recycledCount > 0)
                WSLog.Log($"[EquipmentAttributeUpgradeListView] 刷新升级对比行，获取={acquiredCount}，归还={recycledCount}，activeCount={activeLineViews.Count}。");
        }

        /// <summary>清空升级对比行并归还对象池实例。</summary>
        public void Clear()
        {
            ReleaseAll();
            // 清空层级后由 ContentSizeFitter 将高度回算为零，再把视口移回顶部。
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 1f;
        }

        #endregion

        #region 对象池管理

        /// <summary>取出一条升级属性行并挂入序列化 Content。</summary>
        private void AcquireLine()
        {
            GameObject pooledObject = PoolManager.Instance.Get(linePrefab.gameObject, contentRoot);
            if (pooledObject == null)
                throw new InvalidOperationException("[EquipmentAttributeUpgradeListView] 对象池未能提供升级属性行。");
            EquipmentAttributeUpgradeLineView lineView = pooledObject.GetComponent<EquipmentAttributeUpgradeLineView>();
            if (lineView == null)
                throw new InvalidOperationException("[EquipmentAttributeUpgradeListView] 池化实例缺少升级属性行 View。");
            activeLineViews.Add(lineView);
        }

        /// <summary>将末尾升级属性行归还对象池。</summary>
        private void RecycleLastLine()
        {
            int lastIndex = activeLineViews.Count - 1;
            EquipmentAttributeUpgradeLineView lineView = activeLineViews[lastIndex];
            activeLineViews.RemoveAt(lastIndex);
            PoolManager.Instance.Recycle(lineView.gameObject);
        }

        /// <summary>归还全部活动行；列表为空时允许生命周期清理重复调用。</summary>
        private void ReleaseAll()
        {
            int releasedCount = activeLineViews.Count;
            for (int index = releasedCount - 1; index >= 0; index--)
                PoolManager.Instance.Recycle(activeLineViews[index].gameObject);
            activeLineViews.Clear();
            if (releasedCount > 0)
                WSLog.Log($"[EquipmentAttributeUpgradeListView] 清理列表并归还 {releasedCount} 条升级属性行。");
        }

        #endregion
    }
}
