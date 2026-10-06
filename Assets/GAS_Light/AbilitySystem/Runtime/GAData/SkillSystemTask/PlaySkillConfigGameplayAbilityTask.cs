using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.TAG;
using RPG.Character;
using RPG.Character.Animation;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.SkillSystem
{
    /// <summary>桥接单次异步 GA 生命周期与角色共享 SkillRuntimeModule。</summary>
    public sealed class PlaySkillConfigGameplayAbilityTask : GameplayAbilityTask
    {
        #region 字段

        private readonly SkillConfig skillConfig;
        private readonly SkillStartMode startMode;
        private IReadOnlyList<GameplayEffectSpec> effectSpecs;
        private bool suppressNextAnimatorMotion;
        // 依赖字段：Owner 提供可选武器姿态能力，Skill 与动作仲裁服务由激活流程提供。
        private ISkillRuntimeHost host;
        private IWeaponSwitch weaponSwitch;
        private IMotionDriver motionDriver;
        private IFullBodyActionArbiter fullBodyActionArbiter;

        private bool ownsWeaponSwitch;
        private bool subscribed;
        private MotionControlHandle motionHandle;
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
            // Runtime 只负责创建封存的 GE Specs，SkillRuntimeHost 才是实际应用者；因此不在 Task 内部直接 Apply。
            if (!GARuntime.Data.TryCreateConfiguredEffectSpecs(
                    GARuntime.SourceASC,
                    GARuntime.Level,
                    GARuntime.SetByCaller,
                    out effectSpecs))
            {
                Debug.LogError(
                    $"Ability '{GARuntime.Data.name}' ActivationId={GARuntime.ActivationId} 无法创建封存的 GE Specs。",
                    GARuntime.SourceASC);
                Complete();
                return;
            }

            if (GARuntime.SourceOwner is not { } skillOwner)
            {
                Debug.LogError(
                    $"Ability '{GARuntime.Data.name}' 的 Source '{GARuntime.SourceASC.name}' 宿主不支持 Skill Gameplay Ability。",
                    GARuntime.SourceASC);
                Complete();
                return;
            }

            // 武器姿态是 Owner 组合的可选能力；空引用表示此宿主无需切换武器姿态。
            weaponSwitch = skillOwner.WeaponSwitch;

            // ASC 已经缓存宿主，Task 只取得接口能力，不再依赖角色具体组件类型。
            host = skillOwner.SkillRuntimeHost;
            if (host == null)
            {
                Debug.LogError(
                    $"Ability '{GARuntime.Data.name}' 的 Source '{GARuntime.SourceASC.name}' 宿主缺少 SkillRuntimeHost。",
                    GARuntime.SourceASC);
                Complete();
                return;
            }

            // 技能是否根运动只决定是否放行 Animator/Vertical 提交；技能一旦占据水平和旋转通道，
            // Locomotion 即使继续运行也不会在技能站桩期间偷偷移动角色。
            motionDriver = skillOwner.MotionDriver ??
                throw new InvalidOperationException($"Ability '{GARuntime.Data.name}' 的角色未绑定 MotionDriver。");
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
                    $"[PlaySkillConfigGameplayAbilityTask] Ability '{GARuntime.Data.name}' " +
                    $"ActivationId={GARuntime.ActivationId} 启动 SkillConfig '{skillConfig.name}'，" +
                    $"StartMode={startMode}。",
                    GARuntime.SourceASC);
                SkillStartResult result = host.TryPlay(skillConfig, startMode);
                if (result.Succeeded)
                {
                    // 只有时间轴实际占用 Host 且当前 Task 仍运行时才接管姿态，避免失败启动改变已有表现。
                    if (State == GameplayAbilityTaskState.Running && host.IsPlaying && weaponSwitch != null)
                    {
                        weaponSwitch.HoldWeapon();
                        ownsWeaponSwitch = true;
                    }

                    suppressNextAnimatorMotion = startMode == SkillStartMode.FirstActivePhase &&
                                                  host.CurrentFrame > 0 && skillConfig.IsRootMotion;
                    // SkillRuntimeHost 已经成功取得表现层后再发布占据，避免播放失败留下虚假的 FullBody 状态。
                    fullBodyActionArbiter = skillOwner.FullBodyActionArbiter ??
                        throw new InvalidOperationException(
                            $"Ability '{GARuntime.Data.name}' 的角色未绑定 FullBody Action Arbiter。");
                    fullBodyActionHandle = fullBodyActionArbiter.RegisterFullBodyAction(
                        GARuntime);
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
                    $"Ability '{GARuntime.Data.name}' 无法播放 SkillConfig：{result.Message}",
                    GARuntime.SourceASC);
                Unsubscribe();
                Complete();
            }
            catch
            {
                // 时间轴启动与 FullBody 注册属于同一事务；异常不能遗留事件、运动权或 Blackboard 占据。
                Unsubscribe();
                ReleaseFullBodyAction();
                ReleaseMotion();
                ReleaseWeaponSwitch();
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
            ReleaseWeaponSwitch();
            host?.Stop();
        }

        /// <summary>被打断时立即取消属于当前 Task 的共享时间轴。</summary>
        protected override void OnCancel()
        {
            Unsubscribe();
            ReleaseFullBodyAction();
            ReleaseMotion();
            ReleaseWeaponSwitch();
            host?.Cancel();
        }

        /// <summary>Task 正常完成时解除 Module 事件，防止后续播放回调旧 Task。</summary>
        protected override void OnComplete()
        {
            ReleaseWeaponSwitch();
            Unsubscribe();
            ReleaseFullBodyAction();
            ReleaseMotion();
        }

        /// <summary>在 Task 发出完成通知或停止 Host 前释放本 Task 接管的武器姿态。</summary>
        private void ReleaseWeaponSwitch()
        {
            if (!ownsWeaponSwitch)
                return;

            // 先清除所有权标记，防止重入清理对 Rig 重复下达收刀命令。
            ownsWeaponSwitch = false;
            weaponSwitch.CarryWeaponOnBack();
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
                    $"[PlaySkillConfigGameplayAbilityTask] Ability '{GARuntime.Data.name}' " +
                    $"忽略 FirstActivePhase Seek 后的首个根运动增量。",
                    GARuntime.SourceASC);
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

            GARuntime.ApplyPhasePolicy(
                phaseRuntimeTags,
                phaseBlockAbilityTags,
                isCancelable,
                hasPhasePolicy);
            Debug.Log(
                $"[PlaySkillConfigGameplayAbilityTask] Ability '{GARuntime.Data.name}' ActivationId={GARuntime.ActivationId} 应用 Phase 策略，Phase={phase}，Frame={frame}，HasPolicy={hasPhasePolicy}，RuntimeTags={runtimeTags.Count}，BlockTags={blockAbilityTags.Count}，IsCancelable={isCancelable}。",
                GARuntime.SourceASC);
        }

        /// <summary>把 SkillSystem 去重后的命中映射为当前 GA 的 Effects 与 Execute Cue。</summary>
        /// <param name="args">命中目标、位置和 SkillExecution 身份。</param>
        private void OnSkillHit(SkillHitEventArgs args)
        {
            if (args.Config != skillConfig || args.Target == null) return;
            GameplayAbilitySystemComponent target =
                args.Target.GetComponentInParent<GameplayAbilitySystemComponent>();
            if (target == null || ReferenceEquals(target, GARuntime.SourceASC)) return;

            GameplayEffectApplicationResult lastApplicationResult = null;
            for (int i = 0; i < effectSpecs.Count; i++)
            {
                if (!target.TryApplyEffect(
                        effectSpecs[i],
                        out GameplayEffectApplicationResult applicationResult))
                    continue;
                lastApplicationResult = applicationResult;

                if (applicationResult.ActiveEffect != null)
                    GARuntime.RetainOwnedEffect(applicationResult.ActiveEffect);
            }

            // 命中 Cue 表示一次有效攻击检测；GE 被免疫或拒绝时也必须发布。GA 与 Clip 标签合并后按 Tag 去重。
            var publishedCueTagSet = new HashSet<GameplayTag>();
            PublishHitCueTags(GARuntime.Data.CueTags, args, target, lastApplicationResult, publishedCueTagSet);
            PublishHitCueTags(args.Clip.CueTags, args, target, lastApplicationResult, publishedCueTagSet);
        }

        /// <summary>发布单个配置来源中尚未发布的命中 Execute CueTag。</summary>
        /// <param name="cueTags">GA 或攻击检测 Clip 配置的标签列表。</param>
        /// <param name="args">当前命中点与目标信息。</param>
        /// <param name="target">接收 Cue 的目标 ASC。</param>
        /// <param name="applicationResult">本次命中最后一次成功的 GE 应用结果。</param>
        /// <param name="publishedCueTagSet">当前命中已发布的精确 Tag 集合。</param>
        private void PublishHitCueTags(IReadOnlyList<GameplayTag> cueTags, SkillHitEventArgs args,
            GameplayAbilitySystemComponent target, GameplayEffectApplicationResult applicationResult,
            HashSet<GameplayTag> publishedCueTagSet)
        {
            for (int index = 0; index < cueTags.Count; index++)
            {
                GameplayTag cueTag = cueTags[index];
                if (!publishedCueTagSet.Add(cueTag)) continue;

                target.PublishGameplayCue(new GameplayCueRequest(
                    cueTag,
                    GameplayCueEventType.Execute,
                    GARuntime.SourceASC,
                    target,
                    effectRuntime: null,
                    abilityRuntime: GARuntime,
                    position: args.Point,
                    rotation: Quaternion.identity,
                    attachTransform: null,
                    effectSpec: applicationResult?.Spec,
                    applicationResult: applicationResult));
            }
        }

        /// <summary>将当前 SkillConfig 的 Projectile 发射事件转换为带有 GA 快照的池化投射物生成。</summary>
        /// <param name="args">已经解析 Marker 与作者 Spawn 配置的时间轴事件。</param>
        private void OnProjectileSpawnRequested(SkillProjectileSpawnEventArgs args)
        {
            if (args.Config != skillConfig) return;

            ProjectileSpawnService.SpawnBatch(
                args.Origin,
                args.SpawnConfig,
                GARuntime.SourceASC,
                effectSpecs,
                GARuntime.Data.CueTags,
                GARuntime,
                GARuntime.SourceASC);
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
