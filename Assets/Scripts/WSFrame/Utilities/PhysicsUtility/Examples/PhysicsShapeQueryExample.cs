using System;
using UnityEngine;

namespace WS_Modules.Utilities.Examples
{
    /// <summary>
    /// 演示如何声明 PhysicsShapeData，并使用 PhysicsUtility 执行 NonAlloc 查询。
    /// </summary>
    public sealed class PhysicsShapeQueryExample : MonoBehaviour
    {
        #region 配置

        [SerializeField] private PhysicsShapeData queryShape = new();
        [SerializeField] private LayerMask targetLayers = Physics.DefaultRaycastLayers;
        [SerializeField] private QueryTriggerInteraction queryTriggerInteraction =
            QueryTriggerInteraction.Ignore;
        [SerializeField] private bool queryEveryFrame;

        #endregion

        #region 查询缓存

        // 调用方长期持有缓冲区，避免每次 Update 查询产生数组分配。
        private readonly Collider[] overlapResults = new Collider[32];
        private readonly RaycastHit[] raycastResults = new RaycastHit[32];

        #endregion

        #region 生命周期

        /// <summary>
        /// 按开关决定是否每帧执行一次示例查询。
        /// </summary>
        private void Update()
        {
            if (queryEveryFrame) ExecuteQuery();
        }

        /// <summary>
        /// 演示由宿主决定只在选中时转发 PhysicsShapeData 的 Gizmo 绘制。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            queryShape?.OnDrawGizmos(transform);
        }

        #endregion

        #region 示例操作

        /// <summary>
        /// 在 Inspector 的组件菜单中执行一次当前形状查询。
        /// </summary>
        [ContextMenu("执行一次 Physics 查询")]
        private void ExecuteQuery()
        {
            if (queryShape == null)
            {
                Debug.LogWarning("PhysicsShapeQueryExample 没有配置查询形状。", this);
                return;
            }

            if (queryShape.Type == PhysicsShapeType.Ray)
            {
                int hitCount = PhysicsUtility.RaycastNonAlloc(transform, queryShape,
                    raycastResults, targetLayers, queryTriggerInteraction);
                Debug.Log($"Raycast 命中 {hitCount} 个 Collider。", this);
                return;
            }

            int overlapCount = PhysicsUtility.OverlapNonAlloc(transform, queryShape,
                overlapResults, targetLayers, queryTriggerInteraction);
            Debug.Log($"{queryShape.Type} 重叠 {overlapCount} 个 Collider。", this);
        }

        #endregion

        private void OnDrawGizmos()
        {
            queryShape.OnDrawGizmos(transform);
        }
    }
}
