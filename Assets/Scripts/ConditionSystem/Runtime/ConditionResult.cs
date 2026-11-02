using System;

namespace RPG.ConditionSystemNS
{
    /// <summary>描述单次判断的不可变结果；未满足时保留具体原因的运行时类型。</summary>
    public sealed class ConditionResult
    {
        // 满足结果没有业务状态，可以安全复用，避免逐节点创建相同对象。
        private static readonly ConditionResult metResult = new ConditionResult(true, null);
        /// <summary>获取条件是否满足。</summary>
        public bool IsMet { get; }
        /// <summary>获取未满足原因；满足时为 null。</summary>
        public ConditionReason Reason { get; }

        /// <summary>创建具有明确满足状态的结果。</summary>
        /// <param name="isMet">是否满足。</param>
        /// <param name="reason">未满足原因。</param>
        private ConditionResult(bool isMet, ConditionReason reason)
        {
            IsMet = isMet;
            Reason = reason;
        }

        /// <summary>创建满足结果。</summary>
        /// <returns>不包含失败原因的满足结果。</returns>
        public static ConditionResult Met() => metResult;

        /// <summary>创建未满足结果并保留原始原因对象。</summary>
        /// <param name="reason">由条件提供的非空原因。</param>
        /// <returns>包含原因的未满足结果。</returns>
        /// <exception cref="ArgumentNullException">原因为空时抛出。</exception>
        public static ConditionResult NotMet(ConditionReason reason) =>
            new ConditionResult(false, reason ?? throw new ArgumentNullException(nameof(reason)));
    }
}
