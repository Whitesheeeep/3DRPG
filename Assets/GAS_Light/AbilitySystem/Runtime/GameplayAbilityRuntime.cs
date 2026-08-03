using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保存一次 GA 激活的等级快照、动态输入与显式生命周期状态。</summary>
    public sealed class GameplayAbilityRuntime
    {
        #region 字段
        private readonly Dictionary<GameplayTag, float> setByCaller;
        #endregion

        #region 属性
        /// <summary>获取所属 Controller 单调分配的单次激活标识。</summary>
        public int ActivationId { get; }
        /// <summary>获取创建该 Runtime 的长期 Ability Spec。</summary>
        public GameplayAbilitySpec Spec { get; }
        /// <summary>获取释放本次 Ability 的 ASC。</summary>
        public AbilitySystemComponentBase Source { get; }
        /// <summary>获取激活时从 Spec 复制的等级快照。</summary>
        public int Level { get; }
        /// <summary>获取当前生命周期状态。</summary>
        public GameplayAbilityRuntimeState State { get; private set; }
        /// <summary>获取该 Runtime 是否已经完成唯一一轮基础 Effect 执行。</summary>
        public bool HasExecuted { get; private set; }
        /// <summary>获取激活时复制的 SetByCaller 只读数据。</summary>
        public IReadOnlyDictionary<GameplayTag, float> SetByCaller => setByCaller;
        #endregion

        #region 构造与查询
        // Runtime 只能由所属 Controller 创建，初始状态固定为 Active 且尚未执行。
        internal GameplayAbilityRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            AbilitySystemComponentBase source,
            IReadOnlyDictionary<GameplayTag, float> values)
        {
            ActivationId = activationId;
            Spec = spec;
            Source = source;
            Level = spec.Level;
            State = GameplayAbilityRuntimeState.Active;
            setByCaller = CopyValues(values);
        }

        /// <summary>尝试读取本次激活以稳定 GameplayTag Key 提供的动态值。</summary>
        /// <param name="key">SetByCaller 的稳定 Tag Key。</param>
        /// <param name="value">成功时返回激活时复制的值。</param>
        /// <returns>当前 Runtime 保存该 Key 时返回 true。</returns>
        public bool TryGetSetByCaller(GameplayTag key, out float value) =>
            setByCaller.TryGetValue(key, out value);
        #endregion

        #region Controller 状态修改
        // 唯一一轮 Effect 调用完成后锁定执行入口，避免同一 Runtime 被重复结算。
        internal void MarkExecuted() => HasExecuted = true;
        // 仅由 Controller 在正常结束并移出 Active 集合时提交状态。
        internal void MarkEnded() => State = GameplayAbilityRuntimeState.Ended;
        // 仅由 Controller 在取消并移出 Active 集合时提交状态。
        internal void MarkCancelled() => State = GameplayAbilityRuntimeState.Cancelled;
        #endregion

        #region 内部辅助
        // 复制调用方字典，确保 Runtime 的动态输入不会被外部后续修改。
        private static Dictionary<GameplayTag, float> CopyValues(
            IReadOnlyDictionary<GameplayTag, float> values)
        {
            var copy = new Dictionary<GameplayTag, float>();
            if (values == null) return copy;
            foreach (KeyValuePair<GameplayTag, float> pair in values) copy.Add(pair.Key, pair.Value);
            return copy;
        }
        #endregion
    }
}
