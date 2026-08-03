using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>定义单个 ASC 的 Ability 授予、激活、Effect 执行与显式结束能力。</summary>
    public interface IGameplayAbilityCtrl
    {
        /// <summary>获取该 Controller 服务的 Source ASC。</summary>
        AbilitySystemComponentBase Owner { get; }
        /// <summary>获取当前被授予的 Ability Specs。</summary>
        IReadOnlyList<GameplayAbilitySpec> GrantedAbilities { get; }
        /// <summary>获取尚未 End 或 Cancel 的激活 Runtime。</summary>
        IReadOnlyList<GameplayAbilityRuntime> ActiveRuntimes { get; }

        /// <summary>向 Owner 授予一个指定等级的 Ability。</summary>
        /// <param name="data">能力作者配置。</param>
        /// <param name="level">初始等级，必须至少为 1。</param>
        /// <returns>授予成功时返回有效 Handle，否则返回 Invalid。</returns>
        GameplayAbilityHandle GiveAbility(GameplayAbilityData data, int level);

        /// <summary>修改后续激活使用的 Spec 等级，不改变已有 Runtime 快照。</summary>
        /// <param name="handle">目标 Spec Handle。</param>
        /// <param name="level">新等级，必须至少为 1。</param>
        /// <returns>Handle 属于当前 Controller 且等级合法时返回 true。</returns>
        bool TrySetAbilityLevel(GameplayAbilityHandle handle, int level);

        /// <summary>查询当前 Controller 中的 Ability Spec。</summary>
        /// <param name="handle">目标 Spec Handle。</param>
        /// <param name="spec">成功时返回对应 Spec。</param>
        /// <returns>Handle 有效且仍被授予时返回 true。</returns>
        bool TryGetAbilitySpec(GameplayAbilityHandle handle, out GameplayAbilitySpec spec);

        /// <summary>移除没有 Active Runtime 的 Ability Spec。</summary>
        /// <param name="handle">目标 Spec Handle。</param>
        /// <returns>Spec 存在且没有激活实例时返回 true。</returns>
        bool TryRemoveAbility(GameplayAbilityHandle handle);

        /// <summary>检查条件并提交 Cost/Cooldown，随后创建单次 Active Runtime。</summary>
        /// <param name="handle">待激活 Spec Handle。</param>
        /// <param name="setByCaller">可为空的动态 GameplayTag 到数值映射。</param>
        /// <param name="runtime">成功时返回新建的 Active Runtime。</param>
        /// <returns>条件、Cooldown 与 Cost 全部提交成功时返回 true。</returns>
        bool TryActivate(
            GameplayAbilityHandle handle,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            out GameplayAbilityRuntime runtime);

        /// <summary>执行 Runtime 的唯一一轮 Self 与 Target GE，并返回成功创建的 Active GE。</summary>
        /// <param name="runtime">属于当前 Controller 的 Active Runtime。</param>
        /// <param name="targets">外部 Targeting 提供的非 null、无重复目标集合；Self 技能可传空集合。</param>
        /// <param name="activeEffects">成功时返回本轮产生的 Duration/Infinite GE Runtime。</param>
        /// <returns>Runtime 与目标集合满足调用契约且执行流程完成时返回 true。</returns>
        bool TryExecuteEffects(
            GameplayAbilityRuntime runtime,
            IReadOnlyList<AbilitySystemComponentBase> targets,
            out IReadOnlyList<GameEffectRuntime> activeEffects);

        /// <summary>正常结束一个属于当前 Controller 的 Active Runtime。</summary>
        bool TryEnd(GameplayAbilityRuntime runtime);
        /// <summary>取消一个属于当前 Controller 的 Active Runtime，比如被打断了，或者手动取消。</summary>
        bool TryCancel(GameplayAbilityRuntime runtime);
        /// <summary>取消全部 Active Runtime 并清除所有 Spec，不移除外部管理的 GE。</summary>
        void Clear();
    }
}
