#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>保存一次 GE Curve 预览烘焙中的 x 坐标和各 Curve Modifier 数值。</summary>
    [Serializable]
    public sealed class BakedGameplayEffectCurveRow
    {
        #region 字段

        [UnityEngine.SerializeField] private float x;
        [UnityEngine.SerializeField] private List<float> magnitudes = new();

        #endregion

        #region 属性

        /// <summary>获取本行采样 x 坐标。</summary>
        public float X => x;

        /// <summary>获取按 Modifier 作者顺序保存的 Magnitude。</summary>
        public IReadOnlyList<float> Magnitudes => magnitudes;

        #endregion

        #region 构造

        /// <summary>创建一行 Curve 预览烘焙结果。</summary>
        /// <param name="x">采样 x 坐标。</param>
        /// <param name="magnitudes">按 Modifier 顺序排列的数值。</param>
        public BakedGameplayEffectCurveRow(float x, IReadOnlyList<float> magnitudes)
        {
            this.x = x;
            this.magnitudes = magnitudes == null
                ? throw new ArgumentNullException(nameof(magnitudes))
                : new List<float>(magnitudes);
        }

        #endregion
    }
}
#endif
