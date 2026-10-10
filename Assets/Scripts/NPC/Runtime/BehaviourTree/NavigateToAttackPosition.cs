using BehaviorDesigner.Runtime;
using BehaviorDesigner.Runtime.Tasks;
using UnityEngine;

namespace RPG.NPC.BehaviourTree
{
    /// <summary>通过 NPCController 的完整 NavMesh 路径追击目标，抵达攻击位置后成功。</summary>
    [TaskCategory("RPG/NPC")]
    [TaskDescription("不使用直线追击兜底；路径不可用时停止并等待控制器按间隔重试。")]
    public sealed class NavigateToAttackPosition : Action
    {
        #region 黑板与依赖

        [UnityEngine.Tooltip("行为树共享的玩家空间根。")]
        public SharedTransform target;
        [UnityEngine.Tooltip("与攻击和瞬移路径查询共用的停止距离。")]
        public SharedFloat attackDistance;
        private NPCController controller;

        #endregion

        #region 行为树生命周期

        /// <summary>解析当前行为树宿主的导航控制器。</summary>
        public override void OnAwake()
        {
            GameObject ownerObject = Owner != null ? Owner.gameObject : null;
            controller = ownerObject != null ? ownerObject.GetComponent<NPCController>() : null;
            if (controller == null)
                Debug.LogError("[NavigateToAttackPosition] 行为树宿主缺少 NPCController。", ownerObject);
        }

        /// <summary>以 NavMesh 完整路径持续追击，攻击条件满足时结束导航节点。</summary>
        /// <returns>抵达攻击位置时返回 Success；其余有效追击状态保持 Running。</returns>
        public override TaskStatus OnUpdate()
        {
            if (controller == null || target == null || !controller.IsValidPlayerTarget(target.Value))
            {
                controller?.StopMove();
                return TaskStatus.Failure;
            }

            if (controller.IsReadyToAttack(target.Value, attackDistance.Value))
            {
                controller.StopMove();
                return TaskStatus.Success;
            }

            controller.TryMoveTo(target.Value, attackDistance.Value);
            return TaskStatus.Running;
        }

        /// <summary>清理移动命令但不取消已启动的普通攻击 GA。</summary>
        public override void OnEnd()
        {
            controller?.StopMove();
        }

        /// <summary>清除本节点运行时的 Controller 缓存并归零共享目标引用。</summary>
        public override void OnReset()
        {
            target = null;
            controller = null;
        }

        #endregion
    }
}
