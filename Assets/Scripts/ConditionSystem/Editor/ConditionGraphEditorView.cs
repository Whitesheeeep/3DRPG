#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace RPG.ConditionSystemNS.Editor
{
    /// <summary>管理窗口控件和原生序列化绑定，只转发用户意图，不保存配置副本。</summary>
    internal sealed class ConditionGraphEditorView : IDisposable
    {
        #region 依赖字段
        private readonly ObjectField assetField;
        private readonly ScrollView details;
        private readonly ScrollView issues;
        private readonly Label status;
        private readonly Button newButton;
        private readonly Button saveButton;
        private readonly Button validateButton;
        private SerializedObject boundObject;
        internal VisualElement GraphContainer { get; }
        #endregion

        #region 事件
        internal event Action<ConditionGraph_SO> AssetRequested;
        internal event Action NewRequested;
        internal event Action SaveRequested;
        internal event Action ValidateRequested;
        internal event Action FieldsChanged;
        internal event Action<ConditionNode, int, int> MoveChildRequested;
        internal event Action<ConditionNode, ConditionNode> DisconnectRequested;
        internal event Action<ConditionNode> LocateRequested;
        #endregion

        #region 生命周期
        /// <summary>查找已加载 UXML 控件并建立输入转发。</summary>
        /// <param name="root">窗口根容器。</param>
        internal ConditionGraphEditorView(VisualElement root)
        {
            assetField = root.Q<ObjectField>("AssetField");
            assetField.objectType = typeof(ConditionGraph_SO);
            assetField.allowSceneObjects = false;
            GraphContainer = root.Q("GraphContainer");
            details = root.Q<ScrollView>("Details");
            issues = root.Q<ScrollView>("Issues");
            status = root.Q<Label>("Status");
            newButton = root.Q<Button>("NewButton");
            saveButton = root.Q<Button>("SaveButton");
            validateButton = root.Q<Button>("ValidateButton");
            assetField.RegisterValueChangedCallback(OnAssetChanged);
            details.RegisterCallback<SerializedPropertyChangeEvent>(OnPropertyChanged);
            newButton.clicked += OnNew;
            saveButton.clicked += OnSave;
            validateButton.clicked += OnValidate;
        }

        /// <summary>对称解除控件回调和旧资产绑定，避免窗口重建重复发送意图。</summary>
        public void Dispose()
        {
            ClearDetails();
            assetField.UnregisterValueChangedCallback(OnAssetChanged);
            details.UnregisterCallback<SerializedPropertyChangeEvent>(OnPropertyChanged);
            newButton.clicked -= OnNew;
            saveButton.clicked -= OnSave;
            validateButton.clicked -= OnValidate;
        }
        #endregion

        #region 视图刷新
        /// <summary>显示当前资产而不再次发送切换请求。</summary>
        /// <param name="graph">当前资产。</param>
        internal void SetAsset(ConditionGraph_SO graph) => assetField.SetValueWithoutNotify(graph);

        /// <summary>显示操作结果。</summary>
        /// <param name="message">简短状态说明。</param>
        internal void SetStatus(string message) => status.text = message;

        /// <summary>解除绑定并释放 SerializedObject；必须先于目标切换或销毁。</summary>
        internal void ClearDetails()
        {
            details.Unbind();
            details.Clear();
            boundObject?.Dispose();
            boundObject = null;
        }

        /// <summary>绑定节点普通参数，使用独立顺序按钮将结构操作交给 Controller。</summary>
        /// <param name="node">所选节点，空选时解除绑定。</param>
        internal void ShowNode(ConditionNode node)
        {
            ClearDetails();
            if (node == null) { details.Add(new Label("选择节点以编辑参数")); return; }
            details.Add(new Label($"{node.name}\n{node.NodeId}"));
            boundObject = new SerializedObject(node);
            SerializedProperty iterator = boundObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script") continue;
                details.Add(new PropertyField(iterator.Copy()));
            }
            details.Bind(boundObject);
            if (node.MaximumChildren > 0)
            {
                details.Add(new Label("子条件顺序（与画布位置无关）"));
                for (int index = 0; index < node.Children.Count; index++) AddChildRow(node, index);
            }
        }

        /// <summary>创建一行可定位、调整顺序和断开的子条件操作。</summary>
        /// <param name="parent">父节点。</param>
        /// <param name="index">子条件顺序。</param>
        private void AddChildRow(ConditionNode parent, int index)
        {
            ConditionNode child = parent.Children[index];
            var row = new VisualElement();
            row.AddToClassList("condition-child-row");
            var locate = new Button(() => LocateRequested?.Invoke(child))
                { text = $"{index + 1}. {(child == null ? "<空引用>" : child.name)}" };
            locate.AddToClassList("condition-child-name");
            row.Add(locate);
            var up = new Button(() => MoveChildRequested?.Invoke(parent, index, index - 1)) { text = "↑" };
            var down = new Button(() => MoveChildRequested?.Invoke(parent, index, index + 1)) { text = "↓" };
            up.SetEnabled(index > 0);
            down.SetEnabled(index < parent.Children.Count - 1);
            row.Add(up);
            row.Add(down);
            row.Add(new Button(() => DisconnectRequested?.Invoke(parent, child)) { text = "断开" });
            details.Add(row);
        }

        /// <summary>展示公共校验结果，点击节点问题时请求画布定位。</summary>
        /// <param name="validationIssues">模型产生的问题集合；尚未打开资产时为 null。</param>
        internal void ShowIssues(IReadOnlyList<ConditionValidationIssue> validationIssues)
        {
            issues.Clear();
            if (validationIssues == null) { issues.Add(new Label("打开条件 SO 后显示校验结果")); return; }
            if (validationIssues.Count == 0) issues.Add(new HelpBox("条件图校验通过", HelpBoxMessageType.Info));
            foreach (ConditionValidationIssue issue in validationIssues)
            {
                issues.Add(new HelpBox(issue.Message, issue.Severity == E_ConditionIssueSeverity.Error
                    ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning));
                if (issue.Node != null)
                    issues.Add(new Button(() => LocateRequested?.Invoke(issue.Node)) { text = $"定位：{issue.Node.name}" });
            }
        }
        #endregion

        #region 输入转发
        /// <summary>转发用户选择的条件资产。</summary>
        /// <param name="evt">对象字段变化。</param>
        private void OnAssetChanged(ChangeEvent<UnityEngine.Object> evt) => AssetRequested?.Invoke(evt.newValue as ConditionGraph_SO);
        /// <summary>通知参数已由原生绑定写回，避免再手动设置同一字段。</summary>
        /// <param name="evt">序列化字段变化。</param>
        private void OnPropertyChanged(SerializedPropertyChangeEvent evt) => FieldsChanged?.Invoke();
        /// <summary>请求新建图资产。</summary>
        private void OnNew() => NewRequested?.Invoke();
        /// <summary>请求保存当前资产。</summary>
        private void OnSave() => SaveRequested?.Invoke();
        /// <summary>请求校验当前资产。</summary>
        private void OnValidate() => ValidateRequested?.Invoke();
        #endregion
    }
}
#endif
