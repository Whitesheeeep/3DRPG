using System;
using RPG.Character.Animation;
using RPG.Markers;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>为角色长期持有唯一 SkillRuntimeModule，并向 GAS Task 提供受控执行入口。</summary>
    [DisallowMultipleComponent]
    public sealed class SkillRuntimeHost : MonoBehaviour, ISkillRuntimeHost
    {
        #region 配置与状态

        [SerializeField, Tooltip("技能检测与无挂点表现使用的空间基准；为空时使用当前 Transform。")]
        private Transform origin;
        [SerializeField, Tooltip("角色技能动画播放器；无动画角色可以为空。")]
        private AnimationController animationController;
        [SerializeField, Tooltip("角色语义挂点提供器；不使用挂点时可以为空。")]
        private MarkerProvider markerProvider;
        [SerializeField, Tooltip("技能动画使用的固定语义层。")]
        private AnimationLayerType animationLayer = AnimationLayerType.Action;
        [SerializeField, Tooltip("攻击检测使用的 Physics LayerMask。")]
        private LayerMask attackLayerMask = ~0;
        [SerializeField, Tooltip("攻击检测对 Trigger 的查询规则。")]
        private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal;

        private readonly SkillRuntimeModule module = new();
        private ISkillAttackTargetFilter targetFilter;
        private Transform weaponRoot;
        private Transform weaponTip;
        private bool initialized;

        /// <summary>获取共享 Module 当前是否正在执行 SkillConfig。</summary>
        public bool IsPlaying => module.IsPlaying;
        /// <summary>获取当前执行最后处理的整数逻辑帧；空闲时为零。</summary>
        public int CurrentFrame => module.CurrentFrame;
        /// <summary>获取当前动作阶段；空闲时为 None。</summary>
        public ActionPhaseType CurrentPhase => module.CurrentPhase;
        /// <summary>获取当前阶段是否允许外部技能打断。</summary>
        public bool CanBeInterrupted => module.CanBeInterrupted;
        /// <summary>获取共享技能通道的全局播放倍率。</summary>
        public float PlaybackSpeed => module.PlaybackSpeed;

        #endregion

        #region 事件

        /// <summary>在共享 Module 产生有效命中后发送。</summary>
        public event Action<SkillHitEventArgs> HitDetected
        {
            add => module.HitDetected += value;
            remove => module.HitDetected -= value;
        }

        /// <summary>在共享 Module 完成清理后发送。</summary>
        public event Action<SkillCompletedEventArgs> Completed
        {
            add => module.Completed += value;
            remove => module.Completed -= value;
        }

        /// <summary>在共享 Module 的动作阶段或可打断状态变化后转发。</summary>
        public event Action<SkillActionPhaseChangedEventArgs> ActionPhaseChanged
        {
            add => module.ActionPhaseChanged += value;
            remove => module.ActionPhaseChanged -= value;
        }

        /// <summary>在当前 SkillConfig 到达 Projectile 发射帧时转发。</summary>
        public event Action<SkillProjectileSpawnEventArgs> ProjectileSpawnRequested
        {
            add => module.ProjectileSpawnRequested += value;
            remove => module.ProjectileSpawnRequested -= value;
        }

        #endregion

        #region Unity 生命周期

        /// <summary>使用角色稳定依赖初始化共享 Module，但不主动推进任何 Unity 阶段。</summary>
        private void Awake()
        {
            Transform resolvedOrigin = origin != null ? origin : transform;
            var actor = new SkillActorContext(
                gameObject,
                resolvedOrigin,
                animationController,
                animationLayer,
                markerProvider);
            var attack = new SkillAttackSettings(
                attackLayerMask,
                triggerInteraction,
                targetFilter);
            module.Initialize(actor, attack);
            initialized = true;
        }

        /// <summary>销毁角色执行环境时取消活动时间轴并释放 Module。</summary>
        private void OnDestroy()
        {
            initialized = false;
            module.Dispose();
        }

        #endregion

        #region 配置与播放

        /// <summary>替换后续 SkillExecution 使用的业务目标过滤器。</summary>
        /// <param name="filter">新的目标解析和合法性过滤器。</param>
        public void SetAttackTargetFilter(ISkillAttackTargetFilter filter)
        {
            targetFilter = filter;
            if (initialized) module.SetAttackTargetFilter(filter);
        }

        /// <summary>设置后续 WeaponTrace 播放请求使用的当前武器节点。</summary>
        /// <param name="root">当前武器刀根。</param>
        /// <param name="tip">当前武器刀尖。</param>
        public void SetWeaponNodes(Transform root, Transform tip)
        {
            weaponRoot = root;
            weaponTip = tip;
        }

        /// <summary>
        /// 设置共享通道及后续技能使用的全局播放倍率。
        /// </summary>
        /// <param name="playbackSpeed">范围为 0 到 2；0 表示冻结时间轴、动画和粒子。</param>
        public void SetPlaybackSpeed(float playbackSpeed)
        {
            module.SetPlaybackSpeed(playbackSpeed);
        }

        /// <summary>使用当前角色上下文和武器节点尝试播放指定 SkillConfig。</summary>
        /// <param name="config">本次播放的技能时间轴配置。</param>
        /// <returns>Module 返回的成功状态或失败原因。</returns>
        public SkillStartResult TryPlay(SkillConfig config)
        {
            if (!initialized) return SkillStartResult.Failure("SkillRuntimeHost 尚未完成 Awake 初始化。");
            return module.TryPlay(new SkillPlayRequest(config, weaponRoot, weaponTip));
        }

        /// <summary>推进共享 Module 的普通时间轴阶段。</summary>
        /// <param name="deltaTime">本次普通更新的秒数。</param>
        public void Tick(float deltaTime) => module.Tick(deltaTime);

        /// <summary>推进共享 Module 的动画姿态稳定阶段。</summary>
        public void LateTick() => module.LateTick();

        /// <summary>按正常停止语义结束当前时间轴。</summary>
        public void Stop() => module.Stop();

        /// <summary>按立即取消语义结束当前时间轴。</summary>
        public void Cancel() => module.Cancel();

        #endregion
    }
}
