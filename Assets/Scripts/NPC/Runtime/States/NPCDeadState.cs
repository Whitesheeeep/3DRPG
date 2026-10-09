using System;
using Animancer;
using RPG.Character.Animation;
using UnityEngine;
using WS_Modules.FSM;

namespace RPG.NPC
{
    /// <summary>播放 NPC 死亡动画并在动画结束后销毁 NPC 根实例。</summary>
    public sealed class NPCDeadState : StateBase<E_NPCStateId, NPCController>
    {
        #region 动画运行时

        private AnimancerState deathAnimationState;
        private bool destructionRequested;

        #endregion

        #region 构造

        /// <summary>创建无 Transition 的终止死亡状态。</summary>
        public NPCDeadState() : base(E_NPCStateId.Dead)
        {
        }

        #endregion

        #region 状态生命周期

        /// <summary>停止活体子状态并播放配置的非循环死亡动画。</summary>
        /// <param name="suppressDefaultState">忽略子状态机默认状态参数。</param>
        public override void OnEnter(bool suppressDefaultState = false)
        {
            Owner.BeginDeath();
            deathAnimationState = Owner.AnimationPlayer.Play(
                AnimationLayerType.Base,
                Owner.Config.DeathTransition);
            deathAnimationState.Events(this).OnEnd += OnDeathAnimationEnded;
            Debug.Log($"[NPCDeadState] NPC '{Owner.name}' 开始播放死亡动画。");
        }

        /// <summary>在动画结束时解绑回调并销毁整个 NPC 实例。</summary>
        private void OnDeathAnimationEnded()
        {
            if (destructionRequested)
                return;

            destructionRequested = true;
            if (deathAnimationState != null)
                deathAnimationState.Events(this).OnEnd -= OnDeathAnimationEnded;
            deathAnimationState = null;
            GameObject npcRoot = Owner.gameObject;
            Debug.Log($"[NPCDeadState] NPC '{npcRoot.name}' 死亡动画播放完成，销毁实例。");
            UnityEngine.Object.Destroy(npcRoot);
        }

        /// <summary>在状态退出或宿主销毁时解除 Animancer 结束回调。</summary>
        public override void OnExit()
        {
            if (deathAnimationState != null)
                deathAnimationState.Events(this).OnEnd -= OnDeathAnimationEnded;
            deathAnimationState = null;
            Debug.Log($"[NPCDeadState] NPC '{Owner.name}' 已解除死亡动画结束回调。");
            base.OnExit();
        }

        #endregion
    }
}
