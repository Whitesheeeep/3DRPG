using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.NPC
{
    /// <summary>
    /// 保存可由 NPC、任务目标与场景实例共同引用的稳定身份数据。
    /// </summary>
    [CreateAssetMenu(fileName = "NPCIdentity", menuName = "NPC/Identity Definition")]
    public sealed class NPCIdentityDefinition : ScriptableObject
    {
        #region 身份数据

        [SerializeField, ReadOnly, LabelText("专属身份 ID")]
        private string identityId = string.Empty;

#if UNITY_EDITOR
        // 该字段仅用于 Editor 判断资产复制来源，不参与运行时身份解析。
        [SerializeField, HideInInspector]
        private string editorAssetGuid = string.Empty;
#endif

        #endregion

        #region 身份属性与校验

        /// <summary>获取由专属身份字符串解析出的稳定 NPC 标识。</summary>
        /// <exception cref="InvalidOperationException">专属身份 ID 格式无效时抛出。</exception>
        public NPCId Id => TryGetId(out NPCId npcId)
            ? npcId
            : throw new InvalidOperationException(
                $"[NPCIdentityDefinition] 身份资产 '{name}' 的 identityId 无效。请在 Unity Editor 中检查资产。");

        /// <summary>获取用于编辑器展示的资产名称。</summary>
        public string DisplayName => name;

        /// <summary>获取只读的专属身份字符串。</summary>
        public string IdentityId => identityId;

        /// <summary>尝试从唯一身份字段解析 NPCId，不执行运行时生成或回退。</summary>
        /// <param name="npcId">解析成功时返回的稳定标识。</param>
        /// <returns>当前 identityId 符合 NPCId 规则时返回 true。</returns>
        public bool TryGetId(out NPCId npcId) => NPCId.TryCreate(identityId, out npcId);

        /// <summary>验证专属身份字段，供引用该资产的场景组件在注册前调用。</summary>
        /// <exception cref="InvalidOperationException">identityId 无效时抛出。</exception>
        public void Validate()
        {
            if (!TryGetId(out _))
            {
                throw new InvalidOperationException(
                    $"[NPCIdentityDefinition] 身份资产 '{name}' 的 identityId 无效。请在 Unity Editor 中检查资产。");
            }
        }

        #endregion

#if UNITY_EDITOR
        #region Editor 资产来源追踪

        /// <summary>获取仅供资产处理器判断复制来源的所属资产 GUID。</summary>
        public string EditorAssetGuid => editorAssetGuid;

        /// <summary>为新建资产或已确认副本写入唯一身份及当前所属资产 GUID。</summary>
        /// <param name="newIdentityId">Editor 生成的专属身份字符串。</param>
        /// <param name="assetGuid">该资产当前的 Unity meta GUID。</param>
        public void EditorInitializeIdentity(string newIdentityId, string assetGuid)
        {
            identityId = newIdentityId;
            editorAssetGuid = assetGuid;
        }

        /// <summary>身份无冲突时仅更新所属资产记录，保留既有专属身份。</summary>
        /// <param name="assetGuid">该资产当前的 Unity meta GUID。</param>
        public void EditorUpdateAssetGuid(string assetGuid)
        {
            editorAssetGuid = assetGuid;
        }

        #endregion
#endif
    }
}
