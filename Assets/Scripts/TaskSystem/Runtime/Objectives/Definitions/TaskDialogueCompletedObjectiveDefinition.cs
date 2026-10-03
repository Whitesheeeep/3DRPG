using System;
using RPG.DialogueSystemModule;
using UnityEngine;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 配置玩家正常完成指定 DialogueAsset 后推进的任务目标。
    /// </summary>
    [Serializable]
    public sealed class TaskDialogueCompletedObjectiveDefinition : TaskObjectiveDefinition
    {
        #region 配置字段

        [SerializeField] private DialogueAsset dialogueAsset;

        #endregion

        #region 构造

        /// <summary>
        /// 创建供 Unity SerializeReference 反序列化使用的目标定义。
        /// </summary>
        public TaskDialogueCompletedObjectiveDefinition()
        {
        }

        /// <summary>
        /// 创建引用指定对话图的目标定义。
        /// </summary>
        /// <param name="objectiveId">所属阶段内唯一的目标标识。</param>
        /// <param name="dialogueAsset">需要正常完成的对话资源。</param>
        /// <param name="required">需要完成对话的次数，默认一次。</param>
        /// <exception cref="ArgumentException">目标标识非法时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">需求数量不是正数时抛出。</exception>
        public TaskDialogueCompletedObjectiveDefinition(
            string objectiveId,
            DialogueAsset dialogueAsset,
            int required = 1)
            : base(objectiveId, required)
        {
            this.dialogueAsset = dialogueAsset;
        }

        #endregion

        #region 属性与校验

        /// <summary>
        /// 获取目标要求完成的 DialogueAsset。
        /// </summary>
        public DialogueAsset DialogueAsset => dialogueAsset;

        /// <summary>
        /// 校验目标基础字段和对话资源引用。
        /// </summary>
        /// <exception cref="ArgumentException">目标基础字段非法或对话资源未配置时抛出。</exception>
        public override void Validate()
        {
            base.Validate();
            if (dialogueAsset != null)
            {
                return;
            }

            const string message = "对话完成目标必须配置 DialogueAsset。";
            Debug.LogError($"[TaskDialogueCompletedObjectiveDefinition] {message}");
            throw new ArgumentException(message, nameof(dialogueAsset));
        }

        #endregion
    }
}
