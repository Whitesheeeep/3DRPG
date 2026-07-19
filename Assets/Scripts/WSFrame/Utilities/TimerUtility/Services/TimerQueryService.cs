using System;

namespace WS_Modules.Utilities
{
    internal sealed class TimerQueryService
    {
        internal bool IsValid(TimerRegistry registry, long timerId)
        {
            return registry.TryGet(timerId, out _);
        }

        internal float GetDuration(TimerRegistry registry, long timerId)
        {
            return registry.TryGet(timerId, out var timer) ? timer.duration : 0f;
        }

        internal float GetTimeElapsed(TimerRegistry registry, long timerId, Func<Timer, float> getClock)
        {
            return registry.TryGet(timerId, out var timer) ? TimerDataProcessor.GetElapsed(timer, getClock(timer)) : 0f;
        }

        internal float GetTimeRemaining(TimerRegistry registry, long timerId, Func<Timer, float> getClock)
        {
            return registry.TryGet(timerId, out var timer)
                ? TimerDataProcessor.GetRemaining(timer, getClock(timer))
                : 0f;
        }

        internal float GetProgress(TimerRegistry registry, long timerId, Func<Timer, float> getClock)
        {
            return registry.TryGet(timerId, out var timer)
                ? TimerDataProcessor.GetProgress(timer, getClock(timer))
                : 0f;
        }
    }
}
