using System;
using RPG.Game;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.NPC
{
    /// <summary>
    /// 为场景中的唯一剧情 NPC 提供稳定身份和 HUD 导航锚点。
    /// </summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖已初始化的 GameArchitecture 中注册 NPCManager，并在 Inspector 指定唯一 NPC ID 和导航锚点 Transform；缺少任一配置时该 NPC 不可注册或导航。")]
    public sealed class NPCIdentity : MonoBehaviour
    {
        #region 身份与锚点配置

        [SerializeField, Required, ValidateInput(nameof(IsNPCIdValid), "NPC ID 必须是非空稳定标识。")]
        private string npcId = string.Empty;
        [SerializeField]
        private Transform navigationAnchor;
        [SerializeField, LabelText("导航锚点偏移"), Tooltip("HUD 导航指示标相对于锚点的偏移位置。")]
        private Vector3 navigationOffset = Vector3.zero;

        #endregion

        #region 依赖字段

        // 依赖字段：OnDisable 必须向实际注册过本组件的 Manager 注销，不在销毁阶段重新获取架构。
        private NPCManager registeredManager;

        #endregion

        #region 身份属性与校验

        /// <summary>获取该场景 NPC 的稳定身份。</summary>
        /// <exception cref="ArgumentException">Inspector 中配置的 NPC ID 格式无效时抛出。</exception>
        public NPCId Id => new NPCId(npcId);

        /// <summary>获取 HUD 世界指示标使用的明确锚点。</summary>
        public Transform NavigationAnchor => navigationAnchor;

        /// <summary>获取 HUD 世界指示标相对于锚点的偏移。</summary>
        public Vector3 NavigationOffset => navigationOffset;

        /// <summary>获取 NPC ID 当前是否可以被解析。</summary>
        public bool NPCIdIsValid => NPCId.TryCreate(npcId, out _);

        /// <summary>验证 ID 和导航锚点，确保注册表不保存不完整对象。</summary>
        /// <exception cref="InvalidOperationException">ID 或导航锚点未配置时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (!NPCId.TryCreate(npcId, out _))
            {
                Debug.LogError($"[NPCIdentity] NPC ID 无效，object={name}。", this);
                throw new InvalidOperationException($"[NPCIdentity] NPC ID 无效，object={name}。");
            }
        }

        #endregion

        #region Unity 生命周期
        private void Awake()
        {
            navigationAnchor ??= transform;
        }

        /// <summary>启用时向架构内的 NPCManager 注册场景身份。</summary>
        /// <exception cref="InvalidOperationException">GameArchitecture 尚未注册 NPCManager 时由架构查询抛出。</exception>
        private void OnEnable()
        {
            NPCManager manager = GameArchitecture.Interface.GetManager<NPCManager>();
            manager.Register(this);
            registeredManager = manager;
        }

        /// <summary>禁用时向曾经注册本组件的 Manager 注销身份。</summary>
        private void OnDisable()
        {
            NPCManager manager = registeredManager;
            registeredManager = null;
            manager?.Unregister(this);
        }

        /// <summary>向 Inspector 校验回调提供稳定 ID 的格式判断。</summary>
        /// <returns>ID 合法时返回 true。</returns>
        private bool IsNPCIdValid() => NPCIdIsValid;

        private void OnDrawGizmos()
        {
           if (navigationAnchor != null)
           {
               Gizmos.color = Color.cyan;
               Gizmos.DrawWireSphere(navigationAnchor.position + navigationOffset, 0.2f);
               Gizmos.DrawLine(navigationAnchor.position, navigationAnchor.position + navigationOffset);
           }
        }
        #endregion
    }
}
