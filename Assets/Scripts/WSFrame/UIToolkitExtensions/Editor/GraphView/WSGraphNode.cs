using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using WS_Modules.UIModule.Editor;

namespace WS_Modules.UIToolkitExtensions.Editor.GraphView
{
    /// <summary>
    /// 为端口与节点中间内容提供统一初始化流程的 GraphView 节点基类。
    /// </summary>
    public abstract class WSGraphNode : Node
    {
        #region 样式常量
        /// <summary>框架节点基础 USS 的项目路径。</summary>
        public const string BaseStyleSheetPath =
            UxmlUssPathConstants.Uss.AssetsScriptsWSFrameUIToolkitExtensionsEditorGraphViewWSGraphNode;
        /// <summary>节点根元素的稳定样式类。</summary>
        public const string RootClassName = "ws-graph-node";
        /// <summary>节点主要结构容器的稳定样式类。</summary>
        public const string MainClassName = "ws-graph-node__main";
        /// <summary>节点标题区域的稳定样式类。</summary>
        public const string TitleClassName = "ws-graph-node__title";
        /// <summary>节点输入端口容器的稳定样式类。</summary>
        public const string InputClassName = "ws-graph-node__input";
        /// <summary>节点输出端口容器的稳定样式类。</summary>
        public const string OutputClassName = "ws-graph-node__output";
        /// <summary>节点中间内容容器的稳定样式类。</summary>
        public const string ContentClassName = "ws-graph-node__content";
        /// <summary>所有节点端口共用的稳定样式类。</summary>
        public const string PortClassName = "ws-graph-node__port";
        /// <summary>输入端口的稳定状态样式类。</summary>
        public const string InputPortClassName = "ws-graph-node__port--input";
        /// <summary>输出端口的稳定状态样式类。</summary>
        public const string OutputPortClassName = "ws-graph-node__port--output";
        /// <summary>节点被选择时由所属 GraphView 维护的稳定状态样式类。</summary>
        public const string SelectedClassName = "ws-graph-node--selected";
        #endregion

        #region 字段与属性
        // GraphView 节点的端口在创建时会被 GraphView 维护，但 GraphView 不提供按业务标识查找端口的能力。
        private readonly Dictionary<string, Port> portsById = new Dictionary<string, Port>();
        private readonly HashSet<string> loadedStyleSheetPaths = new HashSet<string>();
        private readonly HashSet<string> appliedStyleClassNames = new HashSet<string>();
        private bool initialized;

        /// <summary>
        /// 获取节点是否已经完成通用内容与端口初始化。
        /// </summary>
        public bool IsInitialized => initialized;
        #endregion

        #region 公开查询
        /// <summary>
        /// 按稳定端口标识查找已经创建的端口。
        /// </summary>
        /// <param name="portId">端口在节点内的稳定标识。</param>
        /// <param name="port">找到的端口。</param>
        /// <returns>找到端口时返回 true。</returns>
        public bool TryGetPort(string portId, out Port port) => portsById.TryGetValue(portId, out port);
        #endregion

        #region 初始化
        /// <summary>
        /// 在派生节点完成构造后初始化自定义内容和端口；重复调用不会重复创建 UI。
        /// </summary>
        internal void InitializeNode()
        {
            if (initialized) return;

            initialized = true;
            // 应用框架基础样式和派生节点自定义样式，保证节点结构和样式的稳定性。
            ApplyFrameworkStyles();
            ApplyCustomStyles();
            // 调用节点内容提供者和端口提供者填充中间内容和端口。
            PopulateNodeContent();
            PopulatePorts();
            RefreshExpandedState();
            RefreshPorts();
        }

        #region 节点样式
        /// <summary>
        /// 加载基础 USS，并为节点固定结构添加可供业务覆盖的稳定样式类。
        /// </summary>
        private void ApplyFrameworkStyles()
        {
            AddStyleSheet(BaseStyleSheetPath);
            AddRootStyleClass(RootClassName);
            mainContainer.AddToClassList(MainClassName);
            titleContainer.AddToClassList(TitleClassName);
            inputContainer.AddToClassList(InputClassName);
            outputContainer.AddToClassList(OutputClassName);
            extensionContainer.AddToClassList(ContentClassName);
        }

        /// <summary>
        /// 按派生节点声明顺序加载业务 USS 和根样式类，使其能够覆盖基础节点样式。
        /// </summary>
        private void ApplyCustomStyles()
        {
            // 实现 IGraphNodeStyleProvider 的派生节点可以提供自定义样式表路径和根样式类。
            if (!(this is IGraphNodeStyleProvider styleProvider)) return;

            IEnumerable<string> styleSheetPaths = styleProvider.GetStyleSheetPaths();
            if (styleSheetPaths != null)
            {
                foreach (string styleSheetPath in styleSheetPaths) AddStyleSheet(styleSheetPath);
            }

            IEnumerable<string> styleClassNames = styleProvider.GetStyleClassNames();
            if (styleClassNames == null) return;
            foreach (string styleClassName in styleClassNames) AddRootStyleClass(styleClassName);
        }

        /// <summary>
        /// 加载并附加一份 USS；无效路径作为节点配置错误直接抛出。
        /// </summary>
        /// <param name="styleSheetPath">以 Assets 开头的 USS 资源路径。</param>
        /// <exception cref="ArgumentException">样式表路径为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">路径下不存在 StyleSheet 时抛出。</exception>
        private void AddStyleSheet(string styleSheetPath)
        {
            if (string.IsNullOrWhiteSpace(styleSheetPath))
                throw new ArgumentException("节点 USS 路径不能为空。", nameof(styleSheetPath));
            if (!loadedStyleSheetPaths.Add(styleSheetPath)) return;

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(styleSheetPath);
            if (styleSheet == null)
                throw new InvalidOperationException($"无法加载节点 USS：{styleSheetPath}。");
            styleSheets.Add(styleSheet);
        }

        /// <summary>
        /// 为节点根元素添加一次非空样式类。
        /// </summary>
        /// <param name="styleClassName">待添加的 USS class。</param>
        /// <exception cref="ArgumentException">样式类名称为空时抛出。</exception>
        private void AddRootStyleClass(string styleClassName)
        {
            if (string.IsNullOrWhiteSpace(styleClassName))
                throw new ArgumentException("节点样式类名称不能为空。", nameof(styleClassName));
            if (appliedStyleClassNames.Add(styleClassName)) AddToClassList(styleClassName);
        }

        #endregion

        #region 扩展内容的填充与端口创建
        /// <summary>
        /// 调用节点内容提供者 (常见子类 <see cref="IGraphNodeContentProvider"/>) 填充 extensionContainer。
        /// </summary>
        private void PopulateNodeContent()
        {
            if (this is IGraphNodeContentProvider contentProvider)
                contentProvider.PopulateContent(extensionContainer);
        }

        /// <summary>
        /// 根据端口提供者 (<see cref="IGraphPortProvider"/>) 创建输入和输出端口，并记录业务标识映射。
        /// </summary>
        private void PopulatePorts()
        {
            if (!(this is IGraphPortProvider portProvider)) return;

            IEnumerable<GraphPortDescriptor> descriptors = portProvider.GetPortDescriptors();
            if (descriptors == null) return;

            foreach (GraphPortDescriptor descriptor in descriptors)
            {
                if (descriptor == null)
                    throw new InvalidOperationException($"节点 {GetType().Name} 的端口描述不能为 null。");
                if (portsById.ContainsKey(descriptor.Id))
                    throw new InvalidOperationException($"节点 {GetType().Name} 存在重复端口标识：{descriptor.Id}。");

                // 描述对象保存在 userData 中，使连接规则和结果回调能够还原业务端口语义。
                Port port = Port.Create<Edge>(descriptor.Orientation, descriptor.Direction,
                    descriptor.Capacity, descriptor.DataType);
                port.portName = descriptor.DisplayName;
                port.userData = descriptor;
                port.AddToClassList(PortClassName);
                port.AddToClassList(descriptor.Direction == Direction.Input
                    ? InputPortClassName
                    : OutputPortClassName);
                portsById.Add(descriptor.Id, port);

                VisualElement portContainer = descriptor.Direction == Direction.Input
                    ? inputContainer
                    : outputContainer;
                portContainer.Add(port);
            }
        }
        #endregion
        #endregion
    }
}
