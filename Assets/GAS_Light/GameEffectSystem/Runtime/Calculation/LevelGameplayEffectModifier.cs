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

        /// <summary>根据 Runtime.Level 选择最近且不高于它的离散 Magnitude。</summary>
        // 线性查找不高于 Runtime.Level 的最高配置等级，列表作者顺序不影响结果。
        protected override float CalculateMagnitude(
            GameplayAbilitySystemComponent source,
            GameplayAbilitySystemComponent target,
            GameEffectRuntime runtime)
        {
            int selectedLevel = 0;
            float selectedMagnitude = float.NaN;
            for (int i = 0; i < levelMagnitudes.Count; i++)
            {
                GameplayEffectLevelMagnitude item = levelMagnitudes[i];
                // 当 Runtime 的等级大于等于配置等级，且该配置等级高于当前选中等级时，更新选中等级与 Magnitude。
                // 这样可以确保选中的是不高于 Runtime.Level 的最高配置等级。
                if (item.Level <= runtime.Level && item.Level > selectedLevel)
                {
                    selectedLevel = item.Level;
                    selectedMagnitude = item.Magnitude;
                }
            }

            return selectedMagnitude;
        }
    }
}
