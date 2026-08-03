using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>聚合单个 GAS Owner 的 Attribute、TagCount 与 GameplayEffect 运行服务。</summary>
    public abstract class AbilitySystemComponentBase
    {
        #region 属性

        /// <summary>获取该 Owner 的分来源 GameplayTag 计数容器。</summary>
        public GameplayTagCountContainer Tags { get; }
        /// <summary>获取该 Owner 的运行时 Attribute 容器。</summary>
        public GameplayAttributeContainer Attributes { get; }
        /// <summary>获取以当前实例为隐式 Target 的 GE Controller。</summary>
        public IGameEffectCtrl GameEffectCtrl { get; }
        /// <summary>获取以当前实例为 Source 的 Gameplay Ability Controller。</summary>
        public IGameplayAbilityCtrl Abilities { get; }

        #endregion

        #region 构造

        // 创建相互绑定且仅由当前 ASC 拥有的运行时容器与 Controller。
        protected AbilitySystemComponentBase()
        {
            Tags = new GameplayTagCountContainer();
            Attributes = new GameplayAttributeContainer();
            GameEffectCtrl = new GameEffectCtrl(this);
            Abilities = new GameplayAbilityCtrl(this);
        }

        #endregion
    }
}
