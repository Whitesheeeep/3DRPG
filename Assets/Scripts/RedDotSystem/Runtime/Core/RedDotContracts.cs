namespace RPG.RedDotSystemNS
{
    /// <summary>描述一个红点节点已经发生的数值变化。</summary>
    public readonly struct RedDotValueChangedEvent
    {
        /// <summary>创建红点节点变化事件。</summary>
        /// <param name="key">发生变化的节点。</param>
        /// <param name="previousValue">变化前数值。</param>
        /// <param name="currentValue">变化后数值。</param>
        public RedDotValueChangedEvent(RedDotKey key, int previousValue, int currentValue)
        {
            Key = key;
            PreviousValue = previousValue;
            CurrentValue = currentValue;
        }

        /// <summary>获取发生变化的节点。</summary>
        public RedDotKey Key { get; }

        /// <summary>获取变化前数值。</summary>
        public int PreviousValue { get; }

        /// <summary>获取变化后数值。</summary>
        public int CurrentValue { get; }
    }

#if UNITY_EDITOR
    /// <summary>向 Editor 调试窗口暴露的只读红点节点快照。</summary>
    public readonly struct DebugRedDotNodeSnapshot
    {
        /// <summary>创建一份红点节点调试快照。</summary>
        /// <param name="key">节点标识。</param>
        /// <param name="parentKey">直接父节点标识。</param>
        /// <param name="derivedPath">由 Parent 链推导的显示路径。</param>
        /// <param name="selfValue">业务写入的自身值。</param>
        /// <param name="effectiveSelfValue">应用临时覆盖后的自身值。</param>
        /// <param name="totalValue">当前节点及子节点聚合后的值。</param>
        /// <param name="hasDebugOverride">是否存在临时覆盖。</param>
        /// <param name="debugOverrideValue">临时覆盖值。</param>
        /// <param name="isDirty">是否等待刷新。</param>
        /// <param name="childCount">直接子节点数量。</param>
        public DebugRedDotNodeSnapshot(
            RedDotKey key,
            RedDotKey parentKey,
            string derivedPath,
            int selfValue,
            int effectiveSelfValue,
            int totalValue,
            bool hasDebugOverride,
            int debugOverrideValue,
            bool isDirty,
            int childCount)
        {
            Key = key;
            ParentKey = parentKey;
            DerivedPath = derivedPath;
            SelfValue = selfValue;
            EffectiveSelfValue = effectiveSelfValue;
            TotalValue = totalValue;
            HasDebugOverride = hasDebugOverride;
            DebugOverrideValue = debugOverrideValue;
            IsDirty = isDirty;
            ChildCount = childCount;
        }

        /// <summary>获取节点标识。</summary>
        public RedDotKey Key { get; }
        /// <summary>获取直接父节点；根节点为空。</summary>
        public RedDotKey ParentKey { get; }
        /// <summary>获取节点的派生显示路径。</summary>
        public string DerivedPath { get; }
        /// <summary>获取业务写入的自身值。</summary>
        public int SelfValue { get; }
        /// <summary>获取应用临时覆盖后的自身值。</summary>
        public int EffectiveSelfValue { get; }
        /// <summary>获取节点及其子节点聚合后的值。</summary>
        public int TotalValue { get; }
        /// <summary>获取节点是否存在 Editor 临时覆盖。</summary>
        public bool HasDebugOverride { get; }
        /// <summary>获取 Editor 临时覆盖值。</summary>
        public int DebugOverrideValue { get; }
        /// <summary>获取节点是否等待帧末刷新。</summary>
        public bool IsDirty { get; }
        /// <summary>获取直接子节点数量。</summary>
        public int ChildCount { get; }
    }
#endif
}
