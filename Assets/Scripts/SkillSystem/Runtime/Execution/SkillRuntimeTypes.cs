using System;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 标识一次技能执行结束的原因，供外部状态机决定后续状态，而不由技能播放器接管动画退出。
    /// </summary>
    public enum SkillCompletionReason
    {
        Natural,
        Stopped,
        Cancelled
    }

    /// <summary>
    /// 表示尝试启动技能的结果；失败结果不会创建执行实例或修改当前技能状态。
    /// </summary>
    public readonly struct SkillStartResult
    {
        public bool Succeeded { get; }
        public string Message { get; }

        /// <summary>
        /// 创建技能启动结果。
        /// </summary>
        /// <param name="succeeded">是否成功创建执行实例。</param>
        /// <param name="message">失败原因；成功时为空字符串。</param>
        private SkillStartResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message;
        }

        /// <summary>
        /// 创建成功结果。
        /// </summary>
        /// <returns>成功的启动结果。</returns>
        public static SkillStartResult Success() => new(true, string.Empty);

        /// <summary>
        /// 创建带有明确原因的失败结果。
        /// </summary>
        /// <param name="message">可直接展示或记录的失败原因。</param>
        /// <returns>失败的启动结果。</returns>
        public static SkillStartResult Failure(string message) => new(false, message);
    }

    /// <summary>
    /// 保存一次技能播放的动态输入；武器节点属于装备实例，不写回 SkillConfig。
    /// </summary>
    public readonly struct SkillPlayRequest
    {
        public SkillConfig Config { get; }
        public Transform WeaponRoot { get; }
        public Transform WeaponTip { get; }

        /// <summary>
        /// 创建技能播放请求。
        /// </summary>
        /// <param name="config">本次执行使用的技能配置。</param>
        /// <param name="weaponRoot">当前武器刀根；非 WeaponTrace 技能可为空。</param>
        /// <param name="weaponTip">当前武器刀尖；非 WeaponTrace 技能可为空。</param>
        public SkillPlayRequest(SkillConfig config, Transform weaponRoot = null, Transform weaponTip = null)
        {
            Config = config;
            WeaponRoot = weaponRoot;
            WeaponTip = weaponTip;
        }
    }

    /// <summary>
    /// 描述一次技能执行结束事件；事件发送前 Module 已释放当前执行引用，可在回调中立即播放下一技能。
    /// </summary>
    public readonly struct SkillCompletedEventArgs
    {
        public ulong ExecutionId { get; }
        public SkillConfig Config { get; }
        public GameObject Owner { get; }
        public SkillCompletionReason Reason { get; }
        public int LastFrame { get; }

        /// <summary>
        /// 创建技能结束事件快照。
        /// </summary>
        /// <param name="executionId">Module 内单调递增的执行标识。</param>
        /// <param name="config">已经结束的技能配置。</param>
        /// <param name="owner">本次技能施法者。</param>
        /// <param name="reason">自然结束、主动停止或取消原因。</param>
        /// <param name="lastFrame">结束前最后处理的整数帧。</param>
        public SkillCompletedEventArgs(ulong executionId, SkillConfig config, GameObject owner,
            SkillCompletionReason reason, int lastFrame)
        {
            ExecutionId = executionId;
            Config = config;
            Owner = owner;
            Reason = reason;
            LastFrame = lastFrame;
        }
    }
}
