using System;
using System.Collections.Generic;

namespace WS_Modules.SceneModule
{
    /// <summary>在配置保存前或运行前验证组合任务的结构、Addressables 地址和场景就绪依赖。</summary>
    public static class SceneLoadValidator
    {
        #region 验证入口

        /// <summary>校验场景配置、所有树引用、场景任务数量与并行前置条件。</summary>
        /// <param name="config">待校验配置。</param>
        /// <param name="initializesCurrentScene">是否在已经加载的目标场景中初始化。</param>
        /// <param name="database">可选场景数据库；提供时同时校验稳定 SceneId 在库内唯一。</param>
        /// <returns>可在 Editor 展示并由 Runtime 执行的结构化错误列表。</returns>
        public static SceneLoadValidationResult Validate(
            SceneLoadConfig config,
            bool initializesCurrentScene = false,
            SceneLoadDatabase database = null)
        {
            var issues = new List<string>();
            if (config == null)
            {
                issues.Add("场景配置引用为空。");
                return new SceneLoadValidationResult(issues);
            }

            if (string.IsNullOrWhiteSpace(config.SceneId)) issues.Add("稳定 SceneId 不能为空。");
            if (string.IsNullOrWhiteSpace(config.SceneName)) issues.Add("SceneName 不能为空。");
            if (config.SceneReference == null || string.IsNullOrWhiteSpace(config.SceneReference.AssetGUID))
                issues.Add("必须配置有效的 Addressables 场景引用。");
            if (database != null && !string.IsNullOrWhiteSpace(config.SceneId))
            {
                int matchingSceneConfigCount = 0;
                for (int index = 0; index < database.SceneConfigs.Count; index++)
                {
                    SceneLoadConfig candidate = database.SceneConfigs[index];
                    if (candidate != null && string.Equals(candidate.SceneId, config.SceneId, StringComparison.Ordinal))
                        matchingSceneConfigCount++;
                }
                if (matchingSceneConfigCount > 1)
                    issues.Add($"稳定 SceneId '{config.SceneId}' 在当前场景数据库中出现 {matchingSceneConfigCount} 次，必须唯一。");
            }
            if (config.RootTask == null)
            {
                issues.Add("必须配置根场景任务。");
                return new SceneLoadValidationResult(issues);
            }

            var ancestorSet = new HashSet<SceneLoadTask>();
            int sceneLoadCount = 0;
            Visit(config.RootTask, initializesCurrentScene, true, config, ancestorSet, "Root", issues, ref sceneLoadCount);
            if (sceneLoadCount != 1)
                issues.Add($"场景切换配置必须恰好包含一个 Addressables 场景加载任务，当前数量为 {sceneLoadCount}。");
            return new SceneLoadValidationResult(issues);
        }

        #endregion

        #region 递归校验

        /// <summary>检查共享子树、引用环及进入组合节点时各分支的场景就绪状态。</summary>
        /// <param name="task">当前任务节点。</param>
        /// <param name="sceneReady">当前串行路径是否已经激活目标场景。</param>
        /// <param name="allowSceneLoadToSatisfyTasks">当前组合是否已离开包含场景加载的并行边界。</param>
        /// <param name="config">当前场景配置。</param>
        /// <param name="ancestorSet">当前祖先节点集合，仅用于检测环。</param>
        /// <param name="path">当前引用位置路径。</param>
        /// <param name="issues">累计错误信息。</param>
        /// <param name="sceneLoadCount">当前配置包含的场景加载节点总数。</param>
        /// <returns>完成此节点后目标场景是否就绪。</returns>
        private static bool Visit(
            SceneLoadTask task,
            bool sceneReady,
            bool allowSceneLoadToSatisfyTasks,
            SceneLoadConfig config,
            HashSet<SceneLoadTask> ancestorSet,
            string path,
            List<string> issues,
            ref int sceneLoadCount)
        {
            if (task == null)
            {
                issues.Add($"{path}：子任务引用为空。");
                return sceneReady;
            }
            if (!ancestorSet.Add(task))
            {
                issues.Add($"{path}：检测到组合任务循环引用 '{task.name}'。");
                return sceneReady;
            }
            if (task.RequiresTargetSceneReady && !sceneReady)
                issues.Add($"{path}：任务 '{task.name}' 要求目标场景就绪，不能安排在场景加载前或同一个 Parallel 分支中。");

            if (!(task is SequenceSceneLoadTask) && !(task is ParallelSceneLoadTask) &&
                (float.IsNaN(task.ProgressWeight) || float.IsInfinity(task.ProgressWeight) || task.ProgressWeight <= 0f))
                issues.Add($"{path}：叶子任务进度权重必须是有限的正数。");

            bool result = sceneReady;
            if (task is AddressableSceneLoadTask)
            {
                sceneLoadCount++;
                if (config.SceneReference == null || string.IsNullOrWhiteSpace(config.SceneReference.AssetGUID))
                    issues.Add($"{path}：Addressables 场景加载任务缺少有效场景地址。");
                result = allowSceneLoadToSatisfyTasks || sceneReady;
            }
            else if (task is SequenceSceneLoadTask sequence)
            {
                for (int index = 0; index < sequence.Children.Count; index++)
                    result = Visit(sequence.Children[index], result, allowSceneLoadToSatisfyTasks, config, ancestorSet,
                        $"{path}/{sequence.name}[{index}]", issues, ref sceneLoadCount);
            }
            else if (task is ParallelSceneLoadTask parallel)
            {
                int sceneLoadCountBeforeParallel = sceneLoadCount;
                for (int index = 0; index < parallel.Children.Count; index++)
                {
                    Visit(parallel.Children[index], sceneReady, false, config, ancestorSet,
                        $"{path}/{parallel.name}[{index}]", issues, ref sceneLoadCount);
                }
                // 子分支不继承同组场景加载带来的就绪；只有该 Parallel 返回后，外层序列才能使用新场景。
                result = sceneReady || (allowSceneLoadToSatisfyTasks && sceneLoadCount > sceneLoadCountBeforeParallel);
            }
            ancestorSet.Remove(task);
            return result;
        }

        #endregion
    }
}
