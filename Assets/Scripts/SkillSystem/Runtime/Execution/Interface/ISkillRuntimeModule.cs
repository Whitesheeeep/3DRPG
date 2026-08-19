using System;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 定义单个角色技能执行通道的播放、状态查询和外部帧驱动契约。
    /// </summary>
    public interface ISkillRuntimeModule : IDisposable
    {
        #region 事件

        /// <summary>
        /// 在攻击检测完成目标过滤和单个 Clip 命中去重后触发。
        /// </summary>
        event Action<SkillHitEventArgs> HitDetected;

        /// <summary>
        /// 在当前执行完成清理且 Module 已允许启动下一技能后触发。
        /// </summary>
        event Action<SkillCompletedEventArgs> Completed;

        /// <summary>在整数逻辑帧切换动作阶段或可打断状态后触发。</summary>
        event Action<SkillActionPhaseChangedEventArgs> ActionPhaseChanged;

        #endregion

        #region 状态查询

        /// <summary>
        /// 获取当前是否存在活动技能执行。
        /// </summary>
        bool IsPlaying { get; }

        /// <summary>
        /// 获取当前执行最后处理的整数帧；空闲时为第 0 帧。
        /// </summary>
        int CurrentFrame { get; }

        /// <summary>
        /// 获取当前动作阶段；没有活动阶段时为 None。
        /// </summary>
        ActionPhaseType CurrentPhase { get; }

        /// <summary>
        /// 获取当前动作阶段是否允许外部系统发起打断。
        /// </summary>
        bool CanBeInterrupted { get; }

        /// <summary>
        /// 获取当前通道的全局播放倍率；该值会保留到后续技能执行。
        /// </summary>
        float PlaybackSpeed { get; }

        #endregion

        #region 配置与播放

        /// <summary>
        /// 初始化角色稳定依赖与后续技能使用的攻击筛选设置。
        /// </summary>
        /// <param name="actorContext">角色、动画、坐标根与 Marker Provider。</param>
        /// <param name="attackSettings">Physics 粗筛选和可选业务目标过滤设置。</param>
        void Initialize(SkillActorContext actorContext, SkillAttackSettings attackSettings);

        /// <summary>
        /// 替换后续技能执行使用的业务目标过滤器。
        /// </summary>
        /// <param name="filter">新的过滤器；为空表示不做额外业务筛选。</param>
        void SetAttackTargetFilter(ISkillAttackTargetFilter filter);

        /// <summary>
        /// 替换后续技能执行使用的 Physics LayerMask。
        /// </summary>
        /// <param name="layerMask">新的目标层掩码。</param>
        void SetAttackLayerMask(LayerMask layerMask);

        /// <summary>
        /// 设置技能逻辑、动画与粒子系统使用的全局播放倍率。
        /// </summary>
        /// <param name="playbackSpeed">范围为 0 到 2；0 表示冻结当前技能时间轴。</param>
        /// <exception cref="ArgumentOutOfRangeException">倍率不是有限数值或超出有效范围。</exception>
        void SetPlaybackSpeed(float playbackSpeed);

        /// <summary>
        /// 尝试启动技能；已有活动执行时不会自动抢占。
        /// </summary>
        /// <param name="request">技能配置与本次执行使用的动态武器节点。</param>
        /// <returns>成功状态或不会修改当前状态的失败原因。</returns>
        SkillStartResult TryPlay(in SkillPlayRequest request);

        /// <summary>
        /// 使用调用方提供的时间增量推进普通逻辑帧。
        /// </summary>
        /// <param name="deltaTime">本次推进使用的缩放时间秒数。</param>
        void Tick(float deltaTime);

        /// <summary>
        /// 在 Animator 和绑定节点姿态稳定后处理等待中的 Late 帧，并完成自然结束判断。
        /// </summary>
        void LateTick();

        /// <summary>
        /// 正常停止当前技能，允许动态资源按 Stop 语义保留自然尾迹。
        /// </summary>
        void Stop();

        /// <summary>
        /// 立即取消当前技能并回收本次执行仍持有的动态资源。
        /// </summary>
        void Cancel();

        #endregion
    }
}
