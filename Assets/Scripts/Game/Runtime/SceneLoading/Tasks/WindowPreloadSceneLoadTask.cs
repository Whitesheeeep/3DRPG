using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.DialogueSystemModule;
using RPG.Game.UI;
using RPG.InteractionSystem.UI;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;
using WS_Modules.UIModule;

namespace RPG.Game.Loading.Tasks
{
    /// <summary>按场景流程准备六个项目窗口，并将实际完成数汇报到加载进度。</summary>
    [CreateAssetMenu(fileName = "WindowPreloadSceneLoadTask", menuName = "RPG/Scene Loading/Preload Windows")]
    public sealed class WindowPreloadSceneLoadTask : SceneLoadTask
    {
        #region 场景依赖

        /// <summary>业务窗口初始化依赖目标场景中的 Player 完成初始化。</summary>
        public override bool RequiresTargetSceneReady => true;

        #endregion

        #region 执行

        /// <summary>逐个复用 UIManager 中的窗口实例并等待内部 View 初始化。</summary>
        /// <param name="context">本次流程上下文。</param>
        /// <param name="cancellationToken">取消后续等待；已开始的 UIManager 窗口创建会完成其自身生命周期。</param>
        /// <returns>所有配置窗口完成初始化后的异步任务。</returns>
        public override async UniTask ExecuteAsync(SceneLoadContext context, CancellationToken cancellationToken)
        {
            // 在 UIManager 实例化首个窗口前校验场景和玩家依赖，避免 Controller 初始化到一半才发现前置条件缺失。
            if (!context.TargetSceneReady)
            {
                string message = $"[WindowPreloadSceneLoadTask] 目标场景尚未就绪，无法初始化业务窗口，sceneId={context.Config.SceneId}，taskPath={context.TaskPath}。";
                WSLog.LogError(message);
                throw new InvalidOperationException(message);
            }

            PlayerController playerController = PlayerController.Instance;
            if (playerController == null || !playerController.IsReady)
            {
                string message = $"[WindowPreloadSceneLoadTask] PlayerController 尚未完成初始化，无法初始化依赖玩家数据的业务窗口，sceneId={context.Config.SceneId}，taskPath={context.TaskPath}。";
                WSLog.LogError(message);
                throw new InvalidOperationException(message);
            }

            WSLog.Log($"[WindowPreloadSceneLoadTask] 开始等待窗口预加载，sceneId={context.Config.SceneId}。");
            // 直接打开目标场景时，框架根节点由场景引导组件较早创建；这里仍等待 UIManager 完成初始化再调用窗口 API。
            await UniTask.WaitUntil(() => UIManager.Instance != null && UIManager.Instance.IsInitialized)
                .AttachExternalCancellation(cancellationToken);
            Type[] windowTypes =
            {
                typeof(HUDWindow),
                typeof(ChoiceWindow),
                typeof(DialogueWindow),
                typeof(BagWindow),
                typeof(CharacterWindow),
                typeof(EquipmentDevelopmentWindow)
            };
            int completedWindowCount = 0;
            foreach (Type windowType in windowTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await PreloadWindowAsync(windowType, cancellationToken);
                completedWindowCount++;
                context.ReportProgress(completedWindowCount / (float)windowTypes.Length);
            }
            WSLog.Log($"[WindowPreloadSceneLoadTask] 窗口预加载完成，sceneId={context.Config.SceneId}。");
        }

        /// <summary>通过 UIManager 复用或创建一种窗口，并等待该窗口特有的内部资源准备。</summary>
        /// <param name="windowType">需要准备的项目窗口类型。</param>
        /// <param name="cancellationToken">窗口创建与内部初始化之间的协作式取消令牌。</param>
        /// <returns>窗口及其内部 View 完成初始化后的异步任务。</returns>
        /// <exception cref="OperationCanceledException">当前窗口准备前或准备后发现流程已取消时抛出。</exception>
        private static async UniTask PreloadWindowAsync(Type windowType, CancellationToken cancellationToken)
        {
            await PreloadWindowByTypeAsync(windowType).AttachExternalCancellation(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (windowType == typeof(ChoiceWindow))
            {
                if (!UIManager.Instance.TryGetWindow(out ChoiceWindow choiceWindow))
                    throw new InvalidOperationException("[WindowPreloadSceneLoadTask] ChoiceWindow 预加载后未找到实例。");
                await choiceWindow.WaitUntilReadyAsync().AttachExternalCancellation(cancellationToken);
                choiceWindow.ActivateInteractionController();
            }
            else if (windowType == typeof(DialogueWindow))
            {
                if (!UIManager.Instance.TryGetWindow(out DialogueWindow dialogueWindow))
                    throw new InvalidOperationException("[WindowPreloadSceneLoadTask] DialogueWindow 预加载后未找到实例。");
                await dialogueWindow.WaitUntilReadyAsync().AttachExternalCancellation(cancellationToken);
            }
        }

        /// <summary>按运行时类型分派到 UIManager 保持静态类型契约的预加载入口。</summary>
        /// <param name="windowType">六种固定项目窗口之一。</param>
        /// <returns>窗口实例创建并注册完成后的异步任务。</returns>
        private static UniTask PreloadWindowByTypeAsync(Type windowType)
        {
            if (windowType == typeof(HUDWindow)) return UIManager.Instance.PreLoadWindowAsync<HUDWindow>();
            if (windowType == typeof(ChoiceWindow)) return UIManager.Instance.PreLoadWindowAsync<ChoiceWindow>();
            if (windowType == typeof(DialogueWindow)) return UIManager.Instance.PreLoadWindowAsync<DialogueWindow>();
            if (windowType == typeof(BagWindow)) return UIManager.Instance.PreLoadWindowAsync<BagWindow>();
            if (windowType == typeof(CharacterWindow)) return UIManager.Instance.PreLoadWindowAsync<CharacterWindow>();
            if (windowType == typeof(EquipmentDevelopmentWindow))
                return UIManager.Instance.PreLoadWindowAsync<EquipmentDevelopmentWindow>();
            throw new ArgumentOutOfRangeException(nameof(windowType), windowType, "未配置的项目窗口类型。");
        }

        #endregion
    }
}
