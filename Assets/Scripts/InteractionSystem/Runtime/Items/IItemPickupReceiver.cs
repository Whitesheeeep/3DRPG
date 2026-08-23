namespace RPG.InteractionSystem
{
    /// <summary>定义玩家侧接收场景物品拾取请求的业务边界。</summary>
    public interface IItemPickupReceiver
    {
        /// <summary>判断当前接收器是否能接收指定物品。</summary>
        /// <param name="request">待判断的物品拾取请求。</param>
        /// <returns>允许接收时返回 true。</returns>
        bool CanReceive(in ItemPickupRequest request);

        /// <summary>尝试接收指定物品并提交到接收器业务状态。</summary>
        /// <param name="request">待提交的物品拾取请求。</param>
        /// <returns>接收成功时返回 true。</returns>
        bool TryReceive(in ItemPickupRequest request);
    }
}
