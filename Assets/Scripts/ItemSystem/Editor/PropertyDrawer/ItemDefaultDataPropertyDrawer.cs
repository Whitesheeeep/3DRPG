#if UNITY_EDITOR
using System;
using UnityEditor;
using WS_Modules.Utilities.Editor;

namespace RPG.ItemSystem.Editor
{
    /// <summary>为 ItemDefaultData 托管引用提供中文类型选择和字段绘制。</summary>
    [CustomPropertyDrawer(typeof(ItemDefaultData), true)]
    internal sealed class ItemDefaultDataPropertyDrawer : ManagedReferenceDropdownPropertyDrawer<ItemDefaultData>
    {
        /// <summary>获取默认数据类型切换使用的 Undo 名称。</summary>
        protected override string UndoActionName => "切换物品默认数据类型";

        /// <summary>获取默认数据派生类型的中文显示名称。</summary>
        /// <param name="type">派生类型。</param>
        /// <returns>Inspector 中显示的类型名称。</returns>
        protected override string GetTypeDisplayName(Type type)
        {
            if (type == typeof(StackableItemDefaultData)) return "可堆叠物品默认数据";
            if (type == typeof(WeaponItemDefaultData)) return "武器默认数据";
            if (type == typeof(ArtifactItemDefaultData)) return "圣遗物默认数据";
            return base.GetTypeDisplayName(type);
        }

        /// <summary>限制托管引用候选类型为本 Item 系统支持的三种默认数据。</summary>
        /// <param name="type">待筛选的类型。</param>
        /// <returns>类型受支持时返回 true。</returns>
        protected override bool IsSelectableType(Type type)
        {
            return type == typeof(StackableItemDefaultData) ||
                   type == typeof(WeaponItemDefaultData) ||
                   type == typeof(ArtifactItemDefaultData);
        }
    }
}
#endif
