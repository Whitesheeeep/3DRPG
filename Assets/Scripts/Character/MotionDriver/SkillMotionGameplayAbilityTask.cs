using System;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.Character
{
    /// <summary>由具体 GA 代码构造的限时技能运动任务，不依赖 SkillConfig 的根运动标记。</summary>
    public sealed class SkillMotionGameplayAbilityTask : GameplayAbilityTask
    {
        #region 配置与生命周期状态
        private readonly MotionChannels channels;
        private readonly bool consumeRootMotion;
        private readonly Vector3 worldVelocity;
        private readonly float duration;
        // 仅持有请求接口，不允许 Task 直接进行阶段结算或调用 CharacterController。
        private IMotionDriver driver;
        private MotionControlHandle handle;
        private float elapsed;
        #endregion

        /// <summary>创建站桩、代码冲刺或根运动技能的运动步骤。</summary>
        /// <param name="runtime">所属异步 GA。</param>
        /// <param name="channels">技能占用通道。</param>
        /// <param name="duration">持续秒数，必须大于零。</param>
        /// <param name="worldVelocity">代码运动世界速度；站桩或根运动传零。</param>
        /// <param name="consumeRootMotion">是否消费获胜通道的 Animator 增量。</param>
        public SkillMotionGameplayAbilityTask(AsynchronousGameplayAbilityRuntime runtime,
            MotionChannels channels, float duration, Vector3 worldVelocity, bool consumeRootMotion = false)
            : base(runtime)
        {
            if (duration <= 0 || float.IsNaN(duration) || float.IsInfinity(duration))
                throw new ArgumentOutOfRangeException(nameof(duration));
            if (consumeRootMotion && worldVelocity != Vector3.zero)
                throw new ArgumentException("同一运动任务不能在相同通道同时提交代码移动并消费根运动。");
            this.channels = channels;
            this.duration = duration;
            this.worldVelocity = worldVelocity;
            this.consumeRootMotion = consumeRootMotion;
        }

        /// <summary>取得现有 IMotionDriver 并申请 Skill 优先级的持续控制权。</summary>
        protected override void OnStart()
        {
            driver = Runtime.SourceOwner.MotionDriver ??
                throw new InvalidOperationException("[SkillMotionTask] 当前角色未绑定 MotionDriver。");
            // 请求成功后不再执行可能失败的初始化操作，所有终态都对称释放 Handle。
            handle = driver.RequestControl(new MotionControlRequest(Runtime.SourceOwner,
                MotionPriority.Skill, channels, consumeRootMotion));
        }

        /// <summary>在物理阶段提交按秒数积分后的代码运动。</summary>
        /// <param name="fixedDeltaTime">当前物理步时长。</param>
        protected override void OnFixedTick(float fixedDeltaTime)
        {
            if (!consumeRootMotion)
                driver.SubmitFixed(handle, FixedMotionRequest.TranslationOnly(worldVelocity * fixedDeltaTime));
        }

        /// <summary>在普通阶段累计生命周期并结束任务。</summary>
        /// <param name="deltaTime">本帧秒数。</param>
        protected override void OnTick(float deltaTime)
        {
            elapsed += deltaTime;
            if (elapsed >= duration) Complete();
        }

        /// <summary>正常停止时释放持续控制权。</summary>
        protected override void OnStop() => ReleaseControl();
        /// <summary>技能取消时释放持续控制权。</summary>
        protected override void OnCancel() => ReleaseControl();
        /// <summary>技能完成时释放持续控制权。</summary>
        protected override void OnComplete() => ReleaseControl();
        /// <summary>对称结束 Handle 生命周期，重复清理不产生副作用。</summary>
        private void ReleaseControl()
        {
            handle?.Dispose();
            handle = null;
        }
    }
}
