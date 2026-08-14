using System;
using UnityEditor.Experimental.GraphView;

namespace WS_Modules.UIToolkitExtensions.Editor.GraphView
{
    /// <summary>
    /// 描述通用图节点上的一个端口，并作为端口 UI 与业务标识之间的稳定映射。
    /// </summary>
    public sealed class GraphPortDescriptor
    {
        #region 属性

        /// <summary>
        /// 获取端口在所属节点内的稳定标识。
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// 获取端口显示名称。
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// 获取端口的数据方向。
        /// </summary>
        public Direction Direction { get; }

        /// <summary>
        /// 获取端口允许的连接容量。
        /// </summary>
        public Port.Capacity Capacity { get; }

        /// <summary>
        /// 获取端口在节点上的布局方向。
        /// </summary>
        public Orientation Orientation { get; }

        /// <summary>
        /// 获取端口传输的数据类型。
        /// </summary>
        public Type DataType { get; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建一个图端口描述。
        /// </summary>
        /// <param name="id">端口在所属节点内的稳定标识。</param>
        /// <param name="displayName">端口显示名称。</param>
        /// <param name="direction">端口的数据方向。</param>
        /// <param name="capacity">端口允许的连接容量。</param>
        /// <param name="dataType">端口传输的数据类型。</param>
        /// <param name="orientation">端口在节点上的布局方向。</param>
        /// <exception cref="ArgumentException">端口标识为空时抛出。</exception>
        /// <exception cref="ArgumentNullException">数据类型为空时抛出。</exception>
        public GraphPortDescriptor(string id, string displayName, Direction direction,
            Port.Capacity capacity, Type dataType, Orientation orientation = Orientation.Horizontal)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("端口标识不能为空。", nameof(id));

            Id = id;
            DisplayName = displayName ?? string.Empty;
            Direction = direction;
            Capacity = capacity;
            DataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
            Orientation = orientation;
        }

        #endregion
    }
}
