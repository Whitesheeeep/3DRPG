using System.Globalization;
using WS_Modules.GAS.AttributeSystem;

namespace RPG.Game.UI.WeaponDevelopment
{
    /// <summary>统一背包和武器培养页面的静态属性数值格式。</summary>
    internal static class WeaponAttributeTextFormatter
    {
        /// <summary>格式化一个已聚合的属性值。</summary>
        /// <param name="type">Modifier 类型。</param>
        /// <param name="value">已聚合数值。</param>
        /// <returns>详情显示文本。</returns>
        public static string Format(AttributeModifierType type, float value)
        {
            if (type == AttributeModifierType.Add && value >= 0f && value <= 1f)
                return value.ToString("0.##%", CultureInfo.InvariantCulture);
            if (type == AttributeModifierType.Multiply)
                return (value - 1f).ToString("+0.##%;-0.##%;0%", CultureInfo.InvariantCulture);
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
