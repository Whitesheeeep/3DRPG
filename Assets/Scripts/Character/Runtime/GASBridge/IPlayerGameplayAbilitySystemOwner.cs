using RPG.PlayerInputSystem;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>为玩家专属 Gameplay Ability 提供输入槽映射、当前激活手势和移动方向。</summary>
    public interface IPlayerGameplayAbilitySystemOwner : IGameplayAbilitySystemOwner
    {
        /// <summary>获取玩家输入请求缓冲区，用于订阅与读取本次手势。</summary>
        IPlayerInputRequestBuffer InputRequestBuffer { get; }

        /// <summary>获取黑板中摄像机相对换算后的世界空间移动输入。</summary>
        Vector3 MoveWorldInput { get; }

        /// <summary>按玩家输入类型查询角色当前授予的 GA Handle。</summary>
        /// <param name="inputType">需要查询的技能槽输入类型。</param>
        /// <param name="handle">找到时返回对应的 Ability Handle。</param>
        /// <returns>角色配置包含该技能槽时返回 true。</returns>
        bool TryGetGameplayAbilityHandle(E_PlayerInputType inputType, out GameplayAbilityHandle handle);

        /// <summary>读取 TryActivateAbility 同步调用期间保存的实际输入手势来源。</summary>
        /// <param name="inputType">找到时返回触发本次激活的输入槽。</param>
        /// <param name="inputHandle">找到时返回触发本次激活的 Press 手势句柄。</param>
        /// <returns>当前有技能输入正在同步尝试激活时返回 true。</returns>
        bool TryGetCurrentAbilityInputSource(
            out E_PlayerInputType inputType,
            out InputRequestHandle inputHandle);
    }
}
