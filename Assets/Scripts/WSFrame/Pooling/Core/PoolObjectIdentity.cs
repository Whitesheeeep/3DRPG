using UnityEngine;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 用于标记池化对象所属的 Pool Key，解决路径加载资源导致回收时无法通过 Name 正确找到池子的问题
    /// </summary>
    public class PoolObjectIdentity : MonoBehaviour
    {
        /// <summary>
        /// 记录该对象所属的池子 Key，在预热和获取时通过 MarkObjectIdentity 方法设置，在回收时通过这个字段来正确找到对应的池子进行回收，避免因为对象名称不一致（如路径加载资源时带有路径前缀）导致回收失败的问题。这个字段的值应该与预热和获取时使用的 key 保持一致，确保对象能正确回收到对应的池子中。
        /// </summary>
        public string PoolKey;
    }
}