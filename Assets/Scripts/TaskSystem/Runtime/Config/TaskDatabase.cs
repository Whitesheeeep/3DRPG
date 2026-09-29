using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.TaskSystem
{
    /// <summary>
    /// 集中引用每任务独立配置资产并按稳定 TaskId 建立索引。
    /// </summary>
    [CreateAssetMenu(fileName = "TaskDatabase", menuName = "RPG/TaskSystem/Task Database", order = 0)]
    public sealed class TaskDatabase : ScriptableObject
    {
        #region 配置与索引字段

        [SerializeField]
        private List<TaskDefinition> definitions = new List<TaskDefinition>();

        // key：TaskId；value：对应的独立任务定义资产，一对一且重复 ID 直接报配置错误。
        private Dictionary<TaskId, TaskDefinition> definitionByIdMap;

        #endregion

        #region 查询与校验

        /// <summary>
        /// 获取数据库中任务资产的只读列表。
        /// </summary>
        public IReadOnlyList<TaskDefinition> Definitions => definitions;

        /// <summary>
        /// 尝试按稳定任务标识获取定义资产。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="definition">找到的任务定义。</param>
        /// <returns>找到定义时返回 true。</returns>
        public bool TryGetDefinition(TaskId taskId, out TaskDefinition definition)
        {
            EnsureIndex();
            return definitionByIdMap.TryGetValue(taskId, out definition);
        }

        /// <summary>
        /// 校验全部任务资产并建立运行时索引。
        /// </summary>
        /// <exception cref="InvalidOperationException">数据库包含空资产、重复任务 ID 或不存在的前置任务引用时抛出。</exception>
        /// <exception cref="ArgumentException">数据库中的任务定义配置非法时抛出。</exception>
        [Button("验证并建立索引")]
        public void ValidateAndBuildIndex()
        {
            definitions ??= new List<TaskDefinition>();
            var definitionByIdMap = new Dictionary<TaskId, TaskDefinition>();
            for (int index = 0; index < definitions.Count; index++)
            {
                TaskDefinition definition = definitions[index];
                if (definition == null)
                {
                    throw new InvalidOperationException($"任务数据库第 {index} 项为空。 ");
                }

                definition.Validate();
                if (definitionByIdMap.ContainsKey(definition.TaskId))
                {
                    throw new InvalidOperationException($"任务数据库包含重复 TaskId：{definition.TaskId}。 ");
                }

                definitionByIdMap.Add(definition.TaskId, definition);
            }

            // 任务索引建立后再解析前置任务引用，避免错误配置形成永久 Locked 任务。
            foreach (KeyValuePair<TaskId, TaskDefinition> definitionById in definitionByIdMap)
            {
                IReadOnlyList<TaskConditionDefinition> conditions = definitionById.Value.UnlockConditions;
                for (int conditionIndex = 0; conditionIndex < conditions.Count; conditionIndex++)
                {
                    if (conditions[conditionIndex] is TaskPrerequisiteCompletedConditionDefinition prerequisite &&
                        !definitionByIdMap.ContainsKey(prerequisite.PrerequisiteTaskId))
                    {
                        throw new InvalidOperationException(
                            $"任务 {definitionById.Key} 引用了不存在的前置任务：{prerequisite.PrerequisiteTaskId}。");
                    }
                }
            }

            this.definitionByIdMap = definitionByIdMap;
        }

        #endregion

        #region 编辑器支持

#if UNITY_EDITOR
        /// <summary>
        /// 创建由代码定义的临时任务数据库，供 Odin 手动测试使用。
        /// </summary>
        /// <param name="taskDefinitions">独立任务资产集合。</param>
        /// <returns>已校验并建立索引的临时数据库。</returns>
        internal static TaskDatabase CreateRuntime(IEnumerable<TaskDefinition> taskDefinitions)
        {
            TaskDatabase database = CreateInstance<TaskDatabase>();
            database.hideFlags = HideFlags.DontSave;
            database.definitions = taskDefinitions == null
                ? new List<TaskDefinition>()
                : new List<TaskDefinition>(taskDefinitions);
            database.ValidateAndBuildIndex();
            return database;
        }
#endif

        /// <summary>
        /// 在 Inspector 修改配置时报告编辑期校验失败。
        /// </summary>
        private void OnValidate()
        {
            // 编辑器阶段只报告错误，运行时注入时会再次严格校验配置。
            try
            {
                ValidateAndBuildIndex();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TaskDatabase] {name} 校验失败：{exception.Message}", this);
                definitionByIdMap = null;
            }
        }

        #endregion

        #region 索引保障

        /// <summary>
        /// 确保查询前已经建立任务索引。
        /// </summary>
        private void EnsureIndex()
        {
            if (definitionByIdMap == null)
            {
                ValidateAndBuildIndex();
            }
        }

        #endregion
    }
}
