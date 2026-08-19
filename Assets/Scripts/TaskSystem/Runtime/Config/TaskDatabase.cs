using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.TaskSystem
{
    /// <summary>
    /// 集中保存任务定义并在运行时建立稳定 TaskId 索引的配置资产。
    /// </summary>
    [CreateAssetMenu(fileName = "TaskDatabase", menuName = "RPG/TaskSystem/Task Database", order = 0)]
    public sealed class TaskDatabase : ScriptableObject
    {
        [SerializeField]
        private List<TaskDefinition> definitions = new List<TaskDefinition>();

        private Dictionary<TaskId, TaskDefinition> definitionIndex;

        /// <summary>
        /// 获取数据库中的任务定义只读列表。
        /// </summary>
        public IReadOnlyList<TaskDefinition> Definitions => definitions;

        /// <summary>
        /// 尝试按稳定任务标识获取定义。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="definition">找到的任务定义。</param>
        /// <returns>找到定义时返回 true。</returns>
        public bool TryGetDefinition(TaskId taskId, out TaskDefinition definition)
        {
            EnsureIndex();
            return definitionIndex.TryGetValue(taskId, out definition);
        }

        /// <summary>
        /// 校验全部任务并建立运行时索引。
        /// </summary>
        /// <exception cref="InvalidOperationException">数据库包含空定义或重复任务 ID 时抛出。</exception>
        [Button("验证并建立索引")]
        public void ValidateAndBuildIndex()
        {
            if (definitions == null)
            {
                definitions = new List<TaskDefinition>();
            }

            var index = new Dictionary<TaskId, TaskDefinition>();
            for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
            {
                TaskDefinition definition = definitions[definitionIndex];
                if (definition == null)
                {
                    throw new InvalidOperationException($"任务数据库第 {definitionIndex} 项为空。 ");
                }

                definition.Validate();
                if (index.ContainsKey(definition.TaskId))
                {
                    throw new InvalidOperationException($"任务数据库包含重复 TaskId：{definition.TaskId}。 ");
                }

                index.Add(definition.TaskId, definition);
            }

            this.definitionIndex = index;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 创建一个由代码定义的运行时数据库，供手动测试和临时组合根使用。
        /// </summary>
        /// <param name="taskDefinitions">任务定义集合。</param>
        /// <returns>已经完成校验和索引构建的数据库对象。</returns>
        internal static TaskDatabase CreateRuntime(IEnumerable<TaskDefinition> taskDefinitions)
        {
            var database = CreateInstance<TaskDatabase>();
            database.definitions = taskDefinitions == null
                ? new List<TaskDefinition>()
                : new List<TaskDefinition>(taskDefinitions);
            database.ValidateAndBuildIndex();
            return database;
        }
#endif


        /// <summary>
        /// 在 Unity Inspector 修改配置时执行编辑期校验。
        /// </summary>
        private void OnValidate()
        {
            // 编辑器阶段只报告配置问题，不阻止资产保存；运行时初始化会再次严格抛出异常。
            try
            {
                ValidateAndBuildIndex();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TaskDatabase] {name} 校验失败：{exception.Message}", this);
                definitionIndex = null;
            }
        }

        /// <summary>
        /// 确保读取任务前已经完成索引构建。
        /// </summary>
        private void EnsureIndex()
        {
            if (definitionIndex == null)
            {
                ValidateAndBuildIndex();
            }
        }
    }
}