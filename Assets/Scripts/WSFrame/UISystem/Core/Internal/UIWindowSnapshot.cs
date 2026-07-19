namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI 窗口运行时只读快照，用于对外查看窗口状态，避免暴露内部可变集合。
    /// </summary>
    public readonly struct UIWindowSnapshot
    {
        /// <summary>
        /// 创建窗口运行时快照。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="state">窗口状态。</param>
        /// <param name="visible">窗口是否处于可见状态。</param>
        /// <param name="sortingOrder">窗口 Canvas 排序层级。</param>
        /// <param name="siblingIndex">窗口在父节点下的顺序。</param>
        /// <param name="fullScreenWindow">是否是全屏窗口。</param>
        /// <param name="hasMask">是否存在遮罩节点。</param>
        /// <param name="hasGameObject">是否已经绑定 GameObject。</param>
        public UIWindowSnapshot(
            string windowName,
            UIWindowState state,
            bool visible,
            int sortingOrder,
            int siblingIndex,
            bool fullScreenWindow,
            bool hasMask,
            bool hasGameObject)
        {
            WindowName = windowName;
            State = state;
            Visible = visible;
            SortingOrder = sortingOrder;
            SiblingIndex = siblingIndex;
            FullScreenWindow = fullScreenWindow;
            HasMask = hasMask;
            HasGameObject = hasGameObject;
        }

        /// <summary>
        /// 空快照，用于没有顶层窗口时返回默认值。
        /// </summary>
        public static UIWindowSnapshot Empty => new UIWindowSnapshot(string.Empty, UIWindowState.Hidden, false, 0, -1, false, false, false);

        /// <summary>
        /// 窗口名称。
        /// </summary>
        public string WindowName { get; }

        /// <summary>
        /// 窗口状态。
        /// </summary>
        public UIWindowState State { get; }

        /// <summary>
        /// 窗口是否处于可见状态。
        /// </summary>
        public bool Visible { get; }

        /// <summary>
        /// 窗口 Canvas 排序层级。
        /// </summary>
        public int SortingOrder { get; }

        /// <summary>
        /// 窗口在父节点下的顺序。
        /// </summary>
        public int SiblingIndex { get; }

        /// <summary>
        /// 是否是全屏窗口。
        /// </summary>
        public bool FullScreenWindow { get; }

        /// <summary>
        /// 是否存在遮罩节点。
        /// </summary>
        public bool HasMask { get; }

        /// <summary>
        /// 是否已经绑定 GameObject。
        /// </summary>
        public bool HasGameObject { get; }
    }
}
