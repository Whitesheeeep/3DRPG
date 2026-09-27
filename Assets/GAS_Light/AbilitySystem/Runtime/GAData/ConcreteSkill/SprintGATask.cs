using System;
using System.Collections.Generic;
using Animancer;
using RPG.Character;
using RPG.Character.Animation;
using RPG.Character.State;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.Generated;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    #region 方向动画配置类型

    /// <summary>QuickShift 动画可选择的八个角色局部方向。</summary>
    public enum SprintAnimationDirection
    {
        /// <summary>角色正前方。</summary>
        Forward,
        /// <summary>角色正后方。</summary>
        Backward,
        /// <summary>角色左侧。</summary>
        Left,
        /// <summary>角色右侧。</summary>
        Right,
        /// <summary>角色前左方。</summary>
        ForwardLeft,
        /// <summary>角色前右方。</summary>
        ForwardRight,
        /// <summary>角色后左方。</summary>
        BackwardLeft,
        /// <summary>角色后右方。</summary>
        BackwardRight
    }

    /// <summary>把一个八方向动画方向绑定到独立的 Animancer TransitionAsset。</summary>
    [Serializable]
    public sealed class SprintDirectionTransitionBinding
    {
        #region 配置字段与属性

        [SerializeField, LabelText("动画方向")]
        private SprintAnimationDirection direction;
        [SerializeField, Required, LabelText("方向动画 Transition")]
        private TransitionAsset transition;

        /// <summary>获取该动画绑定对应的角色局部方向。</summary>
        public SprintAnimationDirection Direction => direction;

        /// <summary>获取该方向使用的单段动画 Transition。</summary>
        public TransitionAsset Transition => transition;

        #endregion
    }

    #endregion

    /// <summary>按冻结世界方向播放最近的八方向动画，并在命名事件窗口内提交定距位移。</summary>
    public sealed class SprintGATask : GameplayAbilityTask
    {
        #region 配置快照与依赖字段

        // 每次激活固定方向到动画的映射，避免运行中编辑 Ability 配置改变当前冲刺。
        private readonly float targetDistance;
        private readonly SprintAnimationDirection[] transitionDirections;
        private readonly TransitionAsset[] directionTransitions;
        private readonly StringAsset movementStartEvent;
        private readonly StringAsset movementStopEvent;
        private readonly float fadeOutDuration;

        // 依赖字段：MotionDriver 负责碰撞结算；Animancer State 提供进度与事件时序。
        private CharacterActor character;
        private PlayerStateBlackboard stateBlackboard;
        private IMotionDriver motionDriver;
        private MotionControlHandle motionHandle;
        private AnimancerState animationState;
        private AnimancerEvent.Sequence animationEvents;
        private Vector3 worldDirection;
        private float lastAnimationProgress;
        private float movementStartNormalizedTime;
        private float movementStopNormalizedTime;
        private bool movementEnabled;
        private bool movementStopPending;

        #endregion

        #region 构造与生命周期

        /// <summary>创建持有本次冲刺配置快照的运行实例。</summary>
        /// <param name="runtime">所属异步 Ability Runtime。</param>
        /// <param name="targetDistance">位移事件窗口内计划移动的水平世界距离。</param>
        /// <param name="directions">与 Transition 数组逐项对应的八方向枚举快照。</param>
        /// <param name="transitions">与方向数组逐项对应的动画 Transition 快照。</param>
        /// <param name="startEvent">开启程序位移的 Animancer 事件名。</param>
        /// <param name="stopEvent">关闭程序位移的 Animancer 事件名。</param>
        /// <param name="fadeOutDuration">释放 Action 动画层时的淡出时长。</param>
        public SprintGATask(
            AsynchronousGameplayAbilityRuntime runtime,
            float targetDistance,
            SprintAnimationDirection[] directions,
            TransitionAsset[] transitions,
            StringAsset startEvent,
            StringAsset stopEvent,
            float fadeOutDuration)
            : base(runtime)
        {
            this.targetDistance = targetDistance;
            transitionDirections = directions;
            directionTransitions = transitions;
            movementStartEvent = startEvent;
            movementStopEvent = stopEvent;
            this.fadeOutDuration = fadeOutDuration;
        }

        /// <summary>解析方向动画和事件窗口，取得运动控制权并播放对应动画。</summary>
        protected override void OnStart()
        {
            base.OnStart();
            character = GARuntime.SourceOwner as CharacterActor ??
                throw new InvalidOperationException(
                    "[SprintGATask] QuickShift 只能由 CharacterActor 作为 Ability Owner 激活。");
            stateBlackboard = character.StateBlackboard ??
                throw new InvalidOperationException(
                    $"[SprintGATask] 角色 '{character.name}' 尚未注入 PlayerStateBlackboard。");
            Transform root = GARuntime.SourceOwner.RootTransform;
            worldDirection = ResolveWorldDirection(root);

            // 动画使用角色局部八方向选择，位移仍沿激活时冻结的原始世界方向。
            Vector3 localDirection = root.InverseTransformDirection(worldDirection);
            localDirection = Vector3.ProjectOnPlane(localDirection, Vector3.up).normalized;
            SprintAnimationDirection selectedDirection =
                SprintGATaskConfig.GetClosestDirection(localDirection);
            TransitionAsset selectedTransition = ResolveTransition(selectedDirection);

            // Runtime 创建后若资源被改成非法事件配置，必须在取得控制权和播放前明确失败。
            string eventWindowError = SprintGATaskConfig.GetMovementWindowError(
                selectedTransition, movementStartEvent, movementStopEvent);
            if (eventWindowError != null)
            {
                string error =
                    $"[SprintGATask] 方向 {selectedDirection} 的 Transition '{selectedTransition.name}' " +
                    $"事件配置无效：{eventWindowError}";
                Debug.LogError(error, GARuntime.SourceOwner as UnityEngine.Object);
                throw new InvalidOperationException(error);
            }

            motionDriver = GARuntime.SourceOwner.MotionDriver ??
                throw new InvalidOperationException("[SprintGATask] 当前角色未绑定 MotionDriver。");
            // 水平与旋转控制权在动画开始前建立，防止同帧 Locomotion 改变冲刺轨迹。
            motionHandle = motionDriver.RequestControl(new MotionControlRequest(
                GARuntime.SourceOwner,
                MotionPriority.Skill,
                MotionChannels.Horizontal | MotionChannels.Rotation));

            IAnimationPlayer animationPlayer = GARuntime.SourceOwner.AnimationPlayer;
            animationState = animationPlayer.Play(AnimationLayerType.Action, selectedTransition);
            // Animancer 会复用 Transition 对应的 State；ref 序列为本次 Task 建立独立回调快照。
            animationState.Events(ref animationEvents);
            int startEventIndex = animationEvents.IndexOfRequired(movementStartEvent);
            int stopEventIndex = animationEvents.IndexOfRequired(movementStopEvent);
            movementStartNormalizedTime = animationEvents[startEventIndex].normalizedTime;
            movementStopNormalizedTime = animationEvents[stopEventIndex].normalizedTime;
            animationEvents.AddCallback(startEventIndex, OnMovementStart);
            animationEvents.AddCallback(stopEventIndex, OnMovementStop);
            animationEvents.OnEnd += OnAnimationEnd;
            lastAnimationProgress = Mathf.Clamp01(animationState.NormalizedTime);
            movementEnabled = false;
            movementStopPending = false;

            Debug.Log(
                $"[SprintGATask] 冲刺启动，方向={worldDirection}，动画方向={selectedDirection}，" +
                $"位移窗口={movementStartNormalizedTime:F3}..{movementStopNormalizedTime:F3}，" +
                $"目标距离={targetDistance:F2}m，角色={root.name}。",
                GARuntime.SourceOwner as UnityEngine.Object);
        }

        /// <summary>按最近一次 Animator 求值进度，在事件窗口内提交本帧程序化位移。</summary>
        /// <param name="deltaTime">当前普通更新阶段的秒数；距离由动画归一化进度决定。</param>
        protected override void OnTick(float deltaTime)
        {
            if (animationState == null || motionHandle == null)
                return;

            float animationProgress = Mathf.Clamp01(animationState.NormalizedTime);
            float previousProgress = lastAnimationProgress;
            lastAnimationProgress = animationProgress;

            // 即使窗口关闭也持续同步进度，确保稍后开启时不追补前摇位移。
            if (!movementEnabled && !movementStopPending)
                return;

            float overlapStart = Mathf.Max(previousProgress, movementStartNormalizedTime);
            float overlapEnd = Mathf.Min(animationProgress, movementStopNormalizedTime);
            float windowProgressDelta = Mathf.Max(0f, overlapEnd - overlapStart);
            if (windowProgressDelta > 0f)
            {
                // 将窗口内的动画进度线性映射到完整配置距离；碰撞由 MotionDriver 统一裁切。
                float normalizedWindowDelta = windowProgressDelta /
                    (movementStopNormalizedTime - movementStartNormalizedTime);
                Vector3 translation = worldDirection * (targetDistance * normalizedWindowDelta);
                motionDriver.SubmitUpdate(
                    motionHandle,
                    UpdateMotionSubmission.TranslationOnly(translation));
            }

            // Stop 回调后保留一个 Update 结算事件边界，再停止提交程序位移。
            if (movementStopPending)
                movementStopPending = false;
        }

        /// <summary>外部正常停止时淡出当前动作并释放运动控制权。</summary>
        protected override void OnStop() => ReleaseResources("Stop");

        /// <summary>Ability 被取消时淡出当前动作并释放运动控制权。</summary>
        protected override void OnCancel() => ReleaseResources("Cancel");

        /// <summary>动画自然结束时淡出当前动作并释放运动控制权。</summary>
        protected override void OnComplete() => ReleaseResources("Complete");

        #endregion

        #region 方向解析与事件处理

        /// <summary>优先使用一次性 SetByCaller 世界方向；缺省输入回退到角色水平前方。</summary>
        /// <param name="root">冲刺角色的稳定空间根。</param>
        /// <returns>单位化的世界水平冲刺方向。</returns>
        private Vector3 ResolveWorldDirection(Transform root)
        {
            bool hasX = GARuntime.TryGetSetByCaller(GameplayTags.Tag_Skill_QuickShift_DirX, out float x);
            bool hasZ = GARuntime.TryGetSetByCaller(GameplayTags.Tag_Skill_QuickShift_DirY, out float z);
            Vector3 direction = hasX && hasZ ? new Vector3(x, 0f, z) : Vector3.zero;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.ProjectOnPlane(root.forward, Vector3.up);
                if (direction.sqrMagnitude <= 0.0001f)
                {
                    string error =
                        $"[SprintGATask] 角色 '{root.name}' 的前向投影无效，无法建立水平冲刺方向。";
                    Debug.LogError(error, GARuntime.SourceOwner as UnityEngine.Object);
                    throw new InvalidOperationException(error);
                }
            }

            return direction.normalized;
        }

        /// <summary>查找配置给定方向的 Transition；缺失时暴露配置契约错误。</summary>
        /// <param name="direction">已经由八方向选择器确定的动画方向。</param>
        /// <returns>该方向配置的 TransitionAsset。</returns>
        private TransitionAsset ResolveTransition(SprintAnimationDirection direction)
        {
            for (int index = 0; index < transitionDirections.Length; index++)
                if (transitionDirections[index] == direction)
                    return directionTransitions[index];

            string error = $"[SprintGATask] 配置快照中缺少方向 {direction} 的动画 Transition。";
            Debug.LogError(error, GARuntime.SourceOwner as UnityEngine.Object);
            throw new InvalidOperationException(error);
        }

        /// <summary>Animancer 位移开启事件将普通更新中的位移门控打开。</summary>
        private void OnMovementStart()
        {
            movementEnabled = true;
            Debug.Log(
                $"[SprintGATask] 位移窗口开启，角色={GARuntime.SourceOwner.RootTransform.name}。",
                GARuntime.SourceOwner as UnityEngine.Object);
        }

        /// <summary>Animancer 位移停止事件关闭门控，并保留一次边界结算。</summary>
        private void OnMovementStop()
        {
            movementEnabled = false;
            movementStopPending = true;
            Debug.Log(
                $"[SprintGATask] 位移窗口关闭，等待一次 Update 边界结算，" +
                $"角色={GARuntime.SourceOwner.RootTransform.name}。",
                GARuntime.SourceOwner as UnityEngine.Object);
        }

        /// <summary>Animancer 自然结束回调立即完成 Task，动作层在 Complete 生命周期中淡出。</summary>
        private void OnAnimationEnd()
        {
            Debug.Log(
                $"[SprintGATask] 冲刺动画自然结束，角色={GARuntime.SourceOwner.RootTransform.name}。",
                GARuntime.SourceOwner as UnityEngine.Object);
            Complete();
        }

        #endregion

        #region 资源清理

        /// <summary>解除事件订阅、淡出动作层、按输入调整朝向并交接 Locomotion。</summary>
        /// <param name="reason">触发清理的 Task 终态原因。</param>
        private void ReleaseResources(string reason)
        {
            // 仅移除本 Task 添加的回调，保留 Transition 原先可能配置的其他 Animancer 事件行为。
            if (animationEvents != null)
            {
                animationEvents.RemoveCallback(movementStartEvent, OnMovementStart);
                animationEvents.RemoveCallback(movementStopEvent, OnMovementStop);
                animationEvents.OnEnd -= OnAnimationEnd;
                animationEvents = null;
            }

            if (animationState != null)
            {
                if (animationState.Layer != null &&
                    animationState.Layer.CurrentState == animationState)
                    GARuntime.SourceOwner.AnimationPlayer.FadeLayer(
                        AnimationLayerType.Action, 0f, fadeOutDuration);
                animationState = null;
            }

            bool appliedFacing = ApplyFinalInputFacing();
            motionHandle?.Dispose();
            motionHandle = null;
            motionDriver = null;
            // 先归还技能运动通道，再切入 Locomotion，避免 Run/Walk 在同一帧被技能优先级压住。
            CharacterLocomotionStateId handoffState = TryHandoffToLocomotion();
            string handoffSummary = handoffState == CharacterLocomotionStateId.Disable
                ? "跳过"
                : handoffState.ToString();
            character = null;
            stateBlackboard = null;
            movementEnabled = false;
            movementStopPending = false;
            Debug.Log(
                $"[SprintGATask] 冲刺资源已清理，原因={reason}，Action 淡出={fadeOutDuration:F2}s，" +
                $"结束朝向={(appliedFacing ? "已按当前输入更新" : "保持原朝向")}，" +
                $"Locomotion 交接={handoffSummary}，" +
                $"角色={GARuntime.SourceOwner.RootTransform.name}。",
                GARuntime.SourceOwner as UnityEngine.Object);
        }

        /// <summary>读取结束时已经结算的移动输入，并在有有效水平输入时更新角色朝向。</summary>
        /// <returns>本次确实应用了输入朝向时返回 true；输入无效时保持原朝向并返回 false。</returns>
        private bool ApplyFinalInputFacing()
        {
            Vector3 inputDirection = Vector3.ProjectOnPlane(stateBlackboard.MoveWorldInput, Vector3.up);
            if (inputDirection.sqrMagnitude <= 0.0001f)
                return false;

            // 直接设置 RootTransform，保证冲刺结束交接到 Locomotion 前已经完成朝向结算。
            GARuntime.SourceOwner.RootTransform.rotation =
                Quaternion.LookRotation(inputDirection.normalized, Vector3.up);
            return true;
        }

        /// <summary>在有效接地移动输入下直接交接到 Run，并把速度交给 Run 状态继续管理。</summary>
        /// <returns>交接到 Run 时返回 Run；不满足条件或状态机拒绝时返回 Disable。</returns>
        private CharacterLocomotionStateId TryHandoffToLocomotion()
        {
            if (!stateBlackboard.HasMovement || !stateBlackboard.IsGrounded ||
                character.Locomotion.CurrentRootState != CharacterLocomotionStateId.Grounded)
                return CharacterLocomotionStateId.Disable;

            CharacterLocomotionStateId targetState = CharacterLocomotionStateId.Run;
            if (character.Locomotion.CurrentState == targetState)
            {
                // 冲刺期间 Locomotion 可能已经保持在 Run；此时不重播动画，只校准共享速度。
                if (character.Locomotion.TrySetRunTargetSpeed())
                    return targetState;

                Debug.LogWarning(
                    $"[SprintGATask] 角色 '{character.name}' 当前标记为 Run，但无法取得 Run 状态实例，" +
                    "冲刺结束未应用目标速度。",
                    character);
                return CharacterLocomotionStateId.Disable;
            }

            CharacterLocomotionStateId currentState = character.Locomotion.CurrentState;
            bool changed = character.Locomotion.ChangeState(targetState, enteredState =>
            {
                if (enteredState is not RunLocomotionState runState)
                    throw new InvalidOperationException(
                        $"[SprintGATask] Locomotion 声称已进入 Run，但回调目标实际为 " +
                        $"'{enteredState?.StateId}'。 ");

                // 回调发生在 Run.OnEnter 完成后，直接把冲刺结束时的共享速度提升到 Run 目标值。
                runState.SetSpeedToRunTargetSpeed();
            });
            if (!changed)
            {
                Debug.LogWarning(
                    $"[SprintGATask] 角色 '{character.name}' 冲刺结束后无法交接 Locomotion，" +
                    $"当前状态={currentState}，目标状态={targetState}，" +
                    $"IsGrounded={stateBlackboard.IsGrounded}，MoveWorldInput={stateBlackboard.MoveWorldInput}。",
                    character);
                return CharacterLocomotionStateId.Disable;
            }

            return targetState;
        }

        #endregion
    }

    /// <summary>配置 QuickShift 八方向动画、位移事件窗口、目标距离与结束淡出。</summary>
    [Serializable]
    public sealed class SprintGATaskConfig : GameplayAbilityTaskConfig
    {
        #region 配置字段与属性

        // 八方向顺序与 SprintAnimationDirection 枚举值一致，用于选择器和完整性校验。
        private static readonly SprintAnimationDirection[] allDirections =
        {
            SprintAnimationDirection.Forward,
            SprintAnimationDirection.Backward,
            SprintAnimationDirection.Left,
            SprintAnimationDirection.Right,
            SprintAnimationDirection.ForwardLeft,
            SprintAnimationDirection.ForwardRight,
            SprintAnimationDirection.BackwardLeft,
            SprintAnimationDirection.BackwardRight
        };

        [SerializeField, MinValue(0.01f), LabelText("冲刺目标距离（米）")]
        private float targetDistance = 3f;
        [SerializeField, Required, ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true), LabelText("八方向动画")]
        private List<SprintDirectionTransitionBinding> directionalTransitions = new();
        [SerializeField, Required, LabelText("开启位移 Animancer 事件")]
        private StringAsset movementStartEvent;
        [SerializeField, Required, LabelText("停止位移 Animancer 事件")]
        private StringAsset movementStopEvent;
        [SerializeField, MinValue(0f), LabelText("动作层淡出时长（秒）")]
        private float fadeOutDuration = 0.2f;

        /// <summary>获取作者配置的八方向动画绑定列表。</summary>
        public IReadOnlyList<SprintDirectionTransitionBinding> DirectionalTransitions =>
            directionalTransitions;

        /// <summary>获取开启位移窗口的共享 Animancer 事件名。</summary>
        public StringAsset MovementStartEvent => movementStartEvent;

        /// <summary>获取关闭位移窗口的共享 Animancer 事件名。</summary>
        public StringAsset MovementStopEvent => movementStopEvent;

        /// <summary>获取结束时 Action 动画层淡出时长。</summary>
        public float FadeOutDuration => fadeOutDuration;

        /// <summary>获取事件窗口内计划完成的冲刺距离。</summary>
        public float TargetDistance => targetDistance;

        #endregion

        #region 配置校验与方向选择

        /// <summary>拒绝方向或事件窗口配置无效的 Ability，并记录首个具体配置错误。</summary>
        internal override bool IsConfigurationValid
        {
            get
            {
                string error = GetConfigurationError();
                if (error == null)
                    return true;

                Debug.LogError($"[SprintGATaskConfig] QuickShift 配置无效：{error}");
                return false;
            }
        }

        /// <summary>返回配置中的首个错误；全部合法时返回 null。</summary>
        /// <returns>可直接用于诊断的错误内容，或合法时的 null。</returns>
        internal string GetConfigurationError()
        {
            if (targetDistance <= 0f || float.IsNaN(targetDistance) || float.IsInfinity(targetDistance))
                return $"目标距离必须是大于零的有限值，当前值={targetDistance}";
            if (fadeOutDuration < 0f || float.IsNaN(fadeOutDuration) || float.IsInfinity(fadeOutDuration))
                return $"Action 淡出时长必须是非负有限值，当前值={fadeOutDuration}";
            if (movementStartEvent == null || movementStopEvent == null)
                return "必须同时指定开启和停止位移的 StringAsset 事件名";
            if (movementStartEvent.name == movementStopEvent.name)
                return $"开启与停止事件名不能相同：{movementStartEvent.name}";
            if (directionalTransitions == null || directionalTransitions.Count != allDirections.Length)
                return $"八方向动画绑定必须恰有 {allDirections.Length} 项";

            bool[] directionSeen = new bool[allDirections.Length];
            for (int index = 0; index < directionalTransitions.Count; index++)
            {
                SprintDirectionTransitionBinding binding = directionalTransitions[index];
                if (binding == null)
                    return $"第 {index} 项方向动画绑定为空";

                int directionIndex = (int)binding.Direction;
                if ((uint)directionIndex >= (uint)allDirections.Length)
                    return $"第 {index} 项方向值无效：{binding.Direction}";
                if (directionSeen[directionIndex])
                    return $"方向 {binding.Direction} 重复配置";
                directionSeen[directionIndex] = true;

                TransitionAsset transition = binding.Transition;
                if (transition == null)
                    return $"方向 {binding.Direction} 未配置 TransitionAsset";
                if (!transition.IsValid)
                    return $"方向 {binding.Direction} 的 Transition '{transition.name}' 无效";
                for (int previousIndex = 0; previousIndex < index; previousIndex++)
                    if (directionalTransitions[previousIndex].Transition == transition)
                        return $"方向 {binding.Direction} 与前序方向共用了 Transition '{transition.name}'";

                ITransition transitionData = transition.GetTransition();
                if (transitionData is not ClipTransition)
                    return $"方向 {binding.Direction} 的 Transition '{transition.name}' 必须是单 Clip Transition";
                if (transitionData.IsLooping)
                    return $"方向 {binding.Direction} 的 Transition '{transition.name}' 不能循环";
                if (transitionData.Speed <= 0f || float.IsNaN(transitionData.Speed) ||
                    float.IsInfinity(transitionData.Speed))
                    return $"方向 {binding.Direction} 的 Transition '{transition.name}' 必须正向且速度有限";

                string eventError = GetMovementWindowError(
                    transition, movementStartEvent, movementStopEvent);
                if (eventError != null)
                    return $"方向 {binding.Direction} 的 Transition '{transition.name}'：{eventError}";
            }

            return null;
        }

        /// <summary>从完整事件时间数组中验证 Start/Stop 命名事件及结束边界。</summary>
        /// <param name="transition">待验证的方向动画 Transition。</param>
        /// <param name="startEvent">位移开启事件名资产。</param>
        /// <param name="stopEvent">位移停止事件名资产。</param>
        /// <returns>首个事件配置错误，全部合法时返回 null。</returns>
        internal static string GetMovementWindowError(
            TransitionAsset transition, StringAsset startEvent, StringAsset stopEvent)
        {
            if (transition == null || !transition.IsValid)
                return "TransitionAsset 缺失或无效";
            if (startEvent == null || stopEvent == null)
                return "Start/Stop StringAsset 事件名缺失";
            if (startEvent.name == stopEvent.name)
                return $"Start/Stop 事件名重复：{startEvent.name}";

            ITransition transitionData = transition.GetTransition();
            if (transitionData is not ClipTransition)
                return "Transition 必须是单 Clip Transition";
            if (transitionData.IsLooping)
                return "动画不能循环";
            if (transitionData.Speed <= 0f || float.IsNaN(transitionData.Speed) ||
                float.IsInfinity(transitionData.Speed))
                return "动画速度必须为有限正数";

            AnimancerEvent.Sequence.Serializable events = transitionData.SerializedEvents;
            if (events == null)
                return "尚未配置 Animancer 命名事件";

            float[] normalizedTimes = events.NormalizedTimes;
            StringAsset[] names = events.Names;
            int eventCount = normalizedTimes == null ? 0 : Mathf.Max(0, normalizedTimes.Length - 1);
            int startCount = CountNamedEvents(names, eventCount, startEvent.name);
            int stopCount = CountNamedEvents(names, eventCount, stopEvent.name);
            if (startCount != 1)
                return $"事件 {startEvent.name} 必须恰好存在一次，当前次数={startCount}";
            if (stopCount != 1)
                return $"事件 {stopEvent.name} 必须恰好存在一次，当前次数={stopCount}";

            float startTime = FindNamedEventTime(normalizedTimes, names, eventCount, startEvent.name);
            float stopTime = FindNamedEventTime(normalizedTimes, names, eventCount, stopEvent.name);
            float endTime = events.GetNormalizedEndTime(transitionData.Speed);
            if (float.IsNaN(endTime))
                endTime = AnimancerEvent.Sequence.GetDefaultNormalizedEndTime(transitionData.Speed);
            if (float.IsNaN(startTime) || float.IsInfinity(startTime) ||
                float.IsNaN(stopTime) || float.IsInfinity(stopTime) ||
                float.IsNaN(endTime) || float.IsInfinity(endTime))
                return "Start、Stop 与 End 时间必须是有限归一化值";
            if (startTime < 0f || startTime >= stopTime || stopTime >= endTime)
                return $"必须满足 0 <= Start < Stop < End，当前值={startTime:F3}/{stopTime:F3}/{endTime:F3}";

            return null;
        }

        /// <summary>根据向量和八个角色局部方向点积选择最接近的动画方向。</summary>
        /// <param name="localDirection">角色局部空间中的水平冲刺方向。</param>
        /// <returns>点积最大的八方向动画；无有效输入时返回 Forward。</returns>
        internal static SprintAnimationDirection GetClosestDirection(Vector3 localDirection)
        {
            localDirection = Vector3.ProjectOnPlane(localDirection, Vector3.up);
            if (localDirection.sqrMagnitude <= 0.0001f)
                return SprintAnimationDirection.Forward;

            localDirection.Normalize();
            SprintAnimationDirection closestDirection = SprintAnimationDirection.Forward;
            float closestDot = float.NegativeInfinity;
            for (int index = 0; index < allDirections.Length; index++)
            {
                SprintAnimationDirection candidate = allDirections[index];
                float candidateDot = Vector3.Dot(localDirection, GetDirectionVector(candidate));
                if (candidateDot > closestDot)
                {
                    closestDot = candidateDot;
                    closestDirection = candidate;
                }
            }

            return closestDirection;
        }

        /// <summary>取得八方向枚举对应的角色局部水平单位向量。</summary>
        /// <param name="direction">需要转换的动画方向。</param>
        /// <returns>以角色前方为 Z 正向、右方为 X 正向的方向向量。</returns>
        private static Vector3 GetDirectionVector(SprintAnimationDirection direction)
        {
            return direction switch
            {
                SprintAnimationDirection.Forward => Vector3.forward,
                SprintAnimationDirection.Backward => Vector3.back,
                SprintAnimationDirection.Left => Vector3.left,
                SprintAnimationDirection.Right => Vector3.right,
                SprintAnimationDirection.ForwardLeft => new Vector3(-1f, 0f, 1f).normalized,
                SprintAnimationDirection.ForwardRight => new Vector3(1f, 0f, 1f).normalized,
                SprintAnimationDirection.BackwardLeft => new Vector3(-1f, 0f, -1f).normalized,
                SprintAnimationDirection.BackwardRight => new Vector3(1f, 0f, -1f).normalized,
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "未知冲刺动画方向。")
            };
        }

        /// <summary>统计一个 StringAsset 名称在普通事件区间中的出现次数。</summary>
        /// <param name="names">Animancer 普通事件的命名资产数组。</param>
        /// <param name="eventCount">不包含 End 的普通事件数量。</param>
        /// <param name="targetName">需要计数的事件名。</param>
        /// <returns>名称匹配的普通事件数量。</returns>
        private static int CountNamedEvents(StringAsset[] names, int eventCount, string targetName)
        {
            if (names == null)
                return 0;

            int count = 0;
            int nameCount = Mathf.Min(names.Length, eventCount);
            for (int index = 0; index < nameCount; index++)
                if (names[index] != null && names[index].name == targetName)
                    count++;
            return count;
        }

        /// <summary>根据事件名称读取对应的归一化时间。</summary>
        /// <param name="normalizedTimes">Animancer 完整归一化时间数组。</param>
        /// <param name="names">Animancer 普通事件命名资产数组。</param>
        /// <param name="eventCount">不包含 End 的普通事件数量。</param>
        /// <param name="targetName">要查找的事件名称。</param>
        /// <returns>匹配事件时间；名称不存在时返回 NaN。</returns>
        private static float FindNamedEventTime(
            float[] normalizedTimes, StringAsset[] names, int eventCount, string targetName)
        {
            if (normalizedTimes == null || names == null)
                return float.NaN;

            int count = Mathf.Min(Mathf.Min(names.Length, eventCount), normalizedTimes.Length);
            for (int index = 0; index < count; index++)
                if (names[index] != null && names[index].name == targetName)
                    return normalizedTimes[index];
            return float.NaN;
        }

        #endregion

        #region Task 工厂

        /// <summary>从八个唯一方向、有效单 Clip Transition 与完整事件窗口创建冲刺 Task。</summary>
        /// <param name="runtime">拥有本次 Root Task 的异步 Ability Runtime。</param>
        /// <returns>绑定方向与动画资源快照的新冲刺 Task。</returns>
        protected override GameplayAbilityTask CreateTask(AsynchronousGameplayAbilityRuntime runtime)
        {
            SprintAnimationDirection[] directions = new SprintAnimationDirection[directionalTransitions.Count];
            TransitionAsset[] transitions = new TransitionAsset[directionalTransitions.Count];
            for (int index = 0; index < directionalTransitions.Count; index++)
            {
                directions[index] = directionalTransitions[index].Direction;
                transitions[index] = directionalTransitions[index].Transition;
            }

            return new SprintGATask(
                runtime,
                targetDistance,
                directions,
                transitions,
                movementStartEvent,
                movementStopEvent,
                fadeOutDuration);
        }

        #endregion
    }
}
