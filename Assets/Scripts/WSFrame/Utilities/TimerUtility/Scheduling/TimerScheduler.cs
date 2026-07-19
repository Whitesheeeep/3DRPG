using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.Utilities
{
    internal sealed class TimerScheduler : ITimerScheduleController
    {
        private readonly List<Timer> _pendingTimers = new List<Timer>(100);
        private readonly List<Timer> _updateTimers = new List<Timer>(100);
        private readonly TimerMinHeap _scaledHeap = new TimerMinHeap();
        private readonly TimerMinHeap _unscaledHeap = new TimerMinHeap();

        internal void AddPending(Timer timer)
        {
            timer.scheduleMode = TimerManager.TimerScheduleMode.Pending;
            timer.collectionIndex = _pendingTimers.Count;
            _pendingTimers.Add(timer);
        }

        internal void Update(ITimerSchedulerContext context)
        {
            SchedulePendingTimers();
            UpdateProgressTimers(context);
            UpdateHeapTimers(_scaledHeap, Time.time, context);
            UpdateHeapTimers(_unscaledHeap, Time.unscaledTime, context);
        }

        public void ScheduleTimer(Timer timer)
        {
            if (timer.IsRecycled || timer.isCompleted) return;

            if (timer.isPaused)
            {
                timer.scheduleMode = TimerManager.TimerScheduleMode.Paused;
                return;
            }

            if (timer.onUpdate != null)
            {
                AddUpdateTimer(timer);
                return;
            }

            TimerDataProcessor.RefreshHeapDueTime(timer, GetTimerClock(timer));

            if (timer.useUnscaledTime)
            {
                _unscaledHeap.Add(timer);
                timer.scheduleMode = TimerManager.TimerScheduleMode.HeapUnscaled;
            }
            else
            {
                _scaledHeap.Add(timer);
                timer.scheduleMode = TimerManager.TimerScheduleMode.HeapScaled;
            }
        }

        public void RemoveFromSchedule(Timer timer)
        {
            switch (timer.scheduleMode)
            {
                case TimerManager.TimerScheduleMode.Pending:
                    RemoveFromIndexedList(_pendingTimers, timer);
                    break;
                case TimerManager.TimerScheduleMode.UpdateList:
                    RemoveFromIndexedList(_updateTimers, timer);
                    break;
                case TimerManager.TimerScheduleMode.HeapScaled:
                    _scaledHeap.Remove(timer);
                    break;
                case TimerManager.TimerScheduleMode.HeapUnscaled:
                    _unscaledHeap.Remove(timer);
                    break;
            }

            timer.collectionIndex = -1;
            timer.heapIndex = -1;
            if (timer.scheduleMode != TimerManager.TimerScheduleMode.Recycled)
            {
                timer.scheduleMode = TimerManager.TimerScheduleMode.Detached;
            }
        }

        public float GetTimerClock(Timer timer)
        {
            return timer.useUnscaledTime ? Time.unscaledTime : Time.time;
        }

        internal void Clear()
        {
            _pendingTimers.Clear();
            _updateTimers.Clear();
            _scaledHeap.Clear();
            _unscaledHeap.Clear();
        }

        private void SchedulePendingTimers()
        {
            while (_pendingTimers.Count > 0)
            {
                var timer = _pendingTimers[_pendingTimers.Count - 1];
                RemoveFromIndexedList(_pendingTimers, timer);

                if (timer.IsRecycled || timer.isCompleted)
                {
                    continue;
                }

                if (timer.isPaused)
                {
                    timer.scheduleMode = TimerManager.TimerScheduleMode.Paused;
                    continue;
                }

                ScheduleTimer(timer);
            }
        }

        private void UpdateProgressTimers(ITimerSchedulerContext context)
        {
            int i = 0;
            while (i < _updateTimers.Count)
            {
                var timer = _updateTimers[i];

                if (timer.IsRecycled || timer.isCompleted)
                {
                    context.RecycleTimer(timer);
                    continue;
                }

                if (timer.isPaused)
                {
                    RemoveFromSchedule(timer);
                    timer.scheduleMode = TimerManager.TimerScheduleMode.Paused;
                    continue;
                }

                float dt = (timer.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime) * timer.timeScale;
                timer.timeElapsed += dt;

                timer.onUpdate?.Invoke(TimerDataProcessor.GetProgress(timer));

                if (timer.IsRecycled || timer.scheduleMode != TimerManager.TimerScheduleMode.UpdateList ||
                    timer.collectionIndex != i || _updateTimers[i] != timer)
                {
                    continue;
                }

                if (timer.timeElapsed >= timer.duration)
                {
                    timer.onComplete?.Invoke();

                    if (timer.IsRecycled || timer.scheduleMode != TimerManager.TimerScheduleMode.UpdateList ||
                        timer.collectionIndex != i || _updateTimers[i] != timer)
                    {
                        continue;
                    }

                    if (TimerDataProcessor.ShouldContinueLoop(timer))
                    {
                        timer.timeElapsed = 0f;
                        i++;
                    }
                    else
                    {
                        context.RecycleTimer(timer);
                    }
                }
                else
                {
                    i++;
                }
            }
        }

        private void UpdateHeapTimers(
            TimerMinHeap heap,
            float now,
            ITimerSchedulerContext context)
        {
            while (heap.Count > 0)
            {
                var timer = heap.Peek();

                if (timer.IsRecycled || timer.isCompleted)
                {
                    heap.Pop();
                    context.RecycleTimer(timer);
                    continue;
                }

                if (timer.nextDueTime > now)
                {
                    break;
                }

                heap.Pop();
                timer.scheduleMode = TimerManager.TimerScheduleMode.Executing;
                timer.heapIndex = -1;
                TimerDataProcessor.CompleteHeapRound(timer, now);

                timer.onComplete?.Invoke();

                if (timer.IsRecycled)
                {
                    continue;
                }

                if (timer.isCompleted)
                {
                    context.RecycleTimer(timer);
                    continue;
                }

                if (TimerDataProcessor.ShouldContinueLoop(timer))
                {
                    timer.timeElapsed = 0f;
                    timer.scheduleMode = TimerManager.TimerScheduleMode.Detached;
                    ScheduleTimer(timer);
                }
                else
                {
                    context.RecycleTimer(timer);
                }
            }
        }

        private void AddUpdateTimer(Timer timer)
        {
            timer.scheduleMode = TimerManager.TimerScheduleMode.UpdateList;
            timer.collectionIndex = _updateTimers.Count;
            _updateTimers.Add(timer);
        }

        private void RemoveFromIndexedList(List<Timer> list, Timer timer)
        {
            int index = timer.collectionIndex;
            if (index < 0 || index >= list.Count || list[index] != timer)
            {
                timer.collectionIndex = -1;
                return;
            }

            int lastIndex = list.Count - 1;
            if (index < lastIndex)
            {
                var last = list[lastIndex];
                list[index] = last;
                last.collectionIndex = index;
            }

            list.RemoveAt(lastIndex);
            timer.collectionIndex = -1;
        }
    }
}