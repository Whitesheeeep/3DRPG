using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.TAG;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 描述 SkillConfig 本次执行采用的时间轴入口语义。
    /// </summary>
    public enum SkillStartMode
    {
        /// <summary>从完整时间轴的第 0 帧进入，保留 Startup 表现。</summary>
        TimelineStart = 0,
        /// <summary>从最早 Active Phase 的起始帧进入，用于上一段普通攻击的实时交接。</summary>
        FirstActivePhase = 1
    }

    /// <summary>
    /// 标识一次技能执行结束的原因；外部状态机据此处理后续业务，动画处理器负责归还技能动画层。
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
    /// 保存一次 SkillExecution 在指定逻辑帧切换后的动作阶段与 Runtime 策略快照。
    /// </summary>
    public readonly struct SkillActionPhaseChangedEventArgs
    {
        public ulong ExecutionId { get; }
        public SkillConfig Config { get; }
        public int Frame { get; }
        public ActionPhaseType Phase { get; }
        public bool HasPhasePolicy { get; }
        public bool IsCancelable { get; }
        public IReadOnlyList<GameplayTag> RuntimeTags { get; }
        public IReadOnlyList<GameplayTag> BlockAbilityTags { get; }

        /// <summary>创建动作阶段变化事件快照。</summary>
        /// <param name="executionId">Module 内单调递增的执行标识。</param>
        /// <param name="config">本次执行使用的 SkillConfig。</param>
        /// <param name="frame">阶段状态生效的整数逻辑帧。</param>
        /// <param name="phase">当前动作阶段。</param>
        /// <param name="hasPhasePolicy">当前是否存在覆盖策略的 Clip。</param>
        /// <param name="isCancelable">当前 Phase 是否接受普通取消。</param>
        /// <param name="runtimeTags">当前 Phase 贡献的 RuntimeTags 快照。</param>
        /// <param name="blockAbilityTags">当前 Phase 使用的完整 BlockAbilityTags 快照。</param>
        public SkillActionPhaseChangedEventArgs(
            ulong executionId,
            SkillConfig config,
            int frame,
            ActionPhaseType phase,
            bool hasPhasePolicy,
            bool isCancelable,
            IReadOnlyList<GameplayTag> runtimeTags,
            IReadOnlyList<GameplayTag> blockAbilityTags)
        {
            ExecutionId = executionId;
            Config = config;
            Frame = frame;
            Phase = phase;
            HasPhasePolicy = hasPhasePolicy;
            IsCancelable = isCancelable;
            RuntimeTags = runtimeTags ?? Array.Empty<GameplayTag>();
            BlockAbilityTags = blockAbilityTags ?? Array.Empty<GameplayTag>();
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
        public SkillStartMode StartMode { get; }

        /// <summary>
        /// 创建技能播放请求。
        /// </summary>
        /// <param name="config">本次执行使用的技能配置。</param>
        /// <param name="weaponRoot">当前武器刀根；非 WeaponTrace 技能可为空。</param>
        /// <param name="weaponTip">当前武器刀尖；非 WeaponTrace 技能可为空。</param>
        /// <param name="startMode">本次执行从第 0 帧还是最早 Active Phase 进入。</param>
        public SkillPlayRequest(SkillConfig config, Transform weaponRoot = null, Transform weaponTip = null,
            SkillStartMode startMode = SkillStartMode.TimelineStart)
        {
            Config = config;
            WeaponRoot = weaponRoot;
            WeaponTip = weaponTip;
            StartMode = startMode;
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
