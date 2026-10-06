using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WS_Modules.SceneModule
{
    /// <summary>同时启动所有子任务、等待全部结束后再传播失败的组合节点。</summary>
    [CreateAssetMenu(fileName = "ParallelSceneLoadTask", menuName = "WSFrame/Scene Loading/Parallel")]
    public sealed class ParallelSceneLoadTask : SceneLoadTask
    {
        #region 并行任务配置

        // 配置：所有子任务并行启动，并在组合末尾汇合。
        // 同一帧启动全部分支；任何分支失败仍会等其余已启动分支收尾。
        [SerializeField]
        private List<SceneLoadTask> children = new();

        #endregion

        #region 属性

        /// <summary>获取只读子任务视图。</summary>
        public IReadOnlyList<SceneLoadTask> Children => children;

        #endregion

        #region 执行

        /// <summary>并发运行所有子任务并观察每个分支的成功、失败或取消结果。</summary>
        /// <param name="context">本次场景切换共用的执行上下文。</param>
        /// <param name="cancellationToken">调用方协作式取消令牌。</param>
        /// <returns>所有子任务收尾后的异步任务。</returns>
        public override async UniTask ExecuteAsync(SceneLoadContext context, CancellationToken cancellationToken)
        {
            var tasks = new List<UniTask<Exception>>(children.Count);
            for (int index = 0; index < children.Count; index++)
            {
                SceneLoadTask child = children[index];
                if (child == null)
                    throw new MissingReferenceException($"[ParallelSceneLoadTask] '{name}' 的子节点 {index} 未配置。");
                tasks.Add(ObserveChildAsync(child, context, index, cancellationToken));
            }

            Exception[] exceptions = await UniTask.WhenAll(tasks);
            for (int index = 0; index < exceptions.Length; index++)
            {
                if (exceptions[index] != null)
                    throw exceptions[index];
            }
        }

        /// <summary>运行一个并行分支，并把异常变成可等待的结果以便其他分支继续收尾。</summary>
        /// <param name="child">并行分支任务。</param>
        /// <param name="context">共用的场景加载上下文。</param>
        /// <param name="cancellationToken">调用方协作式取消令牌。</param>
        /// <returns>成功时返回空值，失败或取消时返回实际异常。</returns>
        private async UniTask<Exception> ObserveChildAsync(
            SceneLoadTask child,
            SceneLoadContext context,
            int childIndex,
            CancellationToken cancellationToken)
        {
            try
            {
                await context.ExecuteTaskAsync(child, childIndex, cancellationToken);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        #endregion
    }
}
