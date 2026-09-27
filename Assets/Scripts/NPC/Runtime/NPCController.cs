using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.Character.Animation;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.NPC
{
    /// <summary>独立驱动 NPC 的 ASC、Boss 移动状态与 CharacterController 位移结算。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(NPCActor))]
    [InfoBox("依赖同节点 CharacterController 与 NPCActor；NPCActor 提供 ASC、Animator、SkillRuntimeHost 和挂点，NPCConfig 提供初始 AttributeSet、技能和 Idle/Move 动画。")]
    public sealed class NPCController : MonoBehaviour
    {
        #region 配置与依赖字段

        [SerializeField, Required] private NPCConfig config;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private NPCActor actor;
        [SerializeField] private MotionDriver motionDriver = new();
        [SerializeField] private BossLocomotionStateMachine locomotion = new();

        #endregion

        #region 运行时依赖

        private GameplayAbilitySystemComponent abilitySystemComponent;
        private NPCActionArbiter actionArbiter;
        private bool initialized;
        private int lastAnimatorMoveFrame = -1;

        #endregion

        #region 查询属性

        /// <summary>获取 Controller 所属 NPC。</summary>
        public NPCActor Actor => actor;

        /// <summary>获取该 NPC 初始化时使用的 NPCConfig。</summary>
        public NPCConfig Config => config;

        /// <summary>获取驱动所有 NPC 能力和移动状态的 MotionDriver。</summary>
        public IMotionDriver MotionDriver => motionDriver;

        /// <summary>获取所属 NPC 的动画播放器，供状态机播放基础移动动画。</summary>
        public IAnimationPlayer AnimationPlayer => actor.AnimationPlayer;

        /// <summary>获取当前 NPC 唯一的 FullBody Action 注册器。</summary>
        public IFullBodyActionArbiter FullBodyActionArbiter => actionArbiter ??
            throw new InvalidOperationException($"NPCController '{name}' 尚未创建 Action Arbiter。");

        /// <summary>获取 Boss Idle/Move 状态机。</summary>
        public BossLocomotionStateMachine Locomotion => locomotion;

        /// <summary>获取该 NPC 是否被 FullBody Ability 占据。</summary>
        public bool IsFullBodyActionOccupied => actionArbiter != null && actionArbiter.IsOccupied;

        /// <summary>获取 NPC 运行时是否已完成属性、技能和状态机初始化。</summary>
        public bool IsInitialized => initialized;

        #endregion

        #region Unity 生命周期与初始化

        // Unity 生命周期：NPC Controller 先建立运行依赖，再在 Start 导入资产配置。
        /// <summary>解析同节点角色与运动依赖，并先建立 FullBody Action 所有者。</summary>
        private void Awake()
        {
            if (characterController == null)
                characterController = GetComponent<CharacterController>();
            if (actor == null)
                actor = GetComponent<NPCActor>();
            if (characterController == null || actor == null)
                throw new InvalidOperationException(
                    $"NPCController '{name}' 必须与 CharacterController 和 NPCActor 挂在同一 GameObject。 ");

            motionDriver.Initialize(characterController);
            actionArbiter = new NPCActionArbiter(this);
            abilitySystemComponent = actor.AbilitySystemComponent;
            motionDriver.SetActiveOwner(actor, abilitySystemComponent);
            motionDriver.Resume();
        }

        /// <summary>导入 NPC 属性、授予配置技能并进入 Idle 动画状态。</summary>
        private void Start() => InitializeRuntime();

        /// <summary>推进 NPC Ability、状态机及 Update 位移结算。</summary>
        private void Update()
        {
            if (!initialized)
                return;

            try
            {
                abilitySystemComponent.Tick(Time.deltaTime);
                locomotion.Tick();
                motionDriver.ResolveUpdateMotion();
            }
            catch
            {
                motionDriver.ClearTransientRequests();
                throw;
            }
        }

        /// <summary>推进 NPC Ability 物理阶段并统一结算物理位移。</summary>
        private void FixedUpdate()
        {
            if (!initialized)
                return;

            try
            {
                abilitySystemComponent.FixedTick(Time.fixedDeltaTime);
                locomotion.FixedTick();
                motionDriver.ResolveFixedMotion();
            }
            catch
            {
                motionDriver.ClearTransientRequests();
                throw;
            }
        }

        /// <summary>推进 NPC Ability 与 Boss 状态机的延迟阶段。</summary>
        private void LateUpdate()
        {
            if (!initialized)
                return;
            abilitySystemComponent.LateTick(Time.deltaTime);
            locomotion.LateTick();
        }

        /// <summary>释放动作占据与仍属于 NPC 的运动控制权。</summary>
        private void OnDestroy()
        {
            locomotion?.Deactivate();
            actionArbiter?.Dispose();
            if (actor != null)
                motionDriver.ReleaseAll(actor);
            motionDriver.Suspend();
            Debug.Log($"[NPCController] NPC '{name}' 已释放控制器运行时资源。", this);
        }

        // 配置初始化
        /// <summary>校验 NPCConfig 并按固定顺序初始化 ASC、Granted Ability 与 Boss Locomotion。</summary>
        private void InitializeRuntime()
        {
            if (initialized)
                return;
            if (config == null)
                throw new InvalidOperationException($"NPCController '{name}' 未配置 NPCConfig。");

            config.Validate();
            if (!ReferenceEquals(abilitySystemComponent.Owner, actor))
                throw new InvalidOperationException(
                    $"NPCController '{name}' 的 ASC Owner 不是同节点 NPCActor。");
            if (!abilitySystemComponent.IsInitialized)
                abilitySystemComponent.Initialize(config.InitialAttributeSets);

            GrantConfiguredAbilities();
            locomotion.Initialize(this, config.IdleTransition, config.MoveTransition);
            initialized = true;
            Debug.Log(
                $"[NPCController] NPC '{name}' 初始化完成，AttributeSets={config.InitialAttributeSets.Count}，" +
                $"Abilities={config.GrantedAbilities.Count}，State={locomotion.CurrentState}。",
                this);
        }

        /// <summary>为 NPC 授予 NPCConfig 中尚未存在的 Ability Spec。</summary>
        private void GrantConfiguredAbilities()
        {
            IReadOnlyList<GameplayAbilityData> grantedAbilities = config.GrantedAbilities;
            for (int abilityIndex = 0; abilityIndex < grantedAbilities.Count; abilityIndex++)
            {
                GameplayAbilityData ability = grantedAbilities[abilityIndex];
                bool alreadyGranted = false;
                IReadOnlyList<GameplayAbilitySpec> currentSpecs = abilitySystemComponent.GrantedAbilities;
                for (int specIndex = 0; specIndex < currentSpecs.Count; specIndex++)
                {
                    if (!ReferenceEquals(currentSpecs[specIndex].Data, ability))
                        continue;
                    alreadyGranted = true;
                    break;
                }

                if (alreadyGranted)
                    continue;

                GameplayAbilityHandle handle = abilitySystemComponent.GiveAbility(ability, 1);
                if (!handle.IsValid)
                    throw new InvalidOperationException(
                        $"NPCController '{name}' 无法授予配置 Ability '{ability.name}'。");
                Debug.Log(
                    $"[NPCController] NPC '{name}' 授予 Ability '{ability.Name}'，Handle={handle}。",
                    this);
            }
        }

        #endregion

        #region 测试与技能入口

        /// <summary>请求进入 Idle 或 Move 状态；被 FullBody 技能占据时拒绝切换。</summary>
        /// <param name="stateId">测试或后续 AI 选择的目标状态。</param>
        /// <returns>状态成功切换时返回 true。</returns>
        public bool TrySetLocomotionState(BossLocomotionStateId stateId)
        {
            if (!initialized)
                throw new InvalidOperationException($"NPCController '{name}' 尚未初始化。");
            if (IsFullBodyActionOccupied)
            {
                Debug.Log(
                    $"[NPCController] NPC '{name}' 仍被 FullBody Ability 占据，拒绝切换到 {stateId}。",
                    this);
                return false;
            }

            // Move Mixer 的采样位置要在进入 Transition 前写入，避免首帧先播放 Idle 混合点。
            if (stateId == BossLocomotionStateId.Move)
                actor.AnimationPlayer.SetFloatParameter(config.MoveParameterX, 1f);

            bool changed = locomotion.TryChangeState(stateId);
            if (changed)
                Debug.Log($"[NPCController] NPC '{name}' 状态切换为 {stateId}。", this);
            return changed;
        }

        /// <summary>激活 NPCConfig 已授予的指定 Ability。</summary>
        /// <param name="ability">NPCConfig 中的技能资产。</param>
        /// <param name="runtime">成功时返回新建 Ability Runtime。</param>
        /// <returns>技能成功激活时返回 true。</returns>
        public bool TryActivateAbility(GameplayAbilityData ability, out GameplayAbilityRuntime runtime)
        {
            if (!initialized)
                throw new InvalidOperationException($"NPCController '{name}' 尚未初始化。");
            if (ability == null)
                throw new ArgumentNullException(nameof(ability));
            if (IsFullBodyActionOccupied)
            {
                runtime = null;
                Debug.Log(
                    $"[NPCController] NPC '{name}' 已被 FullBody Ability 占据，拒绝激活 '{ability.Name}'。",
                    this);
                return false;
            }

            IReadOnlyList<GameplayAbilitySpec> specs = abilitySystemComponent.GrantedAbilities;
            for (int index = 0; index < specs.Count; index++)
            {
                GameplayAbilitySpec spec = specs[index];
                if (!ReferenceEquals(spec.Data, ability))
                    continue;

                bool activated = abilitySystemComponent.TryActivateAbility(spec.Handle, out runtime);
                Debug.Log(
                    $"[NPCController] NPC '{name}' 激活 Ability '{ability.Name}'，成功={activated}。",
                    this);
                return activated;
            }

            runtime = null;
            Debug.LogWarning(
                $"[NPCController] NPC '{name}' 未授予 Ability '{ability.Name}'，无法激活。",
                this);
            return false;
        }

        /// <summary>取消该 NPC 当前所有 Active Ability，供场景 OdinTester 验证清理。</summary>
        public void CancelActiveAbilities()
        {
            IReadOnlyList<GameplayAbilityRuntime> activeAbilities = abilitySystemComponent.ActiveAbilities;
            int cancelledCount = 0;
            for (int index = activeAbilities.Count - 1; index >= 0; index--)
            {
                if (abilitySystemComponent.TryCancelAbility(activeAbilities[index]))
                    cancelledCount++;
            }
            Debug.Log(
                $"[NPCController] NPC '{name}' 批量取消 Active Ability，成功数量={cancelledCount}。",
                this);
        }

        /// <summary>接收 Animator 阶段增量并按 GAS 提交结果执行一次运动结算。</summary>
        /// <param name="deltaPosition">Animator 本次根位移。</param>
        /// <param name="deltaRotation">Animator 本次根旋转。</param>
        internal void ProcessAnimatorMotion(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            if (!initialized || !isActiveAndEnabled || lastAnimatorMoveFrame == Time.frameCount)
                return;

            try
            {
                abilitySystemComponent.UpdateAnimationMove(deltaPosition, deltaRotation);
                locomotion.AnimatorMove();
                motionDriver.ResolveAnimatorMotion();
                lastAnimatorMoveFrame = Time.frameCount;
            }
            catch
            {
                motionDriver.ClearTransientRequests();
                throw;
            }
        }

        #endregion
    }
}
