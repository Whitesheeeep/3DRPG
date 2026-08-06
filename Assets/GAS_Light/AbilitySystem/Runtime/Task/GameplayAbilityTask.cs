using System;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>定义异步 Ability 的单个运行步骤及其受控生命周期。</summary>
    public abstract class GameplayAbilityTask
    {
        #region 事件与属性
        // 父 Sequence 或 Root Runtime 监听正常完成；Stop/Cancel 不发送完成通知。
        internal event Action<GameplayAbilityTask> Completed;

        /// <summary>获取当前 Task 状态。</summary>
        public GameplayAbilityTaskState State { get; private set; }
        /// <summary>获取拥有该 Task 的异步 Runtime。</summary>
        protected AsynchronousGameplayAbilityRuntime Runtime { get; }
        #endregion

        #region 构造
        // Config 创建阶段仅绑定 Runtime，不注册更新或启动业务。
        /// <summary>创建尚未启动的 Task。</summary>
        protected GameplayAbilityTask(AsynchronousGameplayAbilityRuntime runtime)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            State = GameplayAbilityTaskState.Inactive;
        }
        #endregion

        #region 生命周期入口
        // 仅允许首次从 Inactive 启动。
        internal bool Start()
        {
            if (State != GameplayAbilityTaskState.Inactive) return false;
            State = GameplayAbilityTaskState.Running;
            OnStart();
            return true;
        }

        // 正常提前结束只传播到当前运行步骤。
        internal bool Stop()
        {
            if (State != GameplayAbilityTaskState.Running) return false;
            State = GameplayAbilityTaskState.Stopped;
            OnStop();
            return true;
        }

        // 打断只传播到当前运行步骤。
        internal bool Cancel()
        {
            if (State != GameplayAbilityTaskState.Running) return false;
            State = GameplayAbilityTaskState.Cancelled;
            OnCancel();
            return true;
        }

        // 只有 Running Task 可以正常完成，并且完成通知只发送一次。
        protected bool Complete()
        {
            if (State != GameplayAbilityTaskState.Running) return false;
            State = GameplayAbilityTaskState.Completed;
            OnComplete();
            Completed?.Invoke(this);
            return true;
        }
        #endregion

        #region 子类业务逻辑
        // 具体 Task 在这里启动同步逻辑或注册外部更新。
        protected abstract void OnStart();

        // 具体 Task 在正常提前结束时释放注册。
        protected virtual void OnStop()
        {
        }

        // 具体 Task 在中断时释放注册。
        protected virtual void OnCancel()
        {
        }

        // 需要外部资源的 Task 在完成通知前释放资源。
        protected virtual void OnComplete()
        {
        }
        #endregion
    }
}
