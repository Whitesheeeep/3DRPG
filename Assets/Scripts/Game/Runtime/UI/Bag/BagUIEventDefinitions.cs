using System;
using RPG.ItemSystem;

namespace RPG.Game.UI.Bag
{
    /// <summary>区分背包窗口请求来源，供协调器记录和诊断。</summary>
    public enum BagWindowRequestSource
    {
        /// <summary>来自键盘或手柄背包快捷键。</summary>
        Shortcut,
        /// <summary>来自键盘或手柄取消快捷键。</summary>
        CancelShortcut,
        /// <summary>来自窗口内关闭按钮。</summary>
        CloseButton
    }

    /// <summary>请求切换背包窗口显隐的类型化事件。</summary>
    public readonly struct BagWindowToggleRequestedEventArgs
    {
        /// <summary>创建背包窗口切换请求。</summary>
        /// <param name="source">请求来源。</param>
        public BagWindowToggleRequestedEventArgs(BagWindowRequestSource source) => Source = source;

        /// <summary>获取请求来源。</summary>
        public BagWindowRequestSource Source { get; }
    }

    /// <summary>请求关闭当前游戏窗口的类型化事件。</summary>
    public readonly struct GameWindowCancelRequestedEventArgs
    {
        /// <summary>创建窗口取消请求。</summary>
        /// <param name="source">请求来源。</param>
        public GameWindowCancelRequestedEventArgs(BagWindowRequestSource source) => Source = source;

        /// <summary>获取请求来源。</summary>
        public BagWindowRequestSource Source { get; }
    }

    /// <summary>请求删除一个武器实例的只读事件参数；本轮只发送请求，不执行删除。</summary>
    public readonly struct BagWeaponDeleteRequestedEventArgs
    {
        /// <summary>创建武器删除请求。</summary>
        /// <param name="instanceId">目标武器实例。</param>
        public BagWeaponDeleteRequestedEventArgs(EquipmentInstanceId instanceId) => InstanceId = instanceId;

        /// <summary>获取目标武器实例。</summary>
        public EquipmentInstanceId InstanceId { get; }
    }

    /// <summary>请求查看一个背包条目详情的只读事件参数。</summary>
    public readonly struct BagItemDetailsRequestedEventArgs
    {
        /// <summary>创建背包详情请求。</summary>
        /// <param name="entryKey">目标条目键。</param>
        public BagItemDetailsRequestedEventArgs(BagEntryKey entryKey) => EntryKey = entryKey;

        /// <summary>获取目标条目键。</summary>
        public BagEntryKey EntryKey { get; }
    }
}
