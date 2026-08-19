using System;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

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
    /// 保存一次 SkillExecution 在指定逻辑帧切换后的动作阶段与可打断状态快照。
    /// </summary>
    public readonly struct SkillActionPhaseChangedEventArgs
    {
        public ulong ExecutionId { get; }
        public SkillConfig Config { get; }
        public int Frame { get; }
        public ActionPhaseType Phase { get; }
        public bool CanBeInterrupted { get; }

        /// <summary>创建动作阶段变化事件快照。</summary>
        /// <param name="executionId">Module 内单调递增的执行标识。</param>
        /// <param name="config">本次执行使用的 SkillConfig。</param>
        /// <param name="frame">阶段状态生效的整数逻辑帧。</param>
        /// <param name="phase">当前动作阶段。</param>
        /// <param name="canBeInterrupted">当前阶段是否允许外部打断。</param>
        public SkillActionPhaseChangedEventArgs(
            ulong executionId,
            SkillConfig config,
            int frame,
            ActionPhaseType phase,
            bool canBeInterrupted)
        {
            ExecutionId = executionId;
            Config = config;
            Frame = frame;
            Phase = phase;
            CanBeInterrupted = canBeInterrupted;
        }
    }

    /// <summary>保存 Projectile Track 到达发射帧时的无 GAS 依赖事件快照。</summary>
    public readonly struct SkillProjectileSpawnEventArgs
    {
        /// <summary>获取本次 SkillExecution 的稳定标识。</summary>
        public ulong ExecutionId { get; }
        /// <summary>获取本次播放使用的 SkillConfig。</summary>
        public SkillConfig Config { get; }
        /// <summary>获取触发发射的单帧 Projectile Clip。</summary>
        public ProjectileSkillClipConfig Clip { get; }
        /// <summary>获取 Clip 使用的完整 Projectile Spawn 配置。</summary>
        public ProjectileSpawnConfig SpawnConfig { get; }
        /// <summary>获取已经解析完成的发射空间基准。</summary>
        public Transform Origin { get; }
        /// <summary>获取触发该事件的整数逻辑帧。</summary>
        public int Frame { get; }

        /// <summary>创建一次 Projectile 发射事件快照。</summary>
        /// <param name="executionId">Module 内单调递增的执行标识。</param>
        /// <param name="config">本次播放使用的 SkillConfig。</param>
        /// <param name="clip">达到发射帧的 Projectile Clip。</param>
        /// <param name="origin">已解析的 Marker 或角色 Origin。</param>
        /// <param name="frame">当前整数逻辑帧。</param>
        public SkillProjectileSpawnEventArgs(
            ulong executionId,
            SkillConfig config,
            ProjectileSkillClipConfig clip,
            Transform origin,
            int frame)
        {
            ExecutionId = executionId;
            Config = config;
            Clip = clip;
            SpawnConfig = clip.SpawnConfig;
            Origin = origin;
            Frame = frame;
        }
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
