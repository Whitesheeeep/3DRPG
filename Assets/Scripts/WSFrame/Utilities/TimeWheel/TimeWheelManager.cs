using System;
using UnityEngine;
using WS_Modules.Singleton;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// 可选的 Unity 驱动器，默认使用 Time.deltaTime 推进一个以真实秒为单位的 scaled 时间轮。
    /// </summary>
    public sealed class TimeWheelManager : AutoConfigSingletonMonoBase<TimeWheelManager, TimeWheelConfig>
    {
        private TimeWheelScheduler _scheduler;

        public TimeWheelScheduler Scheduler => _scheduler;

        protected override TimeWheelConfig CreateDefaultConfig()
        {
            return new TimeWheelConfig();
        }

        protected override void InitWithConfig(TimeWheelConfig config)
        {
            _scheduler = new TimeWheelScheduler(config);
        }

        public void Initialize(TimeWheelConfig config)
        {
            if (_scheduler is { ActiveCount: > 0 })
            {
                Debug.LogWarning("TimeWheelManager.Initialize 被忽略：当前仍有活跃任务。请先调用 Clear 再重新初始化。");
                return;
            }

            _scheduler = new TimeWheelScheduler(config);
        }

        public TimeWheelHandle Schedule(float delay, Action callback)
        {
            EnsureScheduler();
            return _scheduler.Schedule(delay, callback);
        }

        public TimeWheelHandle ScheduleRepeat(float interval, Action callback, int repeatCount = -1)
        {
            EnsureScheduler();
            return _scheduler.ScheduleRepeat(interval, callback, repeatCount);
        }

        public bool Cancel(TimeWheelHandle handle)
        {
            EnsureScheduler();
            return _scheduler.Cancel(handle);
        }

        public bool Pause(TimeWheelHandle handle)
        {
            EnsureScheduler();
            return _scheduler.Pause(handle);
        }

        public bool Resume(TimeWheelHandle handle)
        {
            EnsureScheduler();
            return _scheduler.Resume(handle);
        }

        public void Clear()
        {
            _scheduler?.Clear();
        }

        private void Update()
        {
            // TimeWheelManager 的输入单位是真实秒，因此这里传入 Unity 的 Time.deltaTime。
            _scheduler?.Tick(Time.deltaTime);
        }

        protected override void OnDestroy()
        {
            _scheduler?.Clear();
            base.OnDestroy();
        }

        private void EnsureScheduler()
        {
            if (_scheduler == null)
            {
                _scheduler = new TimeWheelScheduler();
            }
        }
    }
}
