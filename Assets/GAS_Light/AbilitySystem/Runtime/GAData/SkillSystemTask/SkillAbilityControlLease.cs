using System;
using RPG.Character;
using RPG.Character.Animation;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.SkillSystem
{
    /// <summary>在蓄力 GA 生命周期内持有角色动作、运动通道和武器姿态控制权。</summary>
    public sealed class SkillAbilityControlLease : IDisposable
    {
        #region 依赖字段

        // 生命周期依赖：释放表现借用同一个 Host 与控制句柄，最终由 GA Runtime 统一归还。
        private readonly IGameplayAbilitySystemOwner owner;
        private readonly IMotionDriver motionDriver;
        private readonly IAnimationPlayer animationPlayer;
        private readonly IWeaponSwitch weaponSwitch;
        private MotionControlHandle motionHandle;
        private FullBodyActionHandle fullBodyActionHandle;
        private bool ownsWeaponPose;
        private bool disposed;

        #endregion

        #region 属性

        /// <summary>获取由本 Lease 持有的 Skill Runtime Host。</summary>
        public ISkillRuntimeHost Host { get; }
        /// <summary>获取由本 Lease 持有的角色运动控制句柄。</summary>
        public MotionControlHandle MotionHandle => motionHandle;
        /// <summary>获取准备阶段播放并在蓄力期间冻结的 Animancer 状态。</summary>
        public Animancer.AnimancerState PreparationState { get; private set; }
        /// <summary>获取原始 GAS Owner。</summary>
        public IGameplayAbilitySystemOwner Owner => owner;

        #endregion

        #region 构造与创建

        /// <summary>保存已成功取得的共享角色控制资源。</summary>
        /// <param name="sourceOwner">提供角色控制接口的 Owner。</param>
        /// <param name="sourceMotionDriver">负责仲裁角色位移通道的 MotionDriver。</param>
        /// <param name="sourceHost">用于播放释放 SkillConfig 的 Host。</param>
        /// <param name="sourceMotionHandle">本次冲刺共用的运动控制句柄。</param>
        /// <param name="sourceFullBodyActionHandle">本次动作占据句柄。</param>
        /// <param name="sourceWeaponSwitch">可选的武器姿态控制能力。</param>
        /// <param name="didHoldWeaponPose">是否已成功取得武器姿态。</param>
        private SkillAbilityControlLease(
            IGameplayAbilitySystemOwner sourceOwner,
            IMotionDriver sourceMotionDriver,
            ISkillRuntimeHost sourceHost,
            MotionControlHandle sourceMotionHandle,
            FullBodyActionHandle sourceFullBodyActionHandle,
            IWeaponSwitch sourceWeaponSwitch,
            bool didHoldWeaponPose)
        {
            owner = sourceOwner;
            motionDriver = sourceMotionDriver;
            Host = sourceHost;
            motionHandle = sourceMotionHandle;
            fullBodyActionHandle = sourceFullBodyActionHandle;
            weaponSwitch = sourceWeaponSwitch;
            ownsWeaponPose = didHoldWeaponPose;
            animationPlayer = sourceOwner.AnimationPlayer;
        }

        /// <summary>原子申请蓄力流程共用的角色控制权，并在失败时回收部分申请。</summary>
        /// <param name="runtime">拥有本 Lease 的 Ability Runtime。</param>
        /// <param name="sourceOwner">本次技能的 Owner。</param>
        /// <returns>全部控制权成功申请后的 Lease。</returns>
        /// <exception cref="ArgumentNullException">Runtime 或 Owner 为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">Owner 缺少必需的技能、运动或动作仲裁能力时抛出。</exception>
        internal static SkillAbilityControlLease Acquire(
            GameplayAbilityRuntime runtime,
            IGameplayAbilitySystemOwner sourceOwner)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (sourceOwner == null) throw new ArgumentNullException(nameof(sourceOwner));

            ISkillRuntimeHost host = sourceOwner.SkillRuntimeHost ??
                throw new InvalidOperationException("蓄力技能 Owner 缺少 SkillRuntimeHost。");
            IMotionDriver motionDriver = sourceOwner.MotionDriver ??
                throw new InvalidOperationException("蓄力技能 Owner 缺少 MotionDriver。");
            IFullBodyActionArbiter actionArbiter = sourceOwner.FullBodyActionArbiter ??
                throw new InvalidOperationException("蓄力技能 Owner 缺少 FullBody Action Arbiter。");
            MotionControlHandle motion = motionDriver.RequestControl(new MotionControlRequest(
                sourceOwner,
                MotionPriority.Skill,
                MotionChannels.Horizontal | MotionChannels.Rotation));
            FullBodyActionHandle fullBody = null;
            IWeaponSwitch weaponSwitch = sourceOwner.WeaponSwitch;
            bool ownsWeaponPose = false;
            try
            {
                fullBody = actionArbiter.RegisterFullBodyAction(runtime);
                if (weaponSwitch != null)
                {
                    weaponSwitch.HoldWeapon();
                    ownsWeaponPose = true;
                }

                Debug.Log(
                    $"[SkillAbilityControlLease] ActivationId={runtime.ActivationId} 已取得蓄力技能控制权。",
                    runtime.SourceASC);
                return new SkillAbilityControlLease(
                    sourceOwner, motionDriver, host, motion, fullBody, weaponSwitch, ownsWeaponPose);
            }
            catch
            {
                if (ownsWeaponPose) weaponSwitch.CarryWeaponOnBack();
                fullBody?.Dispose();
                motion.Dispose();
                throw;
            }
        }

        /// <summary>保存准备动画状态，供蓄力等待阶段冻结在同一姿态。</summary>
        /// <param name="state">Animancer 返回的准备动画状态。</param>
        internal void SetPreparationState(Animancer.AnimancerState state)
        {
            PreparationState = state ?? throw new ArgumentNullException(nameof(state));
        }

        #endregion

        #region 根运动控制权

        /// <summary>在释放动画启动前原子地把运动句柄扩展到根运动所需通道。</summary>
        internal void ExpandMotionControlForRootMotion()
        {
            MotionControlHandle expandedMotionHandle = motionDriver.RequestControl(new MotionControlRequest(
                owner,
                MotionPriority.Skill,
                MotionChannels.Horizontal | MotionChannels.Vertical | MotionChannels.Rotation));

            // 新请求建立后立即成为最新的同优先级请求，再释放旧请求，避免控制通道交接出现空档。
            MotionControlHandle previousMotionHandle = motionHandle;
            motionHandle = expandedMotionHandle;
            previousMotionHandle.Dispose();
            Debug.Log("[SkillAbilityControlLease] 已将释放控制权扩展到根运动水平、垂直和旋转通道。", owner.RootTransform);
        }

        #endregion

        #region 释放

        /// <summary>停止 Action 层并按相反顺序释放武器、FullBody 与运动控制权。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            // 动画先停止，随后归还姿态与控制权，防止下一帧 Locomotion 接管时仍残留蓄力姿势。
            animationPlayer.FadeLayer(AnimationLayerType.Action, 0, .2f);
            if (ownsWeaponPose)
            {
                ownsWeaponPose = false;
                weaponSwitch.CarryWeaponOnBack();
            }
            fullBodyActionHandle?.Dispose();
            fullBodyActionHandle = null;
            motionHandle?.Dispose();
            motionHandle = null;
            Debug.Log("[SkillAbilityControlLease] 蓄力技能控制权已统一释放。", owner.RootTransform);
        }

        #endregion
    }
}
