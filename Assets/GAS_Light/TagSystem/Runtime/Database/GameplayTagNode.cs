using System;
using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.GAS.TAG
{
    /// <summary>保存单个 Gameplay Tag 的运行时父级关系缓存。</summary>
    [Serializable]
    public readonly struct GameplayTagNode
    {
        #region 字段与属性
        [SerializeField]
        private readonly GameplayTag tag;
        [SerializeField]
        private readonly GameplayTag parent;
        [SerializeField]
        private readonly GameplayTag[] ancestors;
        /// <summary>获取当前节点对应标签。</summary>
        public GameplayTag Tag => tag;
        /// <summary>获取直接父标签；根节点返回 Empty。</summary>
        public GameplayTag Parent => parent;
        /// <summary>获取从直接父级到根节点排列的全部祖先。</summary>
        public IReadOnlyList<GameplayTag> Ancestors => ancestors ?? Array.Empty<GameplayTag>();
        /// <summary>获取节点深度；根节点深度为 0。</summary>
        public int Depth => ancestors?.Length ?? 0;
        #endregion

        /// <summary>创建烘焙后的关系节点。</summary>
        public GameplayTagNode(GameplayTag tag, GameplayTag parent, GameplayTag[] ancestors)
        {
            this.tag = tag;
            this.parent = parent;
            this.ancestors = ancestors ?? Array.Empty<GameplayTag>();
        }

        /// <summary>判断当前节点是否包含指定祖先。</summary>
        public bool HasAncestor(GameplayTag queryTag)
        {
            if (ancestors == null) return false;
            for (int i = 0; i < ancestors.Length; i++)
                if (ancestors[i] == queryTag)
                    return true;
            return false;
        }
    }
}