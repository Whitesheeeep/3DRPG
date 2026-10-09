using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.BusinessArchitecture;

namespace RPG.NPC
{
    /// <summary>
    /// 管理当前已启用场景 NPC 的稳定身份注册与查询。
    /// </summary>
    public sealed class NPCManager : AbstractManager
    {
        #region NPC 身份索引

        // key：唯一剧情角色的稳定 NPCId；value：当前场景中注册该身份的 NPCIdentity。
        private readonly Dictionary<NPCId, NPCIdentity> npcByIdMap = new Dictionary<NPCId, NPCIdentity>();

        #endregion

        #region 生命周期

        /// <summary>初始化空的场景 NPC 注册表。</summary>
        protected override void OnInit()
        {
            Debug.Log("[NPCManager] 场景 NPC 身份注册表已初始化。");
        }

        /// <summary>释放架构时清除场景对象引用。</summary>
        protected override void OnDeinit()
        {
            int registeredCount = npcByIdMap.Count;
            npcByIdMap.Clear();
            Debug.Log($"[NPCManager] 已清除场景 NPC 注册，count={registeredCount}。");
        }

        #endregion

        #region 注册与查询

        /// <summary>注册一个当前启用且配置完整的场景 NPC。</summary>
        /// <param name="identity">提供稳定 ID 与导航锚点的场景组件。</param>
        /// <exception cref="ArgumentNullException">身份组件为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">组件配置无效或 ID 已由另一实例注册时抛出。</exception>
        public void Register(NPCIdentity identity)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            identity.ValidateConfiguration();
            NPCId npcId = identity.Id;
            if (npcByIdMap.TryGetValue(npcId, out NPCIdentity registeredIdentity))
            {
                if (ReferenceEquals(registeredIdentity, identity))
                    return;

                string message = $"NPC ID {npcId} 已由其他场景对象注册，不能覆盖现有实例。";
                Debug.LogError($"[NPCManager] {message}", identity);
                throw new InvalidOperationException(message);
            }

            npcByIdMap.Add(npcId, identity);
            Debug.Log($"[NPCManager] 已注册场景 NPC，npcId={npcId}, object={identity.name}。", identity);
        }

        /// <summary>注销由指定组件拥有的 NPC 注册。</summary>
        /// <param name="identity">正在禁用或销毁的身份组件。</param>
        public void Unregister(NPCIdentity identity)
        {
            if (identity == null || !identity.NPCIdIsValid)
                return;

            NPCId npcId = identity.Id;
            if (!npcByIdMap.TryGetValue(npcId, out NPCIdentity registeredIdentity) ||
                !ReferenceEquals(registeredIdentity, identity))
                return;

            npcByIdMap.Remove(npcId);
            Debug.Log($"[NPCManager] 已注销场景 NPC，npcId={npcId}, object={identity.name}。", identity);
        }

        /// <summary>按稳定身份查找当前已注册的场景 NPC。</summary>
        /// <param name="npcId">目标 NPC 稳定标识。</param>
        /// <param name="identity">找到的 NPC 身份组件。</param>
        /// <returns>NPC 当前已注册且组件仍有效时返回 true。</returns>
        public bool TryGetNPC(NPCId npcId, out NPCIdentity identity)
        {
            if (npcId.IsValid && npcByIdMap.TryGetValue(npcId, out identity) && identity != null)
                return true;

            identity = null;
            return false;
        }

        #endregion
    }
}
