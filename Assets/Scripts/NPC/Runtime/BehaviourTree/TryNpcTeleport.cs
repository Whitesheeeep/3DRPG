using BehaviorDesigner.Runtime;
using BehaviorDesigner.Runtime.Tasks;
using UnityEngine;

namespace RPG.NPC.BehaviourTree
{
    /// <summary>重新校验远距路径与安全条件后尝试独立瞬移 GA。</summary>
    [TaskCategory("RPG/NPC")]
    [TaskDescription("没有远距完整路径、技能不可激活或没有安全落点时失败，由 Selector 回退 Walk。")]
    public sealed class TryNpcTeleport : Action
    {
        #region 黑板与依赖

        [UnityEngine.Tooltip("行为树共享的玩家空间根。")]
        public SharedTransform target;
        [UnityEngine.Tooltip("与前置 CanNpcTeleport Conditional 共用的路径阈值。")]
        public SharedFloat pathDistanceThreshold;
        [UnityEngine.Tooltip("与导航、预检和攻击节点共用的水平攻击距离。")]
        public SharedFloat attackDistance;
        [UnityEngine.Tooltip("激活失败后恢复瞬移评估的缩放时间间隔。"), Sirenix.OdinInspector.MinValue(0.01f)]
        public float retryInterval = 0.2f;
        private NPCController controller;

        #endregion

        #region 行为树生命周期

        /// <summary>解析拥有该行为树的 Boss 控制器。</summary>
        public override void OnAwake()
        {
            GameObject ownerObject = Owner != null ? Owner.gameObject : null;
            controller = ownerObject != null ? ownerObject.GetComponent<NPCController>() : null;
            if (controller == null)
                Debug.LogError("[TryNpcTeleport] 行为树宿主缺少 NPCController。", ownerObject);
        }

        /// <summary>重新校验预检条件并调用已授予的瞬移 GA。</summary>
        /// <returns>瞬移 GA Task 实际完成安全重定位时成功。</returns>
        public override TaskStatus OnUpdate()
        {
            if (controller == null || target == null || pathDistanceThreshold == null || attackDistance == null ||
                !IsFinitePositive(retryInterval))
                return TaskStatus.Failure;

            return controller.TryActivateTeleport(
                target.Value,
                pathDistanceThreshold.Value,
                attackDistance.Value,
                retryInterval)
                ? TaskStatus.Success
                : TaskStatus.Failure;
        }

        /// <summary>在行为树资产重置时清理本次 Controller 与目标绑定。</summary>
        public override void OnReset()
        {
            target = null;
            pathDistanceThreshold = null;
            attackDistance = null;
            controller = null;
        }

        /// <summary>判断失败退避时长是否为有限正数。</summary>
        /// <param name="value">节点配置的缩放时间间隔。</param>
        /// <returns>大于零且有限时返回 true。</returns>
        private static bool IsFinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
