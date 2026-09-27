using TMPro;
using RPG.Game.UI.Character;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>角色属性页中的一行基础 Stat 与静态装备净加成。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAttributeLineView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private GameObject attributeIconRoot;
        [SerializeField] private TMP_Text attributeNameText;
        [SerializeField] private TMP_Text attributeValueText;
        [SerializeField] private TMP_Text attributeAddValueText;
        [SerializeField] private Image dividerImage;

        #endregion

        #region 绑定

        /// <summary>绑定一行属性文本并显示由 Prefab 预先配置的图标。</summary>
        /// <param name="data">属性行数据。</param>
        public void Bind(CharacterAttributeViewData data)
        {
            if (data == null)
            {
                gameObject.SetActive(false);
                return;
            }
            gameObject.SetActive(true);
            attributeNameText.text = data.AttributeName;
            attributeValueText.text = data.BaseValueText;
            attributeAddValueText.text = data.EquipmentBonusText;
            attributeAddValueText.gameObject.SetActive(!string.IsNullOrEmpty(data.EquipmentBonusText));
            if (attributeIconRoot != null) attributeIconRoot.SetActive(true);
            if (dividerImage != null) dividerImage.raycastTarget = false;
        }

        /// <summary>隐藏属性行。</summary>
        public void Clear()
        {
            if (attributeNameText != null) attributeNameText.text = string.Empty;
            if (attributeValueText != null) attributeValueText.text = string.Empty;
            if (attributeAddValueText != null)
            {
                attributeAddValueText.text = string.Empty;
                attributeAddValueText.gameObject.SetActive(false);
            }
            if (attributeIconRoot != null) attributeIconRoot.SetActive(false);
            if (dividerImage != null) dividerImage.raycastTarget = false;
            gameObject.SetActive(false);
        }

        #endregion
    }
}
