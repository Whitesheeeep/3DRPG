using System;
using RPG.Game.UI.Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.Pooling;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>以分栏文字和细线装饰呈现一条装备静态属性。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterEquipmentAttributeLineView : PoolObjectIdentity
    {
        #region 依赖字段

        [SerializeField] private TMP_Text attributeNameText;
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private Image dividerImage;

        #endregion

        #region 绑定与池生命周期

        /// <summary>检查 Prefab 显式绑定的文字和分割线组件。</summary>
        protected override void Awake()
        {
            base.Awake();
            if (attributeNameText == null || valueText == null || dividerImage == null)
                throw new InvalidOperationException("[CharacterEquipmentAttributeLineView] 属性行引用未完整绑定。");
            dividerImage.raycastTarget = false;
        }

        /// <summary>绑定一条属性的名称和值，避免将多列压进单个文本框。</summary>
        /// <param name="data">装备属性行显示数据。</param>
        public void Bind(CharacterEquipmentAttributeLineViewData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            attributeNameText.text = data.AttributeName;
            valueText.text = data.ValueText;
            gameObject.SetActive(true);
        }

        /// <summary>归还对象池前清空旧角色或装备的文本。</summary>
        protected override void OnDespawn()
        {
            if (attributeNameText != null) attributeNameText.text = string.Empty;
            if (valueText != null) valueText.text = string.Empty;
        }

        #endregion
    }
}
