using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 表示对话中的唯一参与者身份；对话节点和场景参与者通过同一个资产引用匹配。
    /// </summary>
    [CreateAssetMenu(fileName = "DialogueSpeaker", menuName = "RPG/Dialogue/Speaker", order = 1)]
    public sealed class DialogueSpeaker : ScriptableObject
    {
        #region 属性

        /// <summary>
        /// 获取 Speaker 在编辑器和对话 UI 中使用的显示名称。
        /// 名称来自 ScriptableObject.name，修改资产名称不会改变对象引用身份。
        /// </summary>
        public string SpeakerName => name;

        #endregion
    }
}
