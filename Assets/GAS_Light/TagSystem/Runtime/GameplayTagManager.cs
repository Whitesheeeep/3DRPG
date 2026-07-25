using System.Collections.Generic;
using WS_Modules.GAS.TAG;
using WS_Modules.Singleton;

namespace WS_Modules
{
    /// <summary>提供 Gameplay Tag 数据库初始化、查询和 UE 方向的层级匹配。</summary>
    public sealed class GameplayTagManager : SingletonBase<GameplayTagManager>
    {
        #region 字段与属性
        private GameplayTagDatabase database;
        /// <summary>获取管理器是否已绑定数据库。</summary>
        public bool IsInitialized => database != null;
        /// <summary>获取当前数据库；未初始化时返回 null。</summary>
        public GameplayTagDatabase Database => database;
        #endregion

        // SingletonBase 通过反射调用私有构造函数。
        private GameplayTagManager()
        {
        }

        #region 生命周期
        /// <summary>绑定运行时 Gameplay Tag 数据库。</summary>
        public void Initialize(GameplayTagDatabase database) => this.database = database;

        /// <summary>清除当前数据库引用，供退出流程或测试隔离使用。</summary>
        public void Reset() => database = null;
        #endregion

        #region 查询与匹配
        /// <summary>判断标签是否有效且存在于当前数据库。</summary>
        public bool IsValidTag(GameplayTag tag) => database != null && database.TryGetNode(tag, out _);

        /// <summary>尝试按标签获取关系节点。</summary>
        public bool TryGetNode(GameplayTag tag, out GameplayTagNode node)
        {
            if (database == null)
            {
                node = default;
                return false;
            }

            return database.TryGetNode(tag, out node);
        }

        /// <summary>尝试按稳定 ID 获取关系节点。</summary>
        public bool TryGetNode(int tagId, out GameplayTagNode node) => TryGetNode(new GameplayTag(tagId), out node);

        /// <summary>判断实际标签是否匹配查询标签；实际子标签可匹配自身及任意祖先。</summary>
        public bool MatchesTag(GameplayTag actualTag, GameplayTag queryTag)
        {
            if (!TryGetNode(actualTag, out GameplayTagNode node) || !IsValidTag(queryTag)) return false;
            return actualTag == queryTag || node.HasAncestor(queryTag);
        }

        /// <summary>判断两个存在于数据库中的标签是否精确相等。</summary>
        public bool MatchesTagExact(GameplayTag actualTag, GameplayTag queryTag) =>
            actualTag == queryTag && IsValidTag(actualTag);

        /// <summary>尝试获取标签的直接父级。</summary>
        public bool TryGetParent(GameplayTag tag, out GameplayTag parent)
        {
            if (TryGetNode(tag, out GameplayTagNode node) && node.Parent.IsValid)
            {
                parent = node.Parent;
                return true;
            }

            parent = GameplayTag.Empty;
            return false;
        }

        /// <summary>尝试获取从直接父级到根节点排列的祖先缓存。</summary>
        public bool TryGetAncestors(GameplayTag tag, out IReadOnlyList<GameplayTag> ancestors)
        {
            if (TryGetNode(tag, out GameplayTagNode node))
            {
                ancestors = node.Ancestors;
                return true;
            }

            ancestors = System.Array.Empty<GameplayTag>();
            return false;
        }
        #endregion
    }
}