using UnityEngine;
using WS_Modules.UIModule;

namespace RPG.InteractionSystem.UI
{
    /// <summary>保存 InteractionOptionWindow 的 WSFrame 窗口配置和绑定入口。</summary>
    public sealed class InteractionOptionWindowDataComponent : MonoBehaviour
    {
        #region 配置

        /// <summary>交互 HUD 不是全屏窗口。</summary>
        public bool IsFullWindow;

        /// <summary>交互列表不播放弹窗动画。</summary>
        public bool DoAnimation;

        #endregion

        #region 初始化

        /// <summary>初始化窗口绑定；当前窗口使用运行时生成的列表节点。</summary>
        /// <param name="target">待初始化的窗口。</param>
        public void InitComponent(WindowBase target)
        {
            // 当前窗口没有需要由生成器绑定的静态按钮，保留入口以符合 WSFrame WindowBase 生命周期。
        }

        #endregion
    }
}
