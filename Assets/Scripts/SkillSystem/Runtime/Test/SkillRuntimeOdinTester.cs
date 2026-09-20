#if UNITY_EDITOR
using RPG.Character.Animation;
using RPG.Markers;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 通过 Odin Inspector 手动驱动真实 SkillRunner API，验证播放、停止、取消、阶段与事件生命周期。
    /// </summary>
    public sealed class SkillRuntimeOdinTester : MonoBehaviour
    {
        #region 测试输入

        [Title("角色依赖")]
        [SerializeField] private SkillRunner runner;
        [SerializeField] private GameObject owner;
        [SerializeField] private Transform origin;
        [SerializeField] private AnimationController animationController;
        [SerializeField] private AnimationLayerType skillAnimationLayer = AnimationLayerType.Action;
        [SerializeField] private MarkerProvider markerProvider;

        [Title("播放输入")]
        [SerializeField] private SkillConfig config;
        [SerializeField] private SkillConfig replacementConfig;
        [SerializeField] private SkillStartMode startMode = SkillStartMode.TimelineStart;
        [SerializeField] private Transform weaponRoot;
        [SerializeField] private Transform weaponTip;
        [SerializeField] private LayerMask attackLayerMask = ~0;
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal;
        [Title("攻击检测调试")]
        [SerializeField] private bool drawAttackDetectionDebug;
        [SerializeField, MinValue(0f)] private float attackDetectionDebugDuration;

        #endregion

        #region 生命周期

        /// <summary>
        /// 测试组件启用时订阅 Runner 实例事件，确保帧零命中与结束通知均可观察。
        /// </summary>
        private void OnEnable()
        {
            if (runner == null) return;
            runner.HitDetected += OnHitDetected;
            runner.Completed += OnCompleted;
            runner.ActionPhaseChanged += OnActionPhaseChanged;
        }

        /// <summary>
        /// 测试组件禁用时解除实例事件，避免 Domain Reload 或重复启用产生重复日志。
        /// </summary>
        private void OnDisable()
        {
            if (runner == null) return;
            runner.HitDetected -= OnHitDetected;
            runner.Completed -= OnCompleted;
            runner.ActionPhaseChanged -= OnActionPhaseChanged;
        }

        #endregion

        #region Odin 操作

        /// <summary>
        /// 使用 Inspector 中配置的稳定依赖初始化 SkillRunner。
        /// </summary>
        [Button("初始化 Runner")]
        public void InitializeRunner()
        {
            SkillActorContext actor = new(owner, origin, animationController, skillAnimationLayer, markerProvider);
            SkillAttackSettings attack = new(attackLayerMask, triggerInteraction);
            runner.Initialize(actor, attack);
            runner.SetAttackDetectionDebug(drawAttackDetectionDebug, attackDetectionDebugDuration);
            Debug.Log($"[SkillRuntimeTest] Initialize owner={owner.name}, layer={skillAnimationLayer}", this);
        }

        /// <summary>
        /// 播放当前 SkillConfig，并输出启动结果及武器轨迹输入。
        /// </summary>
        [Button("播放技能")]
        public void PlaySkill()
        {
            SkillStartResult result = runner.TryPlay(
                new SkillPlayRequest(config, weaponRoot, weaponTip, startMode));
            Debug.Log($"[SkillRuntimeTest] Play succeeded={result.Succeeded}, message={result.Message}, " +
                      $"config={(config != null ? config.name : "<null>")}, startMode={startMode}, " +
                      $"currentFrame={runner.CurrentFrame}", this);
        }

        /// <summary>
        /// 从完整时间轴第 0 帧播放当前 SkillConfig，手动确认 Startup 会被保留。
        /// </summary>
        [Button("从第 0 帧播放")]
        public void PlayFromTimelineStart()
        {
            PlayWithStartMode(SkillStartMode.TimelineStart);
        }

        /// <summary>
        /// 从最早 Active Phase 播放当前 SkillConfig，手动确认入口帧与首个 LateTick 攻击检测。
        /// </summary>
        [Button("从最早 Active 播放")]
        public void PlayFromFirstActivePhase()
        {
            PlayWithStartMode(SkillStartMode.FirstActivePhase);
        }

        /// <summary>
        /// 正常停止当前技能，观察 Stopped 结束事件和自然资源尾迹。
        /// </summary>
        [Button("Stop 技能")]
        public void StopSkill()
        {
            runner.Stop();
            Debug.Log("[SkillRuntimeTest] Stop requested.", this);
        }

        /// <summary>
        /// 立即取消当前技能，观察 Cancelled 结束事件和动态资源回收。
        /// </summary>
        [Button("Cancel 技能")]
        public void CancelSkill()
        {
            runner.Cancel();
            Debug.Log("[SkillRuntimeTest] Cancel requested.", this);
        }

        /// <summary>
        /// 在同一调用栈中取消当前技能并立即播放替换技能，验证旧 Action 层停止后新技能能够重新接管。
        /// </summary>
        [Button("取消并播放替换技能")]
        public void CancelAndPlayReplacementSkill()
        {
            if (replacementConfig == null)
            {
                Debug.LogWarning("[SkillRuntimeTest] replacementConfig 未配置，无法执行替换技能测试。", this);
                return;
            }

            runner.Cancel();
            SkillStartResult result = runner.TryPlay(
                new SkillPlayRequest(replacementConfig, weaponRoot, weaponTip));
            Debug.Log(
                $"[SkillRuntimeTest] Replace succeeded={result.Succeeded}, message={result.Message}, " +
                $"config={replacementConfig.name}",
                this);
        }

        /// <summary>
        /// 使用指定入口模式播放当前配置，并输出解析后的首帧状态。
        /// </summary>
        /// <param name="requestedStartMode">需要验证的时间轴入口模式。</param>
        private void PlayWithStartMode(SkillStartMode requestedStartMode)
        {
            SkillStartResult result = runner.TryPlay(
                new SkillPlayRequest(config, weaponRoot, weaponTip, requestedStartMode));
            Debug.Log(
                $"[SkillRuntimeTest] StartMode={requestedStartMode}, succeeded={result.Succeeded}, " +
                $"message={result.Message}, currentFrame={runner.CurrentFrame}, phase={runner.CurrentPhase}.",
                this);
        }

        /// <summary>
        /// 输出当前帧、动作阶段和 RuntimeTag 窗口，供 Action Arbiter 手动验证。
        /// </summary>
        [Button("打印运行状态")]
        public void PrintState()
        {
            Debug.Log($"[SkillRuntimeTest] playing={runner.IsPlaying}, frame={runner.CurrentFrame}, " +
                      $"phase={runner.CurrentPhase}, hasPolicy={runner.HasCurrentPhasePolicy}, " +
                      $"isCancelable={runner.CurrentPhaseIsCancelable}, runtimeTags={runner.CurrentPhaseRuntimeTags.Count}, " +
                      $"blockTags={runner.CurrentPhaseBlockAbilityTags.Count}", this);
        }

        #endregion

        #region 事件日志

        /// <summary>
        /// 输出已经完成过滤和 Detection ID 去重的命中事件。
        /// </summary>
        /// <param name="args">命中事件快照。</param>
        private void OnHitDetected(SkillHitEventArgs args)
        {
            Debug.Log($"[SkillRuntimeTest] Hit execution={args.ExecutionId}, frame={args.Frame}, " +
                      $"target={args.Target.name}, clip={args.Clip.Id}, detectionId={args.Clip.DetectionId}", args.Target);
        }

        /// <summary>
        /// 输出统一技能结束事件，验证技能动画层已经归还且外部系统可按原因处理后续业务。
        /// </summary>
        /// <param name="args">技能结束事件快照。</param>
        private void OnCompleted(SkillCompletedEventArgs args)
        {
            Debug.Log($"[SkillRuntimeTest] Completed execution={args.ExecutionId}, reason={args.Reason}, " +
                      $"lastFrame={args.LastFrame}", this);
        }

        /// <summary>
        /// 输出 SkillRuntime 原生阶段事件，确认 RuntimeTags 只属于单次执行。
        /// </summary>
        /// <param name="args">阶段与转换窗口快照。</param>
        private void OnActionPhaseChanged(SkillActionPhaseChangedEventArgs args)
        {
            Debug.Log($"[SkillRuntimeTest] PhaseChanged execution={args.ExecutionId}, " +
                      $"frame={args.Frame}, phase={args.Phase}, " +
                      $"hasPolicy={args.HasPhasePolicy}, isCancelable={args.IsCancelable}, " +
                      $"runtimeTags={args.RuntimeTags.Count}, blockTags={args.BlockAbilityTags.Count}", this);
        }

        #endregion
    }
}
#endif
