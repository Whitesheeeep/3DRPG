using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>定义单个目标 ASC 的 GE 应用、更新、移除与只读查询能力。</summary>
    public interface IGameEffectCtrl
    {
        /// <summary>获取该 Controller 服务的目标 ASC。</summary>
        AbilitySystemComponentBase Owner { get; }
        /// <summary>获取当前 Duration 与 Infinite Runtime 的只读视图。</summary>
        IReadOnlyList<GameEffectRuntime> ActiveEffects { get; }

        /// <summary>检查配置、来源与目标 Tag 是否允许进入应用流程。</summary>
        /// <param name="data">待应用的 GE 配置。</param>
        /// <param name="source">效果来源 ASC。</param>
        /// <returns>基础配置与目标条件合法时返回 true。</returns>
        bool CanApply(GameplayEffectData data, AbilitySystemComponentBase source);

        /// <summary>向 Owner 应用一次 GE；Target 隐式为 Owner。</summary>
        /// <param name="data">待应用的 GE 配置资产。</param>
        /// <param name="source">效果来源 ASC。</param>
        /// <param name="level">本次应用等级，必须至少为 1。</param>
        /// <param name="setByCaller">可为空的稳定 Tag Key 到 Magnitude 映射。</param>
        /// <param name="activeEffect">持续 GE 成功时返回 Runtime；Instant 成功时返回 null。</param>
        /// <returns>全部计算与提交成功时返回 true。</returns>
        bool TryApply(
            GameplayEffectData data,
            AbilitySystemComponentBase source,
            int level,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            out GameEffectRuntime activeEffect);

        /// <summary>判断当前 Target 是否存在由指定配置产生的 Active GE。</summary>
        /// <param name="data">需要查询的 GE 配置引用。</param>
        /// <returns>存在引用相同且仍 Active 的 Runtime 时返回 true。</returns>
        bool HasActiveEffect(GameplayEffectData data);

        /// <summary>移除一个属于当前 Controller 的 Active Runtime。</summary>
        /// <param name="activeEffect">需要移除的 Runtime 引用。</param>
        /// <returns>Runtime 属于当前 Controller 且已完成清理时返回 true。</returns>
        bool TryRemove(GameEffectRuntime activeEffect);

        /// <summary>推进全部 Active GE 的周期与到期计时。</summary>
        /// <param name="deltaTime">本次同步推进的非负秒数。</param>
        void Tick(float deltaTime);

        /// <summary>移除全部 Active GE，并清理其 Modifier 与 GrantedTags。</summary>
        void Clear();
    }
}
