using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.Game;
using RPG.InteractionSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace RPG.Game.Loading.Interaction
{
    /// <summary>玩家进入指定区域后提供一个通过统一加载流程切换场景的交互命令。</summary>
    [DisallowMultipleComponent]
    [InfoBox("交互区域必须绑定 Trigger BoxCollider，且位于玩家 InteractionDetector 的 detectionMask 中。进入区域只显示选项，选择交互后才开始加载。")]
    public sealed class SceneTransitionInteractable : InteractableObject
    {
        #region 依赖字段

        [SerializeField, Required, LabelText("目标场景配置")]
        private SceneLoadConfig targetSceneConfig;
        [SerializeField, Required, LabelText("交互区域")]
        private BoxCollider interactionArea;
        [SerializeField, LabelText("选项文案")]
        private string optionDisplayName = "进入首领场景";
        [SerializeField, LabelText("选项图标")]
        private Sprite optionIcon;
        [SerializeField, LabelText("优先级")]
        private int priority;

        #endregion

        #region 运行时状态

        private InteractionOption transitionOption;
        private bool requestInProgress;

        #endregion

        #region Unity 生命周期

        /// <summary>校验 Inspector 配置并缓存该交互对象稳定的 Option。</summary>
        private void Awake()
        {
            if (targetSceneConfig == null)
                throw new InvalidOperationException($"[SceneTransitionInteractable] '{name}' 缺少目标 SceneLoadConfig。");
            if (string.IsNullOrWhiteSpace(targetSceneConfig.SceneId))
                throw new InvalidOperationException($"[SceneTransitionInteractable] '{name}' 的目标配置缺少 SceneId。");
            if (interactionArea == null || !interactionArea.isTrigger)
                throw new InvalidOperationException($"[SceneTransitionInteractable] '{name}' 必须绑定 Trigger BoxCollider。");
            if (string.IsNullOrWhiteSpace(optionDisplayName))
                throw new InvalidOperationException($"[SceneTransitionInteractable] '{name}' 的交互文案不能为空。");

            transitionOption = new InteractionOption(
                new InteractionOptionId(GetInstanceID(), "LoadTargetScene"),
                optionDisplayName,
                gameObject,
                transform,
                priority,
                0f,
                CanStartTransition,
                StartTransition,
                optionIcon);
            Debug.Log($"[SceneTransitionInteractable] 已准备场景转换选项，object={name}，sceneId={targetSceneConfig.SceneId}。", this);
        }

        #endregion

        #region 交互选项

        /// <summary>仅在移动角色位于盒形区域内时提供缓存的转换命令。</summary>
        /// <param name="context">当前玩家对象和角色移动 Transform。</param>
        /// <param name="results">交互检测器复用的候选 Option 列表。</param>
        public override void CollectInteractionOptions(in InteractionQueryContext context,
            List<InteractionOption> results)
        {
            if (transitionOption != null && IsInsideInteractionArea(context.InteractorTransform.position))
                results.Add(transitionOption);
        }

        /// <summary>重新检查范围、玩家就绪状态和场景加载互斥，防止执行过期选项。</summary>
        /// <param name="interactor">请求执行的稳定玩家对象。</param>
        /// <returns>此刻可以开始场景加载时返回 true。</returns>
        private bool CanStartTransition(GameObject interactor)
        {
            PlayerController playerController = PlayerController.Instance;
            if (interactor == null || playerController == null || !playerController.IsReady || requestInProgress)
                return false;
            if (playerController.gameObject != interactor ||
                !IsInsideInteractionArea(playerController.CharacterRoot.position))
                return false;

            return !GameArchitecture.Interface.GetSystem<SceneLoadingSystem>().IsLoading;
        }

        /// <summary>接受一次有效请求并启动不依赖当前场景对象寿命的加载操作。</summary>
        /// <param name="interactor">发起交互的稳定玩家对象。</param>
        /// <returns>已接受加载请求时返回 true。</returns>
        private bool StartTransition(GameObject interactor)
        {
            if (!CanStartTransition(interactor)) return false;

            requestInProgress = true;
            string sceneId = targetSceneConfig.SceneId;
            WSLog.Log($"[SceneTransitionInteractable] 已接受场景切换请求，sceneId={sceneId}，source={name}。");
            LoadTargetSceneAsync(sceneId).Forget();
            return true;
        }

        /// <summary>等待目标场景任务树结束并记录失败；Single 加载会销毁本 Provider 所在对象。</summary>
        /// <param name="sceneId">目标场景稳定 ID。</param>
        /// <returns>目标场景统一加载流程。</returns>
        private async UniTask LoadTargetSceneAsync(string sceneId)
        {
            try
            {
                await GameArchitecture.Interface.GetSystem<SceneLoadingSystem>().LoadAsync(sceneId);
            }
            catch (Exception exception)
            {
                WSLog.LogError($"[SceneTransitionInteractable] 场景交互加载失败，sceneId={sceneId}，exception={exception}");
            }
            finally
            {
                requestInProgress = false;
            }
        }

        /// <summary>将角色世界坐标换算到盒体局部坐标后，判断是否仍位于交互区域内。</summary>
        /// <param name="worldPosition">角色根节点的世界坐标。</param>
        /// <returns>位置处于 BoxCollider 范围内时返回 true。</returns>
        private bool IsInsideInteractionArea(Vector3 worldPosition)
        {
            Vector3 localOffset = interactionArea.transform.InverseTransformPoint(worldPosition) - interactionArea.center;
            Vector3 halfSize = interactionArea.size * 0.5f;
            return Mathf.Abs(localOffset.x) <= halfSize.x &&
                   Mathf.Abs(localOffset.y) <= halfSize.y &&
                   Mathf.Abs(localOffset.z) <= halfSize.z;
        }

        #endregion
    }
}
