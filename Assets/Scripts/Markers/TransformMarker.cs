using UnityEngine;

namespace RPG.Markers
{
    /// <summary>
    /// 将当前 GameObject 的 Transform 声明为指定语义挂点，不主动参与所属实例的生命周期或注册。
    /// </summary>
    public sealed class TransformMarker : MonoBehaviour
    {
        [SerializeField] private MarkerKey key;

        /// <summary>
        /// 获取当前节点声明的挂点键。
        /// </summary>
        public MarkerKey Key => key;
    }
}
