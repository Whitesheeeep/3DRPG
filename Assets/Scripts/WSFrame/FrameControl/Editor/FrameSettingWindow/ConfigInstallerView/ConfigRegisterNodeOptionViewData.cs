using System;
using WS_Modules.ConfigInstaller;

namespace WS_Modules
{
    /// <summary>
    /// ConfigInstaller 面板中单个可发现注册节点类型的显示和待应用状态。
    /// </summary>
    internal sealed class ConfigRegisterNodeOptionViewData
    {
        #region 状态

        /// <summary>
        /// 创建一个节点类型选项。
        /// </summary>
        /// <param name="nodeType">可实例化的注册节点类型。</param>
        /// <param name="nodeAsset">当前优先复用的节点资产；没有时为 null。</param>
        /// <param name="isIncluded">该类型当前是否已经存在于所选节点的子节点树中。</param>
        public ConfigRegisterNodeOptionViewData(
            Type nodeType,
            ConfigRegisterNodeBase nodeAsset,
            bool isIncluded)
        {
            NodeType = nodeType;
            NodeAsset = nodeAsset;
            IsIncluded = isIncluded;
            IsSelected = isIncluded;
        }

        /// <summary>
        /// 当前选项对应的注册节点类型。
        /// </summary>
        public Type NodeType { get; }

        /// <summary>
        /// 当前优先复用的节点资产；应用时可能被更新为新创建的资产。
        /// </summary>
        public ConfigRegisterNodeBase NodeAsset { get; set; }

        /// <summary>
        /// 当前类型是否已存在于 SelectedNode 的子节点树中。
        /// </summary>
        public bool IsIncluded { get; set; }

        /// <summary>
        /// 用户本次待应用的选择状态。
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// 面板中显示的完整类型名称，避免不同命名空间的同名类型混淆。
        /// </summary>
        public string TypeDisplayName => NodeType.FullName ?? NodeType.Name;

        /// <summary>
        /// 面板中显示的资产状态。
        /// </summary>
        public string AssetDisplayName => NodeAsset == null
            ? "No asset; create on apply"
            : $"Asset: {NodeAsset.name}";

        /// <summary>
        /// 面板中显示当前类型在所选组合节点中的包含状态。
        /// </summary>
        public string InclusionDisplayName => IsIncluded
            ? "Registered in selected node"
            : "Not registered in selected node";

        #endregion
    }
}
