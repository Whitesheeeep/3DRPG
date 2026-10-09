using System;
using RPG.Game;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.NPC
{
    /// <summary>
    /// 为场景中的唯一 NPC 提供稳定身份和 HUD 导航锚点。
    /// </summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖已初始化的 GameArchitecture 中注册 NPCManager，并在 Inspector 指定 NPCIdentityDefinition 与可选导航锚点 Transform；身份资产缺失时不能注册，导航锚点为空时使用本节点 Transform。")]
    public sealed class NPCIdentity : MonoBehaviour
    {
        #region 身份与锚点配置

        [SerializeField, Required, AssetsOnly, LabelText("NPC 身份资产"), ValidateInput(nameof(IsDefinitionValid), "必须配置具有有效稳定键的 NPCIdentityDefinition。")]
        private NPCIdentityDefinition definition;
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
        /// <exception cref="InvalidOperationException">身份资产为空或其稳定键无效时抛出。</exception>
        public NPCId Id => definition != null
            ? definition.Id
            : throw new InvalidOperationException($"[NPCIdentity] '{name}' 未配置 NPC 身份资产。");

        /// <summary>获取当前场景身份引用的稳定身份资产。</summary>
        public NPCIdentityDefinition Definition => definition;

        /// <summary>获取 HUD 世界指示标使用的明确锚点。</summary>
        public Transform NavigationAnchor => navigationAnchor;

        /// <summary>获取 HUD 世界指示标相对于锚点的偏移。</summary>
        public Vector3 NavigationOffset => navigationOffset;

        /// <summary>获取 NPC ID 当前是否可以被解析。</summary>
        public bool NPCIdIsValid => definition != null && definition.TryGetId(out _);

        /// <summary>验证 ID 和导航锚点，确保注册表不保存不完整对象。</summary>
        /// <exception cref="InvalidOperationException">身份资产为空或其稳定键无效时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (definition == null)
            {
                Debug.LogError($"[NPCIdentity] 未配置身份资产，object={name}。", this);
                throw new InvalidOperationException($"[NPCIdentity] '{name}' 必须引用 NPCIdentityDefinition。");
            }
            definition.Validate();
        }

        #endregion

        #region Unity 生命周期
        /// <summary>将未指定的导航锚点固定到身份组件所在 Transform。</summary>
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
        private bool IsDefinitionValid() => NPCIdIsValid;

        /// <summary>在 NPC 进入 Dead 时立即从场景导航注册表移除。</summary>
        internal void UnregisterForDeath()
        {
            NPCManager manager = registeredManager;
            registeredManager = null;
            manager?.Unregister(this);
        }

        /// <summary>在编辑器新增组件时填充同节点的 NPC 导航锚点依赖。</summary>
        private void Reset()
        {
            navigationAnchor = transform;
        }

        /// <summary>在 Scene 视图绘制导航锚点及偏移，便于检查 HUD 指向位置。</summary>
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
