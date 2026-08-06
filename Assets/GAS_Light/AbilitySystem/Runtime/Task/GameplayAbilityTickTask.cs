using WS_Modules.CustomEventSystem;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>为动画、蓄力和其他持续业务提供基于 ASC Tick 的逐帧 Ability Task 基类。</summary>
    public abstract class GameplayAbilityTickTask : GameplayAbilityTask
    {
        #region 字段
        private IUnRegister tickRegistration;
        #endregion

        #region 构造
        /// <summary>创建尚未注册逐帧更新的 Tick Task。</summary>
        protected GameplayAbilityTickTask(AsynchronousGameplayAbilityRuntime runtime)
            : base(runtime)
        {
        }
        #endregion

        #region 生命周期
        // 启动时注册到所属 ASC 的 AbilityCtrl；具体业务在 OnTick 中处理每帧输入。
        protected sealed override void OnStart()
        {
            tickRegistration = Runtime.Source.RegisterAbilityTick(OnTickCallback);
            OnTickStarted();
        }

        // 正常结束时释放 Tick 回调。
        protected sealed override void OnStop() => ReleaseTick();

        // 取消时释放 Tick 回调。
        protected sealed override void OnCancel() => ReleaseTick();

        // Complete 会先调用此钩子，确保 Completed 事件发送前已注销 Tick。
        protected override void OnComplete() => ReleaseTick();
        #endregion

        #region Tick 扩展
        // 允许业务 Task 在注册完成后初始化动画或计时状态。
        protected virtual void OnTickStarted()
        {
        }

        // 具体业务 Task 实现逐帧逻辑，并在适当时机调用 Complete。
        protected abstract void OnTick(float deltaTime);

        // Controller 使用快照发送 Tick；Task 自身仍检查运行状态，避免完成后推进业务。
        private void OnTickCallback(float deltaTime)
        {
            if (State == GameplayAbilityTaskState.Running) OnTick(deltaTime);
        }

        // IUnRegister 引用在首次释放后清空，保证只注销一次。
        private void ReleaseTick()
        {
            tickRegistration?.UnRegister();
            tickRegistration = null;
        }
        #endregion
    }
}