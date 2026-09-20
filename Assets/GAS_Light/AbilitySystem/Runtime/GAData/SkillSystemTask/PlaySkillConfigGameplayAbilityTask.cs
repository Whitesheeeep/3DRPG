using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.TAG;
using RPG.Character;

namespace RPG.SkillSystem
{
    /// <summary>桥接单次异步 GA 生命周期与角色共享 SkillRuntimeModule。</summary>
    public sealed class PlaySkillConfigGameplayAbilityTask : GameplayAbilityTask
    {
        #region 字段

        private readonly SkillConfig skillConfig;
        private readonly SkillStartMode startMode;
        private bool suppressNextAnimatorMotion;
        private ISkillRuntimeHost host;
        private bool subscribed;
        private IMotionDriver motionDriver;
        private MotionControlHandle motionHandle;
        private IFullBodyActionArbiter fullBodyActionArbiter;
        private FullBodyActionHandle fullBodyActionHandle;

        #endregion

        #region 构造

        /// <summary>
        /// 创建采用默认完整时间轴入口的播放 Task，兼容旧的直接构造调用。
        /// </summary>
        /// <param name="runtime">拥有该 Task 的异步 Runtime。</param>
        /// <param name="config">启动时需要播放的 SkillConfig。</param>
        public PlaySkillConfigGameplayAbilityTask(
            AsynchronousGameplayAbilityRuntime runtime,
            SkillConfig config)
            : this(runtime, config, SkillStartMode.TimelineStart)
        {
        }

        /// <summary>创建尚未占用角色 SkillRuntimeHost 的播放 Task。</summary>
        /// <param name="runtime">拥有该 Task 的异步 Runtime。</param>
        /// <param name="config">启动时需要播放的 SkillConfig。</param>
        /// <param name="requestedStartMode">由 SetByCaller 快照解析出的时间轴入口模式。</param>
        public PlaySkillConfigGameplayAbilityTask(
            AsynchronousGameplayAbilityRuntime runtime,
            SkillConfig config,
            SkillStartMode requestedStartMode)
            : base(runtime)
        {
            skillConfig = config;
            startMode = requestedStartMode;
        }

        #endregion

        #region 生命周期

        /// <summary>获取 Source Host、订阅本次执行事件并尝试播放时间轴。</summary>
        protected override void OnStart()
        {
            if (Runtime.SourceOwner is not { } skillOwner)
            {
                Debug.LogError(
                    $"Ability '{Runtime.Data.name}' 的 Source '{Runtime.SourceASC.name}' 宿主不支持 Skill Gameplay Ability。",
                    Runtime.SourceASC);
                Complete();
                return;
            }

            // ASC 已经缓存宿主，Task 只取得接口能力，不再依赖角色具体组件类型。
            host = skillOwner.SkillRuntimeHost;
            if (host == null)
            {
                Debug.LogError(
                    $"Ability '{Runtime.Data.name}' 的 Source '{Runtime.SourceASC.name}' 宿主缺少 SkillRuntimeHost。",
                    Runtime.SourceASC);
                Complete();
                return;
            }

            // 技能是否根运动只决定是否放行 Animator/Vertical 提交；技能一旦占据水平和旋转通道，
            // Locomotion 即使继续运行也不会在技能站桩期间偷偷移动角色。
            motionDriver = skillOwner.MotionDriver ??
                throw new InvalidOperationException($"Ability '{Runtime.Data.name}' 的角色未绑定 MotionDriver。");
            MotionChannels motionChannels = MotionChannels.Horizontal | MotionChannels.Rotation;
            if (skillConfig.IsRootMotion)
                motionChannels |= MotionChannels.Vertical;
            motionHandle = motionDriver.RequestControl(new MotionControlRequest(
                skillOwner,
                MotionPriority.Skill,
                motionChannels));

            Subscribe();
            try
            {
                Debug.Log(
                    $"[PlaySkillConfigGameplayAbilityTask] Ability '{Runtime.Data.name}' " +
                    $"ActivationId={Runtime.ActivationId} 启动 SkillConfig '{skillConfig.name}'，" +
                    $"StartMode={startMode}。",
                    Runtime.SourceASC);
                SkillStartResult result = host.TryPlay(skillConfig, startMode);
                if (result.Succeeded)
                {
                    suppressNextAnimatorMotion = startMode == SkillStartMode.FirstActivePhase &&
                                                  host.CurrentFrame > 0 && skillConfig.IsRootMotion;
                    // SkillRuntimeHost 已经成功取得表现层后再发布占据，避免播放失败留下虚假的 FullBody 状态。
                    fullBodyActionArbiter = skillOwner.FullBodyActionArbiter ??
                        throw new InvalidOperationException(
                            $"Ability '{Runtime.Data.name}' 的角色未绑定 FullBody Action Arbiter。");
                    fullBodyActionHandle = fullBodyActionArbiter.RegisterFullBodyAction(
                        Runtime);
                    // TryPlay 可能没有 ActionPhase Clip；注册后再次读取 Host 快照，确保初始策略一致。
                    ApplyPhasePolicy(
                        host.CurrentPhase,
                        host.HasCurrentPhasePolicy,
                        host.CurrentPhaseIsCancelable,
                        host.CurrentPhaseRuntimeTags,
                        host.CurrentPhaseBlockAbilityTags,
                        host.CurrentFrame);
                    return;
                }

                Debug.Log(
                    $"Ability '{Runtime.Data.name}' 无法播放 SkillConfig：{result.Message}",
                    Runtime.SourceASC);
                Unsubscribe();
                Complete();
            }
            catch
            {
                // 时间轴启动与 FullBody 注册属于同一事务；异常不能遗留事件、运动权或 Blackboard 占据。
                Unsubscribe();
                ReleaseFullBodyAction();
                ReleaseMotion();
                host.Cancel();
                Complete();
                throw;
            }
        }

        /// <summary>正常提前结束时停止属于当前 Task 的共享时间轴。</summary>
        protected override void OnStop()
        {
            Unsubscribe();
            ReleaseFullBodyAction();
            ReleaseMotion();
            host?.Stop();
        }

        /// <summary>被打断时立即取消属于当前 Task 的共享时间轴。</summary>
        protected override void OnCancel()
        {
            Unsubscribe();
            ReleaseFullBodyAction();
            ReleaseMotion();
            host?.Cancel();
        }

        /// <summary>Task 正常完成时解除 Module 事件，防止后续播放回调旧 Task。</summary>
        protected override void OnComplete()
        {
            Unsubscribe();
            ReleaseFullBodyAction();
            ReleaseMotion();
        }

        /// <summary>使用 ASC 普通阶段推进 Skill 时间轴整数帧。</summary>
        /// <param name="deltaTime">本帧普通更新时间。</param>
        protected override void OnTick(float deltaTime) => host.Tick(deltaTime);

        /// <summary>将根运动技能本次 Animator 增量提交给 MotionDriver。</summary>
        /// <param name="deltaPosition">Animator 根位移增量。</param>
        /// <param name="deltaRotation">Animator 根旋转增量。</param>
        protected override void OnUpdateAnimationMove(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            // 非根运动技能仍可占据水平与旋转通道，但不能消费 Animator 增量；否则站桩技能会意外随动画移动。
            if (!skillConfig.IsRootMotion || motionHandle == null) return;
            if (suppressNextAnimatorMotion)
            {
                // 非零帧 Seek 后 Animator 的第一份增量可能是从素材原点跳到入口的差值，必须丢弃一次。
                suppressNextAnimatorMotion = false;
                Debug.Log(
                    $"[PlaySkillConfigGameplayAbilityTask] Ability '{Runtime.Data.name}' " +
                    $"忽略 FirstActivePhase Seek 后的首个根运动增量。",
                    Runtime.SourceASC);
                return;
            }
            motionDriver.SubmitAnimatorMotion(motionHandle,
                new AnimatorMotionSubmission(deltaPosition, deltaRotation));
        }

        /// <summary>在动画姿态稳定后处理挂点、攻击检测和自然结束。</summary>
        /// <param name="deltaTime">本帧延迟更新的时间，仅用于保持阶段接口一致。</param>
        protected override void OnLateTick(float deltaTime) => host.LateTick();

        #endregion

        #region Module 事件

        /// <summary>处理共享 Module 的本次自然完成并结束 Task。</summary>
        /// <param name="args">已经完成清理的执行快照。</param>
        private void OnSkillCompleted(SkillCompletedEventArgs args)
        {
            if (args.Reason != SkillCompletionReason.Natural || args.Config != skillConfig) return;
            Complete();
        }

        /// <summary>把 SkillRuntime 的 Phase RuntimeTags、阻断快照和取消权限同步到当前 GA Runtime。</summary>
        /// <param name="args">已经去重发布的阶段策略快照。</param>
        private void OnActionPhaseChanged(SkillActionPhaseChangedEventArgs args)
        {
            if (args.Config != skillConfig)
                return;

            ApplyPhasePolicy(
                args.Phase,
                args.HasPhasePolicy,
                args.IsCancelable,
                args.RuntimeTags,
                args.BlockAbilityTags,
                args.Frame);
        }

        /// <summary>把时间轴策略转换为 Runtime 专属容器并应用一次差量更新。</summary>
        /// <param name="phase">当前动作阶段。</param>
        /// <param name="hasPhasePolicy">是否存在 Clip 策略。</param>
        /// <param name="isCancelable">当前阶段是否允许普通取消。</param>
        /// <param name="runtimeTags">当前阶段 RuntimeTags。</param>
        /// <param name="blockAbilityTags">当前阶段完整 BlockAbilityTags。</param>
        /// <param name="frame">策略生效帧。</param>
        private void ApplyPhasePolicy(
            ActionPhaseType phase,
            bool hasPhasePolicy,
            bool isCancelable,
            IReadOnlyList<GameplayTag> runtimeTags,
            IReadOnlyList<GameplayTag> blockAbilityTags,
            int frame)
        {
            var phaseRuntimeTags = new GameplayTagContainer();
            for (int i = 0; i < runtimeTags.Count; i++) phaseRuntimeTags.AddTag(runtimeTags[i]);
            var phaseBlockAbilityTags = new GameplayTagContainer();
            for (int i = 0; i < blockAbilityTags.Count; i++) phaseBlockAbilityTags.AddTag(blockAbilityTags[i]);

            Runtime.ApplyPhasePolicy(
                phaseRuntimeTags,
                phaseBlockAbilityTags,
                isCancelable,
                hasPhasePolicy);
            Debug.Log(
                $"[PlaySkillConfigGameplayAbilityTask] Ability '{Runtime.Data.name}' ActivationId={Runtime.ActivationId} 应用 Phase 策略，Phase={phase}，Frame={frame}，HasPolicy={hasPhasePolicy}，RuntimeTags={runtimeTags.Count}，BlockTags={blockAbilityTags.Count}，IsCancelable={isCancelable}。",
                Runtime.SourceASC);
        }

        /// <summary>把 SkillSystem 去重后的命中映射为当前 GA 的 Effects 与 Execute Cue。</summary>
        /// <param name="args">命中目标、位置和 SkillExecution 身份。</param>
        private void OnSkillHit(SkillHitEventArgs args)
        {
            if (args.Config != skillConfig || args.Target == null) return;
            GameplayAbilitySystemComponent target =
                args.Target.GetComponentInParent<GameplayAbilitySystemComponent>();
            if (target == null || ReferenceEquals(target, Runtime.SourceASC)) return;

            Runtime.Data.ApplyConfiguredEffects(
                Runtime.SourceASC,
                target,
                Runtime.Level,
                Runtime.SetByCaller);
            Runtime.Data.PublishConfiguredCues(
                GameplayCueEventType.Execute,
                Runtime.SourceASC,
                target,
                abilityRuntime: Runtime,
                position: args.Point,
                rotation: Quaternion.identity);
        }

        /// <summary>将当前 SkillConfig 的 Projectile 发射事件转换为带有 GA 快照的池化投射物生成。</summary>
        /// <param name="args">已经解析 Marker 与作者 Spawn 配置的时间轴事件。</param>
        private void OnProjectileSpawnRequested(SkillProjectileSpawnEventArgs args)
        {
            if (args.Config != skillConfig) return;

            ProjectileSpawnService.SpawnBatch(
                args.Origin,
                args.SpawnConfig,
                Runtime.SourceASC,
                Runtime.Level,
                Runtime.SetByCaller,
                Runtime.Data.Effects,
                Runtime.Data.CueTags,
                Runtime,
                Runtime.SourceASC);
        }

        /// <summary>订阅当前共享 Module 的完成、Phase、命中与投射物事件。</summary>
        private void Subscribe()
        {
            if (subscribed) return;
            host.Completed += OnSkillCompleted;
            host.ActionPhaseChanged += OnActionPhaseChanged;
            host.HitDetected += OnSkillHit;
            host.ProjectileSpawnRequested += OnProjectileSpawnRequested;
            subscribed = true;
        }

        /// <summary>幂等解除共享 Module 事件订阅。</summary>
        private void Unsubscribe()
        {
            if (!subscribed || host == null) return;
            host.Completed -= OnSkillCompleted;
            host.ActionPhaseChanged -= OnActionPhaseChanged;
            host.HitDetected -= OnSkillHit;
            host.ProjectileSpawnRequested -= OnProjectileSpawnRequested;
            subscribed = false;
        }

        /// <summary>对称释放技能占用的运动控制权。</summary>
        private void ReleaseMotion()
        {
            motionHandle?.Dispose();
            motionHandle = null;
            motionDriver = null;
        }

        /// <summary>幂等注销当前 Skill 的 FullBody 执行并归还共享 Blackboard 占据。</summary>
        private void ReleaseFullBodyAction()
        {
            fullBodyActionHandle?.Dispose();
            fullBodyActionHandle = null;
            fullBodyActionArbiter = null;
        }

        #endregion
    }
}
