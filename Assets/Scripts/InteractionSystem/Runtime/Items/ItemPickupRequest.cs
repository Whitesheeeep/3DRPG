using System;
using UnityEngine;

namespace RPG.InteractionSystem
{
    /// <summary>描述一次由场景物品提交给玩家物品接收器的拾取请求。</summary>
    public readonly struct ItemPickupRequest
    {
        #region 属性

        /// <summary>获取被拾取的物品定义资产。</summary>
        public ScriptableObject ItemDefinition { get; }

        /// <summary>获取本次拾取的物品数量。</summary>
        public int Quantity { get; }

        /// <summary>获取发起请求的场景物品对象。</summary>
        public GameObject SourceObject { get; }

        #endregion

        #region 构造

        /// <summary>创建一条经过边界校验的物品拾取请求。</summary>
        /// <param name="itemDefinition">物品定义资产。</param>
        /// <param name="quantity">拾取数量，必须为正数。</param>
        /// <param name="sourceObject">场景中的物品来源对象。</param>
        public ItemPickupRequest(ScriptableObject itemDefinition, int quantity, GameObject sourceObject)
        {
            if (itemDefinition == null) throw new ArgumentNullException(nameof(itemDefinition));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (sourceObject == null) throw new ArgumentNullException(nameof(sourceObject));

            ItemDefinition = itemDefinition;
            Quantity = quantity;
            SourceObject = sourceObject;
        }

        #endregion
    }
}
