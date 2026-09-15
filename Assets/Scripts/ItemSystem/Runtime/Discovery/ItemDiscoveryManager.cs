using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.Singleton;

namespace RPG.ItemSystem
{
    /// <summary>
    /// 永久记录玩家历史上已经获得过的 ItemDefinition，不承担当前 New 提示的生命周期。
    /// </summary>
    public sealed class ItemDiscoveryManager : SingletonBase<ItemDiscoveryManager>
    {
        #region 状态字段

        // key：已解锁的 ItemDefinition 标识；集合不会因消耗、删除或确认 New 而回退。
        private readonly HashSet<ItemId> discoveredDefinitionIds = new HashSet<ItemId>();

        #endregion

        #region 生命周期

        /// <summary>创建空的物品发现状态；实例由 SingletonBase 延迟创建。</summary>
        private ItemDiscoveryManager()
        {
        }

        #endregion

        #region 查询与写入

        /// <summary>判断指定 ItemDefinition 是否已经被永久解锁。</summary>
        /// <param name="definitionId">待查询的物品 Definition 标识。</param>
        /// <returns>已经记录过时返回 true。</returns>
        /// <exception cref="ArgumentException">标识无效时抛出。</exception>
        /// <exception cref="InvalidOperationException">ItemManager 中不存在该定义时抛出。</exception>
        public bool IsDiscovered(ItemId definitionId)
        {
            ValidateDefinition(definitionId);
            return discoveredDefinitionIds.Contains(definitionId);
        }

        /// <summary>
        /// 记录一次 ItemDefinition 首次解锁；重复获得不会改变状态，也不会产生重复日志。
        /// </summary>
        /// <param name="definitionId">已经成功入库的物品 Definition 标识。</param>
        /// <returns>本次首次写入发现集合时返回 true。</returns>
        /// <exception cref="ArgumentException">标识无效时抛出。</exception>
        /// <exception cref="InvalidOperationException">ItemManager 中不存在该定义时抛出。</exception>
        public bool MarkDiscovered(ItemId definitionId)
        {
            ItemDefinition definition = ValidateDefinition(definitionId);
            if (!discoveredDefinitionIds.Add(definitionId)) return false;

            // 发现记录写入成功后只记录一次分类和稳定 ID，避免批量同名物品造成日志洪水。
            Debug.Log($"[ItemDiscoveryManager] 首次解锁物品 Definition：id={definitionId}, category={definition.Category}。", definition);
            return true;
        }

        /// <summary>获取按 ItemId 稳定排序的发现记录副本。</summary>
        /// <returns>不暴露内部 HashSet 的只读列表。</returns>
        public IReadOnlyList<ItemId> GetDiscoveredDefinitionIds()
        {
            var result = new List<ItemId>(discoveredDefinitionIds);
            result.Sort((left, right) => left.CompareTo(right));
            return result.AsReadOnly();
        }

        #endregion

        #region 存档支持

        /// <summary>清空运行时发现状态，供业务架构反初始化时使用。</summary>
        internal void ClearRuntimeState()
        {
            if (discoveredDefinitionIds.Count > 0)
                Debug.Log($"[ItemDiscoveryManager] 清空运行时发现状态：count={discoveredDefinitionIds.Count}。" );
            discoveredDefinitionIds.Clear();
        }

        /// <summary>用已完成校验的存档集合整体替换发现状态。</summary>
        /// <param name="restoredDefinitionIds">存档中的已发现 Definition 标识。</param>
        /// <exception cref="ArgumentNullException">集合为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">集合包含无效、未知或重复标识时抛出。</exception>
        internal void RestoreState(IReadOnlyList<ItemId> restoredDefinitionIds)
        {
            if (restoredDefinitionIds == null) throw new ArgumentNullException(nameof(restoredDefinitionIds));

            var restoredSet = new HashSet<ItemId>();
            for (int index = 0; index < restoredDefinitionIds.Count; index++)
            {
                ItemId definitionId = restoredDefinitionIds[index];
                ValidateDefinition(definitionId);
                if (!restoredSet.Add(definitionId))
                    throw new InvalidOperationException($"物品发现存档包含重复 Definition：{definitionId}。" );
            }

            discoveredDefinitionIds.Clear();
            foreach (ItemId definitionId in restoredSet) discoveredDefinitionIds.Add(definitionId);
            Debug.Log($"[ItemDiscoveryManager] 恢复发现状态：count={discoveredDefinitionIds.Count}。" );
        }

        #endregion

        #region 内部校验

        /// <summary>校验标识有效且仍能从 ItemManager 查询到 Definition。</summary>
        /// <param name="definitionId">待校验的 Definition 标识。</param>
        /// <returns>对应的物品 Definition。</returns>
        /// <exception cref="ArgumentException">标识无效时抛出。</exception>
        /// <exception cref="InvalidOperationException">找不到对应定义时抛出。</exception>
        private static ItemDefinition ValidateDefinition(ItemId definitionId)
        {
            if (!definitionId.IsValid)
                throw new ArgumentException("物品发现记录不能使用无效 ItemId。", nameof(definitionId));
            if (!ItemManager.Instance.TryGetDefinition(definitionId, out ItemDefinition definition) || definition == null)
                throw new InvalidOperationException($"物品发现记录引用了未知 ItemDefinition：{definitionId}。" );
            return definition;
        }

        #endregion
    }
}
