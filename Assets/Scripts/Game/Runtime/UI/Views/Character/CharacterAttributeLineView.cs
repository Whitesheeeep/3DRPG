using TMPro;
using RPG.Game.UI.Character;
using UnityEngine;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>角色属性页中的一行静态基础 Stat。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAttributeLineView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private GameObject attributeIconRoot;
        [SerializeField] private TMP_Text attributeNameText;
        [SerializeField] private TMP_Text attributeValueText;
        [SerializeField] private TMP_Text attributeAddValueText;

        #endregion

        #region 绑定

        /// <summary>绑定一行属性文本；当前没有图标配置时隐藏图标节点。</summary>
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
            if (attributeIconRoot != null) attributeIconRoot.SetActive(false);
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
            gameObject.SetActive(false);
        }

        #endregion
    }
}
