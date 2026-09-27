namespace RPG.Character
{
    /// <summary>定义 FullBody Action Handle 注销其注册记录所需的最小所有者契约。</summary>
    internal interface IFullBodyActionRegistrationOwner
    {
        /// <summary>按单调递增的注册标识注销 FullBody Action。</summary>
        /// <param name="registrationId">创建 Handle 时分配的注册标识。</param>
        void UnregisterFullBodyAction(int registrationId);
    }
}
