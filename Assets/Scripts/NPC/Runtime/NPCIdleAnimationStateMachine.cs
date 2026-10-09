using System;
using Animancer;
using RPG.Character.Animation;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.FSM;

namespace RPG.NPC
{
    /// <summary>表示站桩 NPC 基础动画状态机当前所处的生命周期状态。</summary>
    public enum E_NPCIdleAnimationState
    {
        /// <summary>状态机尚未激活或已经释放。</summary>
        Disabled = -1,
        /// <summary>播放循环待机动画。</summary>
        Idle = 0
    }

    /// <summary>使用 Base 动画层为站桩 NPC 持续播放待机动作。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AnimationController))]
    [InfoBox("依赖同节点 Animator、AnimancerComponent 与 AnimationController，并需配置有效的待机 TransitionAsset；缺少任一项时组件无法启动。")]
    public sealed class NPCIdleAnimationStateMachine : MonoBehaviour
    {
        #region 依赖字段

        // 依赖字段：显式引用同一角色上的 Animancer 播放入口。
        [SerializeField, Required] private AnimationController animationController;
        [SerializeField, Required] private TransitionAsset idleTransition;

        #endregion

        #region 运行时状态

        // 状态机仅在组件启用期间存在；Idle 状态只在进入时提交一次动画播放。
        private StateMachine<E_NPCIdleAnimationState, NPCIdleAnimationStateMachine> stateMachine;

        /// <summary>获取当前状态；组件未启用时返回 Disabled。</summary>
        public E_NPCIdleAnimationState CurrentState =>
            stateMachine?.CurrentState?.StateId ?? E_NPCIdleAnimationState.Disabled;

        #endregion

        #region 生命周期

        /// <summary>组件启用时创建状态机并进入默认 Idle 状态。</summary>
        private void OnEnable()
        {
            ActivateStateMachine();
        }

        /// <summary>组件禁用时退出状态并停止本组件独占的 Base 动画层。</summary>
        private void OnDisable()
        {
            DeactivateStateMachine();
        }

        /// <summary>在 Inspector 添加组件时填入同节点的动画播放入口。</summary>
        private void Reset()
        {
            animationController = GetComponent<AnimationController>();
        }

        #endregion

        #region 状态机控制

        /// <summary>校验显式配置后创建并启动 Idle 状态机。</summary>
        /// <exception cref="InvalidOperationException">缺少动画控制器或 Idle Transition 配置。</exception>
        private void ActivateStateMachine()
        {
            if (stateMachine != null)
                return;

            ValidateConfiguration();

            stateMachine = new StateMachine<E_NPCIdleAnimationState, NPCIdleAnimationStateMachine>(
                E_NPCIdleAnimationState.Disabled);
            stateMachine.State(E_NPCIdleAnimationState.Idle)
                .OnEnter(_ => animationController.Play(
                    AnimationLayerType.Base,
                    idleTransition));
            stateMachine.SetDefaultState(E_NPCIdleAnimationState.Idle);
            stateMachine.Init(this, null);
            stateMachine.OnEnter();

            Debug.Log($"[NPCIdleAnimationStateMachine] NPC '{name}' 进入 Idle，动画层={AnimationLayerType.Base}。", this);
        }

        /// <summary>退出当前状态、停止 Base 层并释放本组件持有的 FSM。</summary>
        private void DeactivateStateMachine()
        {
            if (stateMachine == null)
                return;

            stateMachine.OnExit();
            stateMachine = null;
            animationController.StopLayer(AnimationLayerType.Base);
            Debug.Log($"[NPCIdleAnimationStateMachine] NPC '{name}' 退出 Idle 并停止 Base 动画层。", this);
        }

        /// <summary>确保组件已显式绑定可用的动画控制器和待机 Transition。</summary>
        /// <exception cref="InvalidOperationException">任一依赖未配置。</exception>
        private void ValidateConfiguration()
        {
            if (animationController != null && idleTransition != null)
                return;

            string reason = animationController == null
                ? "未配置同节点 AnimationController。"
                : "未配置 Idle TransitionAsset。";
            string message = $"NPCIdleAnimationStateMachine '{name}' 无法启动：{reason}";
            Debug.LogError($"[NPCIdleAnimationStateMachine] {message}", this);
            throw new InvalidOperationException(message);
        }

        #endregion
    }
}
