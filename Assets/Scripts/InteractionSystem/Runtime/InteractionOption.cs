using System;
using UnityEngine;

namespace RPG.InteractionSystem
{
    /// <summary>
    /// 标识一个 Provider 在当前运行时实例上贡献的稳定交互动作。
    /// </summary>
    public readonly struct InteractionOptionId : IEquatable<InteractionOptionId>, IComparable<InteractionOptionId>
    {
        #region 属性

        /// <summary>获取 Provider 的 Unity 运行时实例 ID。</summary>
        public int ProviderInstanceId { get; }

        /// <summary>获取 Provider 内部稳定的动作 ID。</summary>
        public string ActionId { get; }

        #endregion

        #region 构造与比较

        /// <summary>创建一个交互选项 ID。</summary>
        /// <param name="providerInstanceId">Provider 的 Unity 运行时实例 ID。</param>
        /// <param name="actionId">Provider 内部稳定的动作 ID。</param>
        public InteractionOptionId(int providerInstanceId, string actionId)
        {
            ProviderInstanceId = providerInstanceId;
            if (string.IsNullOrWhiteSpace(actionId))
                throw new ArgumentException("InteractionOptionId 的 ActionId 不能为空。", nameof(actionId));
            ActionId = actionId;
        }

        /// <summary>判断两个 ID 是否相等。</summary>
        public bool Equals(InteractionOptionId other) =>
            ProviderInstanceId == other.ProviderInstanceId &&
            string.Equals(ActionId, other.ActionId, StringComparison.Ordinal);

        /// <summary>判断对象是否为相同的交互选项 ID。</summary>
        public override bool Equals(object obj) => obj is InteractionOptionId other && Equals(other);

        /// <summary>返回基于 Provider 实例和动作 ID 的哈希值。</summary>
        public override int GetHashCode() => HashCode.Combine(ProviderInstanceId, ActionId);

        /// <summary>按 Provider 实例 ID 和动作 ID 执行稳定字典序比较。</summary>
        public int CompareTo(InteractionOptionId other)
        {
            int providerComparison = ProviderInstanceId.CompareTo(other.ProviderInstanceId);
            return providerComparison != 0
                ? providerComparison
                : string.Compare(ActionId, other.ActionId, StringComparison.Ordinal);
        }

        /// <summary>返回用于日志和排序诊断的 ID 文本。</summary>
        public override string ToString() => $"{ProviderInstanceId}:{ActionId}";

        #endregion

        #region 运算符

        /// <summary>判断两个交互选项 ID 是否相等。</summary>
        public static bool operator ==(InteractionOptionId left, InteractionOptionId right) => left.Equals(right);

        /// <summary>判断两个交互选项 ID 是否不相等。</summary>
        public static bool operator !=(InteractionOptionId left, InteractionOptionId right) => !left.Equals(right);

        #endregion
    }

    /// <summary>
    /// 表示交互列表中可被玩家选择并尝试执行的一条命令。
    /// </summary>
    public sealed class InteractionOption
    {
        #region 字段与属性

        private readonly Func<GameObject, bool> canExecute;
        private readonly Func<GameObject, bool> execute;

        /// <summary>获取当前 Option 的稳定 ID。</summary>
        public InteractionOptionId Id { get; }

        /// <summary>获取 UI 展示名称。</summary>
        public string DisplayName { get; }

        /// <summary>获取 UI 展示图标；没有图标时为空。</summary>
        public Sprite Icon { get; }

        /// <summary>获取排序优先级，数值越大越优先。</summary>
        public int Priority { get; }

        /// <summary>获取用于距离和遮挡判断的目标对象。</summary>
        public GameObject InteractionObject { get; }

        /// <summary>获取用于距离和视野判断的交互中心。</summary>
        public Transform InteractionOrigin { get; }

        /// <summary>获取 Option 的最大有效距离；零表示不额外收紧检测范围。</summary>
        public float MaxDistance { get; }

        #endregion

        #region 构造

        /// <summary>创建一个可缓存并重复查询的交互命令。</summary>
        /// <param name="id">当前 Provider 贡献的稳定动作 ID。</param>
        /// <param name="displayName">UI 展示名称。</param>
        /// <param name="interactionObject">用于遮挡判断的目标对象。</param>
        /// <param name="interactionOrigin">用于距离和视野判断的交互中心。</param>
        /// <param name="priority">排序优先级。优先级越高越优先。</param>
        /// <param name="maxDistance">Option 最大有效距离。</param>
        /// <param name="canExecute">执行前的业务可用性判断。</param>
        /// <param name="execute">业务执行回调，返回本次执行是否成功。</param>
        /// <param name="icon">可选 UI 图标。</param>
        public InteractionOption(InteractionOptionId id, string displayName, GameObject interactionObject,
            Transform interactionOrigin, int priority, float maxDistance,
            Func<GameObject, bool> canExecute, Func<GameObject, bool> execute, Sprite icon = null)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Option 展示名称不能为空。", nameof(displayName));
            if (interactionObject == null) throw new ArgumentNullException(nameof(interactionObject));
            if (interactionOrigin == null) throw new ArgumentNullException(nameof(interactionOrigin));
            if (canExecute == null) throw new ArgumentNullException(nameof(canExecute));
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            if (float.IsNaN(maxDistance) || float.IsInfinity(maxDistance) || maxDistance < 0f)
                throw new ArgumentOutOfRangeException(nameof(maxDistance), "Option 最大距离必须是有限非负数。");

            Id = id;
            DisplayName = displayName;
            InteractionObject = interactionObject;
            InteractionOrigin = interactionOrigin;
            Priority = priority;
            MaxDistance = maxDistance;
            this.canExecute = canExecute;
            this.execute = execute;
            Icon = icon;
        }

        #endregion

        #region 执行

        /// <summary>判断当前玩家是否仍可执行此 Option。</summary>
        /// <param name="interactor">发起执行的玩家对象。</param>
        /// <returns>业务允许执行时返回 true。</returns>
        public bool CanExecute(GameObject interactor) => canExecute(interactor);

        /// <summary>在执行前再次校验并尝试执行此 Option。</summary>
        /// <param name="interactor">发起执行的玩家对象。</param>
        /// <returns>校验通过且业务执行成功时返回 true。</returns>
        public bool TryExecute(GameObject interactor)
        {
            // 选项从 UI 选择到真正执行之间可能跨越多个帧，因此必须在命令入口重新校验。
            return CanExecute(interactor) && execute(interactor);
        }

        #endregion
    }
}
