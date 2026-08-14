using System;
using RPG.Character;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;

namespace RPG.SkillSystem
{
    /// <summary>桥接单次异步 GA 生命周期与角色共享 SkillRuntimeModule。</summary>
    public sealed class PlaySkillConfigGameplayAbilityTask : GameplayAbilityTask
    {
        #region 字段

        private readonly SkillConfig skillConfig;
        private SkillRuntimeHost host;
        private MotionDriver motionDriver;
        private bool subscribed;
        private GameplayTag appliedPhaseTag;
        private GameplayTag appliedInterruptTag;
        private bool hasAppliedPhaseTag;
        private bool hasAppliedInterruptTag;

        #endregion

        #region 构造

        /// <summary>创建尚未占用角色 SkillRuntimeHost 的播放 Task。</summary>
        /// <param name="runtime">拥有该 Task 的异步 Runtime。</param>
        /// <param name="config">启动时需要播放的 SkillConfig。</param>
        public PlaySkillConfigGameplayAbilityTask(
            AsynchronousGameplayAbilityRuntime runtime,
            SkillConfig config)
            : base(runtime)
        {
            skillConfig = config;
        }

        #endregion

        #region 生命周期

        /// <summary>获取 Source Host、订阅本次执行事件并尝试播放时间轴。</summary>
        protected override void OnStart()
        {
            host = Runtime.SourceASC.GetComponent<SkillRuntimeHost>();
            if (host == null)
            {
                Debug.LogError(
                    $"Ability '{Runtime.Data.name}' 的 Source '{Runtime.SourceASC.name}' 缺少 SkillRuntimeHost。",
                    Runtime.SourceASC);
                Complete();
                return;
            }

            if (skillConfig.IsRootMotion)
            {
                PlayerController playerController = Runtime.SourceASC.GetComponent<PlayerController>();
                if (playerController == null || playerController.MotionDriver == null)
                {
                    Debug.LogError(
                        $"Ability '{Runtime.Data.name}' 的 RootMotion SkillConfig 需要已初始化的 PlayerController。",
                        Runtime.SourceASC);
                    Complete();
                    return;
                }

                // Task 直接缓存角色移动服务；SkillRuntimeHost 只保留时间轴执行职责。
                motionDriver = playerController.MotionDriver;
            }

            Subscribe();
            SkillStartResult result = host.TryPlay(skillConfig);
            if (result.Succeeded) return;

            Debug.LogError(
                $"Ability '{Runtime.Data.name}' 无法播放 SkillConfig：{result.Message}",
                Runtime.SourceASC);
            Unsubscribe();
            Complete();
        }

        /// <summary>正常提前结束时停止属于当前 Task 的共享时间轴。</summary>
        protected override void OnStop()
        {
            Unsubscribe();
            ClearRuntimeTags();
            host?.Stop();
        }

        /// <summary>被打断时立即取消属于当前 Task 的共享时间轴。</summary>
        protected override void OnCancel()
        {
            Unsubscribe();
            ClearRuntimeTags();
            host?.Cancel();
        }

        /// <summary>Task 正常完成时解除 Module 事件，防止后续播放回调旧 Task。</summary>
        protected override void OnComplete()
        {
            Unsubscribe();
            ClearRuntimeTags();
        }

        /// <summary>使用 ASC 普通阶段推进 Skill 时间轴整数帧。</summary>
        /// <param name="deltaTime">本帧普通更新时间。</param>
        protected override void OnTick(float deltaTime) => host.Tick(deltaTime);

        /// <summary>在动画姿态稳定后处理挂点、攻击检测和自然结束。</summary>
        /// <param name="deltaTime">本帧延迟更新的时间，仅用于保持阶段接口一致。</param>
        protected override void OnLateTick(float deltaTime) => host.LateTick();

        /// <summary>仅为启用根运动的 SkillConfig 消费 Animator 当前帧位移与旋转。</summary>
        protected override void OnUpdateAnimationMove()
        {
            if (skillConfig.IsRootMotion) motionDriver.UpdateAnimationMove();
        }

        #endregion

        #region Module 事件

        /// <summary>处理共享 Module 的本次自然完成并结束 Task。</summary>
        /// <param name="args">已经完成清理的执行快照。</param>
        private void OnSkillCompleted(SkillCompletedEventArgs args)
        {
            if (args.Reason != SkillCompletionReason.Natural || args.Config != skillConfig) return;
            Complete();
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

        /// <summary>把当前 SkillExecution 的阶段快照替换为 Source ASC 上的两个引用计数 Tag。</summary>
        /// <param name="args">同帧生效的阶段与可打断状态。</param>
        private void OnActionPhaseChanged(SkillActionPhaseChangedEventArgs args)
        {
            if (args.Config != skillConfig) return;
            ApplyRuntimeTags(args.Phase, args.CanBeInterrupted);
        }

        /// <summary>订阅当前共享 Module 的完成与命中事件。</summary>
        private void Subscribe()
        {
            if (subscribed) return;
            host.Completed += OnSkillCompleted;
            host.HitDetected += OnSkillHit;
            host.ActionPhaseChanged += OnActionPhaseChanged;
            subscribed = true;
        }

        /// <summary>幂等解除共享 Module 事件订阅。</summary>
        private void Unsubscribe()
        {
            if (!subscribed || host == null) return;
            host.Completed -= OnSkillCompleted;
            host.HitDetected -= OnSkillHit;
            host.ActionPhaseChanged -= OnActionPhaseChanged;
            subscribed = false;
        }

        /// <summary>先撤销上一帧贡献，再写入当前阶段与打断状态，避免同一 Task 产生计数泄漏。</summary>
        /// <param name="phase">当前动作阶段。</param>
        /// <param name="canBeInterrupted">当前阶段是否允许外部打断。</param>
        private void ApplyRuntimeTags(ActionPhaseType phase, bool canBeInterrupted)
        {
            GameplayTag nextPhaseTag = GetPhaseTag(phase);
            GameplayTag nextInterruptTag = canBeInterrupted
                ? GameplayTags.Tag_State_Action_Skill_Interruptible
                : GameplayTags.Tag_State_Action_Skill_Uninterruptible;
            if (hasAppliedPhaseTag && appliedPhaseTag == nextPhaseTag &&
                hasAppliedInterruptTag && appliedInterruptTag == nextInterruptTag)
                return;

            ClearRuntimeTags();
            Runtime.SourceASC.UpdateRuntimeTagCount(nextPhaseTag, 1);
            Runtime.SourceASC.UpdateRuntimeTagCount(nextInterruptTag, 1);
            appliedPhaseTag = nextPhaseTag;
            appliedInterruptTag = nextInterruptTag;
            hasAppliedPhaseTag = true;
            hasAppliedInterruptTag = true;
        }

        /// <summary>对称撤销当前 Task 写入 Source ASC 的阶段与打断状态 Tag。</summary>
        private void ClearRuntimeTags()
        {
            if (hasAppliedPhaseTag)
                Runtime.SourceASC.UpdateRuntimeTagCount(appliedPhaseTag, -1);
            if (hasAppliedInterruptTag)
                Runtime.SourceASC.UpdateRuntimeTagCount(appliedInterruptTag, -1);
            hasAppliedPhaseTag = false;
            hasAppliedInterruptTag = false;
        }

        /// <summary>将 SkillSystem 阶段枚举映射到正式 GameplayTag；空白区间使用 Phase.None。</summary>
        /// <param name="phase">SkillConfig 当前动作阶段。</param>
        /// <returns>对应的正式阶段 Tag。</returns>
        private static GameplayTag GetPhaseTag(ActionPhaseType phase) => phase switch
        {
            ActionPhaseType.Startup => GameplayTags.Tag_State_Action_Skill_Phase_StartUp,
            ActionPhaseType.Active => GameplayTags.Tag_State_Action_Skill_Phase_Active,
            ActionPhaseType.Recovery => GameplayTags.Tag_State_Action_Skill_Phase_Recovery,
            _ => GameplayTags.Tag_State_Action_Skill_Phase_None
        };

        #endregion
    }
}
