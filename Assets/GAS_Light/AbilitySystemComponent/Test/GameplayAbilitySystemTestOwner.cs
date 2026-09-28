using RPG.Character;
using RPG.Character.Animation;
using RPG.Character.Combat;
using RPG.Markers;
using RPG.SkillSystem;
using UnityEngine;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>为纯 GAS 测试对象提供最小 ASC Owner 宿主，不引入角色输入或 SkillSystem 依赖。</summary>
    [DisallowMultipleComponent]
    public sealed class GameplayAbilitySystemTestOwner : MonoBehaviour, IGameplayAbilitySystemOwner, IHitStopReceiver
    {
        #region 依赖字段

        // 该独立计时状态让纯 GAS 测试宿主验证与角色相同的未缩放卡帧时序。
        private CharacterHitStopState hitStopState;
        private bool isHitStopPaused;

        #endregion

        #region 缓存字段

        private IMarkerProvider markerProvider;
        private ISkillRuntimeHost skillRuntimeHost;
        private IMotionDriver motionDriver;

        #endregion

        /// <inheritdoc />
        public Transform RootTransform => transform;

        /// <inheritdoc />
        public IMarkerProvider MarkerProvider => markerProvider;
        /// <inheritdoc />
        public ISkillRuntimeHost SkillRuntimeHost => skillRuntimeHost;

        /// <inheritdoc />
        public IMotionDriver MotionDriver => motionDriver;
        public IAnimationPlayer AnimationPlayer { get; }

        /// <inheritdoc />
        public IFullBodyActionArbiter FullBodyActionArbiter => null;

        /// <inheritdoc />
        public bool IsActionPaused => isHitStopPaused;

        /// <inheritdoc />
        public bool IsActionPauseAppliedThisFrame => hitStopState?.WasAppliedThisFrame ?? false;

        #region 局部卡帧

        /// <summary>为纯 GAS 测试宿主提交未缩放卡帧请求。</summary>
        /// <param name="durationSeconds">本次暂停请求的时长，单位为秒。</param>
        /// <exception cref="System.ArgumentOutOfRangeException">时长不是有限正数时抛出。</exception>
        public void ApplyHitStop(float durationSeconds)
        {
            hitStopState ??= new CharacterHitStopState(this, SetHitStopPaused);
            hitStopState.Apply(durationSeconds);
        }

        /// <summary>更新测试宿主对 ASC 暴露的动作暂停状态。</summary>
        /// <param name="paused">当前是否处于卡帧时段。</param>
        private void SetHitStopPaused(bool paused)
        {
            // 纯 GAS 测试对象没有动画图；保存暂停标记供 ASC 和集成断言读取。
            isHitStopPaused = paused;
        }

        /// <summary>销毁测试宿主时取消未缩放恢复回调。</summary>
        private void OnDestroy()
        {
            hitStopState?.Clear();
        }

        #endregion

        /// <summary>缓存测试对象根节点的 Marker Provider。</summary>
        private void Awake()
        {
            markerProvider = GetComponent<IMarkerProvider>();
            skillRuntimeHost = GetComponent<ISkillRuntimeHost>();
            motionDriver = GetComponent<IMotionDriver>();
        }
    }
}
