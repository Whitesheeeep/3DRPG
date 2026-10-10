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
    [InfoBox("目标场景优先使用 SceneLoadConfig；未指定时必须选择目标 SceneId。交互区域必须绑定 Trigger BoxCollider，且位于玩家 InteractionDetector 的 detectionMask 中。进入区域只显示选项，选择交互后才开始加载。")]
    public sealed class SceneTransitionInteractable : InteractableObject
    {
        #region 依赖字段

        // 目标配置为可选依赖；指定时始终优先于 SceneId。
        [SerializeField, LabelText("目标场景配置"), Tooltip("指定后优先使用该配置；为空时使用下方 SceneId。")]
        private SceneLoadConfig targetSceneConfig;
        [SerializeField, SceneIdDropdown, ShowIf("@targetSceneConfig == null"), LabelText("目标场景 ID"), Tooltip("仅在未指定目标场景配置时生效。")]
        private string targetSceneId = string.Empty;
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
            string sceneId = targetSceneConfig != null ? targetSceneConfig.SceneId : targetSceneId;
            if (targetSceneConfig != null && string.IsNullOrWhiteSpace(sceneId))
                throw new InvalidOperationException($"[SceneTransitionInteractable] '{name}' 的目标配置缺少 SceneId。");
            if (targetSceneConfig == null && string.IsNullOrWhiteSpace(sceneId))
                throw new InvalidOperationException($"[SceneTransitionInteractable] '{name}' 未指定目标 SceneLoadConfig 或 SceneId。");
            if (interactionArea == null)
                throw new InvalidOperationException($"[SceneTransitionInteractable] '{name}' 为空。");
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
            Debug.Log($"[SceneTransitionInteractable] 已准备场景转换选项，object={name}，sceneId={sceneId}，source={(targetSceneConfig != null ? "SceneLoadConfig" : "SceneId")}。", this);
        }

        #endregion

        #region 交互选项

        /// <summary>将缓存的场景转换命令加入交互检测器提供的候选列表。</summary>
        /// <param name="context">当前玩家对象和角色移动 Transform。</param>
        /// <param name="results">交互检测器复用的候选 Option 列表。</param>
        public override void CollectInteractionOptions(in InteractionQueryContext context,
            List<InteractionOption> results)
        {
            if (transitionOption != null)
                results.Add(transitionOption);
        }

        /// <summary>重新检查玩家身份、就绪状态和场景加载互斥，防止执行过期选项。</summary>
        /// <param name="interactor">请求执行的稳定玩家对象。</param>
        /// <returns>此刻可以开始场景加载时返回 true。</returns>
        private bool CanStartTransition(GameObject interactor)
        {
            PlayerController playerController = PlayerController.Instance;
            if (interactor == null || playerController == null || !playerController.IsReady || requestInProgress)
                return false;
            if (playerController.gameObject != interactor)
                return false;

            return !GameArchitecture.Interface.GetSystem<SceneLoadingSystem>().IsLoading;
        }

        /// <summary>接受一次有效请求并在异步切换前捕获目标配置和 SceneId。</summary>
        /// <param name="interactor">发起交互的稳定玩家对象。</param>
        /// <returns>已接受加载请求时返回 true。</returns>
        private bool StartTransition(GameObject interactor)
        {
            if (!CanStartTransition(interactor)) return false;

            // 提前捕获值而非在异步加载中回读组件字段，Single 切场会销毁此交互对象。
            SceneLoadConfig sceneConfig = targetSceneConfig;
            string sceneId = sceneConfig != null ? sceneConfig.SceneId : targetSceneId;
            requestInProgress = true;
            WSLog.Log($"[SceneTransitionInteractable] 已接受场景切换请求，sceneId={sceneId}，source={(sceneConfig != null ? "SceneLoadConfig" : "SceneId")}，interactable={name}。");
            LoadTargetSceneAsync(sceneConfig, sceneId).Forget();
            return true;
        }

        /// <summary>按已捕获的配置或 SceneId 执行加载，并记录失败后释放请求互斥状态。</summary>
        /// <param name="sceneConfig">在场景替换前捕获的目标场景资产配置。</param>
        /// <param name="sceneId">在场景替换前捕获的目标稳定场景标识。</param>
        /// <returns>目标场景统一加载流程。</returns>
        private async UniTask LoadTargetSceneAsync(SceneLoadConfig sceneConfig, string sceneId)
        {
            try
            {
                SceneLoadingSystem sceneLoadingSystem = GameArchitecture.Interface.GetSystem<SceneLoadingSystem>();
                if (sceneConfig != null)
                    await sceneLoadingSystem.LoadAsync(sceneConfig);
                else
                    await sceneLoadingSystem.LoadAsync(sceneId);
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
