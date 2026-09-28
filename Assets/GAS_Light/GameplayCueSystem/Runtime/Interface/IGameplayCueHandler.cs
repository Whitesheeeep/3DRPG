using System;
using WS_Modules.GAS.AbilitySystemComponent;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>
    /// 定义可由共享 ScriptableObject 或 ASC 私有实例执行的 Cue 生命周期处理器。
    /// </summary>
    public interface IGameplayCueHandler
    {
        /// <summary>执行一次性 Cue；Execute 不会登记持续句柄。</summary>
        /// <param name="data">与当前 Handler 类型匹配的 Cue 配置。</param>
        /// <param name="request">发布 Cue 的来源、目标和空间信息。</param>
        /// <param name="controller">拥有当前请求和 Active 记录的目标 Cue Controller。</param>
        void Execute(GameplayCueData data, GameplayCueRequest request, GameplayCueCtrl controller);

        /// <summary>启动持续 Cue 并返回由该 Handler 拥有的状态对象。</summary>
        /// <param name="data">与当前 Handler 类型匹配的 Cue 配置。</param>
        /// <param name="request">发布 Cue 的来源、目标和空间信息。</param>
        /// <param name="controller">拥有当前请求和 Active 记录的目标 Cue Controller。</param>
        /// <returns>Remove 或 Clear 时交还给原 Handler 的状态对象，可为空。</returns>
        object Active(GameplayCueData data, GameplayCueRequest request, GameplayCueCtrl controller);

        /// <summary>结束一个由当前 Handler 创建的持续 Cue。</summary>
        /// <param name="record">需要移除的 Handler 状态记录。</param>
        void Remove(GameplayCueActiveRecord record);

        /// <summary>清理 Controller 关闭时仍由当前 Handler 持有的持续 Cue。</summary>
        /// <param name="record">需要清理的 Handler 状态记录。</param>
        void Clear(GameplayCueActiveRecord record);
    }
}
