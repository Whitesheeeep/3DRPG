using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>提供可直接创建的基础异步 Ability，默认等待一秒后由 Root Task 完成。</summary>
    [CreateAssetMenu(fileName = "AsyncGameplayAbility", menuName = "WSFrame/GAS/Gameplay Ability/Async")]
    public sealed class AsyncGameplayAbilityData : AsynchronousGameplayAbilityData
    {
        #region 构造
        /// <summary>创建默认使用一秒 WaitDuration Root Task 的异步 Ability 数据。</summary>
        public AsyncGameplayAbilityData()
        {
            SetRootTask(new WaitDurationGameplayAbilityTaskConfig(1f));
        }
        #endregion
    }
}