using System;
using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>
    /// 保存一个以等级为输入的可缩放浮点数；最终值由基础值乘以等级曲线倍率得到。
    /// </summary>
    [Serializable]
    public sealed class GameplayScalableFloat
    {
        #region 字段

        [SerializeField]
        private float baseValue = 1f;

        [SerializeField, Tooltip("以 Ability Level 为横轴的倍率曲线；为空或无关键帧时按 1 处理。")]
        private AnimationCurve levelCurve;

        #endregion

        #region 属性

        /// <summary>获取等级曲线求值前使用的基础值。</summary>
        public float BaseValue => baseValue;

        /// <summary>获取以 Ability Level 为横轴的倍率曲线；未配置时返回 null。</summary>
        public AnimationCurve LevelCurve => levelCurve;

        #endregion

        #region 构造

        /// <summary>创建默认值为 1 且不使用曲线的 ScalableFloat。</summary>
        public GameplayScalableFloat()
        {
            baseValue = 1f;
        }

        /// <summary>创建指定基础值且不使用曲线的 ScalableFloat。</summary>
        /// <param name="value">不使用曲线时的最终值。</param>
        public GameplayScalableFloat(float value)
        {
            baseValue = value;
        }

        #endregion

        #region 求值

        /// <summary>
        /// 使用指定等级求出最终值，并拒绝非法等级、曲线和非有限结果。
        /// </summary>
        /// <param name="level">用于采样曲线的等级，必须至少为 1。</param>
        /// <param name="value">求值成功时返回最终值。</param>
        /// <returns>配置和求值结果都有效时返回 true。</returns>
        public bool TryEvaluate(int level, out float value)
        {
            value = default;
            if (level < 1 || !IsFinite(baseValue)) return false;
            if (levelCurve != null)
            {
                Keyframe[] keys = levelCurve.keys;
                for (int i = 0; i < keys.Length; i++)
                    if (!IsFinite(keys[i].time) || !IsFinite(keys[i].value))
                        return false;
            }

            float curveMultiplier = levelCurve == null || levelCurve.length == 0
                ? 1f
                : levelCurve.Evaluate(level);
            if (!IsFinite(curveMultiplier)) return false;

            value = baseValue * curveMultiplier;
            return IsFinite(value);
        }

        #endregion

        #region 校验

        /// <summary>判断一个浮点数是否可以安全进入等级数值计算。</summary>
        /// <param name="value">待校验值。</param>
        /// <returns>不是 NaN 且不是 Infinity 时返回 true。</returns>
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
