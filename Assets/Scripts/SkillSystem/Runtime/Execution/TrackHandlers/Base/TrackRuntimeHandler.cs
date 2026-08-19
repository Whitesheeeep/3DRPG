using System.Collections.Generic;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 为一种具体轨道类型集中收集未静音轨道，并向运行时处理器提供稳定的执行期数据视图。
    /// </summary>
    /// <typeparam name="TTrack">当前处理器负责的具体轨道配置类型。</typeparam>
    internal abstract class TrackRuntimeHandler<TTrack> : ISkillTrackRuntimeHandler
        where TTrack : TrackConfigBase
    {
        #region 执行状态

        private readonly List<TTrack> tracks = new();

        /// <summary>
        /// 获取本次技能执行共享的角色、攻击与播放请求上下文。
        /// </summary>
        protected SkillRuntimeContext Context { get; private set; }

        /// <summary>
        /// 获取按 SkillConfig 物理顺序收集的全部同类型未静音轨道。
        /// </summary>
        protected IReadOnlyList<TTrack> Tracks => tracks;

        #endregion

        #region 初始化

        /// <summary>
        /// 绑定执行上下文，并在执行开始时一次性收集全部同类型未静音轨道。
        /// </summary>
        /// <param name="context">本次技能执行共享上下文。</param>
        /// <param name="config">本次执行期间视为不可变的技能配置。</param>
        public void Initialize(SkillRuntimeContext context, SkillConfig config)
        {
            Context = context;
            tracks.Clear();

            // 保留统一轨道列表中的物理顺序，使同类型轨道的优先级稳定可预测。
            IReadOnlyList<TrackConfigBase> configuredTracks = config.Tracks;
            for (int index = 0; index < configuredTracks.Count; index++)
            {
                if (configuredTracks[index] is TTrack { Muted: false } track)
                    tracks.Add(track);
            }

            OnInitialized();
        }

        /// <summary>
        /// 在同类型轨道收集完成后初始化具体处理器的执行期状态。
        /// </summary>
        protected virtual void OnInitialized()
        {
        }

        #endregion

        #region 帧处理契约

        /// <summary>
        /// 按同类型轨道的物理顺序处理一个已经到达的整数逻辑帧。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public abstract void ProcessFrame(int frame);

        /// <summary>
        /// 在 LateUpdate 中处理依赖 Animator、Marker 或武器最终姿态的逻辑帧。
        /// </summary>
        /// <param name="frame">此前已经完成普通阶段处理的整数帧。</param>
        public abstract void ProcessLateFrame(int frame);

        /// <summary>
        /// 结束当前类型全部轨道的执行状态并释放其持有的动态资源。
        /// </summary>
        /// <param name="reason">本次技能结束原因。</param>
        public abstract void Complete(SkillCompletionReason reason);

        #endregion
    }
}
