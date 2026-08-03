using UnityEngine;

namespace RPG.Markers
{
    /// <summary>
    /// 以项目资产身份标识一种实例挂点语义，供不同实例层级共享同一查询键。
    /// </summary>
    [CreateAssetMenu(fileName = "MarkerKey", menuName = "RPG/Markers/Marker Key")]
    public sealed class MarkerKey : ScriptableObject
    {
    }
}
