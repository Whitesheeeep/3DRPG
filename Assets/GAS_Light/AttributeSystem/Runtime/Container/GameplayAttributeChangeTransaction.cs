using System.Collections.Generic;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>
    /// 为单个 Attribute Container 管理 BaseValue 修改 FIFO、事务防重入和修改环检测。
    /// </summary>
    internal sealed class GameplayAttributeChangeTransaction
    {
        #region 嵌套类型

        /// <summary>保存一个尚未由 Container 执行的 BaseValue 修改请求。</summary>
        private readonly struct PendingBaseValueChange
        {
            // 仅由事务在请求首次通过重复检测时创建。
            internal PendingBaseValueChange(GameplayAttribute attribute, float value)
            {
                Attribute = attribute;
                Value = value;
            }

            internal GameplayAttribute Attribute { get; }
            internal float Value { get; }
        }

        #endregion

        #region 字段

        private readonly Queue<PendingBaseValueChange> pendingChanges = new();
        private readonly HashSet<GameplayAttribute> scheduledAttributes = new();
        private readonly HashSet<GameplayAttribute> processedAttributes = new();
        private bool processingChanges;

        #endregion

        #region 属性

        // 指示当前是否已有 Base 或 Modifier 修改事务占用该 Container。
        internal bool IsProcessing => processingChanges;

        #endregion

        #region 事务操作

        // 开始一次同步修改事务；已有事务正在执行时拒绝重入。
        internal bool TryBegin()
        {
            if (processingChanges) return false;
            processingChanges = true;
            return true;
        }

        // 将 BaseValue 修改加入 FIFO；同一事务中拒绝重复排队和已经处理的 Attribute。
        internal bool TryScheduleBaseValueChange(GameplayAttribute attribute, float value)
        {
            if (scheduledAttributes.Contains(attribute) ||
                processedAttributes.Contains(attribute))
                return false;

            scheduledAttributes.Add(attribute);
            pendingChanges.Enqueue(new PendingBaseValueChange(attribute, value));
            return true;
        }

        // 按 FIFO 取出请求，并在交给 Container 前将 Attribute 标记为本事务已处理。
        internal bool TryDequeueBaseValueChange(
            out GameplayAttribute attribute,
            out float value)
        {
            if (pendingChanges.Count == 0)
            {
                attribute = default;
                value = default;
                return false;
            }

            PendingBaseValueChange change = pendingChanges.Dequeue();
            scheduledAttributes.Remove(change.Attribute);
            processedAttributes.Add(change.Attribute);
            attribute = change.Attribute;
            value = change.Value;
            return true;
        }

        // 结束事务并清空全部瞬时状态，确保失败和异常不会污染下一次修改。
        internal void Complete()
        {
            processingChanges = false;
            pendingChanges.Clear();
            scheduledAttributes.Clear();
            processedAttributes.Clear();
        }

        #endregion
    }
}