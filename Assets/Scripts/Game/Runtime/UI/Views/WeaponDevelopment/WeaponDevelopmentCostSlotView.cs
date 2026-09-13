using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.WeaponDevelopment
{
    /// <summary>显示突破或精炼成本的一格 UGUI 占位 View。</summary>
    public sealed class WeaponDevelopmentCostSlotView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text quantityText;

        #endregion

        #region 展示

        /// <summary>绑定一个成本条目的名称、数量和可选图标。</summary>
        /// <param name="displayName">显示名称。</param>
        /// <param name="owned">拥有数量。</param>
        /// <param name="required">需求数量。</param>
        /// <param name="sprite">条目图标。</param>
        public void Bind(string displayName, int owned, int required, Sprite sprite)
        {
            if (icon != null) icon.sprite = sprite;
            if (nameText != null) nameText.text = displayName ?? string.Empty;
            if (quantityText != null) quantityText.text = $"{owned}/{required}";
        }

        #endregion
    }
}
