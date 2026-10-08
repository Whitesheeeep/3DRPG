using UnityEngine;

namespace RPG.Game.Loading
{
    /// <summary>描述目标场景中玩家共享根节点的出生位置与本地 +Z 前方向。</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        #region 出生位姿

        /// <summary>获取此出生点的世界位置，作为 CharacterRoot 的原点。</summary>
        public Vector3 WorldPosition => transform.position;

        /// <summary>获取此出生点世界空间的 +Z 前方向。</summary>
        public Vector3 WorldForward => transform.forward;

        #endregion

        #region 场景视图标记

        /// <summary>在 Scene 视图绘制出生位置及本地 +Z 前方向。</summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.15f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.25f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.5f);
            Gizmos.DrawSphere(transform.position + transform.forward * 1.5f, 0.08f);
        }

        #endregion
    }
}
