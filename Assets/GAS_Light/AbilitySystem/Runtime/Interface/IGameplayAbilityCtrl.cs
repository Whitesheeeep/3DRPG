using System;
using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>定义单个 ASC 的 Ability 授予、激活、Tick 推进与显式生命周期控制。</summary>
    public interface IGameplayAbilityCtrl
    {
        /// <summary>Runtime 成功进入 Active 后发送。</summary>
        event Action<GameplayAbilityRuntime> AbilityActivated;
        /// <summary>Runtime 正常结束且移出 Active 集合后发送。</summary>
        event Action<GameplayAbilityRuntime> AbilityEnded;
        /// <summary>Runtime 被取消且移出 Active 集合后发送。</summary>
        event Action<GameplayAbilityRuntime> AbilityCancelled;

        /// <summary>获取该 Controller 服务的 Source ASC。</summary>
        GameplayAbilitySystemComponent Owner { get; }
        /// <summary>获取当前被授予的 Ability Specs。</summary>
        IReadOnlyList<GameplayAbilitySpec> GrantedAbilities { get; }
        /// <summary>获取尚未 End 或 Cancel 的激活 Runtime。</summary>
        IReadOnlyList<GameplayAbilityRuntime> ActiveRuntimes { get; }

        /// <summary>向 Owner 授予一个指定等级的 Ability。</summary>
        /// <param name="data">要授予的 Ability 作者数据。</param>
        /// <param name="level">初始等级，必须至少为 1。</param>
        /// <returns>授予成功时返回有效 Handle，否则返回 Invalid。</returns>
        GameplayAbilityHandle GiveAbility(GameplayAbilityData data, int level);

        /// <summary>修改后续激活使用的 Spec 等级，不改变已有 Runtime 快照。</summary>
        /// <param name="handle">目标 Ability Spec Handle。</param>
        /// <param name="level">新等级，必须至少为 1。</param>
        /// <returns>Handle 属于当前 Controller 且等级合法时返回 true。</returns>
        bool TrySetAbilityLevel(GameplayAbilityHandle handle, int level);

        /// <summary>查询当前 Controller 中的 Ability Spec。</summary>
        /// <param name="handle">目标 Ability Spec Handle。</param>
        /// <param name="spec">成功时返回对应 Spec。</param>
        /// <returns>Handle 有效且仍被授予时返回 true。</returns>
        bool TryGetAbilitySpec(GameplayAbilityHandle handle, out GameplayAbilitySpec spec);

        /// <summary>移除没有 Active Runtime 的 Ability Spec。</summary>
        /// <param name="handle">目标 Ability Spec Handle。</param>
        /// <returns>Spec 存在且没有激活实例时返回 true。</returns>
        bool TryRemoveAbility(GameplayAbilityHandle handle);

        /// <summary>检查条件并提交 Cost/Cooldown，然后启动新 Runtime。</summary>
        /// <param name="handle">待激活 Spec Handle。</param>
        /// <param name="setByCaller">本次激活的动态 GameplayTag 数值；可以为 null。</param>
        /// <param name="runtime">成功时返回新建的 Runtime。</param>
        /// <returns>条件、Cooldown、Cost 和 Runtime 创建全部成功时返回 true。</returns>
        bool TryActivate(
            GameplayAbilityHandle handle,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            out GameplayAbilityRuntime runtime);

        /// <summary>推进该 Controller 当前注册的 Ability Tick Task。</summary>
        /// <param name="deltaTime">本次推进的秒数，负数、NaN 和 Infinity 会被忽略。</param>
        void Tick(float deltaTime);

        /// <summary>正常结束属于当前 Controller 的 Active Runtime。</summary>
        /// <param name="runtime">要结束的 Runtime。</param>
        /// <returns>Runtime 属于当前 Controller 且成功从 Active 结束时返回 true。</returns>
        bool TryEnd(GameplayAbilityRuntime runtime);

        /// <summary>取消属于当前 Controller 的 Active Runtime。</summary>
        /// <param name="runtime">要取消的 Runtime。</param>
        /// <returns>Runtime 属于当前 Controller 且成功取消时返回 true。</returns>
        bool TryCancel(GameplayAbilityRuntime runtime);

        /// <summary>逐个取消 Active Runtime 并清除所有 Spec。</summary>
        void Clear();
    }
}