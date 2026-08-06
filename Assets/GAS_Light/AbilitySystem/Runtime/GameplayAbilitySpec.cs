namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保存单个 ASC 被授予能力后的长期身份、配置与等级。</summary>
    public sealed class GameplayAbilitySpec
    {
        #region 属性
        /// <summary>获取当前 Controller 内定位该 Spec 的 Handle。</summary>
        public GameplayAbilityHandle Handle { get; }
        /// <summary>获取该 Spec 引用的不可变作者配置。</summary>
        public GameplayAbilityData Data { get; }
        /// <summary>获取后续激活 Runtime 将复制的当前能力等级。</summary>
        public int Level { get; private set; }
        #endregion

        #region 构造与内部修改
        // Spec 只能由所属 Controller 创建，确保 Handle 和 Level 满足授予契约。
        internal GameplayAbilitySpec(GameplayAbilityHandle handle, GameplayAbilityData data, int level)
        {
            Handle = handle;
            Data = data;
            Level = level;
        }
        // 等级只能通过 Controller 修改，避免绕过有效范围和所有权检查。
        internal void SetLevel(int level) => Level = level;
        #endregion
    }
}
