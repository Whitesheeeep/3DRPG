using System;

namespace WS_Modules.Utilities
{
    internal sealed class TimerStateController
    {
        internal void SetTag(TimerRegistry registry, long timerId, TimerManager.TimerTags tag)
        {
            if (!registry.TryGet(timerId, out var timer)) return;
            timer.tag = tag;
        }

        internal void SetLoop(TimerRegistry registry, long timerId, int count)
        {
            if (!registry.TryGet(timerId, out var timer)) return;
            timer.isLoop = true;
            timer.loopCount = count;
        }

        internal void SetOnUpdate(TimerRegistry registry, ITimerScheduleController scheduler, long timerId, Action<float> action)
        {
            if (!registry.TryGet(timerId, out var timer)) return;
            ChangeOnUpdate(registry, scheduler, timer, action);
        }

        internal void SetUnscaledTime(TimerRegistry registry, ITimerScheduleController scheduler, long timerId, bool useUnscaled)
        {
            if (!registry.TryGet(timerId, out var timer)) return;
            ChangeUnscaledTime(registry, scheduler, timer, useUnscaled);
        }

        internal void SetTimeScale(TimerRegistry registry, ITimerScheduleController scheduler, long timerId, float scale)
        {
            if (!registry.TryGet(timerId, out var timer)) return;
            ChangeTimeScale(registry, scheduler, timer, scale);
        }

        internal void ResetTime(TimerRegistry registry, ITimerScheduleController scheduler, long timerId, float? newDuration)
        {
            if (!registry.TryGet(timerId, out var timer)) return;
            ResetTimer(registry, scheduler, timer, newDuration);
        }

        internal void Pause(TimerRegistry registry, ITimerScheduleController scheduler, long timerId)
        {
            if (!registry.TryGet(timerId, out var timer)) return;
            PauseTimer(registry, scheduler, timer);
        }

        internal void Resume(TimerRegistry registry, ITimerScheduleController scheduler, long timerId)
        {
            if (!registry.TryGet(timerId, out var timer)) return;
            ResumeTimer(registry, scheduler, timer);
        }

        internal void Cancel(TimerRegistry registry, long timerId, Action<Timer> recycleTimer)
        {
            if (!registry.TryGet(timerId, out var timer)) return;
            CancelTimer(timer, recycleTimer);
        }

        internal void ChangeTimeScale(TimerRegistry registry, ITimerScheduleController scheduler, Timer timer, float scale)
        {
            if (!IsManagedTimer(registry, timer)) return;

            if (timer.scheduleMode == TimerManager.TimerScheduleMode.Pending)
            {
                timer.timeScale = scale;
                return;
            }

            if (timer.scheduleMode == TimerManager.TimerScheduleMode.Executing)
            {
                timer.timeScale = scale;
                return;
            }

            if (timer.scheduleMode == TimerManager.TimerScheduleMode.UpdateList)
            {
                timer.timeScale = scale;
                return;
            }

            TimerDataProcessor.SettleHeapElapsed(timer, scheduler.GetTimerClock(timer));
            scheduler.RemoveFromSchedule(timer);

            timer.timeScale = scale;

            if (timer.isPaused)
            {
                timer.scheduleMode = TimerManager.TimerScheduleMode.Paused;
            }
            else
            {
                scheduler.ScheduleTimer(timer);
            }
        }

        internal void PauseTimer(TimerRegistry registry, ITimerScheduleController scheduler, Timer timer)
        {
            if (!IsManagedTimer(registry, timer)) return;
            if (timer.isPaused) return;

            TimerDataProcessor.SettleHeapElapsed(timer, scheduler.GetTimerClock(timer));
            scheduler.RemoveFromSchedule(timer);

            timer.isPaused = true;
            timer.scheduleMode = TimerManager.TimerScheduleMode.Paused;
        }

        internal void ResumeTimer(TimerRegistry registry, ITimerScheduleController scheduler, Timer timer)
        {
            if (!IsManagedTimer(registry, timer)) return;
            if (!timer.isPaused) return;

            timer.isPaused = false;
            timer.isCompleted = false;
            scheduler.RemoveFromSchedule(timer);
            scheduler.ScheduleTimer(timer);
        }

        internal void CancelTimer(Timer timer, Action<Timer> recycleTimer)
        {
            if (timer == null || timer.IsRecycled) return;

            if (timer.scheduleMode == TimerManager.TimerScheduleMode.Executing)
            {
                timer.isCompleted = true;
                return;
            }

            recycleTimer(timer);
        }

        private void ChangeOnUpdate(TimerRegistry registry, ITimerScheduleController scheduler, Timer timer, Action<float> action)
        {
            if (!IsManagedTimer(registry, timer)) return;

            if (timer.scheduleMode == TimerManager.TimerScheduleMode.Pending)
            {
                timer.onUpdate = action;
                return;
            }

            if (timer.scheduleMode == TimerManager.TimerScheduleMode.Executing)
            {
                timer.onUpdate = action;
                return;
            }

            if (timer.scheduleMode == TimerManager.TimerScheduleMode.UpdateList && action != null)
            {
                timer.onUpdate = action;
                return;
            }

            TimerDataProcessor.SettleHeapElapsed(timer, scheduler.GetTimerClock(timer));
            scheduler.RemoveFromSchedule(timer);

            timer.onUpdate = action;
            timer.isCompleted = false;

            if (timer.isPaused)
            {
                timer.scheduleMode = TimerManager.TimerScheduleMode.Paused;
            }
            else
            {
                scheduler.ScheduleTimer(timer);
            }
        }

        private void ChangeUnscaledTime(TimerRegistry registry, ITimerScheduleController scheduler, Timer timer, bool useUnscaled)
        {
            if (!IsManagedTimer(registry, timer)) return;

            if (timer.scheduleMode == TimerManager.TimerScheduleMode.Pending)
            {
                timer.useUnscaledTime = useUnscaled;
                return;
            }

            if (timer.scheduleMode == TimerManager.TimerScheduleMode.Executing)
            {
                timer.useUnscaledTime = useUnscaled;
                return;
            }

            if (timer.scheduleMode == TimerManager.TimerScheduleMode.UpdateList)
            {
                timer.useUnscaledTime = useUnscaled;
                return;
            }

            TimerDataProcessor.SettleHeapElapsed(timer, scheduler.GetTimerClock(timer));
            scheduler.RemoveFromSchedule(timer);

            timer.useUnscaledTime = useUnscaled;

            if (timer.isPaused)
            {
                timer.scheduleMode = TimerManager.TimerScheduleMode.Paused;
            }
            else
            {
                scheduler.ScheduleTimer(timer);
            }
        }

        private void ResetTimer(TimerRegistry registry, ITimerScheduleController scheduler, Timer timer, float? newDuration)
        {
            if (!IsManagedTimer(registry, timer)) return;

            if (timer.scheduleMode == TimerManager.TimerScheduleMode.Pending ||
                timer.scheduleMode == TimerManager.TimerScheduleMode.Executing ||
                timer.scheduleMode == TimerManager.TimerScheduleMode.UpdateList)
            {
                TimerDataProcessor.ResetTime(timer, newDuration);
                return;
            }

            scheduler.RemoveFromSchedule(timer);
            TimerDataProcessor.ResetTime(timer, newDuration);
            scheduler.ScheduleTimer(timer);
        }

        private bool IsManagedTimer(TimerRegistry registry, Timer timer)
        {
            return registry.Contains(timer);
        }
    }
}
