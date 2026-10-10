using BehaviorDesigner.Runtime.Tasks;
using UnityEngine;

namespace RPG.NPC.BehaviourTree
{
    /// <summary>在无有效玩家目标时停止导航与步行动画，等待根树下一次重试。</summary>
    [TaskCategory("RPG/NPC")]
    public sealed class StopNpcNavigation : Action
    {
        #region 依赖字段

        private NPCController controller;

        #endregion

        #region 行为树生命周期

        /// <summary>解析当前行为树宿主的 NPCController。</summary>
        public override void OnStart()
        {
            GameObject ownerObject = Owner != null ? Owner.gameObject : null;
            controller = ownerObject != null ? ownerObject.GetComponent<NPCController>() : null;
            if (controller == null)
                Debug.LogError("[StopNpcNavigation] 行为树宿主缺少 NPCController。", ownerObject);
        }

        /// <summary>释放 NavMesh 目标与 MotionDriver 步行通道。</summary>
        /// <returns>清理完成后返回 Success。</returns>
        public override TaskStatus OnUpdate()
        {
            if (controller == null)
                return TaskStatus.Failure;
            controller.StopMove();
            return TaskStatus.Success;
        }

        /// <summary>清除该 Task 的宿主缓存。</summary>
        public override void OnReset() => controller = null;

        #endregion
    }
}
