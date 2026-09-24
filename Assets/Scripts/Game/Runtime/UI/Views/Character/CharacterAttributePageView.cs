using System;
using System.Collections.Generic;
using RPG.Game.UI.Character;
using UnityEngine;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>角色基础属性页面 View。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAttributePageView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private CharacterAttributeLineView[] lines = Array.Empty<CharacterAttributeLineView>();

        #endregion

        #region 生命周期

        /// <summary>校验属性行依赖。</summary>
        private void Awake()
        {
            if (lines == null || lines.Length == 0)
                throw new InvalidOperationException("[CharacterAttributePageView] 未绑定属性行。");
            for (int index = 0; index < lines.Length; index++)
                if (lines[index] == null) throw new InvalidOperationException($"[CharacterAttributePageView] 属性行 {index} 为空。");
        }

        #endregion

        #region 绑定

        /// <summary>绑定当前角色的基础 Stat 行。</summary>
        /// <param name="data">基础属性列表。</param>
        public void Bind(IReadOnlyList<CharacterAttributeViewData> data)
        {
            int count = data?.Count ?? 0;
            for (int index = 0; index < lines.Length; index++)
                if (index < count) lines[index].Bind(data[index]);
                else lines[index].Clear();
        }

        /// <summary>清空属性页。</summary>
        public void Clear()
        {
            for (int index = 0; index < lines.Length; index++) lines[index].Clear();
        }

        #endregion
    }
}
