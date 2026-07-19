using System;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// 业务层持有的计时器控制句柄。
    /// 句柄只保存 Manager 与 Timer Id，不直接持有可被对象池复用的 Timer 实例。
    /// </summary>
    public readonly struct TimerHandle
    {
        private readonly TimerManager _manager;
        private readonly long _timerId;

        internal TimerHandle(TimerManager manager, long timerId)
        {
            _manager = manager;
            _timerId = timerId;
        }

        public bool IsValid => _manager != null && _manager.IsTimerValid(_timerId);

        public float Duration => _manager != null ? _manager.GetDuration(_timerId) : 0f;
        public float TimeElapsed => _manager != null ? _manager.GetTimeElapsed(_timerId) : 0f;
        public float TimeRemaining => _manager != null ? _manager.GetTimeRemaining(_timerId) : 0f;
        public float Progress => _manager != null ? _manager.GetProgress(_timerId) : 0f;

        public TimerHandle SetTag(TimerManager.TimerTags tag)
        {
            _manager?.SetTag(_timerId, tag);
            return this;
        }

        public TimerHandle SetLoop(int count)
        {
            _manager?.SetLoop(_timerId, count);
            return this;
        }

        public TimerHandle SetUnscaledTime(bool useUnscaled)
        {
            _manager?.SetUnscaledTime(_timerId, useUnscaled);
            return this;
        }

        public TimerHandle SetTimeScale(float scale)
        {
            _manager?.SetTimeScale(_timerId, scale);
            return this;
        }

        public TimerHandle OnUpdate(Action<float> action)
        {
            _manager?.SetOnUpdate(_timerId, action);
            return this;
        }

        public TimerHandle ResetTime(float? newDuration = null)
        {
            _manager?.ResetTime(_timerId, newDuration);
            return this;
        }

        public void Cancel()
        {
            _manager?.Cancel(_timerId);
        }

        public TimerHandle Pause()
        {
            _manager?.Pause(_timerId);
            return this;
        }

        public TimerHandle Resume()
        {
            _manager?.Resume(_timerId);
            return this;
        }
    }
}
