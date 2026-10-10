using System;
using System.Collections.Generic;
using RPG.SkillSystem;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;

namespace RPG.NPC
{
    /// <summary>管理 Boss 普攻段位、Phase 续接机会与第三段结束后的休息。</summary>
    public sealed class NPCCombatSystem : IDisposable
    {
        #region 依赖字段

        // NPC Controller 提供 ASC 事务、目标条件与移动状态查询；Skill Host 提供逐执行的 Phase 变化。
        private NPCController owner;
        private ISkillRuntimeHost skillRuntimeHost;

        #endregion

        #region 配置快照

        // Handle 与 SkillConfig 按作者配置的 Attack1、Attack2、Attack3 顺序一一对应。
        private GameplayAbilityHandle[] normalAttackHandles = Array.Empty<GameplayAbilityHandle>();
        private SkillConfig[] normalAttackSkillConfigs = Array.Empty<SkillConfig>();
        private float comboRetentionDuration;
        private float attackDistance;
        private float attackInterval;

        #endregion

        #region 连段与窗口状态

        // currentRuntime 是 NPCCombatSystem 当前跟踪的普攻；窗口快照用 Host ExecutionId 隔离各次时间轴执行。
        private GameplayAbilityRuntime currentRuntime;
        private int currentAttackIndex;
        private int nextAttackIndex;
        private float comboRetentionRemaining;
        private float recoveryRemaining;
        private bool recoveryStarted;
        private bool attackCompletionPending;
        private bool cancelledRuntimeHandled;
        private Transform attackTarget;
        private ulong observedExecutionId;
        private bool hasObservedExecution;
        private bool observedAbilityWindowOpen;
        private bool handoffOpportunityAvailable;
        private int lastActivationFrame = -1;
        private string lastWindowDecision = "尚未进入普攻取消窗口。";

        // 激活 SkillConfig 时，Host 会在 TryActivateNormalAttack 返回前同步发布入口 Phase。
        private bool activatingNormalAttack;
        private int activatingAttackIndex = -1;
        private bool hasPendingActivationPhaseChange;
        private SkillActionPhaseChangedEventArgs pendingActivationPhaseChange;

        /// <summary>获取最近一次成功启动的普通攻击 Runtime。</summary>
        public GameplayAbilityRuntime CurrentRuntime => currentRuntime;

        /// <summary>获取当前 Runtime 对应的 Attack1–3 零基序号。</summary>
        public int CurrentAttackIndex => currentAttackIndex;

        /// <summary>获取下一次成功启动时将使用的普通攻击序号，范围为 0–2。</summary>
        public int NextAttackIndex => nextAttackIndex;

        /// <summary>获取第三段结束后尚未完成的缩放时间休息时长。</summary>
        public float RecoveryRemaining => recoveryRemaining;

        /// <summary>获取是否有一个自然结束结果正等待行为树消费。</summary>
        public bool HasPendingAttackCompletion => attackCompletionPending;

        /// <summary>获取最近一次可打断窗口入口的检测结果，供 Odin 手动观察。</summary>
        public string LastWindowDecision => lastWindowDecision;

        /// <summary>获取当前攻击节点提供的距离策略快照。</summary>
        public float AttackDistanceSnapshot => attackDistance;

        /// <summary>获取第三段自然结束后由本系统持有的休息时长快照。</summary>
        public float AttackRestDurationSnapshot => attackInterval;

        /// <summary>判断当前 Runtime 已终止且第三段休息时间已结束。</summary>
        public bool IsReadyForNextAttack =>
            (currentRuntime == null || currentRuntime.State != GameplayAbilityRuntimeState.Active) &&
            recoveryRemaining <= 0f &&
            (currentRuntime == null || currentRuntime.State != GameplayAbilityRuntimeState.Ended || recoveryStarted);

        #endregion

        #region 初始化与逐帧状态

        /// <summary>解析三段配置对应的 ASC Spec 与 SkillConfig，并订阅 Phase 变化。</summary>
        /// <param name="controller">持有 NPC ASC、Skill Host 和目标条件的控制器。</param>
        /// <param name="normalAttacks">按 Attack1 至 Attack3 排列的技能资产。</param>
        /// <param name="comboRetention">下一段普通攻击的缩放时间缓存。</param>
        internal void Initialize(
            NPCController controller,
            IReadOnlyList<GameplayAbilityData> normalAttacks,
            float comboRetention)
        {
            owner = controller ?? throw new ArgumentNullException(nameof(controller));
            if (normalAttacks == null || normalAttacks.Count != 3)
                throw new InvalidOperationException("NPCCombatSystem 必须接收有序的三段普通攻击配置。");

            normalAttackHandles = new GameplayAbilityHandle[normalAttacks.Count];
            normalAttackSkillConfigs = new SkillConfig[normalAttacks.Count];
            for (int index = 0; index < normalAttacks.Count; index++)
            {
                GameplayAbilityData ability = normalAttacks[index];
                normalAttackHandles[index] = owner.FindGrantedAbilityHandle(ability);
                if (ability is not AsynchronousGameplayAbilityData asynchronousAbility)
                    throw new InvalidOperationException(
                        $"NPC 普攻 '{ability.name}' 必须使用异步 Ability 才能订阅 SkillRuntimeHost Phase。");

                normalAttackSkillConfigs[index] = FindSkillConfig(asynchronousAbility.RootTask);
                if (normalAttackSkillConfigs[index] == null)
                    throw new InvalidOperationException(
                        $"NPC 普攻 '{ability.name}' 的 Task 配置中没有 PlaySkillConfigGameplayAbilityTaskConfig。");
            }

            comboRetentionDuration = comboRetention;
            skillRuntimeHost = owner.Actor.SkillRuntimeHost;
            skillRuntimeHost.ActionPhaseChanged += OnActionPhaseChanged;
            Debug.Log(
                $"[NPCCombatSystem] Boss '{owner.name}' 已绑定普攻 Phase 监听，" +
                $"连段缓存={comboRetentionDuration:F2}s。",
                owner);
        }

        /// <summary>在 NPCController 普通 Update 阶段推进自然结束状态与缩放计时。</summary>
        /// <param name="deltaTime">缩放后的普通帧时间。</param>
        internal void AdvanceFrame(float deltaTime)
        {
            if (owner == null)
                return;

            HandleCancelledCurrentRuntime();

            if (currentRuntime != null && currentRuntime.State == GameplayAbilityRuntimeState.Active)
            {
                AdvanceComboRetention(deltaTime);
                return;
            }

            if (currentRuntime != null && currentRuntime.State == GameplayAbilityRuntimeState.Ended &&
                !recoveryStarted)
            {
                recoveryStarted = true;
                attackCompletionPending = true;
                recoveryRemaining = currentAttackIndex == normalAttackHandles.Length - 1
                    ? attackInterval
                    : 0f;
                Debug.Log(
                    $"[NPCCombatSystem] Boss '{owner.name}' 普攻 Attack{currentAttackIndex + 1} 自然结束，" +
                    $"第三段休息剩余={recoveryRemaining:F2}s。",
                    owner);
                return;
            }

            AdvanceComboRetention(deltaTime);
            recoveryRemaining = Math.Max(0f, recoveryRemaining - deltaTime);
        }

        /// <summary>推进成功激活后缓存的下一段；超时回到 Attack1。</summary>
        /// <param name="deltaTime">本次缩放帧时间。</param>
        private void AdvanceComboRetention(float deltaTime)
        {
            comboRetentionRemaining = Math.Max(0f, comboRetentionRemaining - deltaTime);
            if (comboRetentionRemaining > 0f || nextAttackIndex == 0)
                return;

            nextAttackIndex = 0;
            Debug.Log($"[NPCCombatSystem] Boss '{owner.name}' 普攻缓存过期，连段回到 Attack1。", owner);
        }

        #endregion

        #region 攻击启动与 Phase 续接

        /// <summary>更新行为树共享黑板提供的当前玩家目标，用于下一次窗口入口判定。</summary>
        /// <param name="target">当前有效玩家目标；无有效目标时传入 null。</param>
        public void SetAttackTarget(Transform target)
        {
            if (attackTarget == target)
                return;

            attackTarget = target;
            Debug.Log(
                $"[NPCCombatSystem] Boss '{owner?.name ?? "<released>"}' 普攻判定目标更新为 " +
                $"'{(attackTarget != null ? attackTarget.name : "<none>")}'。",
                owner);
        }

        /// <summary>冻结当前行为树攻击节点的范围判定与第三段休息策略。</summary>
        /// <param name="requestedAttackDistance">导航、攻击与 Phase 续接共用的水平距离。</param>
        /// <param name="requestedAttackRestDuration">第三段自然结束后的缩放时间休息时长。</param>
        public void ConfigureAttackDecision(float requestedAttackDistance, float requestedAttackRestDuration)
        {
            attackDistance = requestedAttackDistance;
            attackInterval = requestedAttackRestDuration;
            Debug.Log(
                $"[NPCCombatSystem] Boss '{owner?.name ?? "<released>"}' 应用行为树攻击策略，" +
                $"AttackDistance={attackDistance:F2}m，Attack3Rest={attackInterval:F2}s。",
                owner);
        }

        /// <summary>从当前缓存序号启动下一段普通攻击；只有成功激活才推进序号。</summary>
        /// <param name="runtime">成功时返回新段 Runtime。</param>
        /// <returns>普通攻击实际激活时返回 true。</returns>
        public bool TryStartNext(out GameplayAbilityRuntime runtime)
        {
            // BT 可能在 ASC Cancel 与下一次 NPC Update 之间重启；启动前先回滚被取消段位。
            HandleCancelledCurrentRuntime();
            if (!IsReadyForNextAttack)
            {
                runtime = null;
                return false;
            }
            return TryActivateCurrentIndex(null, out runtime);
        }

        /// <summary>消费 Phase 入口时缓存的单次交接机会，并通过 ASC 事务尝试启动下一段。</summary>
        /// <param name="runtime">交接成功时返回新段 Runtime。</param>
        /// <returns>ASC 接受新段并完成交接时返回 true。</returns>
        public bool TryConsumeHandoffOpportunity(out GameplayAbilityRuntime runtime)
        {
            runtime = null;
            if (!handoffOpportunityAvailable)
                return false;

            // 新段入口 Phase 会在激活调用中同步发布；留到下一帧消费，避免同帧连续交接。
            if (lastActivationFrame == Time.frameCount)
                return false;

            handoffOpportunityAvailable = false;
            if (currentRuntime == null || currentRuntime.State != GameplayAbilityRuntimeState.Active ||
                currentAttackIndex >= normalAttackHandles.Length - 1 ||
                !currentRuntime.AbilityTags.HasTag(GameplayTags.Tag_Skill_NormalAttack) ||
                !currentRuntime.RuntimeTags.HasTag(GameplayTags.Tag_Skill_Window_CancelBy_Ability))
            {
                lastWindowDecision = $"Attack{currentAttackIndex + 1} 窗口机会消费时已失效。";
                Debug.Log(
                    $"[NPCCombatSystem] Boss '{owner.name}' 普攻续接机会失效，" +
                    $"Attack={currentAttackIndex + 1}，ExecutionId={observedExecutionId}。",
                    owner);
                return false;
            }

            if (!TryActivateCurrentIndex(currentRuntime, out runtime))
            {
                lastWindowDecision = $"Attack{currentAttackIndex + 1} 窗口续接激活失败，本窗口不重试。";
                Debug.Log(
                    $"[NPCCombatSystem] Boss '{owner.name}' 普攻取消窗口内 ASC 激活下一段失败，" +
                    $"本窗口不再重试，当前段={currentAttackIndex + 1}。",
                    owner);
                return false;
            }

            return true;
        }

        /// <summary>只消费一次自然结束结果；第三段必须先完成配置的休息时长。</summary>
        /// <returns>自然结束信号存在且休息已完成时返回 true。</returns>
        public bool ConsumeAttackCompletion()
        {
            if (!attackCompletionPending || !IsReadyForNextAttack)
                return false;

            attackCompletionPending = false;
            Debug.Log(
                $"[NPCCombatSystem] Boss '{owner.name}' 已消费 Attack{currentAttackIndex + 1} 自然结束信号。",
                owner);
            return true;
        }

        /// <summary>调用 Controller 的 ASC 事务入口，并在新 Runtime 成功后提交连段状态。</summary>
        /// <param name="previousRuntime">实时交接的上一段；普通空闲启动时为空。</param>
        /// <param name="runtime">成功创建的新 Runtime。</param>
        /// <returns>新段激活成功时返回 true。</returns>
        private bool TryActivateCurrentIndex(
            GameplayAbilityRuntime previousRuntime,
            out GameplayAbilityRuntime runtime)
        {
            int activationIndex = nextAttackIndex;
            GameplayAbilityHandle handle = normalAttackHandles[activationIndex];
            hasPendingActivationPhaseChange = false;
            activatingAttackIndex = activationIndex;
            activatingNormalAttack = true;
            bool activated;
            try
            {
                activated = owner.TryActivateNormalAttack(handle, previousRuntime, out runtime);
            }
            finally
            {
                activatingNormalAttack = false;
                activatingAttackIndex = -1;
            }

            if (!activated)
            {
                hasPendingActivationPhaseChange = false;
                runtime = null;
                return false;
            }

            currentRuntime = runtime;
            currentAttackIndex = activationIndex;
            nextAttackIndex = (activationIndex + 1) % normalAttackHandles.Length;
            comboRetentionRemaining = comboRetentionDuration;
            recoveryRemaining = 0f;
            recoveryStarted = false;
            attackCompletionPending = false;
            cancelledRuntimeHandled = false;
            handoffOpportunityAvailable = false;
            lastActivationFrame = Time.frameCount;

            // 入口帧事件在 TryActivateNormalAttack 内同步发布；此处先确立 Runtime 所有权再处理快照。
            if (hasPendingActivationPhaseChange)
            {
                SkillActionPhaseChangedEventArgs phaseChange = pendingActivationPhaseChange;
                hasPendingActivationPhaseChange = false;
                ApplyPhaseChange(phaseChange, activationIndex);
            }

            Debug.Log(
                $"[NPCCombatSystem] Boss '{owner.name}' 成功启动普通攻击 Attack{currentAttackIndex + 1}，" +
                $"下一段缓存=Attack{nextAttackIndex + 1}。",
                owner);
            return true;
        }

        /// <summary>把被外部取消的当前段位恢复为下次缓存选择，并且不刷新 Combo 时间。</summary>
        private void HandleCancelledCurrentRuntime()
        {
            if (currentRuntime == null || currentRuntime.State != GameplayAbilityRuntimeState.Cancelled ||
                cancelledRuntimeHandled)
                return;

            nextAttackIndex = currentAttackIndex;
            attackCompletionPending = false;
            recoveryRemaining = 0f;
            recoveryStarted = false;
            handoffOpportunityAvailable = false;
            cancelledRuntimeHandled = true;
            Debug.Log(
                $"[NPCCombatSystem] Boss '{owner.name}' Attack{currentAttackIndex + 1} 被外部取消，" +
                $"缓存段位回到当前段，Combo 剩余={comboRetentionRemaining:F2}s。",
                owner);
        }

        /// <summary>接收 SkillRuntimeHost 事件，并隔离普攻、瞬移与其他技能的时间轴。</summary>
        /// <param name="args">Host 发布的动作阶段与 RuntimeTag 快照。</param>
        private void OnActionPhaseChanged(SkillActionPhaseChangedEventArgs args)
        {
            if (activatingNormalAttack)
            {
                if (activatingAttackIndex >= 0 &&
                    ReferenceEquals(normalAttackSkillConfigs[activatingAttackIndex], args.Config))
                {
                    pendingActivationPhaseChange = args;
                    hasPendingActivationPhaseChange = true;
                }
                return;
            }

            if (currentRuntime == null || currentRuntime.State != GameplayAbilityRuntimeState.Active ||
                !ReferenceEquals(normalAttackSkillConfigs[currentAttackIndex], args.Config))
                return;

            ApplyPhaseChange(args, currentAttackIndex);
        }

        /// <summary>按 ExecutionId 追踪取消窗口边界，并只在关闭到开启时记录一次判定。</summary>
        /// <param name="args">去重后的完整动作阶段快照。</param>
        /// <param name="attackIndex">当前事件所属的普攻段位。</param>
        private void ApplyPhaseChange(SkillActionPhaseChangedEventArgs args, int attackIndex)
        {
            if (args.Frame < 0)
            {
                if (hasObservedExecution && observedExecutionId == args.ExecutionId)
                {
                    observedAbilityWindowOpen = false;
                    handoffOpportunityAvailable = false;
                    hasObservedExecution = false;
                }
                return;
            }

            bool windowOpen = ContainsTag(args.RuntimeTags, GameplayTags.Tag_Skill_Window_CancelBy_Ability);
            if (!hasObservedExecution || observedExecutionId != args.ExecutionId)
            {
                observedExecutionId = args.ExecutionId;
                hasObservedExecution = true;
                observedAbilityWindowOpen = windowOpen;
                handoffOpportunityAvailable = false;
                if (windowOpen)
                    RecordWindowEntry(attackIndex, args.ExecutionId, args.Frame);
                return;
            }

            if (!observedAbilityWindowOpen && windowOpen)
                RecordWindowEntry(attackIndex, args.ExecutionId, args.Frame);
            else if (observedAbilityWindowOpen && !windowOpen)
                handoffOpportunityAvailable = false;

            observedAbilityWindowOpen = windowOpen;
        }

        /// <summary>在窗口进入边界快照目标条件；第三段只记录窗口信息，不创建续接机会。</summary>
        /// <param name="attackIndex">刚进入窗口的普攻段位。</param>
        /// <param name="executionId">产生窗口边界的 SkillRuntimeHost 执行标识。</param>
        /// <param name="frame">窗口实际生效的逻辑帧。</param>
        private void RecordWindowEntry(int attackIndex, ulong executionId, int frame)
        {
            if (attackIndex >= normalAttackHandles.Length - 1)
            {
                handoffOpportunityAvailable = false;
                lastWindowDecision = $"Attack{attackIndex + 1} 窗口已开启；第三段不续接。";
                Debug.Log(
                    $"[NPCCombatSystem] Boss '{owner.name}' Attack3 取消窗口开启，连段终段不尝试交接，" +
                    $"ExecutionId={executionId}。",
                    owner);
                return;
            }

            bool canContinue = attackTarget != null && owner.IsReadyToAttack(attackTarget, attackDistance);
            handoffOpportunityAvailable = canContinue;
            lastWindowDecision = canContinue
                ? $"Attack{attackIndex + 1} 窗口开启：目标当前可攻击，等待消费一次续接机会。"
                : $"Attack{attackIndex + 1} 窗口开启：目标当前不可攻击，本窗口不续接。";
            Debug.Log(
                $"[NPCCombatSystem] Boss '{owner.name}' 普攻取消窗口进入，Attack={attackIndex + 1}，" +
                $"ExecutionId={executionId}，Frame={frame}，" +
                $"TargetReady={canContinue}。",
                owner);
        }

        /// <summary>按事件中的精确 GameplayTag 检查当前 Phase 是否声明了普通技能取消窗口。</summary>
        /// <param name="tags">Host 发布的 Phase RuntimeTags。</param>
        /// <param name="targetTag">需要检查的窗口标签。</param>
        /// <returns>RuntimeTags 中存在指定标签时返回 true。</returns>
        private static bool ContainsTag(IReadOnlyList<GameplayTag> tags, GameplayTag targetTag)
        {
            for (int index = 0; index < tags.Count; index++)
                if (tags[index] == targetTag)
                    return true;
            return false;
        }

        /// <summary>从异步 Ability 的 Task 配置树中查找首个 PlaySkillConfig 使用的时间轴。</summary>
        /// <param name="taskConfig">待查找的 Root 或 Sequence 子配置。</param>
        /// <returns>找到时返回 SkillConfig；不包含播放 Task 时返回 null。</returns>
        private static SkillConfig FindSkillConfig(GameplayAbilityTaskConfig taskConfig)
        {
            if (taskConfig is PlaySkillConfigGameplayAbilityTaskConfig skillTask)
                return skillTask.SkillConfig;
            if (taskConfig is not SequenceGameplayAbilityTaskConfig sequence)
                return null;

            IReadOnlyList<GameplayAbilityTaskConfig> childConfigs = sequence.Children;
            for (int index = 0; index < childConfigs.Count; index++)
            {
                SkillConfig skillConfig = FindSkillConfig(childConfigs[index]);
                if (skillConfig != null)
                    return skillConfig;
            }
            return null;
        }

        #endregion

        #region 资源释放

        /// <summary>解除 SkillRuntimeHost 事件订阅并释放目标与执行快照。</summary>
        public void Dispose()
        {
            if (skillRuntimeHost != null)
            {
                skillRuntimeHost.ActionPhaseChanged -= OnActionPhaseChanged;
                Debug.Log($"[NPCCombatSystem] Boss '{owner?.name ?? "<released>"}' 已解除普攻 Phase 监听。", owner);
            }

            skillRuntimeHost = null;
            attackTarget = null;
            hasPendingActivationPhaseChange = false;
            handoffOpportunityAvailable = false;
            hasObservedExecution = false;
            owner = null;
        }

        #endregion
    }
}
