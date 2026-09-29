using System;
using RPG.Game.UI.WeaponDevelopment;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.Pooling;

namespace RPG.Game.UI.Views.WeaponDevelopment
{
    /// <summary>以独立列呈现装备升级属性的当前值、预计值和变化方向。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖升级行 Prefab 显式绑定的渐变底、属性名、当前值、对比箭头、预计值和涨跌标记。")]
    public sealed class EquipmentAttributeUpgradeLineView : PoolObjectIdentity
    {
        #region 依赖字段

        [SerializeField] private Image backgroundImage;
        [SerializeField] private TMP_Text attributeNameText;
        [SerializeField] private TMP_Text currentValueText;
        [SerializeField] private Image comparisonArrowImage;
        [SerializeField] private TMP_Text projectedValueText;
        [SerializeField] private Image directionIndicatorImage;
        [SerializeField] private Color increaseColor = new Color(0.52f, 0.9f, 0.16f, 1f);
        [SerializeField] private Color decreaseColor = new Color(0.94f, 0.3f, 0.34f, 1f);

        #endregion

        #region 生命周期与绑定

        /// <summary>验证 Prefab 上的列引用，并关闭装饰图像的射线拦截。</summary>
        protected override void Awake()
        {
            base.Awake();
            if (backgroundImage == null || attributeNameText == null || currentValueText == null ||
                comparisonArrowImage == null || projectedValueText == null || directionIndicatorImage == null)
                throw new InvalidOperationException("[EquipmentAttributeUpgradeLineView] 升级属性行引用未完整绑定。");

            backgroundImage.raycastTarget = false;
            comparisonArrowImage.raycastTarget = false;
            directionIndicatorImage.raycastTarget = false;
        }

        /// <summary>绑定一条当前与预计数值，涨跌图标只在数值实际变化时显示。</summary>
        /// <param name="data">升级属性行的结构化展示数据。</param>
        public void Bind(EquipmentAttributeUpgradeLineViewData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            BindComparison(data.AttributeName, data.CurrentValueText, data.ProjectedValueText,
                data.ShowProjectedValue ? data.Direction : EquipmentAttributeUpgradeDirection.None,
                data.ShowProjectedValue);
        }

        /// <summary>绑定通用的当前值与预计值，用于装备以外的成长结果行。</summary>
        /// <param name="label">结果名称，例如突破阶数或等级上限。</param>
        /// <param name="currentValue">操作前的显示值。</param>
        /// <param name="projectedValue">操作后的预计显示值。</param>
        /// <param name="direction">预计结果相对当前值的变化方向。</param>
        public void BindComparison(string label, string currentValue, string projectedValue,
            EquipmentAttributeUpgradeDirection direction)
        {
            BindComparison(label, currentValue, projectedValue, direction, true);
        }

        /// <summary>同步对比列、箭头和变化标记，并使池化行进入可见状态。</summary>
        /// <param name="label">结果名称。</param>
        /// <param name="currentValue">操作前显示值。</param>
        /// <param name="projectedValue">操作后预计显示值。</param>
        /// <param name="direction">预计结果相对当前值的变化方向。</param>
        /// <param name="showProjectedValue">是否显示预计列和比较箭头。</param>
        private void BindComparison(string label, string currentValue, string projectedValue,
            EquipmentAttributeUpgradeDirection direction, bool showProjectedValue)
        {
            attributeNameText.text = label ?? string.Empty;
            currentValueText.text = currentValue ?? string.Empty;
            projectedValueText.text = showProjectedValue ? projectedValue ?? string.Empty : string.Empty;
            projectedValueText.gameObject.SetActive(showProjectedValue);
            comparisonArrowImage.gameObject.SetActive(showProjectedValue);
            ApplyDirection(showProjectedValue ? direction : EquipmentAttributeUpgradeDirection.None);
            gameObject.SetActive(true);
        }

        /// <summary>归还对象池前清除旧属性和涨跌状态，避免下一次取出时串行。</summary>
        protected override void OnDespawn()
        {
            attributeNameText.text = string.Empty;
            currentValueText.text = string.Empty;
            projectedValueText.text = string.Empty;
            projectedValueText.gameObject.SetActive(true);
            comparisonArrowImage.gameObject.SetActive(true);
            directionIndicatorImage.gameObject.SetActive(false);
            directionIndicatorImage.color = increaseColor;
            directionIndicatorImage.rectTransform.localRotation = Quaternion.identity;
        }

        #endregion

        #region 状态刷新

        /// <summary>按属性差值显示上升或下降标记，并为下降图标旋转半周。</summary>
        /// <param name="direction">当前值到预计值的变化方向。</param>
        private void ApplyDirection(EquipmentAttributeUpgradeDirection direction)
        {
            bool hasDirection = direction != EquipmentAttributeUpgradeDirection.None;
            directionIndicatorImage.gameObject.SetActive(hasDirection);
            if (!hasDirection) return;

            bool increases = direction == EquipmentAttributeUpgradeDirection.Increase;
            directionIndicatorImage.color = increases ? increaseColor : decreaseColor;
            directionIndicatorImage.rectTransform.localRotation = increases
                ? Quaternion.identity
                : Quaternion.Euler(0f, 0f, 180f);
        }

        #endregion
    }
}
