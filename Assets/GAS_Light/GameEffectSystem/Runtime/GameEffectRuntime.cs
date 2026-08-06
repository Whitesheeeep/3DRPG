using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>保存一次 GE 应用的运行状态，并作为其持续 Attribute Modifier 的 Source Handle。</summary>
    public sealed class GameEffectRuntime : IModifierSource
    {
        #region 字段

        private Dictionary<GameplayTag, float> setByCaller;

        #endregion

        #region 属性

        /// <summary>获取产生该 Runtime 的不可变配置资产。</summary>
        public GameplayEffectData Data { get; }
        /// <summary>获取当前用于 Modifier Magnitude 计算的来源 ASC。</summary>
        public AbilitySystemComponentBase Source { get; private set; }
        /// <summary>获取拥有并处理该 Runtime 的目标 ASC。</summary>
        public AbilitySystemComponentBase Target { get; }
        /// <summary>获取本次应用等级。</summary>
        public int Level { get; private set; }
        /// <summary>获取当前合并层数。</summary>
        public int StackCount { get; private set; }
        /// <summary>获取 Duration GE 的剩余时间；Infinite 不消费该值。</summary>
        public float RemainingDuration { get; private set; }
        /// <summary>获取距离下一次周期结算的剩余时间。</summary>
        public float RemainingPeriod { get; private set; }
        /// <summary>获取 Runtime 当前是否位于目标 Controller 的 Active 列表。</summary>
        public bool IsActive { get; private set; }
        /// <summary>获取本次应用复制后的 SetByCaller 只读数据。</summary>
        public IReadOnlyDictionary<GameplayTag, float> SetByCaller => setByCaller;

        #endregion

        #region 运行时构造

        // Runtime 只能由 GameEffectCtrl 创建，确保 Target、层数与计时器初始状态一致。
        internal GameEffectRuntime(
            GameplayEffectData data,
            AbilitySystemComponentBase source,
            AbilitySystemComponentBase target,
            int level,
            IReadOnlyDictionary<GameplayTag, float> values)
        {
            Data = data;
            Source = source;
            Target = target;
            Level = level;
            StackCount = 1;
            RemainingDuration = data.Duration;
            RemainingPeriod = data.Period;
            setByCaller = CopyValues(values);
        }

        /// <summary>
        /// 为重复应用创建未提交候选，使 Modifier 结果提交失败时原 Active Runtime 不发生变化。
        /// </summary>
        internal GameEffectRuntime CreateCandidate(
            AbilitySystemComponentBase source,
            int level,
            int stackCount,
            IReadOnlyDictionary<GameplayTag, float> values)
        {
            var candidate = new GameEffectRuntime(Data, source, Target, level, values)
            {
                StackCount = stackCount,
                RemainingDuration = RemainingDuration,
                RemainingPeriod = RemainingPeriod,
                IsActive = IsActive
            };
            return candidate;
        }

        // 原子业务操作成功后复制候选应用参数与计时状态；Data 和 Target 始终不变。
        internal void CommitCandidate(GameEffectRuntime candidate)
        {
            Source = candidate.Source;
            Level = candidate.Level;
            StackCount = candidate.StackCount;
            RemainingDuration = candidate.RemainingDuration;
            RemainingPeriod = candidate.RemainingPeriod;
            IsActive = candidate.IsActive;
            setByCaller = CopyValues(candidate.setByCaller);
        }

        #endregion

        #region 公开查询

        /// <summary>尝试读取调用方以 GameplayTag Key 提供的 Magnitude。</summary>
        /// <param name="key">稳定 SetByCaller Tag Key。</param>
        /// <param name="value">成功时返回本次应用保存的值。</param>
        /// <returns>Key 有效且调用方提供过该值时返回 true。</returns>
        public bool TryGetSetByCaller(GameplayTag key, out float value)
        {
            if (key.IsValid && setByCaller.TryGetValue(key, out value)) return true;
            value = default;
            return false;
        }

        // Controller 已统一确认必需 Key；Modifier 内部直接读取，缺失表示违反调用契约。
        internal float GetSetByCaller(GameplayTag key) => setByCaller[key];

        #endregion

        #region Modifier 计算

        /// <summary>使用当前 Runtime 状态计算全部最终 Modifier，并绑定指定的真实 Source。</summary>
        /// <param name="modifierSource">最终写入每个 AttributeModifier 的 Source。</param>
        /// <returns>与 Data.Modifiers 顺序一致的不可变候选 Modifier。</returns>
        public List<AttributeModifier> CalculateModifiers(IModifierSource modifierSource)
        {
            IReadOnlyList<GameplayEffectModifier> modifiers = Data.Modifiers;
            var results = new List<AttributeModifier>(modifiers.Count);
            for (int i = 0; i < modifiers.Count; i++)
                results.Add(modifiers[i].CreateModifier(modifierSource, Source, Target, this));
            return results;
        }

        #endregion

        #region Controller 状态修改

        // 仅由 Controller 在成功加入或移出 Active 列表时同步生命周期标记。
        internal void SetActive(bool value) => IsActive = value;

        // 叠层持续时间策略在候选 Runtime 上修改，失败时不会污染原状态。
        internal void SetRemainingDuration(float value) => RemainingDuration = value;

        // 周期刷新策略在候选 Runtime 上修改，失败时不会污染原状态。
        internal void SetRemainingPeriod(float value) => RemainingPeriod = value;

        // Duration 到期只减少层数时更新运行状态，不重新创建 Runtime 身份。
        internal void SetStackCount(int value) => StackCount = value;

        #endregion

        #region 内部辅助

        // 复制调用方字典，避免应用成功后外部修改 Magnitude；非法条目由 Controller 预先拒绝。
        private static Dictionary<GameplayTag, float> CopyValues(
            IReadOnlyDictionary<GameplayTag, float> values)
        {
            var copy = new Dictionary<GameplayTag, float>();
            if (values == null) return copy;
            foreach (KeyValuePair<GameplayTag, float> pair in values) copy[pair.Key] = pair.Value;
            return copy;
        }

        #endregion
    }
}
