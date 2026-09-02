using System;
using WS_Modules.Singleton;

namespace RPG.ItemSystem
{
    /// <summary>
    /// 持有 ItemDatabase 配置并提供物品定义查询的纯 C# 单例管理器。
    /// </summary>
    public sealed class ItemManager : SingletonBase<ItemManager>
    {
        #region 配置状态

        // 配置锁独立于实例状态；ConfigInstaller 注入数据库时不会访问 Instance，因此不会提前创建单例。
        private static readonly object configurationGate = new object();
        private static ItemDatabase database;
        private static bool configured;

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建物品管理器；实例由 SingletonBase 通过私有无参构造函数延迟创建。
        /// </summary>
        private ItemManager()
        {
        }

        #endregion

        #region 属性

        /// <summary>获取当前是否已经完成 ItemDatabase 注入。</summary>
        public bool IsConfigured => configured;

        /// <summary>
        /// 获取当前物品数据库；调用方必须先通过配置安装器注入数据库。
        /// </summary>
        /// <exception cref="InvalidOperationException">数据库尚未注入时抛出。</exception>
        public ItemDatabase Database
        {
            get
            {
                EnsureConfigured();
                return database;
            }
        }

        #endregion

        #region 配置操作

        /// <summary>
        /// 注入并验证当前物品数据库，不创建 ItemManager 单例实例。
        /// </summary>
        /// <param name="itemDatabase">待注入的物品数据库。</param>
        /// <exception cref="ArgumentNullException">数据库为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">重复注入其他数据库或数据库校验失败时抛出。</exception>
        public static void Initialize(ItemDatabase itemDatabase)
        {
            if (itemDatabase == null) throw new ArgumentNullException(nameof(itemDatabase));

            lock (configurationGate)
            {
                if (configured)
                {
                    if (!ReferenceEquals(database, itemDatabase))
                    {
                        throw new InvalidOperationException("ItemManager 已经注入其他 ItemDatabase。");
                    }

                    return;
                }

                // 先验证并建立索引，确保静态状态不会指向半初始化数据库。
                itemDatabase.ValidateAndBuildIndex();
                database = itemDatabase;
                configured = true;
            }
        }

        #endregion

        #region 定义查询

        /// <summary>
        /// 按稳定 ItemId 查询物品定义。
        /// </summary>
        /// <param name="itemId">待查询的物品标识。</param>
        /// <param name="definition">找到的物品定义。</param>
        /// <returns>找到定义时返回 true。</returns>
        /// <exception cref="InvalidOperationException">数据库尚未注入时抛出。</exception>
        public bool TryGetDefinition(ItemId itemId, out ItemDefinition definition)
        {
            EnsureConfigured();
            return database.TryGetDefinition(itemId, out definition);
        }

        #endregion

        #region 内部校验

        /// <summary>确保当前管理器已经完成数据库配置。</summary>
        /// <exception cref="InvalidOperationException">数据库尚未注入时抛出。</exception>
        private static void EnsureConfigured()
        {
            if (!configured || database == null)
            {
                throw new InvalidOperationException("ItemManager 尚未通过 ConfigInstaller 注入 ItemDatabase。");
            }
        }

        #endregion
    }
}
