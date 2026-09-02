using System;
using WS_Modules.GAS.AbilitySystemComponent;

namespace RPG.Character
{
    /// <summary>标识一个可幂等释放的持续运动控制请求。</summary>
    public sealed class MotionControlHandle : IDisposable
    {
        private MotionDriver driver;

        /// <summary>由 MotionDriver 创建控制请求句柄。</summary>
        /// <param name="driver">拥有请求注册表的驱动器。</param>
        /// <param name="id">驱动器内唯一请求编号。</param>
        /// <param name="owner">请求所属 Character Owner。</param>
        internal MotionControlHandle(MotionDriver driver, int id, IGameplayAbilitySystemOwner owner)
        {
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
            Id = id;
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        /// <summary>获取驱动器内唯一请求编号。</summary>
        internal int Id { get; }
        /// <summary>获取请求所属 Character Owner。</summary>
        public IGameplayAbilitySystemOwner Owner { get; }
        /// <summary>获取句柄是否仍然有效。</summary>
        public bool IsValid => driver != null;

        /// <summary>幂等释放控制请求。</summary>
        public void Dispose()
        {
            MotionDriver current = driver;
            if (current == null) return;
            driver = null;
            current.Release(this);
        }

        /// <summary>由 MotionDriver 批量清理 Owner 时使句柄失效。</summary>
        internal void Invalidate() => driver = null;
    }
}
