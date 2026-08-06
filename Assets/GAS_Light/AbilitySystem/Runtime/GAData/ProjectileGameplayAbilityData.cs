using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>定义同步创建投射物并立即结束 GA 的业务基类；碰撞和目标由具体子类负责。</summary>
    public abstract class ProjectileGameplayAbilityData : SynchronousGameplayAbilityData
    {

        #region 同步执行
        // 统一由子类同步创建投射物；投射物生命周期与 GA Runtime 解耦。
        protected sealed override void Execute(SynchronousGameplayAbilityRuntime runtime) =>
            SpawnProjectile(runtime);

        /// <summary>在当前调用中创建投射物对象；不负责碰撞、范围筛选或 GE 应用。</summary>
        /// <param name="runtime">本次同步 Ability 的运行快照。</param>
        protected abstract void SpawnProjectile(SynchronousGameplayAbilityRuntime runtime);
        #endregion
    }
}
