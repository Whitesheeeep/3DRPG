using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.Character
{
    /// <summary>保存当前 FullBody Skill 在动作仲裁层中的单次执行快照。</summary>
    internal sealed class FullBodySkillExecution
    {
        /// <summary>获取本次 Action 注册的单调标识。</summary>
        internal int RegistrationId { get; }
        /// <summary>获取实际需要被转换动作取消的 Ability Runtime。</summary>
        internal GameplayAbilityRuntime Runtime { get; }
        /// <summary>获取 Runtime 的稳定激活标识。</summary>
        internal int ActivationId => Runtime.ActivationId;
        /// <summary>创建一个尚未注销的 FullBody Skill 执行快照。</summary>
        /// <param name="registrationId">Action Arbiter 单调分配的注册标识。</param>
        /// <param name="runtime">当前活动 Ability Runtime。</param>
        internal FullBodySkillExecution(
            int registrationId,
            GameplayAbilityRuntime runtime)
        {
            RegistrationId = registrationId;
            Runtime = runtime;
        }
    }
}
