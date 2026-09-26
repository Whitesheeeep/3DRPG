using System;
using System.Collections.Generic;
using RPG.SaveSystem;

namespace RPG.ItemSystem
{
    #region 发现记录快照

    /// <summary>物品 Definition 首次解锁记录的版本化存档快照。</summary>
    [Serializable]
    public sealed class ItemDiscoverySaveSnapshot : ISaveModuleSnapshot
    {
        /// <summary>创建空发现记录快照。</summary>
        public ItemDiscoverySaveSnapshot() => DiscoveredDefinitionIds = new List<string>();

        /// <summary>已发现 Definition 的稳定标识文本。</summary>
        public List<string> DiscoveredDefinitionIds { get; set; }

        /// <summary>校验快照集合结构、ID 格式和重复项。</summary>
        /// <exception cref="InvalidOperationException">集合为 null、ID 无效或存在重复项时抛出。</exception>
        public void ValidateShape()
        {
            if (DiscoveredDefinitionIds == null)
                throw new InvalidOperationException("物品发现快照的 Definition 集合不能为 null。" );

            var discoveredIds = new HashSet<ItemId>();
            for (int index = 0; index < DiscoveredDefinitionIds.Count; index++)
            {
                string rawDefinitionId = DiscoveredDefinitionIds[index];
                if (!ItemId.TryCreate(rawDefinitionId, out ItemId definitionId) || !discoveredIds.Add(definitionId))
                    throw new InvalidOperationException($"物品发现快照包含非法或重复 Definition：{rawDefinitionId}。" );
            }
        }
    }

    #endregion

    /// <summary>将 ItemDiscoveryManager 状态接入 SaveSystem。</summary>
    public sealed class ItemDiscoverySaveModule : SaveModule<ItemDiscoverySaveSnapshot>
    {
        #region 常量字段

        /// <summary>发现记录存档模块的稳定 ID。</summary>
        public static readonly SaveModuleId StableModuleId = new SaveModuleId("item-discovery");

        #endregion

        #region 依赖字段

        private readonly ItemDiscoveryManager manager;

        #endregion

        #region 生命周期

        /// <summary>创建物品发现存档模块；旧存档缺少该模块时从空发现集合开始。</summary>
        /// <param name="manager">物品发现状态 Manager。</param>
        /// <exception cref="ArgumentNullException">Manager 为空时抛出。</exception>
        public ItemDiscoverySaveModule(ItemDiscoveryManager manager)
            : base(StableModuleId, 1, SaveMissingModulePolicy.CreateDefault)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        #endregion

        #region 快照操作

        /// <summary>为旧存档缺少发现模块的情况创建空发现快照。</summary>
        /// <returns>没有已发现 Definition 的默认快照。</returns>
        protected override ItemDiscoverySaveSnapshot CreateDefaultTypedSnapshot() =>
            new ItemDiscoverySaveSnapshot();

        /// <summary>按稳定 ItemId 顺序采集物品发现记录。</summary>
        /// <returns>物品发现快照。</returns>
        protected override ItemDiscoverySaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new ItemDiscoverySaveSnapshot();
            IReadOnlyList<ItemId> discoveredIds = manager.GetDiscoveredDefinitionIds();
            // Manager 返回排序副本，避免把内部 HashSet 暴露给存档 DTO。
            for (int index = 0; index < discoveredIds.Count; index++)
            {
                snapshot.DiscoveredDefinitionIds.Add(discoveredIds[index].Value);
            }

            return snapshot;
        }

        /// <summary>把已校验的发现记录恢复到 Manager，且不触发普通获得事件。</summary>
        /// <param name="snapshot">已校验的当前版本快照。</param>
        protected override void RestoreTypedSnapshot(ItemDiscoverySaveSnapshot snapshot)
        {
            var restoredDefinitionIds = new List<ItemId>(snapshot.DiscoveredDefinitionIds.Count);
            // 将 DTO 中的稳定文本标识还原为业务 ID，再整体替换发现集合。
            for (int index = 0; index < snapshot.DiscoveredDefinitionIds.Count; index++)
            {
                restoredDefinitionIds.Add(new ItemId(snapshot.DiscoveredDefinitionIds[index]));
            }

            manager.RestoreState(restoredDefinitionIds);
        }

        /// <summary>验证发现记录的结构及当前 ItemDatabase 中的定义。</summary>
        /// <param name="snapshot">待验证快照。</param>
        /// <exception cref="ArgumentNullException">快照为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">快照引用未知 Definition 时抛出。</exception>
        protected override void ValidateTypedSnapshot(ItemDiscoverySaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.ValidateShape();
            for (int index = 0; index < snapshot.DiscoveredDefinitionIds.Count; index++)
            {
                ItemId definitionId = new ItemId(snapshot.DiscoveredDefinitionIds[index]);
                if (!ItemManager.Instance.TryGetDefinition(definitionId, out ItemDefinition definition) || definition == null)
                    throw new InvalidOperationException($"物品发现快照引用了未知 ItemDefinition：{definitionId}。" );
            }
        }

        #endregion
    }
}
