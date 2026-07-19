using WS_Modules.ConfigInstaller;

namespace WS_Modules
{
    internal sealed class ConfigTreeNodeViewData
    {
        public int Id { get; }
        public int Depth { get; }
        public ConfigRegisterNodeBase Node { get; }
        public ConfigTreeNodeViewData Parent { get; }
        public bool IsNull => Node == null;
        public bool IsComposite => Node is CompositeConfigRegisterNode;

        public string DisplayName => Node == null ? "Missing Node" : Node.name;
        public string TypeName => Node == null ? "Null" : Node.GetType().Name;

        public ConfigTreeNodeViewData(int id, int depth, ConfigRegisterNodeBase node, ConfigTreeNodeViewData parent)
        {
            Id = id;
            Depth = depth;
            Node = node;
            Parent = parent;
        }
    }
}
