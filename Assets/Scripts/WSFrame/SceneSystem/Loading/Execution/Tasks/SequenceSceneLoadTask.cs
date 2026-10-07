using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WS_Modules.SceneModule
{
    /// <summary>按资产列表顺序串行执行子任务的组合节点。</summary>
    [CreateAssetMenu(fileName = "SequenceSceneLoadTask", menuName = "WSFrame/Scene Loading/Sequence")]
    public sealed class SequenceSceneLoadTask : SceneLoadTask
    {
        #region 子任务配置

        // 配置：子任务引用的真实列表顺序就是执行顺序。
        // 列表顺序就是运行顺序，子资产可被多个配置复用。
        [SerializeField]
        private List<SceneLoadTask> children = new();

        #endregion

        #region 属性

        /// <summary>获取只读子任务视图。</summary>
        public IReadOnlyList<SceneLoadTask> Children => children;

        #endregion

        #region 执行

        /// <summary>逐个等待子任务结束；前序失败时不会启动后续节点。</summary>
        /// <param name="context">本次场景切换共用的执行上下文。</param>
        /// <param name="cancellationToken">调用方协作式取消令牌。</param>
        /// <returns>所有子节点依序完成后的异步任务。</returns>
        public override async UniTask ExecuteAsync(SceneLoadContext context, CancellationToken cancellationToken)
        {
            for (int index = 0; index < children.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SceneLoadTask child = children[index];
                if (child == null)
                    throw new MissingReferenceException($"[SequenceSceneLoadTask] '{name}' 的子节点 {index} 未配置。");
                await context.ExecuteTaskAsync(child, index, cancellationToken);
            }
        }

        #endregion
    }
}
