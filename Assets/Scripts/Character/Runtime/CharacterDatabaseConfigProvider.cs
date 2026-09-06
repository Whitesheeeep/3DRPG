using System;
using UnityEngine;
using WS_Modules.ConfigInstaller;

namespace RPG.Character
{
    /// <summary>通过 ConfigInstaller 注入项目 CharacterDatabase 的配置叶节点。</summary>
    [CreateAssetMenu(fileName = "CharacterDatabaseConfigProvider", menuName = "RPG/Character/Character Database Config Provider")]
    public sealed class CharacterDatabaseConfigProvider : ConfigRegisterNodeBase
    {
        [SerializeField] private CharacterDatabase database;

        /// <summary>获取待注册角色数据库。</summary>
        public CharacterDatabase Database => database;

        /// <summary>验证并注册 CharacterDatabase。</summary>
        /// <exception cref="InvalidOperationException">数据库未配置时抛出。</exception>
        public override void Register()
        {
            if (database == null) throw new InvalidOperationException("CharacterDatabaseConfigProvider 未配置 CharacterDatabase。");
            CharacterConfigManager.Instance.Initialize(database);
        }
    }
}
