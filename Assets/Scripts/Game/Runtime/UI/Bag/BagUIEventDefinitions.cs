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
        /// <summary>来自 HUD 上的 Bag UGUI 按钮。</summary>
        HudButton,
        /// <summary>来自窗口内关闭按钮。</summary>
        CloseButton,
        /// <summary>来自 Odin 手动测试器。</summary>
        Tester
    }

    /// <summary>请求打开背包窗口；具体显隐顺序由 BagWindowFlowCoordinator 协调。</summary>
    public readonly struct BagWindowOpenRequestedEventArgs
    {
        /// <summary>创建背包窗口打开请求。</summary>
        /// <param name="source">请求来源。</param>
        public BagWindowOpenRequestedEventArgs(BagWindowRequestSource source) => Source = source;

        /// <summary>获取请求来源。</summary>
        public BagWindowRequestSource Source { get; }
    }

    /// <summary>请求关闭背包窗口的类型化事件。</summary>
    public readonly struct BagWindowCloseRequestedEventArgs
    {
        /// <summary>创建背包窗口关闭请求。</summary>
        /// <param name="source">请求来源。</param>
        public BagWindowCloseRequestedEventArgs(BagWindowRequestSource source) => Source = source;

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

}
