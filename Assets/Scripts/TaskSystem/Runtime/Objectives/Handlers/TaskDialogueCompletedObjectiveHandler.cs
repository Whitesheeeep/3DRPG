namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 将指定对话完成目标定义映射为事件监听运行时。
    /// </summary>
    public sealed class TaskDialogueCompletedObjectiveHandler
        : TaskObjectiveHandler<TaskDialogueCompletedObjectiveDefinition>
    {
        #region 目标运行时创建

        /// <summary>
        /// 创建在当前阶段监听对话结束事实的目标运行时。
        /// </summary>
        /// <param name="definition">包含目标 DialogueAsset 的静态定义。</param>
        /// <param name="context">由所属 TaskRuntime 提供的受限进度上下文。</param>
        /// <returns>负责匹配并累计对话完成事件的运行时。</returns>
        /// <exception cref="System.ArgumentNullException">配置资源或上下文为空时抛出。</exception>
        public override ITaskObjectiveRuntime CreateRuntime(
            TaskDialogueCompletedObjectiveDefinition definition,
            ITaskObjectiveRuntimeContext context)
        {
            if (definition == null)
            {
                throw new System.ArgumentNullException(nameof(definition));
            }

            return new TaskDialogueCompletedObjectiveRuntime(definition.DialogueAsset, context);
        }

        #endregion
    }
}
