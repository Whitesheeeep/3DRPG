using System;
using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>保存一个 GE Modifier 的作者配置，并在应用时计算最终不可变 AttributeModifier。</summary>
    [Serializable]
    public abstract class GameplayEffectModifier
    {
        #region 配置字段

        [UnityEngine.SerializeField] private GameplayAttribute attribute = GameplayAttribute.Empty;
        [UnityEngine.SerializeField] private AttributeModifierType type;
        [UnityEngine.SerializeField] private int priority;

        #endregion

        #region 属性

        /// <summary>获取目标 Attribute。</summary>
        public GameplayAttribute Attribute => attribute;

        /// <summary>获取数值运算类型。</summary>
        public AttributeModifierType Type => type;

        /// <summary>获取从小到大执行的优先级。</summary>
        public int Priority => priority;

        #endregion

        #region 运行时计算

        // 使用计算状态 Runtime 求出 Magnitude，但把最终 Source 绑定到真实 Active Runtime。
        internal AttributeModifier CreateModifier(
            IModifierSource modifierSource,
            AbilitySystemComponentBase source,
            AbilitySystemComponentBase target,
            GameEffectRuntime runtime)
        {
            float magnitude = CalculateMagnitude(source, target, runtime);
            return new AttributeModifier(modifierSource, attribute, type, magnitude, priority);
        }

        // 子类只负责计算本次 Magnitude；合法性由最终 AttributeContainer 边界统一检查。
        protected abstract float CalculateMagnitude(
            AbilitySystemComponentBase source,
            AbilitySystemComponentBase target,
            GameEffectRuntime runtime);

        // 只有依赖调用方动态值的 Modifier 才登记 Key；Controller 在计算前统一检查。
        protected internal virtual void CollectRequiredSetByCallerKeys(ISet<GameplayTag> keys)
        {
        }

        #endregion
    }
}
