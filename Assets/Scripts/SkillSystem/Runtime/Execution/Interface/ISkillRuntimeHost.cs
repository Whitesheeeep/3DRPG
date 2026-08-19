using System;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 为 Gameplay Ability Task 暴露 SkillRuntimeHost 的最小播放契约。
    /// </summary>
    public interface ISkillRuntimeHost
    {
        /// <summary>获取共享技能时间轴当前是否正在执行。</summary>
        bool IsPlaying { get; }

        /// <summary>报告技能时间轴产生有效命中。</summary>
        event Action<SkillHitEventArgs> HitDetected;

        /// <summary>报告技能时间轴完成清理。</summary>
        event Action<SkillCompletedEventArgs> Completed;

        /// <summary>报告技能动作阶段或可打断状态变化。</summary>
        event Action<SkillActionPhaseChangedEventArgs> ActionPhaseChanged;

        /// <summary>报告技能时间轴到达投射物发射帧。</summary>
        event Action<SkillProjectileSpawnEventArgs> ProjectileSpawnRequested;

        /// <summary>尝试启动指定 SkillConfig。</summary>
        /// <param name="config">要播放的技能配置。</param>
        /// <returns>播放成功状态及失败原因。</returns>
        SkillStartResult TryPlay(SkillConfig config);

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
