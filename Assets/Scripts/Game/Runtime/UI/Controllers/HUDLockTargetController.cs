using RPG.Character;
using RPG.Character.Combat;
using Sirenix.OdinInspector;
using UnityEngine;
using RPG.Game.UI.Views.HUD;
using WS_Modules.GAS.AbilitySystemComponent;

namespace RPG.Game.UI.Controllers
{
    /// <summary>连接全局锁定状态、Active 玩家摄像机和静态 HUD 锁定标记。</summary>
    [DefaultExecutionOrder(200), DisallowMultipleComponent]
    [InfoBox("依赖 HUD Prefab 显式绑定的 HUDLockTargetView；通过 LockTargetSystem、PlayerController 单例读取运行时状态，并读取锁定 ASC Owner RootTransform 子层级中的 Renderer。")]
    public sealed class HUDLockTargetController : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required] private HUDLockTargetView lockTargetView;
        [SerializeField, MinValue(0f)] private float fallbackWorldHeight = 1f;
        private LockTargetSystem lockTargetSystem;
        private GameplayAbilitySystemComponent rendererOwnerTarget;
        private Renderer[] targetRenderers = System.Array.Empty<Renderer>();

        #endregion

        #region 状态字段

        private bool initialized;
        private bool visible;

        #endregion

        #region 生命周期

        /// <summary>校验静态 View 引用并清除 Prefab 编辑态的占位锁定显示。</summary>
        /// <exception cref="System.InvalidOperationException">HUD Prefab 缺少静态 View 时抛出。</exception>
        public void Initialize()
        {
            if (initialized)
                return;
            if (lockTargetView == null)
                throw new System.InvalidOperationException("[HUDLockTargetController] HUD Prefab 未绑定 HUDLockTargetView。");

            lockTargetView.Initialize();
            initialized = true;
        }

        /// <summary>HUD 显示时订阅锁定变化并立即读取已有目标。</summary>
        public void HandleWindowShown()
        {
            if (!initialized || visible)
                return;

            lockTargetSystem = LockTargetSystem.Instance;
            lockTargetSystem.LockTargetChanged += HandleLockTargetChanged;
            visible = true;
            SetRendererTarget(lockTargetSystem.CurrentTarget);
            Debug.Log("[HUDLockTargetController] 已订阅锁定目标变化并刷新当前标记。", this);
        }

        /// <summary>HUD 隐藏时解除业务事件并隐藏标记。</summary>
        public void HandleWindowHidden()
        {
            if (!visible)
                return;

            if (lockTargetSystem != null)
                lockTargetSystem.LockTargetChanged -= HandleLockTargetChanged;
            visible = false;
            rendererOwnerTarget = null;
            targetRenderers = System.Array.Empty<Renderer>();
            lockTargetView.Hide();
            Debug.Log("[HUDLockTargetController] 已解除锁定目标订阅并隐藏标记。", this);
        }

        /// <summary>解除事件订阅并释放 View 与 Renderer 快照。</summary>
        public void Dispose()
        {
            HandleWindowHidden();
            initialized = false;
            lockTargetSystem = null;
        }

        /// <summary>在摄像机和目标本帧更新后投影锁定标记到屏幕。</summary>
        private void LateUpdate()
        {
            if (!visible)
                return;

            PlayerController playerController = PlayerController.Instance;
            Camera gameplayCamera = playerController != null ? playerController.GameplayCamera : null;
            GameplayAbilitySystemComponent target = lockTargetSystem.CurrentTarget;
            if (gameplayCamera == null || target == null)
            {
                lockTargetView.Hide();
                return;
            }

            if (!ReferenceEquals(rendererOwnerTarget, target))
                SetRendererTarget(target);

            Vector3 worldPosition = GetTargetDisplayPosition(target);
            Vector3 screenPosition = gameplayCamera.WorldToScreenPoint(worldPosition);
            if (screenPosition.z <= 0f || !gameplayCamera.pixelRect.Contains(screenPosition))
            {
                lockTargetView.Hide();
                return;
            }

            lockTargetView.ShowAtScreenPosition(
                new Vector2(screenPosition.x, screenPosition.y), gameplayCamera);
        }

        #endregion

        #region 锁定事件与位置解析

        /// <summary>锁定目标变化时更新 Renderer 快照，屏幕投影留到 LateUpdate 统一完成。</summary>
        /// <param name="previousTarget">变化前的锁定 ASC。</param>
        /// <param name="currentTarget">变化后的锁定 ASC。</param>
        /// <param name="reason">本次锁定变化原因。</param>
        private void HandleLockTargetChanged(
            GameplayAbilitySystemComponent previousTarget,
            GameplayAbilitySystemComponent currentTarget,
            E_LockTargetChangeReason reason)
        {
            SetRendererTarget(currentTarget);
            Debug.Log(
                $"[HUDLockTargetController] 已接收锁定变化，previous={GetTargetName(previousTarget)}, " +
                $"current={GetTargetName(currentTarget)}, reason={reason}。", this);
        }

        /// <summary>缓存目标 RootTransform 子层级 Renderer，避免每帧重新搜索组件。</summary>
        /// <param name="target">新的锁定目标。</param>
        private void SetRendererTarget(GameplayAbilitySystemComponent target)
        {
            rendererOwnerTarget = target;
            if (target == null || target.Owner?.RootTransform == null)
            {
                targetRenderers = System.Array.Empty<Renderer>();
                return;
            }

            targetRenderers = target.Owner.RootTransform.GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>读取启用 Renderer 合并包围盒中心；无有效 Renderer 时使用根节点上方偏移。</summary>
        /// <param name="target">锁定目标 ASC。</param>
        /// <returns>用于屏幕投影的世界坐标。</returns>
        private Vector3 GetTargetDisplayPosition(GameplayAbilitySystemComponent target)
        {
            bool hasBounds = false;
            Bounds combinedBounds = default;
            for (int index = 0; index < targetRenderers.Length; index++)
            {
                Renderer renderer = targetRenderers[index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (!hasBounds)
                {
                    combinedBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(renderer.bounds);
                }
            }

            Transform rootTransform = target.Owner.RootTransform;
            return hasBounds
                ? combinedBounds.center
                : rootTransform.position + Vector3.up * fallbackWorldHeight;
        }

        /// <summary>返回事件诊断用目标名称。</summary>
        /// <param name="target">目标 ASC，允许为空或已销毁。</param>
        /// <returns>目标名称或 None。</returns>
        private static string GetTargetName(GameplayAbilitySystemComponent target)
        {
            return target == null ? "None" : target.name;
        }

        #endregion
    }
}
