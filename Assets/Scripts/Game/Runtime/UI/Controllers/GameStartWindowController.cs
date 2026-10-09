using System;
using Cysharp.Threading.Tasks;
using RPG.Game;
using RPG.Game.UI;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>协调开始按钮、场景加载请求及请求前失败反馈。</summary>
    public sealed class GameStartWindowController : UnityEngine.MonoBehaviour
    {
        #region 依赖字段

        private GameStartWindowDataComponent data;

        #endregion

        #region 请求状态

        private bool requestActive;

        #endregion

        #region 初始化与释放

        /// <summary>接收 Prefab 显式配置并初始化按钮文本及可交互状态。</summary>
        /// <param name="sourceData">保存该窗口控件和目标场景的 DataComponent。</param>
        public void Initialize(GameStartWindowDataComponent sourceData)
        {
            data = sourceData;
            data.TitleText.text = "探索与冒险";
            data.StartButtonText.text = "开始游戏";
            data.StatusText.text = string.Empty;
            data.StartButton.interactable = true;
        }

        /// <summary>窗口销毁时移除业务状态引用。</summary>
        public void Dispose()
        {
            data = null;
            requestActive = false;
        }

        /// <summary>窗口再次显示时清除上次终态提示，并允许重新开始一次完整流程。</summary>
        public void ResetForOpen()
        {
            bool recoveredRequest = requestActive ||
                                    data != null && (!data.StartButton.interactable ||
                                                     !string.IsNullOrEmpty(data.StatusText.text));
            requestActive = false;
            if (data == null) return;
            data.StatusText.text = string.Empty;
            data.StartButton.interactable = true;
            if (recoveredRequest)
                WSLog.Log("[GameStartWindowController] 开始窗口重新显示，已清除上次请求状态。");
        }

        #endregion

        #region 用户操作

        /// <summary>接收开始按钮请求并拒绝尚未结束的重复调用。</summary>
        public void RequestStart()
        {
            if (requestActive) return;
            requestActive = true;
            data.StartButton.interactable = false;
            data.StatusText.text = string.Empty;
            LoadTargetSceneAsync().Forget(HandleRequestFailure);
        }

        /// <summary>直接使用窗口持有的目标配置进入统一加载流程，不绑定开始场景对象的销毁令牌。</summary>
        /// <returns>目标 Scene 及所有场景准备任务完成后的异步任务。</returns>
        private async UniTask LoadTargetSceneAsync()
        {
            SceneLoadingSystem sceneLoadingSystem = GameArchitecture.Interface.GetSystem<SceneLoadingSystem>();
            SceneLoadConfig targetSceneConfig = data.TargetSceneConfig;
            WSLog.Log($"[GameStartWindowController] 提交开始游戏请求，sceneId={targetSceneConfig.SceneId}。");
            await sceneLoadingSystem.LoadAsync(targetSceneConfig);
        }

        /// <summary>显示加载异常，并允许用户在关闭终态提示后重新发起请求。</summary>
        /// <param name="exception">加载系统返回的失败或取消异常。</param>
        private void HandleRequestFailure(Exception exception)
        {
            requestActive = false;
            if (data == null) return;
            data.StartButton.interactable = true;
            data.StatusText.text = exception.Message;
            WSLog.LogError($"[GameStartWindowController] 开始游戏请求失败，sceneId={data.TargetSceneConfig.SceneId}，exception={exception}");
        }

        #endregion
    }
}
