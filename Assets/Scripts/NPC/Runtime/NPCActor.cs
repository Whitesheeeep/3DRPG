using System;
using RPG.Character;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.NPC
{
    /// <summary>把 NPC 的共用 Ability Owner 能力接到同节点 NPCController。</summary>
    [RequireComponent(typeof(Animator), typeof(CharacterController))]
    [InfoBox("依赖同节点 Animator、CharacterController、NPCController，以及同节点或子节点中的 ASC、MarkerProvider、SkillRuntimeHost 和 AnimationController。NPCController 负责初始化并推进这些能力。")]
    public sealed class NPCActor : CharacterAbilityActor
    {
        #region 依赖字段

        [SerializeField, Required] private NPCController npcController;

        #endregion

        #region GAS Owner 属性

        /// <inheritdoc />
        public override Transform RootTransform => transform;

        /// <inheritdoc />
        public override IMotionDriver MotionDriver => Controller.MotionDriver;

        /// <inheritdoc />
        public override IFullBodyActionArbiter FullBodyActionArbiter => Controller.FullBodyActionArbiter;

        /// <summary>获取此 Actor 所属的 NPCController。</summary>
        public NPCController Controller => npcController != null
            ? npcController
            : throw new InvalidOperationException($"NPCActor '{name}' 尚未绑定 NPCController。");

        #endregion

        #region Unity 生命周期与运动转发

        /// <summary>解析 NPC 的共用 Ability 依赖与同节点 Controller。</summary>
        private void Awake()
        {
            EnsureAbilityDependencies();
            if (npcController == null)
                npcController = GetComponent<NPCController>();
            if (npcController == null)
                throw new InvalidOperationException($"NPCActor '{name}' 缺少同节点 NPCController。");
        }

        // Animator 运动转发
        /// <summary>把 Animator 根运动阶段交给 NPCController 与 MotionDriver 统一结算。</summary>
        private void OnAnimatorMove()
        {
            if (npcController == null || !npcController.IsInitialized)
                return;
            npcController.ProcessAnimatorMotion(animator.deltaPosition, animator.deltaRotation);
        }

        #endregion
    }
}
