#if UNITY_EDITOR
using System;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>定义 GAS 主窗口中可嵌入的 Gameplay Ability 页面能力。</summary>
    public interface IGameplayAbilityWindow : IDisposable
    {
        /// <summary>获取当前编辑的 GA 资产。</summary>
        GameplayAbilityData CurrentAbility { get; }

        /// <summary>切换当前 GA 并按需恢复 Session 选择。</summary>
        void SetAbility(GameplayAbilityData ability, bool restoreSelection);
    }
}
#endif
