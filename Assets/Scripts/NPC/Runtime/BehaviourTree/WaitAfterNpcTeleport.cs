using BehaviorDesigner.Runtime.Tasks;
using UnityEngine;

namespace RPG.NPC.BehaviourTree
{
    /// <summary>瞬移实际成功后按缩放时间等待，再允许行为树重新判断攻击条件。</summary>
    [TaskCategory("RPG/NPC")]
    [TaskDescription("等待期间不提交导航或攻击命令。")]
    public sealed class WaitAfterNpcTeleport : Action
    {
        #region 节点配置

        [UnityEngine.Tooltip("瞬移确认成功后，行为树暂停追击和攻击的缩放时间。"), Sirenix.OdinInspector.MinValue(0f)]
        public float waitDuration = 1f;

        #endregion

        #region 运行状态

        private NPCController controller;
        private float elapsed;
        private bool loggedCompletion;
        private bool hasValidWaitConfiguration;

        #endregion

        #region 行为树生命周期

        /// <summary>捕获 NPC 控制器并开始一次新的等待窗口。</summary>
        public override void OnStart()
        {
            GameObject ownerObject = Owner != null ? Owner.gameObject : null;
            controller = ownerObject != null ? ownerObject.GetComponent<NPCController>() : null;
            elapsed = 0f;
            loggedCompletion = false;
            hasValidWaitConfiguration = IsFiniteNonNegative(waitDuration);
            if (controller == null)
                Debug.LogError("[WaitAfterNpcTeleport] 行为树宿主缺少 NPCController。", ownerObject);
            else if (!hasValidWaitConfiguration)
                Debug.LogError(
                    $"[WaitAfterNpcTeleport] 瞬移等待配置非法，duration={waitDuration}。",
                    controller);
            else
                Debug.Log(
                    $"[WaitAfterNpcTeleport] Boss '{controller.name}' 瞬移成功，开始等待 {waitDuration:F2}s。",
                    controller);
        }

        /// <summary>累计缩放时间，等待完成后只发出一次完成日志。</summary>
        /// <returns>经过配置等待时长后返回 Success。</returns>
        public override TaskStatus OnUpdate()
        {
            if (controller == null || !hasValidWaitConfiguration)
                return TaskStatus.Failure;
            elapsed += Time.deltaTime;
            if (elapsed < waitDuration)
                return TaskStatus.Running;
            if (!loggedCompletion)
            {
                loggedCompletion = true;
                Debug.Log($"[WaitAfterNpcTeleport] Boss '{controller.name}' 瞬移后等待结束。", controller);
            }
            return TaskStatus.Success;
        }

        /// <summary>清空等待进度，避免节点实例重新使用时沿用旧计时。</summary>
        public override void OnReset()
        {
            controller = null;
            elapsed = 0f;
            loggedCompletion = false;
            hasValidWaitConfiguration = false;
        }

        /// <summary>判断等待时长是否为有限非负值。</summary>
        /// <param name="value">节点配置的等待秒数。</param>
        /// <returns>不小于零且有限时返回 true。</returns>
        private static bool IsFiniteNonNegative(float value) =>
            value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
