#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.InteractionSystem.Tests
{
    /// <summary>用于 Odin 手动测试的可配置物品接收器替身。</summary>
    public sealed class ItemPickupReceiverOdinStub : MonoBehaviour, IItemPickupReceiver
    {
        #region 测试配置与结果

        [Title("物品接收测试")]
        [SerializeField] private bool canReceive = true;
        [SerializeField] private bool receiveSucceeds = true;

        /// <summary>获取最近一次是否尝试接收过物品。</summary>
        public bool ReceiveAttempted { get; private set; }

        /// <summary>获取最近一次接收到的物品请求。</summary>
        public ItemPickupRequest LastRequest { get; private set; }

        #endregion

        #region 接收器契约

        /// <summary>按 Inspector 配置返回是否允许接收物品。</summary>
        /// <param name="request">待判断的物品请求。</param>
        /// <returns>允许接收时返回 true。</returns>
        public bool CanReceive(in ItemPickupRequest request) => canReceive;

        /// <summary>记录请求并按 Inspector 配置返回模拟执行结果。</summary>
        /// <param name="request">待接收的物品请求。</param>
        /// <returns>配置为成功时返回 true。</returns>
        public bool TryReceive(in ItemPickupRequest request)
        {
            ReceiveAttempted = true;
            LastRequest = request;
            return receiveSucceeds;
        }

        #endregion

        #region 测试操作

        /// <summary>清除最近一次接收记录，便于重复执行手动测试。</summary>
        [Button("清除接收记录")]
        public void ResetReceiveRecord()
        {
            ReceiveAttempted = false;
            LastRequest = default;
        }

        #endregion
    }
}
#endif
