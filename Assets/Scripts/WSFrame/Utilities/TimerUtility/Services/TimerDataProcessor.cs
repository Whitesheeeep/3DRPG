using UnityEngine;

namespace WS_Modules.Utilities
{
    internal static class TimerDataProcessor
    {
        internal static float GetElapsed(Timer timer, float now)
        {
            if (timer.IsRecycled)
            {
                return 0f;
            }

            if (IsHeapTimer(timer) && !timer.isPaused && timer.timeScale > 0f)
            {
                return Mathf.Min(timer.duration, timer.timeElapsed + (now - timer.lastClockTime) * timer.timeScale);
            }

            return timer.timeElapsed;
        }

        internal static float GetRemaining(Timer timer, float now)
        {
            return Mathf.Max(0f, timer.duration - GetElapsed(timer, now));
        }

        internal static float GetProgress(Timer timer, float now)
        {
            return Mathf.Clamp01(timer.duration > 0f ? GetElapsed(timer, now) / timer.duration : 1f);
        }

        internal static float GetProgress(Timer timer)
        {
            return Mathf.Clamp01(timer.duration > 0f ? timer.timeElapsed / timer.duration : 1f);
        }

        internal static void RefreshHeapDueTime(Timer timer, float now)
        {
            float remaining = GetRemaining(timer, now);

            timer.lastClockTime = now;
            timer.nextDueTime = timer.timeScale <= 0f
                ? float.PositiveInfinity
                : now + remaining / timer.timeScale;
        }

        internal static void SettleHeapElapsed(Timer timer, float now)
        {
            if (!IsHeapTimer(timer))
            {
                return;
            }

            if (timer.timeScale <= 0f)
            {
                timer.lastClockTime = now;
                return;
            }

            timer.timeElapsed = Mathf.Min(timer.duration, timer.timeElapsed + (now - timer.lastClockTime) * timer.timeScale);
            timer.lastClockTime = now;
        }

        internal static void CompleteHeapRound(Timer timer, float now)
        {
            timer.timeElapsed = timer.duration;
            timer.lastClockTime = now;
        }

        internal static bool ShouldContinueLoop(Timer timer)
        {
            if (!timer.isLoop)
            {
                return false;
            }

            if (timer.loopCount == -1)
            {
                return true;
            }

            timer.loopCount--;
            return timer.loopCount > 0;
        }

        internal static void ResetTime(Timer timer, float? newDuration)
        {
            if (newDuration.HasValue)
            {
                timer.duration = Mathf.Max(0f, newDuration.Value);
            }

            timer.timeElapsed = 0f;
            timer.isCompleted = false;
            timer.isPaused = false;
        }

        private static bool IsHeapTimer(Timer timer)
        {
            return timer.scheduleMode == TimerManager.TimerScheduleMode.HeapScaled ||
                   timer.scheduleMode == TimerManager.TimerScheduleMode.HeapUnscaled;
        }
    }
}
