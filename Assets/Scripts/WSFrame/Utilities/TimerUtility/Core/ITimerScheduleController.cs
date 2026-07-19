namespace WS_Modules.Utilities
{
    internal interface ITimerScheduleController
    {
        void ScheduleTimer(Timer timer);
        void RemoveFromSchedule(Timer timer);
        float GetTimerClock(Timer timer);
    }
}
