using System;
using UnityEngine;
using Sirenix.OdinInspector;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保存攻击启动时自动寻找最近敌人并转向的配置。</summary>
    [Serializable]
    public sealed class AttackFacingGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
    {
        #region 配置字段

        // 扫描采用 Enemy Layer 缩小候选范围，具体目标还需解析到有效 ASC。
        [SerializeField, MinValue(0.01f), Tooltip("没有有效锁定目标时，360° 搜索最近敌人的世界空间半径。")]
        private float scanRadius = 3f;
        [SerializeField, Tooltip("自动选敌扫描的 Unity LayerMask；默认值对应项目当前的 Enemy Layer 7。锁定系统目标不受此遮罩限制。")]
        private LayerMask targetLayers = 1 << 7;

        #endregion

        #region 校验与工厂

        // 无半径或无候选层时禁止 GA 提交 Cost/Cooldown 后才发现配置错误。
        /// <summary>确认扫描半径和敌人查询层均可用于自动选敌。</summary>
        internal override bool IsConfigurationValid =>
            scanRadius > 0f && !float.IsNaN(scanRadius) && !float.IsInfinity(scanRadius) &&
            targetLayers.value != 0;

        /// <summary>为本次 Ability 激活创建独立的攻击转向 Task。</summary>
        /// <param name="runtime">拥有该 Task 的异步 Ability Runtime。</param>
        /// <returns>携带扫描半径和 LayerMask 配置快照的 Task。</returns>
        protected override GameplayAbilityTask CreateTask(AsynchronousGameplayAbilityRuntime runtime) =>
            new AttackFacingGameplayAbilityTask(runtime, scanRadius, targetLayers);

        #endregion
    }
}
