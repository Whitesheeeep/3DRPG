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
        [SerializeField] private Transform weaponRoot;
        [SerializeField] private Transform weaponTip;
        [SerializeField] private LayerMask attackLayerMask = ~0;
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal;

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
        }

        /// <summary>
        /// 测试组件禁用时解除实例事件，避免 Domain Reload 或重复启用产生重复日志。
        /// </summary>
        private void OnDisable()
        {
            if (runner == null) return;
            runner.HitDetected -= OnHitDetected;
            runner.Completed -= OnCompleted;
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
            Debug.Log($"[SkillRuntimeTest] Initialize owner={owner.name}, layer={skillAnimationLayer}", this);
        }

        /// <summary>
        /// 播放当前 SkillConfig，并输出启动结果及武器轨迹输入。
        /// </summary>
        [Button("播放技能")]
        public void PlaySkill()
        {
            SkillStartResult result = runner.TryPlay(new SkillPlayRequest(config, weaponRoot, weaponTip));
            Debug.Log($"[SkillRuntimeTest] Play succeeded={result.Succeeded}, message={result.Message}, " +
                      $"config={(config != null ? config.name : "<null>")}", this);
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
        /// 输出当前帧、动作阶段和可打断状态，供状态机接入前手动验证。
        /// </summary>
        [Button("打印运行状态")]
        public void PrintState()
        {
            Debug.Log($"[SkillRuntimeTest] playing={runner.IsPlaying}, frame={runner.CurrentFrame}, " +
                      $"phase={runner.CurrentPhase}, interruptible={runner.CanBeInterrupted}", this);
        }

        #endregion

        #region 事件日志

        /// <summary>
        /// 输出已经完成过滤和 Clip 内去重的命中事件。
        /// </summary>
        /// <param name="args">命中事件快照。</param>
        private void OnHitDetected(SkillHitEventArgs args)
        {
            Debug.Log($"[SkillRuntimeTest] Hit execution={args.ExecutionId}, frame={args.Frame}, " +
                      $"target={args.Target.name}, clip={args.Clip.Id}", args.Target);
        }

        /// <summary>
        /// 输出统一技能结束事件，验证外部状态机可按原因恢复 Locomotion。
        /// </summary>
        /// <param name="args">技能结束事件快照。</param>
        private void OnCompleted(SkillCompletedEventArgs args)
        {
            Debug.Log($"[SkillRuntimeTest] Completed execution={args.ExecutionId}, reason={args.Reason}, " +
                      $"lastFrame={args.LastFrame}", this);
        }

        #endregion
    }
}
#endif
