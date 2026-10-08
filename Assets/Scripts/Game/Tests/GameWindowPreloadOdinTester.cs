#if UNITY_EDITOR
using RPG.Game.UI;
using Sirenix.OdinInspector;
using System;
using UnityEngine;
using WS_Modules.UIModule;

namespace RPG.Game.Tests
{
    /// <summary>
    /// 通过 Odin Inspector 手动预加载 HUD，并确认 UIManager 复用窗口实例。
    /// </summary>
    public sealed class GameWindowPreloadOdinTester : MonoBehaviour
    {
        #region 手动测试入口

        /// <summary>独立预加载 HUD 并输出 UIManager 的实例注册结果。</summary>
        [Button("预加载 HUD")]
        public async void PreloadHud()
        {
            if (UIManager.Instance == null || !UIManager.Instance.IsInitialized)
            {
                Debug.LogError("[WindowPreloadTest] UIManager 尚未初始化。", this);
                return;
            }

            try
            {
                await UIManager.Instance.PreLoadWindowAsync<HUDWindow>();
                bool registered = UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow);
                Debug.Log($"[WindowPreloadTest] HUD registered={registered}, visible={registered && hudWindow.Visible}", this);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[WindowPreloadTest] HUD 预加载失败，exception={exception}", this);
            }
        }

        #endregion

    }
}
#endif
