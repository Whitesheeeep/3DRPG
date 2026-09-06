using System;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules;

namespace RPG.ItemSystem
{
    /// <summary>所有可进入 ItemDatabase 的物品定义基类。</summary>
    public abstract class ItemDefinition : ScriptableObject
    {
        [SerializeField, ReadOnly, LabelText("稳定物品标识")] private ItemId itemId;
        [SerializeField, LabelText("显示名称")] private string displayName;
        [SerializeField, TextArea(2, 6), LabelText("物品描述")] private string description;
        [SerializeField, ReadOnly, LabelText("物品类型")] private ItemCategory category;
        [SerializeField, LabelText("稀有度")] private ItemRarity rarity = ItemRarity.One;
        [SerializeField, LabelText("默认排序优先级")] private int sortPriority;
        [SerializeField, WSAddressableKey("UISpriteAtlas"), LabelText("图标图集资源地址")] private string iconAddress;
        [SerializeField, LabelText("图集内图片名称")] private string iconSpriteName;
        [SerializeField, WSAddressableKey, LabelText("世界预制体资源地址")] private string worldPrefabAddress;
#if UNITY_EDITOR
        [SerializeField, LabelText("编辑器预览图标")] private Sprite editorPreviewIcon;
#endif

        /// <summary>获取稳定物品标识。</summary>
        public ItemId ItemId => itemId;

        /// <summary>获取编辑器和背包显示名称。</summary>
        public string DisplayName => displayName;

        /// <summary>获取物品描述。</summary>
        public string Description => description;

        /// <summary>获取背包分类。</summary>
        public ItemCategory Category => category;

        /// <summary>获取稀有度。</summary>
        public ItemRarity Rarity => rarity;

        /// <summary>获取默认排序优先级。</summary>
        public int SortPriority => sortPriority;

        /// <summary>获取图标 SpriteAtlas 的 Addressable Key。</summary>
        public string IconAddress => iconAddress;

        /// <summary>获取图标图集内的 Sprite 名称。</summary>
        public string IconSpriteName => iconSpriteName;

        /// <summary>获取世界预制体 Addressable Key。</summary>
        public string WorldPrefabAddress => worldPrefabAddress;

#if UNITY_EDITOR
        /// <summary>获取仅供 Unity 编辑器预览使用的物品图标。</summary>
        public Sprite EditorPreviewIcon => editorPreviewIcon;
#endif

        /// <summary>验证通用字段和派生类型字段。</summary>
        /// <exception cref="InvalidOperationException">配置不满足定义契约时抛出。</exception>
        public void Validate()
        {
            if (!ItemId.IsValid) throw new InvalidOperationException($"物品定义 '{name}' 的 ItemId 无效。");
            if (string.IsNullOrWhiteSpace(DisplayName)) throw new InvalidOperationException($"物品定义 '{name}' 的显示名称不能为空。");
            if (!Enum.IsDefined(typeof(ItemCategory), Category)) throw new InvalidOperationException($"物品定义 '{name}' 的 Category 无效。");
            if (!Enum.IsDefined(typeof(ItemRarity), Rarity)) throw new InvalidOperationException($"物品定义 '{name}' 的 Rarity 无效。");
            bool hasIconAddress = !string.IsNullOrWhiteSpace(IconAddress);
            bool hasIconSpriteName = !string.IsNullOrWhiteSpace(IconSpriteName);
            if (hasIconAddress != hasIconSpriteName)
                throw new InvalidOperationException($"物品定义 '{name}' 的图标图集资源地址和图集内图片名称必须同时配置，或同时留空。");
            ValidateSpecific();
        }

        /// <summary>由具体 Definition 校验特有字段。</summary>
        /// <exception cref="InvalidOperationException">特有字段不合法时抛出。</exception>
        protected abstract void ValidateSpecific();

        /// <summary>编辑器序列化回调，用于保持派生类型的分类契约。</summary>
        protected virtual void OnValidate()
        {
            if (this is WeaponDefinition) category = ItemCategory.Weapon;
            else if (this is ArtifactDefinition) category = ItemCategory.Artifact;
            else if (this is DevelopmentItemDefinition) category = ItemCategory.Material;
            else if (category == ItemCategory.Weapon || category == ItemCategory.Artifact)
            {
                category = ItemCategory.Material;
            }
        }
    }
}
