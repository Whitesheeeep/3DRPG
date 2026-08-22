using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>
    /// 保存一个 ASC 在尝试激活 Ability 时需要统一阻断的 Owner GameplayTag 查询集合。
    /// </summary>
    [CreateAssetMenu(
        fileName = "GameplayAbilityActivationRules",
        menuName = "WSFrame/GAS/GA 触发 Block 的TagQuery")]
    public sealed class GameplayAbilityActivationRules : ScriptableObject
    {
        #region 配置字段与只读属性

        [SerializeField]
        [Tooltip("Owner 拥有任意一个 Tag 或其子 Tag 时，TryActivate 会被统一拒绝。")]
        private GameplayTagQuery activationBlockedOwnerTags;

        /// <summary>
        /// 获取 Owner 侧需要统一阻断 Ability 激活的 Tag 查询集合。
        /// </summary>
        public GameplayTagQuery ActivationBlockedOwnerTags =>
            activationBlockedOwnerTags;

        #endregion
    }
}
