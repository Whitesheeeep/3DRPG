using System;

namespace RPG.Character
{
    /// <summary>控制一次 FullBody Action 注册的幂等注销。</summary>
    public sealed class FullBodyActionHandle : IDisposable
    {
        // 生命周期依赖：Handle 只保存登记它的最小所有者契约与不可复用的注册标识。
        private IFullBodyActionRegistrationOwner owner;
        private readonly int registrationId;

        /// <summary>创建绑定指定 Action Arbiter 注册记录的生命周期 Handle。</summary>
        /// <param name="ownerArbiter">拥有注册记录的角色动作仲裁器。</param>
        /// <param name="sourceRegistrationId">注册记录的单调标识。</param>
        internal FullBodyActionHandle(IFullBodyActionRegistrationOwner sourceOwner, int sourceRegistrationId)
        {
            owner = sourceOwner ?? throw new ArgumentNullException(nameof(sourceOwner));
            registrationId = sourceRegistrationId;
        }

        /// <summary>幂等注销本次 FullBody Action，并在最后一个占据者退出时归还 Blackboard。</summary>
        public void Dispose()
        {
            IFullBodyActionRegistrationOwner currentOwner = owner;
            if (currentOwner == null)
                return;

            owner = null;
            currentOwner.UnregisterFullBodyAction(registrationId);
        }
    }
}
