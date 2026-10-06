using System;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.SceneModule;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>将场景进度快照映射成加载窗口的蒙版进度与终态提示。</summary>
    [DisallowMultipleComponent]
    [InfoBox("LoadingWindow 在初始化时显式传入同根 LoadingWindowDataComponent；Prefab 缺少组件时初始化必须失败。")]
    public sealed class LoadingWindowController : MonoBehaviour
    {
        #region 依赖与状态字段

        // 同根窗口数据组件拥有实际 UGUI 控件，控制器只负责快照投影和用户关闭意图。
        private LoadingWindowDataComponent data;
        private Action closeRequested;
        private string sceneId;
        private bool initialized;
        private bool isTerminal;

        #endregion

        #region 生命周期与绑定

        /// <summary>接收已校验的数据组件并登记终态关闭按钮。</summary>
        /// <param name="windowData">保存 Prefab 静态 UGUI 引用的数据组件。</param>
        /// <param name="closeRequested">窗口收到关闭意图时执行的回调。</param>
        /// <exception cref="ArgumentNullException">没有提供数据组件或关闭回调时抛出。</exception>
        public void Initialize(LoadingWindowDataComponent windowData, Action closeRequested)
        {
            if (initialized) return;
            data = windowData ?? throw new ArgumentNullException(nameof(windowData));
            this.closeRequested = closeRequested ?? throw new ArgumentNullException(nameof(closeRequested));
            data.CloseButton.onClick.AddListener(HandleCloseClicked);
            initialized = true;
            WSLog.Log("[LoadingWindowController] Prefab 进度视图和终态关闭意图已绑定。");
        }

        /// <summary>解除按钮回调和数据引用，供窗口销毁阶段成对调用。</summary>
        public void Dispose()
        {
            if (!initialized) return;
            data.CloseButton.onClick.RemoveListener(HandleCloseClicked);
            closeRequested = null;
            data = null;
            initialized = false;
            isTerminal = false;
            WSLog.Log("[LoadingWindowController] 进度视图和终态关闭意图已解绑。");
        }

        #endregion

        #region 快照投影

        /// <summary>获取当前窗口最近显示的场景 ID。</summary>
        public string SceneId => sceneId;
        /// <summary>根据不可变流程快照刷新提示区、蒙版宽度和百分比。</summary>
        /// <param name="snapshot">完整任务树状态。</param>
        /// <exception cref="InvalidOperationException">Controller 尚未初始化时抛出。</exception>
        public void Render(SceneLoadExecutionSnapshot snapshot)
        {
            if (!initialized)
                throw new InvalidOperationException("[LoadingWindowController] 渲染前必须完成 Initialize。");

            sceneId = snapshot.SceneId;
            isTerminal = snapshot.State != E_SceneLoadExecutionState.Loading;
            string failureMessage = snapshot.State switch
            {
                E_SceneLoadExecutionState.Failed => string.IsNullOrWhiteSpace(snapshot.FailureMessage)
                    ? "场景加载失败，请查看 Editor Log。"
                    : LimitFailureMessage(snapshot.FailureMessage),
                E_SceneLoadExecutionState.Cancelled => "加载已取消。关闭提示后可由调用方重新发起加载。",
                _ => string.Empty
            };

            data.Render(snapshot.Progress, failureMessage,
                snapshot.State is E_SceneLoadExecutionState.Failed or E_SceneLoadExecutionState.Cancelled,
                "关闭提示");
        }

        /// <summary>限制异常详情长度，保留主要原因并避免长堆栈挤出提示区域。</summary>
        /// <param name="message">失败任务异常消息。</param>
        /// <returns>适合在加载窗口中显示的错误摘要。</returns>
        private static string LimitFailureMessage(string message)
        {
            const int maxLength = 240;
            return message.Length <= maxLength ? message : message.Substring(0, maxLength - 1) + "…";
        }

        /// <summary>仅在失败或取消终态响应用户关闭意图。</summary>
        private void HandleCloseClicked()
        {
            if (!isTerminal) return;
            closeRequested?.Invoke();
        }

        #endregion
    }
}
