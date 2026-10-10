using System;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.NPC
{
    /// <summary>执行 Boss 瞬移 GA 的同步安全重定位并立即结束异步 Ability。</summary>
    public sealed class NpcTeleportGATask : GameplayAbilityTask
    {
        #region 构造与任务生命周期

        /// <summary>绑定一次 Boss 瞬移 Runtime。</summary>
        /// <param name="runtime">拥有本次瞬移的异步 Ability Runtime。</param>
        public NpcTeleportGATask(AsynchronousGameplayAbilityRuntime runtime)
            : base(runtime)
        {
        }

        /// <summary>调用 Controller 对激活前快照的落点进行二次验证并提交 Warp。</summary>
        protected override void OnStart()
        {
            NPCActor actor = GARuntime.SourceOwner as NPCActor ??
                throw new InvalidOperationException(
                    $"[NpcTeleportGATask] Ability '{GARuntime.Data.name}' 仅支持 NPCActor Owner。");
            bool succeeded = actor.Controller.TryCompletePendingTeleport(GARuntime.ActivationId);
            if (!succeeded)
                Debug.LogWarning(
                    $"[NpcTeleportGATask] Boss '{actor.name}' 的瞬移落点在执行前失效，GA 将结束并让行为树回退 Walk。",
                    actor);
            Complete();
        }

        #endregion
    }

    /// <summary>构造无额外配置的 Boss 瞬移运行 Task。</summary>
    [Serializable]
    public sealed class NpcTeleportGATaskConfig : GameplayAbilityTaskConfig
    {
        #region 配置校验与 Task 工厂

        /// <summary>瞬移距离、落点和目标均由 NPCController 的激活请求快照持有。</summary>
        internal override bool IsConfigurationValid => true;

        /// <summary>为每次 GA 激活创建独立的安全重定位 Task。</summary>
        /// <param name="runtime">本次瞬移所属异步 Runtime。</param>
        /// <returns>绑定给本次激活的全新瞬移 Task。</returns>
        protected override GameplayAbilityTask CreateTask(AsynchronousGameplayAbilityRuntime runtime) =>
            new NpcTeleportGATask(runtime);

        #endregion
    }
}
