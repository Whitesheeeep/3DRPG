using System;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 作为 Unity 自动帧驱动适配器，将播放 API、状态和事件转发给纯 C# SkillRuntimeModule。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkillRunner : MonoBehaviour
    {
        #region 模块与事件

        private readonly SkillRuntimeModule module = new();

        /// <summary>
        /// 在 Module 完成目标过滤和 Detection ID 去重后转发命中事件。
        /// </summary>
        public event Action<SkillHitEventArgs> HitDetected
        {
            add => module.HitDetected += value;
            remove => module.HitDetected -= value;
        }

        /// <summary>
        /// 在 Module 清理当前执行后转发技能完成事件。
        /// </summary>
        public event Action<SkillCompletedEventArgs> Completed
        {
            add => module.Completed += value;
            remove => module.Completed -= value;
        }

        /// <summary>
        /// 在 SkillRuntime 的动作阶段或转换窗口变化后转发事件，供 OdinTester 和未来 Action Execution 观察。
        /// </summary>
        public event Action<SkillActionPhaseChangedEventArgs> ActionPhaseChanged
        {
            add => module.ActionPhaseChanged += value;
            remove => module.ActionPhaseChanged -= value;
        }

        #endregion

        #region 状态查询

        public bool IsPlaying => module.IsPlaying;
        public int CurrentFrame => module.CurrentFrame;
        public ActionPhaseType CurrentPhase => module.CurrentPhase;
        /// <summary>获取当前阶段开放的外部转换窗口。</summary>
        public SkillTransitionMask AllowedTransitions => module.AllowedTransitions;
        /// <summary>获取当前技能通道的全局播放倍率。</summary>
        public float PlaybackSpeed => module.PlaybackSpeed;
        /// <summary>获取是否绘制运行时攻击检测查询形状。</summary>
        public bool DrawAttackDetectionDebug => module.DrawAttackDetectionDebug;
        /// <summary>获取攻击检测调试线框保留秒数；零表示当前帧。</summary>
        public float AttackDetectionDebugDuration => module.AttackDetectionDebugDuration;

        #endregion

        #region 生命周期

        /// <summary>
        /// 推进当前技能的普通帧处理；动画姿态相关检测延后到 LateUpdate。
        /// </summary>
        private void Update()
        {
            module.Tick(Time.deltaTime);
        }

        /// <summary>
        /// 在 Animator 本帧姿态稳定后执行攻击检测，并在最后有效帧检测完成后自然结束。
        /// </summary>
        private void LateUpdate()
        {
            module.LateTick();
        }

        /// <summary>
        /// 组件销毁时立即取消活动技能，确保池对象、音频和事件生命周期完整收口。
        /// </summary>
        private void OnDestroy()
        {
            module.Dispose();
        }

        #endregion

        #region 公开操作

        /// <summary>
        /// 初始化 Runner 的稳定角色依赖与攻击筛选设置。
        /// </summary>
        /// <param name="actor">角色、动画层、坐标根和 Marker Provider。</param>
        /// <param name="attack">LayerMask、Trigger 和可选业务过滤器。</param>
        public void Initialize(SkillActorContext actor, SkillAttackSettings attack)
        {
            module.Initialize(actor, attack);
        }

        /// <summary>
        /// 替换后续技能执行使用的业务目标过滤器；当前执行使用开始时冻结的设置快照。
        /// </summary>
        /// <param name="filter">新的过滤器；为空表示不做额外业务筛选。</param>
        public void SetAttackTargetFilter(ISkillAttackTargetFilter filter)
        {
            module.SetAttackTargetFilter(filter);
        }

        /// <summary>
        /// 替换后续技能执行使用的 Physics LayerMask。
        /// </summary>
        /// <param name="layerMask">新的目标层掩码。</param>
        public void SetAttackLayerMask(LayerMask layerMask)
        {
            module.SetAttackLayerMask(layerMask);
        }

        /// <summary>
        /// 设置当前通道及后续技能使用的全局播放倍率。
        /// </summary>
        /// <param name="playbackSpeed">范围为 0 到 2；0 表示冻结时间轴、动画和粒子。</param>
        public void SetPlaybackSpeed(float playbackSpeed)
        {
            module.SetPlaybackSpeed(playbackSpeed);
        }

        /// <summary>
        /// 设置当前及后续技能是否绘制攻击检测查询形状。
        /// </summary>
        /// <param name="enabled">是否启用调试绘制。</param>
        /// <param name="duration">线框保留秒数；必须是有限非负数。</param>
        public void SetAttackDetectionDebug(bool enabled, float duration)
        {
            module.SetAttackDetectionDebug(enabled, duration);
        }

        /// <summary>
        /// 尝试启动技能；Runner 忙碌时不会替换或中断现有执行。
        /// </summary>
        /// <param name="request">技能配置与当前武器节点。</param>
        /// <returns>成功状态或明确失败原因。</returns>
        public SkillStartResult TryPlay(in SkillPlayRequest request)
        {
            return module.TryPlay(request);
        }

        /// <summary>
        /// 主动正常停止技能；VFX 尾迹与已开始音频允许自然结束。
        /// </summary>
        public void Stop()
        {
            module.Stop();
        }

        /// <summary>
        /// 立即取消技能并回收本次执行仍持有的动态资源。
        /// </summary>
        public void Cancel()
        {
            module.Cancel();
        }

        #endregion
    }
}
