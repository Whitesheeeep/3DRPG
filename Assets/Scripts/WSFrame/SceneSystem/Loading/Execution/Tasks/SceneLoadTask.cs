using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WS_Modules.SceneModule
{
    /// <summary>可配置的场景加载流程节点；配置资产只描述行为，执行状态保存在本次运行上下文。</summary>
    public abstract class SceneLoadTask : ScriptableObject
    {
        #region 进度配置

        [SerializeField, Tooltip("此叶子任务在完整任务树进度中的相对权重；组合任务忽略此值。")]
        private float progressWeight = 1f;

        /// <summary>获取任务在完整加载流程中的相对进度权重。</summary>
        public float ProgressWeight => progressWeight;

        #endregion

        #region 节点依赖

        /// <summary>获取此节点能否在目标场景激活前执行。</summary>
        public virtual bool RequiresTargetSceneReady => false;

        #endregion

        #region 执行

        /// <summary>执行本节点表示的异步加载工作。</summary>
        /// <param name="context">本次场景切换共用的执行上下文。</param>
        /// <param name="cancellationToken">调用方协作式取消令牌。</param>
        /// <returns>节点完成、失败或取消对应的异步任务。</returns>
        public abstract UniTask ExecuteAsync(SceneLoadContext context, CancellationToken cancellationToken);

        #endregion
    }
}
