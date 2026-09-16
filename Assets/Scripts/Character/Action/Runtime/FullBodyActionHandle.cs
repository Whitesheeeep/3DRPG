using System;
using RPG.SkillSystem;

namespace RPG.Character
{
    /// <summary>控制一次 FullBody Action 注册的权限更新与幂等注销。</summary>
    public sealed class FullBodyActionHandle : IDisposable
    {
        // 生命周期依赖：Handle 只保存登记它的 Arbiter 与不可复用的注册标识。
        private CharacterActionArbiter arbiter;
        private readonly int registrationId;

        /// <summary>创建绑定指定 Action Arbiter 注册记录的生命周期 Handle。</summary>
        /// <param name="ownerArbiter">拥有注册记录的角色动作仲裁器。</param>
        /// <param name="sourceRegistrationId">注册记录的单调标识。</param>
        internal FullBodyActionHandle(CharacterActionArbiter ownerArbiter, int sourceRegistrationId)
        {
            arbiter = ownerArbiter ?? throw new ArgumentNullException(nameof(ownerArbiter));
            registrationId = sourceRegistrationId;
        }

        /// <summary>更新当前 Skill Phase 开放的转换窗口；Handle 已释放时忽略调用。</summary>
        /// <param name="allowedTransitions">新的转换权限组合。</param>
        public void UpdateAllowedTransitions(SkillTransitionMask allowedTransitions)
        {
            arbiter?.UpdateAllowedTransitions(registrationId, allowedTransitions);
        }

        /// <summary>幂等注销本次 FullBody Action，并在最后一个占据者退出时归还 Blackboard。</summary>
        public void Dispose()
        {
            CharacterActionArbiter currentArbiter = arbiter;
            if (currentArbiter == null)
                return;

            arbiter = null;
            currentArbiter.UnregisterFullBodyAction(registrationId);
        }
    }
}
