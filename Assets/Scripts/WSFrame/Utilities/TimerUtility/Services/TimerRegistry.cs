using System;
using System.Collections.Generic;

namespace WS_Modules.Utilities
{
    internal sealed class TimerRegistry
    {
        private readonly List<Timer> _activeTimers = new List<Timer>(100);
        private readonly Dictionary<long, Timer> _activeTimerMap = new Dictionary<long, Timer>(100);

        internal void Add(Timer timer)
        {
            timer.activeIndex = _activeTimers.Count;
            _activeTimers.Add(timer);
            _activeTimerMap[timer.Id] = timer;
        }

        internal void Remove(Timer timer)
        {
            int index = timer.activeIndex;
            if (index < 0 || index >= _activeTimers.Count || _activeTimers[index] != timer)
            {
                _activeTimerMap.Remove(timer.Id);
                timer.activeIndex = -1;
                return;
            }

            int lastIndex = _activeTimers.Count - 1;
            if (index < lastIndex)
            {
                var last = _activeTimers[lastIndex];
                _activeTimers[index] = last;
                last.activeIndex = index;
            }

            _activeTimers.RemoveAt(lastIndex);
            _activeTimerMap.Remove(timer.Id);
            timer.activeIndex = -1;
        }

        internal bool Contains(Timer timer)
        {
            return timer != null &&
                   !timer.IsRecycled &&
                   _activeTimerMap.TryGetValue(timer.Id, out var activeTimer) &&
                   ReferenceEquals(activeTimer, timer);
        }

        internal bool TryGet(long timerId, out Timer timer)
        {
            if (_activeTimerMap.TryGetValue(timerId, out timer) && timer != null && timer.Id == timerId)
            {
                return true;
            }

            timer = null;
            return false;
        }

        internal void ForEachReverse(Action<Timer> action)
        {
            for (int i = _activeTimers.Count - 1; i >= 0; i--)
            {
                action(_activeTimers[i]);
            }
        }

        internal void Clear()
        {
            _activeTimers.Clear();
            _activeTimerMap.Clear();
        }
    }
}
