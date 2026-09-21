using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>保存一次已应用 GE 的运行状态，并作为持续 Attribute Modifier 的 Source Handle。</summary>
    public sealed class GameEffectRuntime : IModifierSource
    {
        #region 字段

        // 依赖字段：当前 Runtime 通过封存 Spec 派生 Data、Source、Level 和 SetByCaller。
        private GameplayEffectSpec spec;
        private bool periodicCalculationFailed;

        #endregion

        #region 属性

        /// <summary>获取当前 Runtime 使用的 GE 作者配置。</summary>
        public GameplayEffectData Data => spec.Data;

        /// <summary>获取当前 Spec 固定的 Source ASC 身份。</summary>
        public GameplayAbilitySystemComponent Source => spec.Source;

        /// <summary>获取拥有并处理该 Runtime 的 Target ASC。</summary>
        public GameplayAbilitySystemComponent Target { get; }

        /// <summary>获取当前 Runtime 使用的封存 Spec。</summary>
        public GameplayEffectSpec Spec => spec;

        /// <summary>获取本次应用等级。</summary>
        public int Level => spec.Level;

        /// <summary>获取当前合并层数。</summary>
        public int StackCount { get; private set; }

        /// <summary>获取 Duration GE 的剩余时间；Infinite 不消费该值。</summary>
        public float RemainingDuration { get; private set; }

        /// <summary>获取距离下一次周期结算的剩余时间。</summary>
        public float RemainingPeriod { get; private set; }

        /// <summary>获取 Runtime 当前是否位于目标 Controller 的 Active 列表。</summary>
        public bool IsActive { get; private set; }

        /// <summary>获取当前 Spec 封存的 SetByCaller 只读数据。</summary>
        public IReadOnlyDictionary<GameplayTag, float> SetByCaller => spec.SetByCaller;

        /// <summary>获取最近一次成功的 GE 计算与提交结果。</summary>
        public GameplayEffectApplicationResult LastApplicationResult { get; private set; }

        #endregion

        #region 运行时构造

        // Runtime 只能由 Controller 创建，保证 Target、层数与计时器状态一致。
        /// <summary>创建尚未加入 Target Active 列表的 GE Runtime。</summary>
        /// <param name="effectSpec">已经封存的 GE Spec。</param>
        /// <param name="target">本次应用的 Target ASC。</param>
        /// <param name="stackCount">初始叠层数。</param>
        internal GameEffectRuntime(
            GameplayEffectSpec effectSpec,
            GameplayAbilitySystemComponent target,
            int stackCount = 1)
        {
            spec = effectSpec;
            Target = target;
            StackCount = stackCount;
            RemainingDuration = Data.Duration;
            RemainingPeriod = Data.Period;
        }

        /// <summary>为重应用创建候选 Runtime，使提交失败时原 Runtime 保持不变。</summary>
        /// <param name="effectSpec">候选使用的最新 Spec。</param>
        /// <param name="stackCount">候选层数。</param>
        /// <returns>复制当前计时与 Active 状态的候选 Runtime。</returns>
        internal GameEffectRuntime CreateCandidate(
            GameplayEffectSpec effectSpec,
            int stackCount)
        {
            var candidate = new GameEffectRuntime(effectSpec, Target, stackCount)
            {
                RemainingDuration = RemainingDuration,
                RemainingPeriod = RemainingPeriod,
                IsActive = IsActive,
                LastApplicationResult = LastApplicationResult,
                periodicCalculationFailed = periodicCalculationFailed
            };
            return candidate;
        }

        /// <summary>原子业务操作成功后提交候选 Spec、层数和计时状态。</summary>
        /// <param name="candidate">已经通过计算与提交校验的候选 Runtime。</param>
        internal void CommitCandidate(GameEffectRuntime candidate)
        {
            spec = candidate.spec;
            StackCount = candidate.StackCount;
            RemainingDuration = candidate.RemainingDuration;
            RemainingPeriod = candidate.RemainingPeriod;
            IsActive = candidate.IsActive;
            LastApplicationResult = candidate.LastApplicationResult;
            periodicCalculationFailed = candidate.periodicCalculationFailed;
        }

        #endregion

        #region 公开查询

        /// <summary>尝试读取本次应用以 GameplayTag Key 提供的 Magnitude。</summary>
        /// <param name="key">稳定 SetByCaller Tag Key。</param>
        /// <param name="value">成功时返回本次应用保存的值。</param>
        /// <returns>Key 有效且调用方提供过该值时返回 true。</returns>
        public bool TryGetSetByCaller(GameplayTag key, out float value) =>
            spec.SetByCaller.TryGetValue(key, out value);

        /// <summary>读取已经通过 Controller 校验的 SetByCaller 值。</summary>
        /// <param name="key">已登记为必需输入的 Tag Key。</param>
        /// <returns>对应的 SetByCaller 值。</returns>
        internal float GetSetByCaller(GameplayTag key) => spec.SetByCaller[key];

        #endregion

        #region Modifier 与 Execution 计算

        /// <summary>使用当前 Runtime 状态计算一次完整 GE Output。</summary>
        /// <param name="modifierSource">最终写入 AttributeModifier 的 Source Handle。</param>
        /// <returns>成功时返回聚合 Output；任一计算失败时返回 null。</returns>
        internal GameplayEffectCalculationOutput CalculateApplication(
            IModifierSource modifierSource)
        {
            var context = new GameplayEffectCalculationContext(
                spec,
                Target,
                StackCount,
                modifierSource);
            return Data.CalculateApplication(context);
        }

        #endregion

        #region Controller 状态修改

        /// <summary>仅由 Controller 在成功加入或移出 Active 列表时同步生命周期标记。</summary>
        /// <param name="value">新的 Active 状态。</param>
        internal void SetActive(bool value) => IsActive = value;

        /// <summary>在候选 Runtime 上设置剩余持续时间。</summary>
        /// <param name="value">新的剩余秒数。</param>
        internal void SetRemainingDuration(float value) => RemainingDuration = value;

        /// <summary>在候选 Runtime 上设置下一次周期剩余时间。</summary>
        /// <param name="value">新的周期剩余秒数。</param>
        internal void SetRemainingPeriod(float value) => RemainingPeriod = value;

        /// <summary>在候选 Runtime 上设置叠层数量。</summary>
        /// <param name="value">新的叠层数量。</param>
        internal void SetStackCount(int value) => StackCount = value;

        /// <summary>记录最近一次成功计算结果，供 Cue 和移除阶段查询。</summary>
        /// <param name="result">成功的应用结果。</param>
        internal void SetLastApplicationResult(GameplayEffectApplicationResult result) =>
            LastApplicationResult = result;

        /// <summary>更新周期计算失败状态，并只在状态发生变化时返回 true。</summary>
        /// <param name="failed">本次周期结算是否失败。</param>
        /// <returns>失败状态相对上一次发生变化时返回 true。</returns>
        internal bool SetPeriodicCalculationFailed(bool failed)
        {
            if (periodicCalculationFailed == failed) return false;
            periodicCalculationFailed = failed;
            return true;
        }

        #endregion
    }
}
