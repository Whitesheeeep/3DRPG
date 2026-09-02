using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>保存一个离散 GE 等级及其对应的最终 Magnitude。</summary>
    [Serializable]
    public struct GameplayEffectLevelMagnitude
    {
        [SerializeField, Min(1)] private int level;
        [SerializeField] private float magnitude;

        /// <summary>获取该数值生效的最低 GE 等级。</summary>
        public int Level => level;

        /// <summary>获取该等级对应的最终 Magnitude。</summary>
        public float Magnitude => magnitude;
    }

    /// <summary>按 Runtime Level 选择最近且不高于它的离散 Magnitude。</summary>
    [Serializable]
    public sealed class LevelGameplayEffectModifier : GameplayEffectModifier
    {
        [SerializeField, Tooltip("离散等级与最终 Magnitude；必须包含 Level 1。")]
        private List<GameplayEffectLevelMagnitude> levelMagnitudes = new();

        // 线性查找不高于 Runtime.Level 的最高配置等级，列表作者顺序不影响结果。
        protected override float CalculateMagnitude(
            GameplayAbilitySystemComponent source,
            GameplayAbilitySystemComponent target,
            GameEffectRuntime runtime)
        {
            return TryGetMagnitude(runtime.Level, out float value) ? value : float.NaN;
        }

        /// <summary>按指定等级读取离散 Magnitude。</summary>
        /// <param name="level">查询等级。</param>
        /// <param name="value">成功时返回最近的合法 Magnitude。</param>
        /// <returns>找到不高于查询等级的配置时返回 true。</returns>
        private bool TryGetMagnitude(int level, out float value)
        {
            int selectedLevel = 0;
            float selectedMagnitude = default;
            for (int i = 0; i < levelMagnitudes.Count; i++)
            {
                GameplayEffectLevelMagnitude item = levelMagnitudes[i];
                if (item.Level <= level && item.Level > selectedLevel)
                {
                    selectedLevel = item.Level;
                    selectedMagnitude = item.Magnitude;
                }
            }

            value = selectedMagnitude;
            return selectedLevel > 0;
        }

        /// <summary>在不创建 Runtime 的情况下查询离散 Magnitude。</summary>
        /// <param name="level">查询等级。</param>
        /// <param name="value">成功时返回 Magnitude。</param>
        /// <returns>存在适用等级时返回 true。</returns>
        internal override bool TryCalculateStaticMagnitude(int level, out float value)
        {
            // 缺少可用等级时返回 NaN，让静态查询明确报告配置错误，而不是误判为动态上下文。
            if (TryGetMagnitude(level, out value)) return true;
            value = float.NaN;
            return true;
        }
    }
}
