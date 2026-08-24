using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 表示对话图唯一的入口节点。
    /// </summary>
    public sealed class DialogueEntryNode : DialogueNode
    {
        #region 序列化字段

        [SerializeField] private DialogueSpeechNode firstSpeechNode;

        #endregion

        #region 属性

        /// <summary>
        /// 获取入口直接指向的首个 SpeechNode。
        /// </summary>
        public DialogueSpeechNode FirstSpeechNode => firstSpeechNode;

        #endregion

        #region 编辑操作

        /// <summary>
        /// 设置入口首个 SpeechNode 的直接 ScriptableObject 引用。
        /// </summary>
        /// <param name="speechNode">入口目标 SpeechNode。</param>
        public void SetFirstSpeechNode(DialogueSpeechNode speechNode) => firstSpeechNode = speechNode;

        #endregion
    }
}