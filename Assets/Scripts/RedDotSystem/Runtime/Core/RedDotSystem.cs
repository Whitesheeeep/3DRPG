using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;

namespace RPG.RedDotSystemNS
{
    /// <summary>
    /// 管理静态红点配置树、运行时数值、帧末批处理及定向和全局变化通知。
    /// </summary>
    public sealed class RedDotSystem : AbstractSystem
    {
        #region 静态配置

        /// <summary>
        /// 由 ConfigInstaller 在 GameArchitecture 初始化前写入的正式红点配置。
        /// </summary>
        public static RedDotConfig Config;

        #endregion

        #region 状态字段

        // key：RedDotKey Asset；value：对应节点的运行时可变状态。
        private readonly Dictionary<RedDotKey, RedDotRuntimeNode> runtimeNodeByKeyMap =
            new Dictionary<RedDotKey, RedDotRuntimeNode>();

        // key：本帧需要重新计算的节点 Asset。
        private readonly HashSet<RedDotKey> dirtyNodeKeys = new HashSet<RedDotKey>();

        // key：节点 Asset；value：该节点定向变化事件中心。
        private readonly EventCenterModule<RedDotKey> valueChangedEventCenter =
            new EventCenterModule<RedDotKey>();

        private CancellationTokenSource lifetimeCancellationSource;
        private bool flushScheduled;

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建一个从静态 Config 读取节点的红点系统。
        /// </summary>
        public RedDotSystem()
        {
        }

        /// <summary>
        /// 读取静态配置、校验节点关系并建立运行时节点树。
        /// </summary>
        protected override void OnInit()
        {
            if (Config == null)
            {
                throw new InvalidOperationException(
                    "[RedDotSystem] Config 尚未注入，请检查 ConfigInstaller 初始化顺序。");
            }

            lifetimeCancellationSource = new CancellationTokenSource();
            BuildRuntimeTree(Config);

            Debug.Log(
                $"[RedDotSystem] 初始化完成，config={Config.name}，nodeCount={runtimeNodeByKeyMap.Count}，rootCount={runtimeNodeByKeyMap.Values.Count(node => node.Parent == null)}。");
        }

        /// <summary>
        /// 取消尚未执行的帧末刷新，并清理运行时节点和定向事件。
        /// </summary>
        protected override void OnDeinit()
        {
            lifetimeCancellationSource?.Cancel();
            lifetimeCancellationSource?.Dispose();
            lifetimeCancellationSource = null;
            flushScheduled = false;
            valueChangedEventCenter.Clear();
            dirtyNodeKeys.Clear();
            runtimeNodeByKeyMap.Clear();

            Debug.Log("[RedDotSystem] 已注销并清理运行时节点、定向订阅和待刷新状态。");
        }

        #endregion

        #region 公开查询与业务写入

        /// <summary>
        /// 获取指定节点最近一次 Flush 后提交的聚合值。
        /// </summary>
        /// <param name="key">待查询节点 Asset。</param>
        /// <returns>节点自身值与全部后代值之和。</returns>
        /// <exception cref="KeyNotFoundException">节点未在静态 Config 中注册时抛出。</exception>
        public int GetValue(RedDotKey key)
        {
            return GetRequiredNode(key).TotalValue;
        }

        /// <summary>
        /// 获取指定节点最近一次业务写入的自身值。
        /// </summary>
        /// <param name="key">待查询节点 Asset。</param>
        /// <returns>未应用 Debug Override 的业务自身值。</returns>
        /// <exception cref="KeyNotFoundException">节点未在静态 Config 中注册时抛出。</exception>
        public int GetSelfValue(RedDotKey key)
        {
            return GetRequiredNode(key).SelfValue;
        }

        /// <summary>
        /// 设置指定节点的业务自身值，并安排本帧末重算。
        /// </summary>
        /// <param name="key">待设置节点 Asset。</param>
        /// <param name="value">非负自身值。</param>
        /// <exception cref="ArgumentNullException">节点为空时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">值小于零时抛出。</exception>
        /// <exception cref="KeyNotFoundException">节点未在静态 Config 中注册时抛出。</exception>
        public void SetSelfValue(RedDotKey key, int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "[RedDotSystem] 红点自身值不能小于零。");
            }

            RedDotRuntimeNode node = GetRequiredNode(key);
            if (node.SelfValue == value)
            {
                return;
            }

            node.SelfValue = value;
            MarkDirtyInternal(node.Key);
        }

        /// <summary>
        /// 判断指定节点是否已由静态 Config 注册。
        /// </summary>
        /// <param name="key">待查询节点 Asset。</param>
        /// <returns>节点存在时返回 true。</returns>
        public bool HasNode(RedDotKey key)
        {
            return key != null && runtimeNodeByKeyMap.ContainsKey(key);
        }

        /// <summary>
        /// 标记一个已注册节点，在本帧末重新计算该节点和全部祖先。
        /// </summary>
        /// <param name="key">待重新计算节点 Asset。</param>
        /// <exception cref="ArgumentNullException">节点为空时抛出。</exception>
        /// <exception cref="KeyNotFoundException">节点未在静态 Config 中注册时抛出。</exception>
        public void MarkDirty(RedDotKey key)
        {
            GetRequiredNode(key);
            MarkDirtyInternal(key);
        }

        /// <summary>
        /// 订阅指定节点的定向数值变化。
        /// </summary>
        /// <param name="key">待观察节点 Asset。</param>
        /// <param name="handler">节点实际变化时执行的处理器。</param>
        /// <returns>用于解除订阅的生命周期句柄。</returns>
        /// <exception cref="ArgumentNullException">节点或处理器为空时抛出。</exception>
        /// <exception cref="KeyNotFoundException">节点未在静态 Config 中注册时抛出。</exception>
        public IUnRegister RegisterValueChanged(
            RedDotKey key,
            Action<RedDotValueChangedEvent> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            GetRequiredNode(key);
            return valueChangedEventCenter.Register(key, handler);
        }

        #endregion

#if UNITY_EDITOR
        #region Editor 调试操作

        /// <summary>
        /// 设置指定节点在当前 Play Mode 中的临时自身值覆盖。
        /// </summary>
        /// <param name="key">待覆盖节点 Asset。</param>
        /// <param name="value">非负覆盖值。</param>
        /// <exception cref="ArgumentOutOfRangeException">值小于零时抛出。</exception>
        /// <exception cref="KeyNotFoundException">节点未在静态 Config 中注册时抛出。</exception>
        public void DebugSetSelfOverride(RedDotKey key, int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "[RedDotSystem] Debug Override 不能小于零。");
            }

            RedDotRuntimeNode node = GetRequiredNode(key);
            if (node.DebugSelfOverrideValue == value)
            {
                return;
            }

            node.DebugSelfOverrideValue = value;
            MarkDirtyInternal(node.Key);
        }

        /// <summary>
        /// 清除指定节点的临时自身值覆盖。
        /// </summary>
        /// <param name="key">待清除覆盖的节点 Asset。</param>
        /// <exception cref="ArgumentNullException">节点为空时抛出。</exception>
        /// <exception cref="KeyNotFoundException">节点未在静态 Config 中注册时抛出。</exception>
        public void DebugClearOverride(RedDotKey key)
        {
            RedDotRuntimeNode node = GetRequiredNode(key);
            if (!node.DebugSelfOverrideValue.HasValue)
            {
                return;
            }

            node.DebugSelfOverrideValue = null;
            MarkDirtyInternal(node.Key);
        }

        /// <summary>
        /// 清除全部节点的临时自身值覆盖，并安排一次批量 Flush。
        /// </summary>
        public void DebugClearAllOverrides()
        {
            int clearedCount = 0;
            foreach (RedDotRuntimeNode node in runtimeNodeByKeyMap.Values)
            {
                if (!node.DebugSelfOverrideValue.HasValue)
                {
                    continue;
                }

                node.DebugSelfOverrideValue = null;
                dirtyNodeKeys.Add(node.Key);
                clearedCount++;
            }

            if (clearedCount == 0)
            {
                return;
            }

            ScheduleFlush();
            Debug.Log($"[RedDotSystem] 清除全部 Debug Override，clearedCount={clearedCount}。");
        }

        /// <summary>
        /// 通过调试入口标记指定节点等待帧末计算。
        /// </summary>
        /// <param name="key">待标脏节点 Asset。</param>
        public void DebugMarkDirty(RedDotKey key)
        {
            MarkDirty(key);
        }

        /// <summary>
        /// 立即执行当前待处理刷新，便于在 Debugger 中确定性验证聚合结果。
        /// </summary>
        public void DebugFlushNow()
        {
            FlushDirtyNodes();
        }

        /// <summary>
        /// 创建当前完整红点树的只读调试快照。
        /// </summary>
        /// <returns>按配置树顺序排序的节点快照。</returns>
        public IReadOnlyList<DebugRedDotNodeSnapshot> DebugGetSnapshot()
        {
            return GetRuntimeNodesInTreeOrder()
                .Select(node => new DebugRedDotNodeSnapshot(
                    node.Key,
                    node.Parent == null ? null : node.Parent.Key,
                    node.DerivedPath,
                    node.SelfValue,
                    node.GetEffectiveSelfValue(),
                    node.TotalValue,
                    node.DebugSelfOverrideValue.HasValue,
                    node.DebugSelfOverrideValue.GetValueOrDefault(),
                    dirtyNodeKeys.Contains(node.Key),
                    node.Children.Count))
                .ToArray();
        }

        #endregion
#endif

        #region 帧末刷新

        /// <summary>
        /// 安排一次 LastPostLateUpdate 刷新，并合并本帧的重复请求。
        /// </summary>
        private void ScheduleFlush()
        {
            if (flushScheduled || lifetimeCancellationSource == null)
            {
                return;
            }

            flushScheduled = true;
            FlushAtFrameEndAsync(lifetimeCancellationSource.Token).Forget();
        }

        /// <summary>
        /// 等待至本帧末并执行合并后的红点刷新。
        /// </summary>
        /// <param name="cancellationToken">红点系统生命周期取消令牌。</param>
        private async UniTaskVoid FlushAtFrameEndAsync(CancellationToken cancellationToken)
        {
            try
            {
                // LastPostLateUpdate 汇总本帧全部业务变化，避免 UI 观察到中间值。
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, cancellationToken);
                flushScheduled = false;
                FlushDirtyNodes();
            }
            catch (OperationCanceledException)
            {
                // 系统注销会取消尚未进入帧末的任务，此分支不属于运行错误。
                flushScheduled = false;
            }
        }

        /// <summary>
        /// 重算全部脏节点及其祖先，并按后代到根发送实际变化。
        /// </summary>
        private void FlushDirtyNodes()
        {
            if (dirtyNodeKeys.Count == 0)
            {
                flushScheduled = false;
                return;
            }

            RedDotKey[] dirtyKeys = dirtyNodeKeys.ToArray();
            dirtyNodeKeys.Clear();
            flushScheduled = false;

            var affectedNodes = new HashSet<RedDotRuntimeNode>();
            for (int index = 0; index < dirtyKeys.Length; index++)
            {
                AddNodeAndAncestors(GetRequiredNode(dirtyKeys[index]), affectedNodes);
            }

            // key：受影响节点 Asset；value：本批次计算前已经提交的 TotalValue。
            var previousTotalValueByKeyMap = affectedNodes.ToDictionary(
                node => node.Key,
                node => node.TotalValue);

            RedDotRuntimeNode[] orderedNodes = affectedNodes
                .OrderByDescending(node => node.Depth)
                .ThenBy(node => node.TreeOrder)
                .ToArray();

            // 先完成全部数值计算，再统一发布事件，保证订阅方读取到完整结果。
            for (int index = 0; index < orderedNodes.Length; index++)
            {
                RecalculateNode(orderedNodes[index]);
            }

            int changedCount = PublishChanges(orderedNodes, previousTotalValueByKeyMap);
            Debug.Log(
                $"[RedDotSystem] 完成帧末刷新，dirtyNodeCount={dirtyKeys.Length}，changedNodeCount={changedCount}。");
        }

        #endregion

        #region 静态配置组装

        /// <summary>
        /// 校验 Config 并建立全部 RedDotRuntimeNode 的 Parent/Children 引用。
        /// </summary>
        /// <param name="redDotConfig">待组装的静态配置。</param>
        private void BuildRuntimeTree(RedDotConfig redDotConfig)
        {
            IReadOnlyList<RedDotKey> configuredKeys = redDotConfig.NodeKeys;
            var configuredKeySet = new HashSet<RedDotKey>();

            // 校验节点不为空、名称合法且不重复。
            for (int index = 0; index < configuredKeys.Count; index++)
            {
                RedDotKey key = configuredKeys[index];
                if (key == null)
                {
                    throw new InvalidOperationException(
                        $"[RedDotSystem] Config {redDotConfig.name} 的节点清单包含空引用，index={index}。");
                }

                if (!configuredKeySet.Add(key))
                {
                    throw new InvalidOperationException(
                        $"[RedDotSystem] Config {redDotConfig.name} 重复注册节点：{key.name}。");
                }

                ValidateSegmentName(key);
            }

            // 校验 Parent 链不越界且不循环。
            for (int index = 0; index < configuredKeys.Count; index++)
            {
                RedDotKey key = configuredKeys[index];
                if (key.Parent != null && !configuredKeySet.Contains(key.Parent))
                {
                    throw new InvalidOperationException(
                        $"[RedDotSystem] 节点 {key.name} 的 Parent {key.Parent.name} 不属于 Config {redDotConfig.name}。");
                }

                ValidateParentChain(key, redDotConfig);
            }

            // 创建运行时节点并按 Config 顺序索引。
            for (int index = 0; index < configuredKeys.Count; index++)
            {
                RedDotKey key = configuredKeys[index];
                runtimeNodeByKeyMap.Add(
                    key,
                    new RedDotRuntimeNode(key, index));
            }

            // 建立 Parent/Children 引用并按同级排序值和 Config 顺序排序。
            for (int index = 0; index < configuredKeys.Count; index++)
            {
                RedDotKey key = configuredKeys[index];
                RedDotRuntimeNode node = runtimeNodeByKeyMap[key];
                if (key.Parent == null)
                {
                    continue;
                }

                RedDotRuntimeNode parentNode = runtimeNodeByKeyMap[key.Parent];
                node.Parent = parentNode;
                parentNode.Children.Add(node);
            }

            foreach (RedDotRuntimeNode node in runtimeNodeByKeyMap.Values)
            {
                node.Children.Sort(CompareRuntimeNodeOrder);
            }

            ValidateSiblingNames();
            AssignTreeOrder();
        }

        /// <summary>
        /// 校验节点名称不为空且不包含首尾空白。
        /// </summary>
        /// <param name="key">待校验节点 Asset。</param>
        private static void ValidateSegmentName(RedDotKey key)
        {
            if (string.IsNullOrWhiteSpace(key.SegmentName) ||
                !string.Equals(key.SegmentName, key.SegmentName.Trim(), StringComparison.Ordinal) ||
                key.SegmentName.Contains("/", StringComparison.Ordinal) ||
                key.SegmentName.Contains("\\", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"[RedDotSystem] 节点 {key.name} 的 SegmentName 无效。");
            }
        }

        /// <summary>
        /// 沿 Parent 链检测循环引用。
        /// </summary>
        /// <param name="key">待校验节点 Asset。</param>
        /// <param name="redDotConfig">当前静态配置。</param>
        private static void ValidateParentChain(RedDotKey key, RedDotConfig redDotConfig)
        {
            var visitedKeys = new HashSet<RedDotKey>();
            RedDotKey currentKey = key;
            while (currentKey != null)
            {
                if (!visitedKeys.Add(currentKey))
                {
                    throw new InvalidOperationException(
                        $"[RedDotSystem] Config {redDotConfig.name} 的 Parent 链存在循环，节点={key.name}。");
                }

                currentKey = currentKey.Parent;
            }
        }

        /// <summary>
        /// 检测每个父节点下的直接子节点名称是否重复。
        /// </summary>
        private void ValidateSiblingNames()
        {
            foreach (RedDotRuntimeNode parentNode in runtimeNodeByKeyMap.Values)
            {
                var siblingNameSet = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < parentNode.Children.Count; index++)
                {
                    RedDotRuntimeNode childNode = parentNode.Children[index];
                    if (!siblingNameSet.Add(childNode.Key.SegmentName))
                    {
                        throw new InvalidOperationException(
                            $"[RedDotSystem] 节点 {parentNode.Key.name} 下存在同级重名：{childNode.Key.SegmentName}。");
                    }
                }
            }

            var rootNameSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (RedDotRuntimeNode rootNode in runtimeNodeByKeyMap.Values.Where(node => node.Parent == null))
            {
                if (!rootNameSet.Add(rootNode.Key.SegmentName))
                {
                    throw new InvalidOperationException(
                        $"[RedDotSystem] 根节点存在同级重名：{rootNode.Key.SegmentName}。");
                }
            }
        }

        /// <summary>
        /// 按同级排序值和 Config 原始顺序比较运行时节点。
        /// </summary>
        /// <param name="left">左侧节点。</param>
        /// <param name="right">右侧节点。</param>
        /// <returns>节点顺序比较结果。</returns>
        private static int CompareRuntimeNodeOrder(
            RedDotRuntimeNode left,
            RedDotRuntimeNode right)
        {
            int orderComparison = left.Key.SiblingOrder.CompareTo(right.Key.SiblingOrder);
            return orderComparison != 0
                ? orderComparison
                : left.ConfigOrder.CompareTo(right.ConfigOrder);
        }

        /// <summary>
        /// 按根节点和直接子节点的 SiblingOrder 建立稳定的深度优先配置顺序。
        /// </summary>
        private void AssignTreeOrder()
        {
            List<RedDotRuntimeNode> orderedNodes = GetRuntimeNodesInTreeOrder();
            for (int index = 0; index < orderedNodes.Count; index++)
            {
                orderedNodes[index].TreeOrder = index;
            }
        }

        /// <summary>
        /// 获取按 Config 树顺序排列的全部运行时节点。
        /// </summary>
        /// <returns>根节点优先、同级按 SiblingOrder 排列的节点列表。</returns>
        private List<RedDotRuntimeNode> GetRuntimeNodesInTreeOrder()
        {
            var orderedNodes = new List<RedDotRuntimeNode>(runtimeNodeByKeyMap.Count);
            List<RedDotRuntimeNode> rootNodes = runtimeNodeByKeyMap.Values
                .Where(node => node.Parent == null)
                .OrderBy(node => node.Key.SiblingOrder)
                .ThenBy(node => node.ConfigOrder)
                .ToList();
            for (int index = 0; index < rootNodes.Count; index++)
            {
                AppendNodeAndChildren(rootNodes[index], orderedNodes);
            }

            return orderedNodes;
        }

        /// <summary>
        /// 递归追加一个节点及其已经排序的直接子节点。
        /// </summary>
        /// <param name="node">当前节点。</param>
        /// <param name="orderedNodes">接收深度优先节点顺序的列表。</param>
        private static void AppendNodeAndChildren(
            RedDotRuntimeNode node,
            ICollection<RedDotRuntimeNode> orderedNodes)
        {
            orderedNodes.Add(node);
            for (int index = 0; index < node.Children.Count; index++)
            {
                AppendNodeAndChildren(node.Children[index], orderedNodes);
            }
        }

        #endregion

        #region 节点计算与事件

        /// <summary>
        /// 将指定节点及全部祖先加入当前刷新受影响集合。
        /// </summary>
        /// <param name="node">受业务或调试修改的节点。</param>
        /// <param name="affectedNodes">接收受影响节点的集合。</param>
        private static void AddNodeAndAncestors(
            RedDotRuntimeNode node,
            ISet<RedDotRuntimeNode> affectedNodes)
        {
            RedDotRuntimeNode currentNode = node;
            while (currentNode != null && affectedNodes.Add(currentNode))
            {
                currentNode = currentNode.Parent;
            }
        }

        /// <summary>
        /// 根据自身值、临时覆盖和直接子节点更新 TotalValue。
        /// </summary>
        /// <param name="node">待重新计算节点。</param>
        private static void RecalculateNode(RedDotRuntimeNode node)
        {
            int value = node.GetEffectiveSelfValue();
            for (int index = 0; index < node.Children.Count; index++)
            {
                checked
                {
                    value += node.Children[index].TotalValue;
                }
            }

            node.TotalValue = value;
        }

        /// <summary>
        /// 按后代到根的顺序发送实际变化节点的定向和全局事件。
        /// </summary>
        /// <param name="orderedNodes">已经按计算顺序排列的节点。</param>
        /// <param name="previousTotalValueByKeyMap">计算前的 TotalValue 映射。</param>
        /// <returns>实际发送通知的节点数量。</returns>
        private int PublishChanges(
            IReadOnlyList<RedDotRuntimeNode> orderedNodes,
            IReadOnlyDictionary<RedDotKey, int> previousTotalValueByKeyMap)
        {
            int changedCount = 0;
            for (int index = 0; index < orderedNodes.Count; index++)
            {
                RedDotRuntimeNode node = orderedNodes[index];
                int previousValue = previousTotalValueByKeyMap[node.Key];
                if (previousValue == node.TotalValue)
                {
                    continue;
                }

                var changedEvent = new RedDotValueChangedEvent(
                    node.Key,
                    previousValue,
                    node.TotalValue);
                valueChangedEventCenter.EventTrigger(node.Key, changedEvent);
                this.SendEvent(changedEvent);
                changedCount++;
            }

            return changedCount;
        }

        /// <summary>
        /// 取得已经组装的节点并在缺失时抛出带 Asset 上下文的异常。
        /// </summary>
        /// <param name="key">待取得节点 Asset。</param>
        /// <returns>对应运行时节点。</returns>
        /// <exception cref="ArgumentNullException">节点为空时抛出。</exception>
        /// <exception cref="KeyNotFoundException">节点未注册时抛出。</exception>
        private RedDotRuntimeNode GetRequiredNode(RedDotKey key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key), "[RedDotSystem] 红点节点不能为 null。");
            }

            if (!runtimeNodeByKeyMap.TryGetValue(key, out RedDotRuntimeNode node))
            {
                throw new KeyNotFoundException(
                    $"[RedDotSystem] 找不到已注册红点节点：{key.name}。");
            }

            return node;
        }

        /// <summary>
        /// 将指定节点加入 Dirty 集合，并安排帧末刷新。
        /// </summary>
        /// <param name="key">待标脏节点 Asset。</param>
        private void MarkDirtyInternal(RedDotKey key)
        {
            if (dirtyNodeKeys.Add(key))
            {
                ScheduleFlush();
            }
        }

        #endregion

        #region 嵌套运行时状态

        /// <summary>
        /// 保存单个 RedDotKey 在当前 RedDotSystem 实例中的可变计算状态。
        /// </summary>
        private sealed class RedDotRuntimeNode
        {
            #region 字段与属性

            /// <summary>创建指定配置顺序的运行时节点。</summary>
            /// <param name="key">节点 Asset。</param>
            /// <param name="configOrder">节点在 Config 清单中的稳定顺序。</param>
            public RedDotRuntimeNode(RedDotKey key, int configOrder)
            {
                Key = key;
                ConfigOrder = configOrder;
            }

            /// <summary>获取节点 Asset。</summary>
            public RedDotKey Key { get; }

            /// <summary>获取或设置运行时直接父节点。</summary>
            public RedDotRuntimeNode Parent { get; set; }

            /// <summary>获取运行时直接子节点列表。</summary>
            public List<RedDotRuntimeNode> Children { get; } =
                new List<RedDotRuntimeNode>();

            /// <summary>获取 Config 清单中的稳定顺序。</summary>
            public int ConfigOrder { get; }

            /// <summary>获取或设置按配置树推导的稳定深度优先顺序。</summary>
            public int TreeOrder { get; set; }

            /// <summary>获取或设置业务自身值。</summary>
            public int SelfValue { get; set; }

            /// <summary>获取或设置最近一次 Flush 提交的聚合值。</summary>
            public int TotalValue { get; set; }

            /// <summary>获取或设置临时自身值覆盖。</summary>
            public int? DebugSelfOverrideValue { get; set; }

            /// <summary>获取节点在当前运行时树中的深度。</summary>
            public int Depth
            {
                get
                {
                    int depth = 0;
                    RedDotRuntimeNode currentNode = Parent;
                    while (currentNode != null)
                    {
                        depth++;
                        currentNode = currentNode.Parent;
                    }

                    return depth;
                }
            }

            /// <summary>获取临时覆盖或业务自身值中的有效自身值。</summary>
            /// <returns>当前节点用于聚合的自身值。</returns>
            public int GetEffectiveSelfValue()
            {
                return DebugSelfOverrideValue ?? SelfValue;
            }

            /// <summary>获取由节点 Asset Parent 链推导的显示路径。</summary>
            public string DerivedPath => Key.DerivedPath;

            #endregion
        }

        #endregion
    }
}
