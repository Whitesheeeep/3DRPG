using System;
using UnityEngine;
using WS_Modules.ConfigInstaller;

namespace RPG.ItemSystem
{
    /// <summary>通过 ConfigInstaller 注入项目 ItemDatabase 的配置叶节点。</summary>
    [CreateAssetMenu(fileName = "ItemDatabaseConfigProvider", menuName = "RPG/ItemSystem/Item Database Config Provider", order = 1)]
    public sealed class ItemDatabaseConfigProvider : ConfigRegisterNodeBase
    {
        [SerializeField] private ItemDatabase database;

        /// <summary>获取配置数据库。</summary>
        public ItemDatabase Database => database;

        /// <summary>验证并注册 ItemDatabase。</summary>
        /// <exception cref="InvalidOperationException">数据库没有配置时抛出。</exception>
        public override void Register()
        {
            if (database == null) throw new InvalidOperationException("ItemDatabaseConfigProvider 未配置 ItemDatabase。");
            ItemManager.Initialize(database);
        }
    }
}
