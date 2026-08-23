using System.Collections.Generic;
using UnityEngine;

namespace RPG.InteractionSystem
{
    /// <summary>把场景中的物品拾取行为提供为一个可执行 InteractionOption。</summary>
    [DisallowMultipleComponent]
    public sealed class ItemInteractable : InteractableObject
    {
        #region 配置与缓存

        [Header("物品配置")]
        [SerializeField] private ScriptableObject itemDefinition;
        [SerializeField, Min(1)] private int quantity = 1;

        [Header("交互配置")]
        [SerializeField] private string optionDisplayName = "拾取";
        [SerializeField] private int priority;
        [SerializeField, Min(0f)] private float maxDistance;

        private ItemPickupRequest pickupRequest;
        private InteractionOption pickupOption;
        private GameObject cachedInteractor;
        private MonoBehaviour cachedReceiverComponent;
        private IItemPickupReceiver cachedReceiver;
        private readonly List<MonoBehaviour> receiverComponents = new();

        #endregion

        #region Unity 生命周期

        /// <summary>校验物品配置并缓存稳定的拾取 Option。</summary>
        private void Awake()
        {
            if (itemDefinition == null)
            {
                Debug.LogError("[ItemInteractable] 必须配置 ItemDefinition，当前物品不会贡献拾取 Option。", this);
                return;
            }

            pickupRequest = new ItemPickupRequest(itemDefinition, quantity, gameObject);
            pickupOption = new InteractionOption(
                new InteractionOptionId(GetInstanceID(), "Pickup"),
                string.IsNullOrWhiteSpace(optionDisplayName) ? "拾取" : optionDisplayName,
                gameObject,
                transform,
                priority,
                maxDistance,
                CanPickup,
                TryPickup);
        }

        #endregion

        #region Provider 契约

        /// <inheritdoc />
        public override void CollectInteractionOptions(in InteractionQueryContext context,
            List<InteractionOption> results)
        {
            // Provider 只贡献缓存命令；接收容量和最终成功状态由玩家侧接收器动态判断。
            if (pickupOption != null) results.Add(pickupOption);
        }

        #endregion

        #region 拾取命令

        /// <summary>判断当前玩家是否存在并允许使用物品接收器。</summary>
        /// <param name="interactor">发起拾取的玩家对象。</param>
        /// <returns>接收器允许接收时返回 true。</returns>
        private bool CanPickup(GameObject interactor)
        {
            IItemPickupReceiver receiver = ResolveReceiver(interactor);
            return receiver != null && receiver.CanReceive(in pickupRequest);
        }

        /// <summary>重新校验并尝试接收物品；成功后停用场景物品。</summary>
        /// <param name="interactor">发起拾取的玩家对象。</param>
        /// <returns>接收器成功接收时返回 true。</returns>
        private bool TryPickup(GameObject interactor)
        {
            IItemPickupReceiver receiver = ResolveReceiver(interactor);
            if (receiver == null || !receiver.TryReceive(in pickupRequest)) return false;

            // 只有接收器提交成功后才停用来源对象，避免背包拒绝时场景物品丢失。
            gameObject.SetActive(false);
            return true;
        }

        /// <summary>从玩家根对象查找并缓存第一个物品接收器组件。</summary>
        /// <param name="interactor">当前交互玩家对象。</param>
        /// <returns>找到的接收器；找不到时返回 null。</returns>
        private IItemPickupReceiver ResolveReceiver(GameObject interactor)
        {
            if (interactor == cachedInteractor && cachedReceiverComponent != null)
                return cachedReceiver;

            cachedInteractor = interactor;
            cachedReceiverComponent = null;
            cachedReceiver = null;
            if (interactor == null) return null;

            receiverComponents.Clear();
            interactor.GetComponents(receiverComponents);
            for (int index = 0; index < receiverComponents.Count; index++)
            {
                MonoBehaviour component = receiverComponents[index];
                if (component is not IItemPickupReceiver receiver) continue;

                cachedReceiverComponent = component;
                cachedReceiver = receiver;
                return receiver;
            }

            return null;
        }

        #endregion
    }
}
