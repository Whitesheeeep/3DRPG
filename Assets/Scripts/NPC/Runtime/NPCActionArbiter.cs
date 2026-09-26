using System;
using RPG.Character;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.NPC
{
    /// <summary>记录 NPC 当前 FullBody Ability 的唯一占据者及其生命周期。</summary>
    internal sealed class NPCActionArbiter : IFullBodyActionArbiter, IFullBodyActionRegistrationOwner, IDisposable
    {
        #region 依赖字段

        private readonly NPCController owner;

        #endregion

        #region 运行时状态

        private GameplayAbilityRuntime currentRuntime;
        private int nextRegistrationId;
        private int currentRegistrationId;
        private bool disposed;

        #endregion

        /// <summary>创建绑定单个 NPCController 的 FullBody 占据记录。</summary>
        /// <param name="sourceOwner">拥有 ASC 和状态机的 NPCController。</param>
        internal NPCActionArbiter(NPCController sourceOwner)
        {
            owner = sourceOwner ?? throw new ArgumentNullException(nameof(sourceOwner));
        }

        /// <summary>获取当前 NPC 是否被 FullBody Ability 占据。</summary>
        internal bool IsOccupied => currentRuntime != null;

        /// <summary>登记已经成功启动的 FullBody Ability Runtime。</summary>
        /// <param name="runtime">当前正在运行的 Ability。</param>
        /// <returns>释放本次占据的幂等 Handle。</returns>
        public FullBodyActionHandle RegisterFullBodyAction(GameplayAbilityRuntime runtime)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(NPCActionArbiter));
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (currentRuntime != null)
                throw new InvalidOperationException(
                    $"NPC '{owner.name}' 已被 FullBody Ability ActivationId={currentRuntime.ActivationId} 占据。");

            currentRuntime = runtime;
            currentRegistrationId = ++nextRegistrationId;
            Debug.Log(
                $"[NPCActionArbiter] NPC '{owner.name}' 注册 FullBody Ability，ActivationId={runtime.ActivationId}。",
                owner);
            return new FullBodyActionHandle(this, currentRegistrationId);
        }

        /// <summary>按 Handle 中保存的注册标识释放当前 NPC 占据。</summary>
        /// <param name="registrationId">待注销的注册标识。</param>
        private void UnregisterFullBodyAction(int registrationId)
        {
            if (currentRuntime == null || currentRegistrationId != registrationId)
                return;

            int activationId = currentRuntime.ActivationId;
            currentRuntime = null;
            currentRegistrationId = 0;
            Debug.Log(
                $"[NPCActionArbiter] NPC '{owner.name}' 释放 FullBody Ability 占据，ActivationId={activationId}。",
                owner);
        }

        /// <summary>实现 Handle 的最小注销契约。</summary>
        /// <param name="registrationId">待注销的注册标识。</param>
        void IFullBodyActionRegistrationOwner.UnregisterFullBodyAction(int registrationId) =>
            UnregisterFullBodyAction(registrationId);

        /// <summary>销毁 NPC 动作仲裁器并清除尚存的占据记录。</summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (currentRuntime == null)
                return;

            Debug.LogWarning(
                $"[NPCActionArbiter] NPC '{owner.name}' 销毁时仍有 FullBody Ability ActivationId={currentRuntime.ActivationId}，已强制释放占据。",
                owner);
            currentRuntime = null;
            currentRegistrationId = 0;
        }
    }
}
