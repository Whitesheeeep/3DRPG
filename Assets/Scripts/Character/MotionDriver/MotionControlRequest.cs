using System;
using WS_Modules.GAS.AbilitySystemComponent;

namespace RPG.Character
{
    /// <summary>描述一个跨帧有效的角色运动控制权请求。</summary>
    public readonly struct MotionControlRequest
    {
        /// <summary>创建运动控制请求。</summary>
        /// <param name="owner">请求所属 Character Owner。</param>
        /// <param name="priority">固定优先级段。</param>
        /// <param name="channels">希望控制的运动通道。</param>
        /// <param name="consumeAnimatorMotion">获胜时是否消费 Animator 根运动。</param>
        public MotionControlRequest(IGameplayAbilitySystemOwner owner, MotionPriority priority,
            MotionChannels channels, bool consumeAnimatorMotion)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Priority = priority;
            Channels = channels;
            ConsumeAnimatorMotion = consumeAnimatorMotion;
        }

        /// <summary>获取请求所属 Character Owner。</summary>
        public IGameplayAbilitySystemOwner Owner { get; }
        /// <summary>获取请求优先级。</summary>
        public MotionPriority Priority { get; }
        /// <summary>获取请求控制的通道。</summary>
        public MotionChannels Channels { get; }
        /// <summary>获取获胜时是否消费 Animator 根运动。</summary>
        public bool ConsumeAnimatorMotion { get; }
    }
}
