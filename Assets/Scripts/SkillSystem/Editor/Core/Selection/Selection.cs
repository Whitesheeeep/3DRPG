using System;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 保存可跨时间轴重建和 Undo/Redo 恢复的稳定选择标识。
    /// </summary>
    internal abstract class SelectionState : IEquatable<SelectionState>
    {
        public static readonly SelectionState None = new NoneSelection();
        public virtual string TrackId => string.Empty;
        public virtual string ItemId => string.Empty;

        /// <summary>
        /// 按选择种类及稳定 GUID 判断是否指向同一数据。
        /// </summary>
        /// <param name="other">待比较的选择。</param>
        /// <returns>选择种类和稳定标识都一致时返回 true。</returns>
        public bool Equals(SelectionState other) =>
            other != null && GetType() == other.GetType() &&
            TrackId == other.TrackId && ItemId == other.ItemId;

        /// <summary>
        /// 将对象比较转发到强类型选择比较。
        /// </summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>对象表示同一选择时返回 true。</returns>
        public override bool Equals(object obj) => obj is SelectionState other && Equals(other);

        /// <summary>
        /// 返回与选择相等语义一致的哈希值。
        /// </summary>
        /// <returns>由选择种类及稳定 GUID 组成的哈希值。</returns>
        public override int GetHashCode() => HashCode.Combine(GetType(), TrackId, ItemId);
    }

    /// <summary>
    /// 表示当前没有时间轴选择。
    /// </summary>
    internal sealed class NoneSelection : SelectionState
    {
    }

    /// <summary>
    /// 通过稳定轨道 GUID 表示任意具体类型轨道选择。
    /// </summary>
    internal sealed class TrackSelection : SelectionState
    {
        private readonly string trackId;
        public override string TrackId => trackId;

        /// <summary>
        /// 创建轨道选择，避免使用会随全局重排变化的列表索引。
        /// </summary>
        /// <param name="trackId">轨道子资产的稳定 GUID。</param>
        public TrackSelection(string trackId) => this.trackId = trackId ?? string.Empty;
    }

    /// <summary>
    /// 通过轨道和内容稳定 GUID 表示任意具体类型内容选择。
    /// </summary>
    internal sealed class ItemSelection : SelectionState
    {
        private readonly string trackId;
        private readonly string itemId;
        public override string TrackId => trackId;
        public override string ItemId => itemId;

        /// <summary>
        /// 创建内容选择，保证轨道重排和时间轴重建后仍可恢复。
        /// </summary>
        /// <param name="trackId">内容所属轨道的稳定 GUID。</param>
        /// <param name="itemId">内容自身的稳定 GUID。</param>
        public ItemSelection(string trackId, string itemId)
        {
            this.trackId = trackId ?? string.Empty;
            this.itemId = itemId ?? string.Empty;
        }
    }
}
