using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.Game.UI.Character;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.Pooling;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>顶部角色头像条，按拥有角色数量从对象池获取头像项并支持横向滚动。</summary>
    [DisallowMultipleComponent]
    [InfoBox("头像项 Prefab 根节点必须显式挂载 CharacterPortraitItemView 与 PoolObjectIdentity；头像条通过根组件绑定数据。")]
    public sealed class CharacterRosterStripView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private CharacterPortraitItemView portraitItemPrefab;
        [SerializeField] private RectTransform contentRoot;
        private readonly List<CharacterPortraitItemView> activePortraitItems = new();
        private bool initialized;

        #endregion

        #region 事件

        /// <summary>头像选择意图。</summary>
        public event Action<CharacterId> CharacterSelected;

        #endregion

        #region 生命周期

        /// <summary>校验对象池 Prefab 与横向 Content 配置。</summary>
        private void Awake()
        {
            if (portraitItemPrefab == null || contentRoot == null)
                throw new InvalidOperationException("[CharacterRosterStripView] 未绑定头像项 Prefab 或 Content。");
            if (portraitItemPrefab.GetComponent<PoolObjectIdentity>() == null)
                throw new InvalidOperationException("[CharacterRosterStripView] 头像项 Prefab 根节点缺少 PoolObjectIdentity。");
            initialized = true;
        }

        /// <summary>窗口销毁前归还活动头像实例，避免池对象保留窗口事件订阅。</summary>
        private void OnDestroy()
        {
            if (!initialized) return;
            ReleaseAllPortraitItems();
        }

        #endregion

        #region 绑定

        /// <summary>将全部角色绑定到头像项，数量变化时按差额获取或归还对象池实例。</summary>
        /// <param name="entries">已稳定排序的角色头像数据。</param>
        public void Bind(IReadOnlyList<CharacterRosterEntryViewData> entries)
        {
            int count = entries?.Count ?? 0;
            while (activePortraitItems.Count < count)
                AcquirePortraitItem();
            while (activePortraitItems.Count > count)
                RecycleLastPortraitItem();
            for (int index = 0; index < activePortraitItems.Count; index++)
                activePortraitItems[index].Bind(entries[index]);
        }

        /// <summary>隐藏并归还全部活动头像项。</summary>
        public void Clear()
        {
            ReleaseAllPortraitItems();
        }

        #endregion

        #region 内部状态与事件

        /// <summary>转发头像项的角色选择意图。</summary>
        /// <param name="characterId">被点击角色。</param>
        private void HandleCharacterSelected(CharacterId characterId)
        {
            CharacterSelected?.Invoke(characterId);
        }

        /// <summary>从对象池取出头像项并注册窗口级角色选择回调。</summary>
        private void AcquirePortraitItem()
        {
            GameObject portraitObject = PoolManager.Instance.Get(portraitItemPrefab.gameObject, contentRoot);
            if (portraitObject == null)
                throw new InvalidOperationException("[CharacterRosterStripView] 对象池未能提供角色头像项。");

            // Prefab 根节点通过 Inspector 显式配置 View；对象池仅管理对象生命周期，不动态补组件。
            CharacterPortraitItemView portraitItem = portraitObject.GetComponent<CharacterPortraitItemView>();
            if (portraitItem == null)
                throw new InvalidOperationException("[CharacterRosterStripView] 池化头像实例缺少 CharacterPortraitItemView。");
            portraitItem.Clicked += HandleCharacterSelected;
            activePortraitItems.Add(portraitItem);
            WSLog.Log($"[CharacterRosterStripView] 获取头像项，activeCount={activePortraitItems.Count}。");
        }

        /// <summary>解除末尾头像项事件并归还对象池。</summary>
        private void RecycleLastPortraitItem()
        {
            int lastIndex = activePortraitItems.Count - 1;
            CharacterPortraitItemView portraitItem = activePortraitItems[lastIndex];
            activePortraitItems.RemoveAt(lastIndex);
            portraitItem.Clicked -= HandleCharacterSelected;
            portraitItem.Clear();
            PoolManager.Instance.Recycle(portraitItem.gameObject);
            WSLog.Log($"[CharacterRosterStripView] 归还头像项，activeCount={activePortraitItems.Count}。");
        }

        /// <summary>在窗口隐藏或销毁时解除事件并归还所有头像项。</summary>
        private void ReleaseAllPortraitItems()
        {
            int releasedCount = activePortraitItems.Count;
            for (int index = releasedCount - 1; index >= 0; index--)
            {
                CharacterPortraitItemView portraitItem = activePortraitItems[index];
                portraitItem.Clicked -= HandleCharacterSelected;
                portraitItem.Clear();
                PoolManager.Instance.Recycle(portraitItem.gameObject);
            }
            activePortraitItems.Clear();
            if (releasedCount > 0)
                WSLog.Log($"[CharacterRosterStripView] 清理头像条并归还 {releasedCount} 个对象池实例。");
        }

        #endregion
    }
}
