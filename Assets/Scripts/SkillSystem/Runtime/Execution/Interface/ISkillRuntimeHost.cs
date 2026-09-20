using System;
using System.Collections.Generic;
using WS_Modules.GAS.TAG;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 为 Gameplay Ability Task 暴露 SkillRuntimeHost 的最小播放契约。
    /// </summary>
    public interface ISkillRuntimeHost
    {
        /// <summary>获取共享技能时间轴当前是否正在执行。</summary>
        bool IsPlaying { get; }

        /// <summary>获取当前执行最后处理的整数逻辑帧；空闲时返回零。</summary>
        int CurrentFrame { get; }

        /// <summary>获取当前动作阶段；空闲时返回 None。</summary>
        ActionPhaseType CurrentPhase { get; }

        /// <summary>获取当前是否存在覆盖策略的动作阶段 Clip。</summary>
        bool HasCurrentPhasePolicy { get; }

        /// <summary>获取当前动作阶段是否接受普通取消。</summary>
        bool CurrentPhaseIsCancelable { get; }

        /// <summary>获取当前动作阶段的 RuntimeTags 快照。</summary>
        IReadOnlyList<GameplayTag> CurrentPhaseRuntimeTags { get; }

        /// <summary>获取当前动作阶段的完整 BlockAbilityTags 快照。</summary>
        IReadOnlyList<GameplayTag> CurrentPhaseBlockAbilityTags { get; }

        /// <summary>报告技能时间轴产生有效命中。</summary>
        event Action<SkillHitEventArgs> HitDetected;

        /// <summary>报告技能时间轴完成清理。</summary>
        event Action<SkillCompletedEventArgs> Completed;

        /// <summary>报告技能动作阶段或外部转换窗口变化。</summary>
        event Action<SkillActionPhaseChangedEventArgs> ActionPhaseChanged;

        /// <summary>报告技能时间轴到达投射物发射帧。</summary>
        event Action<SkillProjectileSpawnEventArgs> ProjectileSpawnRequested;

        /// <summary>尝试启动指定 SkillConfig。</summary>
        /// <param name="config">要播放的技能配置。</param>
        /// <param name="startMode">从完整时间轴还是最早 Active Phase 进入。</param>
        /// <returns>播放成功状态及失败原因。</returns>
        SkillStartResult TryPlay(SkillConfig config,
            SkillStartMode startMode = SkillStartMode.TimelineStart);

        /// <summary>推进技能时间轴的普通更新阶段。</summary>
        /// <param name="deltaTime">本次推进的秒数。</param>
        void Tick(float deltaTime);

        /// <summary>推进技能时间轴的延迟更新阶段。</summary>
        void LateTick();

        /// <summary>按正常结束语义停止技能时间轴。</summary>
        void Stop();

        /// <summary>按打断语义取消技能时间轴。</summary>
        void Cancel();
    }
}
