using System;
using System.Collections.Generic;
using RPG.PlayerInputSystem;
using RPG.SkillSystem;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>跟踪一次蓄力手势、分阶段状态和整个技能期间共用的控制权。</summary>
    public sealed class ChargedDashGameplayAbilityRuntime : AsynchronousGameplayAbilityRuntime
    {
        #region 依赖字段

        // 外部输入缓冲仅在 Runtime Active 期间订阅；所有角色控制权由同一个 Lease 持有。
        private IPlayerInputRequestBuffer inputRequestBuffer;
        private bool isSubscribed;
        private SkillAbilityControlLease controlLease;

        #endregion

        #region 手势与释放结果

        private E_PlayerInputType inputType;
        private InputRequestHandle inputGestureHandle;
        private bool hasInputGesture;
        private bool hasReleased;
        private bool hasEnteredChargeWait;
        private bool hasEnteredRelease;

        /// <summary>获取玩家 Owner 输入能力；开始时完成绑定，非玩家 Owner 为 null。</summary>
        public IPlayerGameplayAbilitySystemOwner PlayerOwner { get; private set; }
        /// <summary>获取本次技能是否已收到匹配手势的松开。</summary>
        public bool HasReleased => hasReleased;
        /// <summary>获取从原始 Press 到 Release 的真实按住秒数。</summary>
        public float HeldDurationSeconds { get; private set; }
        /// <summary>获取蓄力等待阶段是否曾经实际进入等待。</summary>
        public bool HasEnteredChargeWait => hasEnteredChargeWait;
        /// <summary>获取当前蓄力流程共用的控制权 Lease。</summary>
        public SkillAbilityControlLease ControlLease => controlLease;
        /// <summary>获取释放阶段锁定的世界水平朝向，供角色转向和根运动播放使用。</summary>
        public Vector3 ReleaseDirection { get; private set; }
        /// <summary>获取按蓄力时间线性计算的伤害倍率。</summary>
        public float ReleaseChargeMultiplier { get; private set; }

        #endregion

        #region 构造

        /// <summary>创建 Runtime 并从异步 Data 构造尚未启动的 Root Task。</summary>
        /// <param name="activationId">当前 Controller 分配的激活标识。</param>
        /// <param name="spec">本次激活使用的已授予 Ability Spec。</param>
        /// <param name="source">释放 Ability 的 Source ASC。</param>
        /// <param name="setByCaller">本次激活复制的 SetByCaller 快照。</param>
        /// <param name="data">本次蓄力技能定义。</param>
        public ChargedDashGameplayAbilityRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            AsynchronousGameplayAbilityData data)
            : base(activationId, spec, source, setByCaller, data)
        {
        }

        #endregion

        #region 生命周期

        /// <summary>绑定实际输入手势并订阅释放与整体清理事件后启动 Task 序列。</summary>
        protected override void OnStart()
        {
            if (SourceOwner is not IPlayerGameplayAbilitySystemOwner playerOwner ||
                !playerOwner.TryGetCurrentAbilityInputSource(out inputType, out inputGestureHandle) ||
                inputGestureHandle.Stage != PlayerInputRequestStage.Press ||
                !playerOwner.TryGetGameplayAbilityHandle(inputType, out GameplayAbilityHandle configuredHandle) ||
                !configuredHandle.Equals(Spec.Handle))
            {
                Debug.LogError(
                    $"[ChargedDashGameplayAbilityRuntime] ActivationId={ActivationId} 缺少匹配的玩家技能输入手势。",
                    SourceASC);
                Complete();
                return;
            }

            PlayerOwner = playerOwner;
            hasInputGesture = true;
            inputRequestBuffer = playerOwner.InputRequestBuffer;
            if (inputRequestBuffer == null)
            {
                Debug.LogError(
                    $"[ChargedDashGameplayAbilityRuntime] ActivationId={ActivationId} 没有可用的输入请求缓冲区。",
                    SourceASC);
                Complete();
                return;
            }

            inputRequestBuffer.RequestReleased += OnInputRequestReleased;
            inputRequestBuffer.RequestsCleared += OnInputRequestsCleared;
            isSubscribed = true;
            Debug.Log(
                $"[ChargedDashGameplayAbilityRuntime] ActivationId={ActivationId} 绑定输入手势 {inputGestureHandle}。",
                SourceASC);

            // Press 缓冲可能在玩家已经松开后才激活；订阅后立即读取手势快照覆盖此前事件。
            if (inputRequestBuffer.TryGetRequest(inputType, out IReadOnlyPlayerInputRequest request) &&
                request.PressHandle.GestureVersion == inputGestureHandle.GestureVersion &&
                request.PhysicalState == PlayerInputPhysicalState.Released)
                RecordRelease(request.HeldDuration);

            base.OnStart();
        }

        /// <summary>正常结束时停止 Root Task 并释放输入订阅与共享控制权。</summary>
        protected override void OnEnd()
        {
            base.OnEnd();
            CleanupRuntimeResources();
        }

        /// <summary>取消或 ASC 清理时停止 Root Task 并释放输入订阅与共享控制权。</summary>
        protected override void OnCancel()
        {
            base.OnCancel();
            CleanupRuntimeResources();
        }

        #endregion

        #region 阶段状态

        /// <summary>申请并保存贯穿准备、蓄力和释放的角色控制权。</summary>
        internal void AcquireControlLease()
        {
            if (controlLease != null)
                throw new InvalidOperationException($"ActivationId={ActivationId} 已持有 SkillAbilityControlLease。");
            controlLease = SkillAbilityControlLease.Acquire(this, SourceOwner);
        }

        /// <summary>在阶段启动失败或 Runtime 终态时幂等释放共享控制权。</summary>
        internal void ReleaseControlLease()
        {
            controlLease?.Dispose();
            controlLease = null;
        }

        /// <summary>标记蓄力 Task 已开始等待，供释放 Task 判断是否需要播放按住后的释放表现。</summary>
        internal void MarkChargeWaitEntered() => hasEnteredChargeWait = true;

        /// <summary>标记进入释放阶段，后续清空输入只清除缓冲，不取消已开始的冲刺。</summary>
        internal void MarkReleaseStarted() => hasEnteredRelease = true;

        /// <summary>保存释放阶段锁定的根运动朝向和伤害倍率。</summary>
        /// <param name="direction">归一化后的世界水平释放方向。</param>
        /// <param name="chargeMultiplier">本次蓄力伤害倍率。</param>
        internal void SetReleaseResult(Vector3 direction, float chargeMultiplier)
        {
            ReleaseDirection = direction;
            ReleaseChargeMultiplier = chargeMultiplier;
        }

        #endregion

        #region 输入事件与清理

        /// <summary>只接受创建当前 Runtime 的 Press 手势所对应的 Release 通知。</summary>
        /// <param name="releaseHandle">输入缓冲生成的 Release 阶段句柄。</param>
        /// <param name="heldDuration">从真实 Press 到 Release 的总秒数。</param>
        private void OnInputRequestReleased(InputRequestHandle releaseHandle, float heldDuration)
        {
            if (!hasInputGesture || hasReleased ||
                releaseHandle.InputType != inputType ||
                releaseHandle.GestureVersion != inputGestureHandle.GestureVersion ||
                releaseHandle.Stage != PlayerInputRequestStage.Release)
                return;

            RecordRelease(heldDuration);
        }

        /// <summary>锁定第一次有效 Release 的真实按住时长，不受缓冲到期或新手势影响。</summary>
        /// <param name="heldDuration">输入系统使用真实时间计算的按住秒数。</param>
        private void RecordRelease(float heldDuration)
        {
            if (float.IsNaN(heldDuration) || float.IsInfinity(heldDuration) || heldDuration < 0f)
            {
                Debug.LogError(
                    $"[ChargedDashGameplayAbilityRuntime] ActivationId={ActivationId} 收到非法按住时长 {heldDuration}。",
                    SourceASC);
                ForceCancel();
                return;
            }

            hasReleased = true;
            hasInputGesture = true;
            HeldDurationSeconds = heldDuration;
            Debug.Log(
                $"[ChargedDashGameplayAbilityRuntime] ActivationId={ActivationId} 锁定释放时长 {heldDuration:F3}s。",
                SourceASC);
        }

        /// <summary>请求缓冲整体清空时取消尚未进入释放的蓄力技能。</summary>
        private void OnInputRequestsCleared()
        {
            if (hasEnteredRelease) return;
            Debug.Log(
                $"[ChargedDashGameplayAbilityRuntime] ActivationId={ActivationId} 因输入请求整体清理而取消。",
                SourceASC);
            ForceCancel();
        }

        /// <summary>幂等退订输入事件并归还唯一共享控制权。</summary>
        private void CleanupRuntimeResources()
        {
            if (isSubscribed)
            {
                inputRequestBuffer.RequestReleased -= OnInputRequestReleased;
                inputRequestBuffer.RequestsCleared -= OnInputRequestsCleared;
                isSubscribed = false;
                Debug.Log(
                    $"[ChargedDashGameplayAbilityRuntime] ActivationId={ActivationId} 已解绑输入事件。",
                    SourceASC);
            }

            ReleaseControlLease();
        }

        #endregion
    }
}
