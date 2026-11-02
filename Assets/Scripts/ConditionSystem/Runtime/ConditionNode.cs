using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.ConditionSystemNS
{
    /// <summary>保存静态参数及子条件引用；派生节点只读取上下文并返回结果，不保存本次求值状态。</summary>
    public abstract class ConditionNode : ScriptableObject
    {
        #region 配置字段
        [SerializeField, HideInInspector] private string nodeId;
        [SerializeField, HideInInspector] private Vector2 editorPosition;
        [SerializeField, HideInInspector] private ConditionGraph_SO owner;
        [SerializeField, HideInInspector] private List<ConditionNode> children = new List<ConditionNode>();
        #endregion

        #region 属性
        /// <summary>获取稳定节点标识。</summary>
        public string NodeId => nodeId;
        /// <summary>获取所属图，用于阻止跨图引用。</summary>
        public ConditionGraph_SO Owner => owner;
        /// <summary>获取按求值顺序排列的只读子条件。</summary>
        public IReadOnlyList<ConditionNode> Children => children.AsReadOnly();
        /// <summary>获取子条件最大数量；叶子节点默认为零。</summary>
        public virtual int MaximumChildren => 0;
        /// <summary>获取有效配置要求的子条件最少数量。</summary>
        public virtual int MinimumChildren => 0;
        /// <summary>获取或设置 GraphView 内容坐标空间中的位置，不影响求值顺序。</summary>
        public Vector2 EditorPosition { get => editorPosition; set => editorPosition = value; }
        #endregion

        #region 求值与校验
        // 求值：只读取本次业务上下文。
        /// <summary>读取本次上下文进行同步判断；内部递归不重复校验整图。</summary>
        /// <param name="context">业务提供的上下文，派生类应明确要求其实际类型。</param>
        /// <returns>满足状态和可扩展原因。</returns>
        public abstract ConditionResult Evaluate(ConditionContext context);

        // 校验：仅检查静态配置，不能引入业务运行时依赖。
        /// <summary>校验节点序列化参数；派生类覆盖时调用基类，不访问业务运行时状态。</summary>
        /// <exception cref="InvalidOperationException">子条件数量或引用不合法时抛出。</exception>
        public virtual void Validate()
        {
            if (children.Count < MinimumChildren || children.Count > MaximumChildren)
                throw new InvalidOperationException($"{name} 子条件数量无效：{children.Count}，允许 {MinimumChildren}..{MaximumChildren}。");
            foreach (ConditionNode child in children)
                if (child == null) throw new InvalidOperationException($"{name} 包含空子条件引用。");
        }
        #endregion

        #region 图结构内部操作
        /// <summary>由图建立所有权并在首次加入时生成稳定标识。</summary>
        /// <param name="graph">所属图，移除时为 null。</param>
        internal void SetOwner(ConditionGraph_SO graph)
        {
            owner = graph;
            if (string.IsNullOrEmpty(nodeId)) nodeId = Guid.NewGuid().ToString("N");
        }

        /// <summary>由图完成连接校验后追加子条件，顺序独立于画布位置。</summary>
        /// <param name="child">已校验的子节点。</param>
        internal void AddChild(ConditionNode child) => children.Add(child);

        /// <summary>移除指向目标的全部引用，供断线和节点删除使用。</summary>
        /// <param name="child">待移除引用的节点。</param>
        internal void RemoveChild(ConditionNode child) => children.RemoveAll(candidate => candidate == child);

        /// <summary>调整既有子条件的求值顺序，不改变引用集合。</summary>
        /// <param name="index">原有索引。</param>
        /// <param name="targetIndex">目标索引。</param>
        /// <exception cref="ArgumentOutOfRangeException">索引超出集合时抛出。</exception>
        public void MoveChild(int index, int targetIndex)
        {
            if (index < 0 || index >= children.Count || targetIndex < 0 || targetIndex >= children.Count)
                throw new ArgumentOutOfRangeException(nameof(targetIndex));
            ConditionNode child = children[index];
            children.RemoveAt(index);
            children.Insert(targetIndex, child);
            Debug.Log($"[ConditionNode] 调整子条件顺序，node={name}，from={index}，to={targetIndex}");
        }
        #endregion
    }
}
