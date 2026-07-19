using System.Collections.Generic;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI 窗口运行时注册表，作为已加载窗口和可见窗口状态的事实来源。
    /// </summary>
    internal sealed class UIWindowRegistry
    {
        private readonly Dictionary<string, UIWindowRecord> records = new Dictionary<string, UIWindowRecord>();
        private readonly List<string> visibleWindowNames = new List<string>();

        /// <summary>
        /// 注册窗口记录。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="window">窗口逻辑对象。</param>
        /// <param name="state">窗口初始状态。</param>
        /// <returns>新注册的窗口记录。</returns>
        public UIWindowRecord Register(string windowName, WindowBase window, UIWindowState state)
        {
            visibleWindowNames.Remove(windowName);
            UIWindowRecord record = new UIWindowRecord(windowName, window, state);
            records[windowName] = record;
            return record;
        }

        /// <summary>
        /// 注销指定窗口。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        public void Unregister(string windowName)
        {
            records.Remove(windowName);
            visibleWindowNames.Remove(windowName);
        }

        /// <summary>
        /// 是否存在指定窗口记录。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <returns>存在返回 true。</returns>
        public bool Contains(string windowName)
        {
            return records.ContainsKey(windowName);
        }

        /// <summary>
        /// 尝试获取窗口记录。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="record">窗口记录。</param>
        /// <returns>找到返回 true。</returns>
        public bool TryGetRecord(string windowName, out UIWindowRecord record)
        {
            return records.TryGetValue(windowName, out record);
        }

        /// <summary>
        /// 尝试获取指定类型窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>窗口存在时返回窗口对象，否则返回 null。</returns>
        public T GetWindow<T>() where T : WindowBase
        {
            string windowName = typeof(T).Name;
            return records.TryGetValue(windowName, out UIWindowRecord record) ? record.Window as T : null;
        }

        /// <summary>
        /// 标记窗口进入可见顺序，重复显示时会移动到最上层。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        public void MarkShown(string windowName)
        {
            visibleWindowNames.Remove(windowName);
            visibleWindowNames.Add(windowName);
        }

        /// <summary>
        /// 标记窗口离开可见顺序。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        public void MarkHidden(string windowName)
        {
            visibleWindowNames.Remove(windowName);
        }

        /// <summary>
        /// 获取所有可参与显示策略计算的窗口。
        /// </summary>
        /// <returns>可见或正在过渡显示/隐藏的窗口列表。</returns>
        public List<WindowBase> GetVisibleWindows()
        {
            List<WindowBase> result = new List<WindowBase>();
            foreach (string windowName in visibleWindowNames)
            {
                if (!records.TryGetValue(windowName, out UIWindowRecord record))
                {
                    continue;
                }

                if (record.Window == null || record.GameObject == null)
                {
                    continue;
                }

                if (record.State is UIWindowState.Visible or UIWindowState.Showing or UIWindowState.Hiding)
                {
                    result.Add(record.Window);
                }
            }

            return result;
        }

        /// <summary>
        /// 获取所有窗口运行时快照。
        /// </summary>
        /// <returns>所有已注册窗口的只读快照。</returns>
        public IReadOnlyList<UIWindowSnapshot> GetWindowSnapshots()
        {
            List<UIWindowSnapshot> snapshots = new List<UIWindowSnapshot>();
            foreach (UIWindowRecord record in records.Values)
            {
                snapshots.Add(CreateSnapshot(record));
            }

            return snapshots;
        }

        /// <summary>
        /// 获取当前顶层窗口快照。
        /// </summary>
        /// <param name="snapshot">顶层窗口快照。</param>
        /// <returns>存在顶层窗口时返回 true。</returns>
        public bool TryGetTopWindowSnapshot(out UIWindowSnapshot snapshot)
        {
            for (int i = visibleWindowNames.Count - 1; i >= 0; i--)
            {
                if (!records.TryGetValue(visibleWindowNames[i], out UIWindowRecord record))
                {
                    continue;
                }

                if (record.Window == null || record.GameObject == null)
                {
                    continue;
                }

                if (record.State == UIWindowState.Visible ||
                    record.State == UIWindowState.Showing ||
                    record.State == UIWindowState.Hiding)
                {
                    snapshot = CreateSnapshot(record);
                    return true;
                }
            }

            snapshot = UIWindowSnapshot.Empty;
            return false;
        }

        /// <summary>
        /// 根据窗口记录创建只读快照。
        /// </summary>
        /// <param name="record">窗口记录。</param>
        /// <returns>窗口运行时快照。</returns>
        public UIWindowSnapshot CreateSnapshot(UIWindowRecord record)
        {
            int sortingOrder = record.Canvas != null ? record.Canvas.sortingOrder : 0;
            int siblingIndex = record.Transform != null ? record.Transform.GetSiblingIndex() : -1;
            bool hasMask = record.Transform != null && record.Transform.Find("UIMask") != null;
            return new UIWindowSnapshot(
                record.WindowName,
                record.State,
                record.Window != null && record.Window.Visible,
                sortingOrder,
                siblingIndex,
                record.Window != null && record.Window.FullScreenWindow,
                hasMask,
                record.GameObject != null);
        }
    }
}
