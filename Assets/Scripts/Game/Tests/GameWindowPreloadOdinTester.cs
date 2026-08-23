#if UNITY_EDITOR
using Cysharp.Threading.Tasks;
using RPG.Game.UI;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.UIModule;

namespace RPG.Game.Tests
{
    /// <summary>
    /// 通过 Odin Inspector 手动验证项目窗口预加载服务及其场景预热契约。
    /// </summary>
    public sealed class GameWindowPreloadOdinTester : MonoBehaviour
    {
        #region 手动测试入口

        /// <summary>启动一次可重复等待的全窗口预加载并输出完成状态。</summary>
        [Button("预加载全部窗口")]
        public void PreloadAllWindows()
        {
            GameWindowPreloadService service = GameWindowPreloadService.Instance;
            if (service == null)
            {
                Debug.LogError("[WindowPreloadTest] 场景中未找到 GameWindowPreloadService。", this);
                return;
            }

            PreloadAllWindowsAsync(service).Forget();
        }

        #endregion

        #region 异步验收

        /// <summary>
        /// 等待服务完成后确认三个窗口实例均已注册且仍处于隐藏状态。
        /// </summary>
        /// <param name="service">待验收的窗口预加载服务。</param>
        private async UniTaskVoid PreloadAllWindowsAsync(GameWindowPreloadService service)
        {
            await service.PreloadAsync();

            bool hasHud = UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow);
            bool hasChoice = UIManager.Instance.TryGetWindow<ChoiceWindow>(out ChoiceWindow choiceWindow);
            bool hasDialogue = UIManager.Instance.TryGetWindow<DialogueWindow>(out DialogueWindow dialogueWindow);
            bool hidden = hasHud && hasChoice && hasDialogue &&
                          !hudWindow.Visible && !choiceWindow.Visible && !dialogueWindow.Visible;

            Debug.Log($"[WindowPreloadTest] preloaded={service.IsPreloaded}, hud={hasHud}, choice={hasChoice}, dialogue={hasDialogue}, hidden={hidden}", this);
        }

        #endregion
    }
}
#endif
