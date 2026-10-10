using BehaviorDesigner.Runtime;
using BehaviorDesigner.Runtime.Tasks;
using UnityEngine;

namespace RPG.NPC.BehaviourTree
{
    /// <summary>以无副作用的路径与激活条件预检判断是否应抢占低优先级 Walk。</summary>
    [TaskCategory("RPG/NPC")]
    [TaskDescription("只读检查远距完整路径、技能状态和安全落点；Lower Priority Conditional Abort 可在追击时重新评估。")]
    public sealed class CanNpcTeleport : Conditional
    {
        #region 黑板与节点策略

        [UnityEngine.Tooltip("与瞬移执行节点共用的玩家目标根。")]
        public SharedTransform target;
        [UnityEngine.Tooltip("必须在完整 NavMesh 路径长度上严格超过的瞬移阈值。")]
        public SharedFloat pathDistanceThreshold = 12f;
        [UnityEngine.Tooltip("与导航和普攻节点共用的攻击水平距离。")]
        public SharedFloat attackDistance;
        [UnityEngine.Tooltip("Conditional Abort 检查频率，使用缩放时间；目标变化时立即重检。"),
         Sirenix.OdinInspector.MinValue(0f)]
        public float recheckInterval = 0.2f;

        #endregion

        #region 依赖与缓存状态

        // 每个 BehaviorTree Task 实例只缓存自己的目标和最近一次预检结果。
        private NPCController controller;
        private Transform cachedTarget;
        private float nextCheckTime;
        private int lastEvaluatedFrame = -1;
        private bool hasCachedResult;
        private bool cachedResult;

        #endregion

        #region 行为树生命周期

        /// <summary>解析当前行为树宿主的 NPCController。</summary>
        public override void OnAwake()
        {
            GameObject ownerObject = Owner != null ? Owner.gameObject : null;
            controller = ownerObject != null ? ownerObject.GetComponent<NPCController>() : null;
            if (controller == null)
                Debug.LogError("[CanNpcTeleport] 行为树宿主缺少同节点 NPCController。", ownerObject);
        }

        /// <summary>按目标与重检间隔缓存只读瞬移预检结果。</summary>
        /// <returns>当前目标超过路径阈值且瞬移前置条件均满足时返回 Success。</returns>
        public override TaskStatus OnUpdate()
        {
            if (controller == null || target == null || pathDistanceThreshold == null || attackDistance == null ||
                !IsFiniteNonNegative(recheckInterval))
                return TaskStatus.Failure;

            Transform currentTarget = target.Value;
            if (cachedTarget != currentTarget)
            {
                cachedTarget = currentTarget;
                hasCachedResult = false;
                nextCheckTime = 0f;
            }

            // 一次失败激活会设置 Controller 退避；即使 Conditional Abort 立即重评估，也先让 Walk 继续。
            if (controller.IsTeleportRetryPending(currentTarget))
            {
                cachedResult = false;
                hasCachedResult = true;
                lastEvaluatedFrame = Time.frameCount;
                nextCheckTime = Time.time + recheckInterval;
                return TaskStatus.Failure;
            }

            if (hasCachedResult && (lastEvaluatedFrame == Time.frameCount || Time.time < nextCheckTime))
                return cachedResult ? TaskStatus.Success : TaskStatus.Failure;

            cachedResult = controller.CanTeleportTo(
                currentTarget,
                pathDistanceThreshold.Value,
                attackDistance.Value,
                out _);
            hasCachedResult = true;
            lastEvaluatedFrame = Time.frameCount;
            nextCheckTime = Time.time + Mathf.Max(0f, recheckInterval);
            return cachedResult ? TaskStatus.Success : TaskStatus.Failure;
        }

        /// <summary>释放该节点实例的目标与采样状态，避免下次运行沿用过期预检。</summary>
        public override void OnReset()
        {
            target = null;
            pathDistanceThreshold = null;
            attackDistance = null;
            controller = null;
            cachedTarget = null;
            nextCheckTime = 0f;
            lastEvaluatedFrame = -1;
            hasCachedResult = false;
            cachedResult = false;
        }

        /// <summary>判断条件节点重检间隔是否为有限非负值。</summary>
        /// <param name="value">节点配置的缩放时间间隔。</param>
        /// <returns>不小于零且有限时返回 true。</returns>
        private static bool IsFiniteNonNegative(float value) =>
            value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
