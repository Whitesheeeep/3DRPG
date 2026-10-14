using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.Character.Combat;
using RPG.Markers;
using RPG.SkillSystem;
using UnityEngine;
using WS_Modules.AudioSystem;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>锁定方向、蓄力数值与伤害 Spec，并借用共享控制权播放释放 SkillConfig。</summary>
    public sealed class ChargedDashReleaseGameplayAbilityTask : GameplayAbilityTask
    {
        #region 配置与运行时状态

        private readonly SkillConfig skillConfig;
        private readonly float maximumChargeDuration;
        private readonly float minimumDamageMultiplier;
        private readonly float maximumDamageMultiplier;
        private readonly string releaseAudioKey;
        private readonly bool releaseAudioIs3D;
        private readonly float releaseAudioVolumeScale;
        private readonly MarkerKey rightHandMarkerKey;
        private readonly GameObject releaseVfxPrefab;
        private readonly Vector3 releaseVfxLocalPosition;
        private readonly Vector3 releaseVfxLocalEulerAngles;
        private readonly float releaseVfxScale;

        // 播放子 Task 借用 Runtime Lease 与预先封存的 GE Specs，不重复申请控制权或创建另一套结算快照。
        private PlaySkillConfigGameplayAbilityTask playbackTask;
        private IReadOnlyList<GameplayEffectSpec> sealedEffectSpecs;
        private bool didPlayReleasePresentation;

        #endregion

        #region 构造

        /// <summary>保存释放配置；不在 Task 构造时读取输入、目标或创建 GE Spec。</summary>
        /// <param name="runtime">拥有该 Task 的异步 Ability Runtime。</param>
        /// <param name="config">释放阶段使用的 SkillConfig。</param>
        /// <param name="chargeCap">伤害倍率开始封顶的蓄力秒数。</param>
        /// <param name="minimumMultiplier">零蓄力伤害倍率。</param>
        /// <param name="maximumMultiplier">满蓄力伤害倍率。</param>
        /// <param name="audioKey">可选释放音频 Key。</param>
        /// <param name="audioIs3D">是否以 3D 方式播放音频。</param>
        /// <param name="audioVolumeScale">音频音量倍率。</param>
        /// <param name="handMarkerKey">右手 MarkerKey。</param>
        /// <param name="vfxPrefab">可选释放特效 Prefab。</param>
        /// <param name="vfxLocalPosition">特效本地位置偏移。</param>
        /// <param name="vfxLocalEulerAngles">特效本地旋转偏移。</param>
        /// <param name="vfxScale">特效缩放倍率。</param>
        public ChargedDashReleaseGameplayAbilityTask(
            AsynchronousGameplayAbilityRuntime runtime,
            SkillConfig config,
            float chargeCap,
            float minimumMultiplier,
            float maximumMultiplier,
            string audioKey,
            bool audioIs3D,
            float audioVolumeScale,
            MarkerKey handMarkerKey,
            GameObject vfxPrefab,
            Vector3 vfxLocalPosition,
            Vector3 vfxLocalEulerAngles,
            float vfxScale)
            : base(runtime)
        {
            skillConfig = config ?? throw new ArgumentNullException(nameof(config));
            maximumChargeDuration = chargeCap;
            minimumDamageMultiplier = minimumMultiplier;
            maximumDamageMultiplier = maximumMultiplier;
            releaseAudioKey = audioKey;
            releaseAudioIs3D = audioIs3D;
            releaseAudioVolumeScale = audioVolumeScale;
            rightHandMarkerKey = handMarkerKey;
            releaseVfxPrefab = vfxPrefab;
            releaseVfxLocalPosition = vfxLocalPosition;
            releaseVfxLocalEulerAngles = vfxLocalEulerAngles;
            releaseVfxScale = vfxScale;
        }

        #endregion

        #region 生命周期与推进

        /// <summary>锁定本次释放结果、封存 GE Specs、播放一次性表现并启动共享时间轴。</summary>
        protected override void OnStart()
        {
            ChargedDashGameplayAbilityRuntime runtime = GetChargedDashRuntime();
            runtime.MarkReleaseStarted();

            float effectiveChargeDuration = Mathf.Min(runtime.HeldDurationSeconds, maximumChargeDuration);
            float chargeProgress = Mathf.Clamp01(effectiveChargeDuration / maximumChargeDuration);
            float chargeMultiplier = Mathf.Lerp(minimumDamageMultiplier, maximumDamageMultiplier, chargeProgress);
            Vector3 releaseDirection = ResolveReleaseDirection(runtime.PlayerOwner);
            runtime.SetReleaseResult(releaseDirection, chargeMultiplier);

            if (!TryCreateReleaseEffectSpecs(runtime, chargeMultiplier, out sealedEffectSpecs))
            {
                Debug.LogError(
                    $"[ChargedDashReleaseGameplayAbilityTask] ActivationId={GARuntime.ActivationId} 无法创建带蓄力倍率的伤害 Specs。",
                    GARuntime.SourceASC);
                Complete();
                return;
            }

            Debug.Log(
                $"[ChargedDashReleaseGameplayAbilityTask] ActivationId={GARuntime.ActivationId} 锁定释放结果，" +
                $"charge={effectiveChargeDuration:F3}s，progress={chargeProgress:F2}，" +
                $"damageMultiplier={chargeMultiplier:F2}，direction={releaseDirection}。",
                GARuntime.SourceASC);

            if (runtime.HasEnteredChargeWait)
                PlayReleasePresentation(runtime);

            SkillAbilityControlLease lease = runtime.ControlLease ??
                throw new InvalidOperationException($"ActivationId={GARuntime.ActivationId} 释放时没有共享控制权 Lease。");
            // 释放动画开始前先取得垂直根运动通道；新句柄获胜后再同步归还旧句柄。
            lease.ExpandMotionControlForRootMotion();
            playbackTask = new PlaySkillConfigGameplayAbilityTask(
                GARuntime, skillConfig, SkillStartMode.TimelineStart, lease, sealedEffectSpecs);
            playbackTask.Completed += OnPlaybackCompleted;
            playbackTask.Start();
        }

        /// <summary>把普通更新转发给 SkillConfig 播放 Task。</summary>
        /// <param name="deltaTime">本次普通更新秒数。</param>
        protected override void OnTick(float deltaTime) => playbackTask?.Tick(deltaTime);

        /// <summary>把固定更新转发给 SkillConfig 播放 Task。</summary>
        /// <param name="fixedDeltaTime">本物理步的真实固定时长。</param>
        protected override void OnFixedTick(float fixedDeltaTime) => playbackTask?.FixedTick(fixedDeltaTime);

        /// <summary>将攻击检测与时间轴自然结束转发到现有 SkillConfig Host。</summary>
        /// <param name="deltaTime">延迟更新阶段使用的秒数。</param>
        protected override void OnLateTick(float deltaTime) => playbackTask?.LateTick(deltaTime);

        /// <summary>将释放动画产生的根运动转发给共享播放 Task，由 MotionDriver 结算碰撞。</summary>
        /// <param name="deltaPosition">Animator 提供的位移增量。</param>
        /// <param name="deltaRotation">Animator 提供的旋转增量。</param>
        protected override void OnUpdateAnimationMove(Vector3 deltaPosition, Quaternion deltaRotation) =>
            playbackTask?.UpdateAnimationMove(deltaPosition, deltaRotation);

        /// <summary>自然结束前解除播放子 Task 的完成回调。</summary>
        protected override void OnComplete() => DetachPlaybackTask();

        /// <summary>正常提前停止时把停止请求转发给共享播放 Task。</summary>
        protected override void OnStop()
        {
            DetachPlaybackTask();
            playbackTask?.Stop();
        }

        /// <summary>取消时把取消请求转发给共享播放 Task。</summary>
        protected override void OnCancel()
        {
            DetachPlaybackTask();
            playbackTask?.Cancel();
        }

        #endregion

        #region 释放准备

        /// <summary>优先采用有效锁定目标，其次世界移动输入，最后角色当前水平朝向。</summary>
        /// <param name="playerOwner">提供移动输入与技能 Owner 能力的玩家接口。</param>
        /// <returns>归一化并同步施法者朝向的水平世界方向。</returns>
        private Vector3 ResolveReleaseDirection(IPlayerGameplayAbilitySystemOwner playerOwner)
        {
            Transform sourceRoot = GARuntime.SourceOwner.RootTransform ??
                throw new InvalidOperationException("蓄力释放 Owner 缺少 RootTransform。");
            Vector3 direction = Vector3.zero;
            if (LockTargetSystem.Instance != null &&
                LockTargetSystem.Instance.TryGetAttackTarget(out GameplayAbilitySystemComponent target) &&
                target != GARuntime.SourceASC && target.isActiveAndEnabled && target.gameObject.activeInHierarchy &&
                target.Owner?.RootTransform is { } targetRoot && targetRoot.gameObject.activeInHierarchy)
            {
                direction = Vector3.ProjectOnPlane(targetRoot.position - sourceRoot.position, Vector3.up);
            }

            if (direction.sqrMagnitude <= 0.0001f)
                direction = Vector3.ProjectOnPlane(playerOwner.MoveWorldInput, Vector3.up);
            if (direction.sqrMagnitude <= 0.0001f)
                direction = Vector3.ProjectOnPlane(sourceRoot.forward, Vector3.up);
            if (direction.sqrMagnitude <= 0.0001f)
                direction = Vector3.forward;

            direction.Normalize();
            sourceRoot.rotation = Quaternion.LookRotation(direction, Vector3.up);
            return direction;
        }

        /// <summary>复制激活 SetByCaller，加入蓄力倍率并在 SkillConfig 启动前封存全部 GE Specs。</summary>
        /// <param name="runtime">本次蓄力 GA Runtime。</param>
        /// <param name="chargeMultiplier">本次释放锁定的蓄力倍率。</param>
        /// <param name="specs">创建成功时返回已封存的效果 Specs。</param>
        /// <returns>全部效果 Spec 创建并封存成功时返回 true。</returns>
        private bool TryCreateReleaseEffectSpecs(
            ChargedDashGameplayAbilityRuntime runtime,
            float chargeMultiplier,
            out IReadOnlyList<GameplayEffectSpec> specs)
        {
            var setByCallerValueByTagMap = new Dictionary<GameplayTag, float>();
            foreach (KeyValuePair<GameplayTag, float> valueByTag in GARuntime.SetByCaller)
                setByCallerValueByTagMap[valueByTag.Key] = valueByTag.Value;
            setByCallerValueByTagMap[GameplayTags.Tag_Data_Damage_ChargeMultiplier] = chargeMultiplier;
            return runtime.Data.TryCreateConfiguredEffectSpecs(
                GARuntime.SourceASC,
                GARuntime.Level,
                setByCallerValueByTagMap,
                out specs);
        }

        /// <summary>按等待阶段是否实际开始，播放一次可选释放音频和右手粒子特效。</summary>
        /// <param name="runtime">本次蓄力 GA Runtime。</param>
        private void PlayReleasePresentation(ChargedDashGameplayAbilityRuntime runtime)
        {
            if (didPlayReleasePresentation) return;
            didPlayReleasePresentation = true;

            Transform sourceRoot = GARuntime.SourceOwner.RootTransform;
            if (!string.IsNullOrWhiteSpace(releaseAudioKey))
            {
                AudioManager.Instance.PlaySFX(
                    releaseAudioKey,
                    sourceRoot.position,
                    releaseAudioIs3D,
                    releaseAudioVolumeScale,
                    loop: false,
                    autoRelease: true);
            }

            if (releaseVfxPrefab != null)
                OneShotVfxPlayer.TryPlay(
                    GARuntime.SourceOwner,
                    rightHandMarkerKey,
                    releaseVfxPrefab,
                    releaseVfxLocalPosition,
                    releaseVfxLocalEulerAngles,
                    releaseVfxScale);

            Debug.Log(
                $"[ChargedDashReleaseGameplayAbilityTask] ActivationId={GARuntime.ActivationId} 已触发释放表现，" +
                $"audioConfigured={!string.IsNullOrWhiteSpace(releaseAudioKey)}，vfxConfigured={releaseVfxPrefab != null}。",
                GARuntime.SourceASC);
        }

        /// <summary>从异步 Runtime 中取得蓄力专用状态。</summary>
        /// <returns>当前蓄力冲刺 Runtime。</returns>
        /// <exception cref="InvalidOperationException">Task 被其他 Ability Data 错误复用时抛出。</exception>
        private ChargedDashGameplayAbilityRuntime GetChargedDashRuntime() =>
            GARuntime as ChargedDashGameplayAbilityRuntime ??
            throw new InvalidOperationException("ChargedDashRelease Task 需要 ChargedDashGameplayAbilityRuntime。");

        /// <summary>解除当前播放子 Task 的完成事件，避免释放后回调旧 Task。</summary>
        private void DetachPlaybackTask()
        {
            if (playbackTask != null)
                playbackTask.Completed -= OnPlaybackCompleted;
        }

        /// <summary>共享 SkillConfig Host 自然结束后完成蓄力释放阶段。</summary>
        /// <param name="task">已经完成的播放 Task。</param>
        private void OnPlaybackCompleted(GameplayAbilityTask task)
        {
            task.Completed -= OnPlaybackCompleted;
            playbackTask = null;
            Complete();
        }

        #endregion
    }
}
