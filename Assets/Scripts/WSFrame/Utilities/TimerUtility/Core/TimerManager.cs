using System;
using WS_Modules.CustomEventSystem;
using WS_Modules.SceneModule;
using WS_Modules.Singleton;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// 全局计时器管理器。
    /// 无 OnUpdate 的 Timer 使用最小堆按到期时间调度；有 OnUpdate 的 Timer 保留逐帧推进。
    /// </summary>
    public class TimerManager : AutoSingletonMonoBase<TimerManager>
    {
        #region Types

        /// <summary>
        /// 预定义的计时器标签。
        /// 使用 Flags 支持一个 Timer 同时拥有多个标签，例如 Test | UI。
        /// 项目中如果需要更多分组，可以继续扩展此枚举。
        /// </summary>
        [Flags]
        public enum TimerTags
        {
            None = 0,
            Test = 1 << 0,
            All = ~0
        }

        internal enum TimerScheduleMode
        {
            /// <summary>
            /// 已回收，不属于任何 TimerManager 容器，外部链式调用应直接忽略。
            /// </summary>
            Recycled,

            /// <summary>
            /// 已由 TimerManager 管理，但当前没有挂在任何调度容器中；通常是迁移容器时的临时状态。
            /// </summary>
            Detached,

            /// <summary>
            /// 新注册后暂存一帧，等待调用者完成链式配置，再决定进入堆还是逐帧列表。
            /// </summary>
            Pending,

            /// <summary>
            /// 使用 Time.time 的最小堆调度路径，适用于不需要 OnUpdate 的 scaled Timer。
            /// </summary>
            HeapScaled,

            /// <summary>
            /// 使用 Time.unscaledTime 的最小堆调度路径，适用于不需要 OnUpdate 的 unscaled Timer。
            /// </summary>
            HeapUnscaled,

            /// <summary>
            /// 逐帧更新列表，适用于设置了 OnUpdate 的 Timer。
            /// </summary>
            UpdateList,

            /// <summary>
            /// 暂停状态，不在堆或逐帧列表中，恢复时会重新进入对应调度路径。
            /// </summary>
            Paused,

            /// <summary>
            /// 正在执行完成回调。回调期间的 Cancel/Reset/OnUpdate 等操作会先记录状态，回调结束后统一处理。
            /// </summary>
            Executing
        }

        #endregion

        #region Fields

        // 当前所有未回收的 Timer。Tag 批量操作扫描这张表，避免额外维护标签索引。
        private readonly TimerRegistry _registry = new TimerRegistry();
        private readonly TimerFactory _factory = new TimerFactory();
        private readonly TimerQueryService _queries = new TimerQueryService();
        private readonly TimerScheduler _scheduler = new TimerScheduler();
        private readonly TimerStateController _states = new TimerStateController();
        private IUnRegister sceneLoadStartedUnregister;
        private ITimerSchedulerContext _schedulerContext;

        private ITimerSchedulerContext SchedulerContext
        {
            get
            {
                if (_schedulerContext == null)
                {
                    _schedulerContext = new TimerSchedulerContext(this);
                }

                return _schedulerContext;
            }
        }

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            // 场景切换时清理所有计时器，避免上一场景的延迟回调在新场景中继续执行。
            sceneLoadStartedUnregister = SceneSystem.RegisterLoadStarted(OnSceneLoadStarted);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            sceneLoadStartedUnregister?.UnRegister();
            sceneLoadStartedUnregister = null;
        }

        private void OnSceneLoadStarted(SceneLoadStartedEventArgs _)
        {
            CancelAll();
        }

        #endregion

        #region Register And Pooling

        /// <summary>
        /// 注册一个新的计时器。
        /// 返回的 TimerHandle 可继续链式配置，例如 SetLoop、SetTag、SetUnscaledTime、OnUpdate。
        /// </summary>
        /// <param name="duration">计时持续时间，单位为秒。</param>
        /// <param name="onComplete">计时完成时触发的回调。</param>
        /// <returns>本次注册得到的 Timer 控制句柄。</returns>
        public static TimerHandle Register(float duration, Action onComplete)
        {
            return Instance.GetTimerFromPool(duration, onComplete);
        }

        private TimerHandle GetTimerFromPool(float duration, Action onComplete)
        {
            Timer timer = _factory.Create();

            timer.Init(duration, onComplete);

            _registry.Add(timer);
            _scheduler.AddPending(timer);
            return new TimerHandle(this, timer.Id);
        }

        #endregion

        #region Update Entry

        private void Update()
        {
            _scheduler.Update(SchedulerContext);
        }

        #endregion

        #region Timer State Changes

        internal bool IsTimerValid(long timerId)
        {
            return _queries.IsValid(_registry, timerId);
        }

        internal float GetDuration(long timerId)
        {
            return _queries.GetDuration(_registry, timerId);
        }

        internal float GetTimeElapsed(long timerId)
        {
            return _queries.GetTimeElapsed(_registry, timerId, _scheduler.GetTimerClock);
        }

        internal float GetTimeRemaining(long timerId)
        {
            return _queries.GetTimeRemaining(_registry, timerId, _scheduler.GetTimerClock);
        }

        internal float GetProgress(long timerId)
        {
            return _queries.GetProgress(_registry, timerId, _scheduler.GetTimerClock);
        }

        internal void SetTag(long timerId, TimerTags tag)
        {
            _states.SetTag(_registry, timerId, tag);
        }

        internal void SetLoop(long timerId, int count)
        {
            _states.SetLoop(_registry, timerId, count);
        }

        internal void SetOnUpdate(long timerId, Action<float> action)
        {
            _states.SetOnUpdate(_registry, _scheduler, timerId, action);
        }

        internal void SetUnscaledTime(long timerId, bool useUnscaled)
        {
            _states.SetUnscaledTime(_registry, _scheduler, timerId, useUnscaled);
        }

        internal void SetTimeScale(long timerId, float scale)
        {
            _states.SetTimeScale(_registry, _scheduler, timerId, scale);
        }

        internal void ResetTime(long timerId, float? newDuration = null)
        {
            _states.ResetTime(_registry, _scheduler, timerId, newDuration);
        }

        internal void Pause(long timerId)
        {
            _states.Pause(_registry, _scheduler, timerId);
        }

        internal void Resume(long timerId)
        {
            _states.Resume(_registry, _scheduler, timerId);
        }

        internal void Cancel(long timerId)
        {
            _states.Cancel(_registry, timerId, RecycleTimer);
        }

        #endregion

        #region Recycling

        private void RecycleTimer(Timer timer)
        {
            if (timer == null || timer.IsRecycled) return;

            _scheduler.RemoveFromSchedule(timer);
            _registry.Remove(timer);

            timer.scheduleMode = TimerScheduleMode.Recycled;

            _factory.Recycle(timer);
        }

        #endregion

        #region Public Cleanup API

        /// <summary>
        /// 立即取消并回收所有活跃 Timer。
        /// 常用于场景切换、模块关闭、测试环境清理。
        /// </summary>
        public static void CancelAll()
        {
            if (Instance == null) return;

            var manager = Instance;
            manager._registry.ForEachReverse(manager.RecycleTimer);
            manager._registry.Clear();
            manager._scheduler.Clear();
        }

        #endregion

        #region Tag Batch API

        /// <summary>
        /// 暂停符合 Flag 条件的所有计时器。
        /// 例如 PauseByTag(TimerTags.Test) 会暂停所有包含 Test 标签的 Timer。
        /// </summary>
        public static void PauseByTag(TimerTags tag)
        {
            if (Instance == null || tag == TimerTags.None) return;

            var manager = Instance;
            manager._registry.ForEachReverse(timer =>
            {
                if (!timer.IsRecycled && (timer.tag & tag) != 0)
                {
                    manager._states.PauseTimer(manager._registry, manager._scheduler, timer);
                }
            });
        }

        /// <summary>
        /// 恢复符合 Flag 条件的所有计时器。
        /// </summary>
        public static void ResumeByTag(TimerTags tag)
        {
            if (Instance == null || tag == TimerTags.None) return;

            var manager = Instance;
            manager._registry.ForEachReverse(timer =>
            {
                if (!timer.IsRecycled && (timer.tag & tag) != 0)
                {
                    manager._states.ResumeTimer(manager._registry, manager._scheduler, timer);
                }
            });
        }

        /// <summary>
        /// 取消符合 Flag 条件的所有计时器。
        /// </summary>
        public static void CancelByTag(TimerTags tag)
        {
            if (Instance == null || tag == TimerTags.None) return;

            var manager = Instance;
            manager._registry.ForEachReverse(timer =>
            {
                if (!timer.IsRecycled && (timer.tag & tag) != 0)
                {
                    manager._states.CancelTimer(timer, manager.RecycleTimer);
                }
            });
        }

        /// <summary>
        /// 设置符合 Flag 条件的所有计时器的局部时间缩放。
        /// 该缩放只影响匹配到的 Timer，不会修改 Unity 的 Time.timeScale。
        /// </summary>
        public static void SetTimeScaleByTag(TimerTags tag, float scale)
        {
            if (Instance == null || tag == TimerTags.None) return;

            var manager = Instance;
            manager._registry.ForEachReverse(timer =>
            {
                if (!timer.IsRecycled && (timer.tag & tag) != 0)
                {
                    manager._states.ChangeTimeScale(manager._registry, manager._scheduler, timer, scale);
                }
            });
        }

        #endregion

        /// <summary>
        /// 用于给 TimerScheduler 提供回调接口访问 TimerManager 的内部方法，避免 Scheduler 直接持有 Manager 引用导致的耦合。
        /// </summary>
        private sealed class TimerSchedulerContext : ITimerSchedulerContext
        {
            private readonly TimerManager _manager;

            internal TimerSchedulerContext(TimerManager manager)
            {
                _manager = manager;
            }

            public void RecycleTimer(Timer timer)
            {
                _manager.RecycleTimer(timer);
            }

        }
    }
}
