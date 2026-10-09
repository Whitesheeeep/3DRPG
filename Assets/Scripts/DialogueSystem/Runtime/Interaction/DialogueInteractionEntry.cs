using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>指定一条 NPC 对话交互是可重复执行还是完成后隐藏。</summary>
    public enum E_DialogueInteractionType
    {
        /// <summary>每次交互都可以重新启动该对话。</summary>
        Repeatable = 0,
        /// <summary>到达正常结束节点后永久隐藏该选项，并由存档保留状态。</summary>
        Toggle = 1
    }

    /// <summary>描述 DialogueInteractable 提供的一条具备稳定身份的对话选项。</summary>
    [Serializable]
    public sealed class DialogueInteractionEntry
    {
        #region 序列化配置

        [SerializeField, ReadOnly, LabelText("Option ID")]
        private string optionId = string.Empty;

        [SerializeField, LabelText("交互名称")]
        private string displayName = "对话";

        [SerializeField, LabelText("对话资源")]
        private DialogueAsset dialogueAsset;

        [SerializeField, LabelText("交互类型")]
        private E_DialogueInteractionType interactionType;

        #endregion

        #region 属性

        /// <summary>获取由对话配置项持有的稳定标识。</summary>
        public string OptionId => optionId;

        /// <summary>获取交互列表中的展示名称；空名称回退为“对话”。</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "对话" : displayName.Trim();

        /// <summary>获取此选项启动的对话资产。</summary>
        public DialogueAsset DialogueAsset => dialogueAsset;

        /// <summary>获取此选项的重复执行策略。</summary>
        public E_DialogueInteractionType InteractionType => interactionType;

        #endregion

        #region 身份维护

        /// <summary>为新配置补充持久化选项身份；运行时不调用此方法。</summary>
        internal void EnsureOptionId()
        {
            if (!Guid.TryParseExact(optionId, "N", out _))
                optionId = Guid.NewGuid().ToString("N");
        }

        /// <summary>为重复复制的列表项生成新身份。</summary>
        internal void RegenerateOptionId() => optionId = Guid.NewGuid().ToString("N");

        /// <summary>检查 OptionId 是否符合存档使用的 GUID N 格式。</summary>
        /// <returns>身份是有效的 N 格式 GUID 时返回 true。</returns>
        internal bool HasValidOptionId() => Guid.TryParseExact(optionId, "N", out _);

        #endregion
    }
}
