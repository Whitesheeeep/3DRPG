using System;
using WS_Modules.CustomEventSystem;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>聚合单个 GAS Owner 的 Attribute、Tag、GE 与 GA 运行服务。</summary>
    public abstract class AbilitySystemComponentBase
    {
        #region 字段
        private readonly GameplayAbilityCtrl abilityController;
        #endregion

        #region 属性
        /// <summary>获取该 Owner 的 GameplayTag 计数容器。</summary>
        public GameplayTagCountContainer Tags { get; }
        /// <summary>获取该 Owner 的 Attribute 容器。</summary>
        public GameplayAttributeContainer Attributes { get; }
        /// <summary>获取以当前实例为隐式 Target 的 GE Controller。</summary>
        public IGameEffectCtrl GameEffectCtrl { get; }
        /// <summary>获取以当前实例为 Source 的 GA Controller。</summary>
        public IGameplayAbilityCtrl Abilities { get; }
        #endregion

        #region 构造与 Tick
        /// <summary>创建运行容器；外部通过 Tick 推进 GE 与 GA。</summary>
        protected AbilitySystemComponentBase()
        {
            Tags = new GameplayTagCountContainer();
            Attributes = new GameplayAttributeContainer();
            GameEffectCtrl = new GameEffectCtrl(this);
            abilityController = new GameplayAbilityCtrl(this);
            Abilities = abilityController;
        }

        /// <summary>按统一顺序推进 GE 和 GA；GE 先更新，Ability Task 后更新。</summary>
        /// <param name="deltaTime">本次推进的秒数。</param>
        public void Tick(float deltaTime)
        {
            GameEffectCtrl.Tick(deltaTime);
            abilityController.Tick(deltaTime);
        }

        /// <summary>获取当前 Ability Tick 注册数量，仅供测试和诊断使用。</summary>
        internal int TickRegistrationCount => abilityController.TickRegistrationCount;

        // TickTask 通过 ASC 进入具体 Controller，避免暴露调度器实现。
        internal IUnRegister RegisterAbilityTick(Action<float> callback) =>
            abilityController.RegisterTick(callback);
        #endregion
    }
}