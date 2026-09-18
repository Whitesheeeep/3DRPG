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
        #region 常量与依赖字段

        /// <summary>发现记录存档模块的稳定 ID。</summary>
        public static readonly SaveModuleId StableModuleId = new SaveModuleId("item-discovery");

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

        /// <summary>按 ItemId 排序采集发现记录。</summary>
        /// <returns>发现记录快照。</returns>
        protected override ItemDiscoverySaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new ItemDiscoverySaveSnapshot();
            IReadOnlyList<ItemId> discoveredDefinitionIds = manager.GetDiscoveredDefinitionIds();
            for (int index = 0; index < discoveredDefinitionIds.Count; index++)
                snapshot.DiscoveredDefinitionIds.Add(discoveredDefinitionIds[index].Value);
            return snapshot;
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

        /// <summary>恢复发现集合，不发布普通物品获得事件。</summary>
        /// <param name="snapshot">已验证快照。</param>
        protected override void RestoreTypedSnapshot(ItemDiscoverySaveSnapshot snapshot)
        {
            var discoveredDefinitionIds = new List<ItemId>(snapshot.DiscoveredDefinitionIds.Count);
            for (int index = 0; index < snapshot.DiscoveredDefinitionIds.Count; index++)
                discoveredDefinitionIds.Add(new ItemId(snapshot.DiscoveredDefinitionIds[index]));
            manager.RestoreState(discoveredDefinitionIds);
        }

        #endregion
    }
}
