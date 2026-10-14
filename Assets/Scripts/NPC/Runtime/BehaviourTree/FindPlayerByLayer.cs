using BehaviorDesigner.Runtime;
using BehaviorDesigner.Runtime.Tasks;
using RPG.Character;
using UnityEngine;

namespace RPG.NPC.BehaviourTree
{
    /// <summary>首次在配置 Layer 与半径内寻找可攻击玩家，并把共享 CharacterRoot 写入黑板。</summary>
    [TaskCategory("RPG/NPC")]
    [TaskDescription("保留仍有效的 CharacterRoot；否则仅在首次发现范围内查找由 CharacterManager 提供有效 Active 角色的目标。")]
    public sealed class FindPlayerByLayer : Action
    {
        #region 黑板与查询缓冲

        [UnityEngine.Tooltip("与其他 NPC 节点共用的玩家空间根。")]
        public SharedTransform target;
        [UnityEngine.Tooltip("只用于首次发现目标；已保存到黑板的有效目标不会因离开该范围而丢失。"), Sirenix.OdinInspector.MinValue(0.01f)]
        public float searchRadius = 8f;
        [UnityEngine.Tooltip("可被识别为玩家角色的物理 Layer。")]
        public LayerMask playerLayerMask = 1 << 6;

        private NPCController controller;
        private Collider[] playerColliderBuffer = new Collider[8];
        private bool hasValidSearchConfiguration;

        #endregion

        #region 行为树生命周期

        /// <summary>由当前 BehaviorTree GameObject 解析 NPCController。</summary>
        public override void OnAwake()
        {
            GameObject ownerObject = Owner != null ? Owner.gameObject : null;
            controller = ownerObject != null ? ownerObject.GetComponent<NPCController>() : null;
            if (controller == null)
                Debug.LogError("[FindPlayerByLayer] 行为树宿主缺少同节点 NPCController。", ownerObject);
            hasValidSearchConfiguration = IsFinitePositive(searchRadius) && playerLayerMask.value != 0;
            if (!hasValidSearchConfiguration)
                Debug.LogError(
                    $"[FindPlayerByLayer] 首次搜索配置无效，radius={searchRadius}，layerMask={playerLayerMask.value}。",
                    ownerObject);
        }

        /// <summary>保留有效目标，或搜索最近的有效玩家；无目标时失败以进入空闲等待分支。</summary>
        /// <returns>共享黑板存在有效玩家目标时返回 Success。</returns>
        public override TaskStatus OnUpdate()
        {
            if (controller == null || target == null || !hasValidSearchConfiguration)
                return TaskStatus.Failure;

            Transform previousTarget = target.Value;
            if (controller.IsValidPlayerTarget(previousTarget))
                return TaskStatus.Success;

            target.Value = null;
            if (previousTarget != null)
                Debug.Log($"[FindPlayerByLayer] Boss '{controller.name}' 清除失效玩家目标 '{previousTarget.name}'。", controller);
            if (!TryFindNearestPlayer(out Transform foundTarget))
                return TaskStatus.Failure;

            target.Value = foundTarget;
            Debug.Log($"[FindPlayerByLayer] Boss '{controller.name}' 首次发现可攻击玩家 CharacterRoot '{foundTarget.name}'。", controller);
            return TaskStatus.Success;
        }

        /// <summary>清理节点持有的查询引用；共享目标由黑板生命周期保留。</summary>
        public override void OnReset()
        {
            target = null;
            controller = null;
            hasValidSearchConfiguration = false;
            System.Array.Clear(playerColliderBuffer, 0, playerColliderBuffer.Length);
        }

        #endregion

        #region 玩家目标搜索

        /// <summary>按节点配置的 Layer 与首次发现半径选择最近的有效玩家 CharacterRoot。</summary>
        /// <param name="nearestTarget">找到时返回目标队伍共用的空间根节点。</param>
        /// <returns>范围内存在提供有效 Active ASC 的 CharacterManager 时返回 true。</returns>
        private bool TryFindNearestPlayer(out Transform nearestTarget)
        {
            nearestTarget = null;
            int colliderCount;
            while (true)
            {
                colliderCount = Physics.OverlapSphereNonAlloc(
                    controller.transform.position,
                    searchRadius,
                    playerColliderBuffer,
                    playerLayerMask,
                    QueryTriggerInteraction.Collide);
                if (colliderCount < playerColliderBuffer.Length)
                    break;
                System.Array.Resize(ref playerColliderBuffer, checked(playerColliderBuffer.Length * 2));
            }

            float nearestDistanceSquared = searchRadius * searchRadius;
            var visitedManagerIds = new System.Collections.Generic.HashSet<int>();
            try
            {
                for (int index = 0; index < colliderCount; index++)
                {
                    Collider collider = playerColliderBuffer[index];
                    if (collider == null)
                        continue;
                    // 同一队伍的多个身体 Collider 只解析一次，存活身份由 Manager 的当前 Active 决定。
                    CharacterManager playerManager = collider.GetComponentInParent<CharacterManager>();
                    if (playerManager == null || !visitedManagerIds.Add(playerManager.GetInstanceID()))
                        continue;

                    CharacterActor activeActor = playerManager.ActiveCharacter;
                    Transform playerRoot = activeActor != null ? activeActor.RootTransform : null;
                    if (playerRoot == null || !controller.IsValidPlayerTarget(playerRoot))
                        continue;
                    float distanceSquared = (playerRoot.position - controller.transform.position).sqrMagnitude;
                    if (distanceSquared >= nearestDistanceSquared)
                        continue;
                    nearestDistanceSquared = distanceSquared;
                    nearestTarget = playerRoot;
                }
            }
            finally
            {
                System.Array.Clear(playerColliderBuffer, 0, colliderCount);
            }
            return nearestTarget != null;
        }

        #endregion

        #region 配置校验

        /// <summary>判断搜索半径是否为有限正数。</summary>
        /// <param name="value">节点配置的首次发现半径。</param>
        /// <returns>大于零且有限时返回 true。</returns>
        private static bool IsFinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
