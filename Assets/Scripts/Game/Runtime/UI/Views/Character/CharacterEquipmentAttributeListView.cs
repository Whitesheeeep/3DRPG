using System;
using System.Collections.Generic;
using RPG.Game.UI.Character;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.Pooling;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>通过 WSFrame 对象池管理武器与圣遗物页面的动态属性行。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterEquipmentAttributeListView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private CharacterEquipmentAttributeLineView linePrefab;
        [SerializeField] private RectTransform contentRoot;
        private readonly List<CharacterEquipmentAttributeLineView> activeLineViews = new();

        #endregion

        #region 生命周期

        /// <summary>验证池化行 Prefab 和行内容父节点已经通过 Inspector 绑定。</summary>
        private void Awake()
        {
            if (linePrefab == null || contentRoot == null)
                throw new InvalidOperationException("[CharacterEquipmentAttributeListView] 行 Prefab 或 Content 未绑定。");
            if (linePrefab.GetComponent<PoolObjectIdentity>() == null)
                throw new InvalidOperationException("[CharacterEquipmentAttributeListView] 属性行 Prefab 根节点缺少 PoolObjectIdentity。");
        }

        /// <summary>窗口销毁时归还所有动态行，避免对象池实例继续引用已销毁的父节点。</summary>
        private void OnDestroy()
        {
            ReleaseAll();
        }

        #endregion

        #region 列表刷新

        /// <summary>按输入行数复用或补充对象池实例，并按输入顺序绑定行数据。</summary>
        /// <param name="lines">属性名和值分开的行数据。</param>
        public void Bind(IReadOnlyList<CharacterEquipmentAttributeLineViewData> lines)
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

            for (int index = 0; index < count; index++)
                activeLineViews[index].Bind(lines[index]);

            // 一次刷新只汇总池变化，避免多条属性同时出现时逐行打印日志。
            if (acquiredCount > 0 || recycledCount > 0)
                WSLog.Log($"[CharacterEquipmentAttributeListView] 刷新装备属性行，获取={acquiredCount}，归还={recycledCount}，activeCount={activeLineViews.Count}。");
        }

        /// <summary>清空属性行并将池化实例归还给 WSFrame。</summary>
        public void Clear()
        {
            ReleaseAll();
        }

        #endregion

        #region 对象池管理

        /// <summary>取出一条动态属性行并挂入序列化 Content。</summary>
        private void AcquireLine()
        {
            GameObject pooledObject = PoolManager.Instance.Get(linePrefab.gameObject, contentRoot);
            if (pooledObject == null)
                throw new InvalidOperationException("[CharacterEquipmentAttributeListView] 对象池未能提供属性行。");
            CharacterEquipmentAttributeLineView lineView = pooledObject.GetComponent<CharacterEquipmentAttributeLineView>();
            if (lineView == null)
                throw new InvalidOperationException("[CharacterEquipmentAttributeListView] 池化实例缺少行 View。");
            activeLineViews.Add(lineView);
        }

        /// <summary>将末尾属性行清空并归还对象池。</summary>
        private void RecycleLastLine()
        {
            int lastIndex = activeLineViews.Count - 1;
            CharacterEquipmentAttributeLineView lineView = activeLineViews[lastIndex];
            activeLineViews.RemoveAt(lastIndex);
            PoolManager.Instance.Recycle(lineView.gameObject);
        }

        /// <summary>释放全部活动行；允许空列表重复调用以支持 OnDestroy。</summary>
        private void ReleaseAll()
        {
            int releasedCount = activeLineViews.Count;
            for (int index = releasedCount - 1; index >= 0; index--)
                PoolManager.Instance.Recycle(activeLineViews[index].gameObject);
            activeLineViews.Clear();
            if (releasedCount > 0)
                WSLog.Log($"[CharacterEquipmentAttributeListView] 销毁列表并归还 {releasedCount} 条属性行。");
        }

        #endregion
    }
}
